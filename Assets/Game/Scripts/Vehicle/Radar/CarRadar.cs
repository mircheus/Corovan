using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPSGame.Vehicle
{
    /// <summary>
    /// In-car top-down radar. A private ortho camera renders a small uGUI canvas (background, car marker,
    /// pooled blips) into a RenderTexture, which is put on the material of an in-cab screen mesh.
    /// Client-side only: runs on the peer that drives the car (isOwned + HasDriver), idle elsewhere.
    /// Threats come from the <see cref="RadarTarget"/> registry, fixed map points from the scene's <see cref="RadarPoints"/>.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class CarRadar : MonoBehaviour
    {
        [Header("Scene refs (all inside the car prefab)")]
        [SerializeField] private Camera radarCamera;
        [SerializeField] private Canvas radarCanvas;
        [Tooltip("Full-stretch child of the canvas; blips are created under it.")]
        [SerializeField] private RectTransform blipContainer;
        [Tooltip("Optional marker of our own car; moved to the car's place on the radar.")]
        [SerializeField] private RectTransform carMarker;
        [Tooltip("Renderer of the in-cab screen mesh.")]
        [SerializeField] private Renderer screenRenderer;
        [SerializeField] private int screenMaterialIndex = 0;

        [Header("Render texture")]
        [SerializeField] private Vector2Int resolution = new Vector2Int(256, 512);

        [Header("Coverage (metres around the car)")]
        [SerializeField] private float rangeForward = 40f;
        [SerializeField] private float rangeBack = 80f;
        [Tooltip("Half-width: the radar covers X from -rangeSide to +rangeSide.")]
        [SerializeField] private float rangeSide = 15f;
        [Tooltip("Up on the screen = where the car looks. Off = world +Z is up.")]
        [SerializeField] private bool rotateWithCar = true;

        [Header("Targets outside the coverage")]
        [Tooltip("Stick them to the edge of the radar (dimmed) as long as they are within trackDistance.")]
        [SerializeField] private bool clampToEdge = true;
        [SerializeField] private float trackDistance = 150f;
        [Range(0f, 1f)] [SerializeField] private float edgeAlpha = 0.5f;

        [Header("Debug")]
        [Tooltip("Run even when nobody drives / the car is not owned (single-scene tests).")]
        [SerializeField] private bool alwaysActive;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private CarController car;
        private RenderTexture rt;
        private Material screenMaterial;
        private readonly List<Image> blips = new List<Image>();
        private bool active;

        public bool IsActive => active;

        private void Awake()
        {
            car = GetComponent<CarController>();

            if (radarCamera == null || radarCanvas == null || blipContainer == null)
            {
                Debug.LogError("[CarRadar] Camera / Canvas / BlipContainer not assigned.", this);
                enabled = false;
                return;
            }

            rt = new RenderTexture(resolution.x, resolution.y, 16, RenderTextureFormat.ARGB32)
            {
                name = "CarRadarRT",
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1
            };
            rt.Create();

            radarCamera.orthographic = true;
            radarCamera.targetTexture = rt;
            radarCamera.clearFlags = CameraClearFlags.SolidColor;
            radarCamera.backgroundColor = Color.black;
            radarCamera.enabled = false;

            radarCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            radarCanvas.worldCamera = radarCamera;
            radarCanvas.planeDistance = Mathf.Max(0.5f, radarCamera.nearClipPlane + 0.5f);

            if (screenRenderer != null)
            {
                var mats = screenRenderer.materials; // instances: cars never share the screen
                int i = Mathf.Clamp(screenMaterialIndex, 0, mats.Length - 1);
                screenMaterial = mats[i];
                if (screenMaterial.HasProperty(BaseMapId)) screenMaterial.SetTexture(BaseMapId, rt);
                if (screenMaterial.HasProperty(MainTexId)) screenMaterial.SetTexture(MainTexId, rt);
                if (screenMaterial.HasProperty(EmissionMapId))
                {
                    screenMaterial.SetTexture(EmissionMapId, rt);
                    screenMaterial.EnableKeyword("_EMISSION");
                }
            }
        }

        private void OnDestroy()
        {
            if (rt != null) { rt.Release(); Destroy(rt); }
        }

        private void LateUpdate()
        {
            bool shouldRun = alwaysActive || (car.isOwned && car.HasDriver);
            if (shouldRun != active)
            {
                active = shouldRun;
                radarCamera.enabled = active;
                if (!active) HideBlipsFrom(0);
            }
            if (!active) return;

            UpdateBlips();
        }

        private void UpdateBlips()
        {
            Rect rect = blipContainer.rect;
            float w = rect.width, h = rect.height;
            if (w <= 1f || h <= 1f) return;

            float totalZ = rangeForward + rangeBack;
            Quaternion toLocal = rotateWithCar
                ? Quaternion.Euler(0f, -transform.eulerAngles.y, 0f)
                : Quaternion.identity;
            Vector3 carPos = transform.position;

            if (carMarker != null)
                carMarker.anchoredPosition = new Vector2(0f, (rangeBack / totalZ - 0.5f) * h);

            int used = 0;

            // Scene points first: drawn below the threats.
            var lists = RadarPoints.All;
            for (int l = 0; l < lists.Count; l++)
            {
                var points = lists[l].Points;
                for (int i = 0; i < points.Count; i++)
                {
                    RadarPoints.Point p = points[i];
                    if (p == null || !p.visible || p.target == null || p.style == null) continue;
                    if (!p.target.gameObject.activeInHierarchy) continue;
                    DrawBlip(p.target.position, p.style, carPos, toLocal, totalZ, w, h, ref used);
                }
            }

            var all = RadarTarget.All;
            for (int i = 0; i < all.Count; i++)
            {
                RadarTarget t = all[i];
                if (t == null || t.style == null || t.transform.IsChildOf(transform)) continue;
                DrawBlip(t.transform.position, t.style, carPos, toLocal, totalZ, w, h, ref used);
            }
            HideBlipsFrom(used);
        }

        private void DrawBlip(Vector3 worldPos, RadarBlipStyle s, Vector3 carPos, Quaternion toLocal,
                              float totalZ, float w, float h, ref int used)
        {
            Vector3 delta = worldPos - carPos;
            delta.y = 0f;
            if (!s.alwaysTrack && delta.sqrMagnitude > trackDistance * trackDistance) return;
            delta = toLocal * delta;

            float x01 = delta.x / (2f * rangeSide) + 0.5f;
            float y01 = (delta.z + rangeBack) / totalZ;

            float alpha = 1f;
            bool outside = x01 < 0f || x01 > 1f || y01 < 0f || y01 > 1f;
            if (outside)
            {
                if (!clampToEdge && !s.alwaysTrack) return;
                float mx = s.size * 0.5f;
                float my = s.size * 0.5f * w / h;
                x01 = Mathf.Clamp(x01, mx, 1f - mx);
                y01 = Mathf.Clamp(y01, my, 1f - my);
                alpha = edgeAlpha;
            }

            if (s.pulse)
                alpha *= 0.55f + 0.45f * Mathf.Sin(Time.time * s.pulseSpeed);

            Image img = GetBlip(used++);
            RectTransform rtf = img.rectTransform;
            rtf.anchoredPosition = new Vector2((x01 - 0.5f) * w, (y01 - 0.5f) * h);
            float px = s.size * w;
            rtf.sizeDelta = new Vector2(px, px);
            img.sprite = s.icon;
            Color c = s.color;
            c.a *= alpha;
            img.color = c;
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
        }

        private Image GetBlip(int index)
        {
            while (blips.Count <= index)
            {
                var go = new GameObject("Blip", typeof(RectTransform), typeof(Image));
                go.layer = blipContainer.gameObject.layer;
                var rtf = (RectTransform)go.transform;
                rtf.SetParent(blipContainer, false);
                rtf.anchorMin = rtf.anchorMax = rtf.pivot = new Vector2(0.5f, 0.5f);
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                blips.Add(img);
            }
            return blips[index];
        }

        private void HideBlipsFrom(int from)
        {
            for (int i = from; i < blips.Count; i++)
                if (blips[i].gameObject.activeSelf) blips[i].gameObject.SetActive(false);
        }
    }
}
