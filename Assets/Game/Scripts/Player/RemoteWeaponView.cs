using System.Collections;
using UnityEngine;
using Mirror;
using Akila.FPSFramework;

namespace FPSGame.Player
{
    /// <summary>
    /// What other players see of this player's weapons — a placeholder until there is a proper
    /// third-person rig. The FPS Framework viewmodel lives only on the owner, so a remote copy shows
    /// a world model per loadout slot (PlayerLoadout.equippedSlot) at a fixed anchor, and replays
    /// the owner's shots: muzzle flash, fire sound, tracer and impact.
    ///
    /// Shots are cosmetic and unreliable: the owner already decided the hit (client-authoritative);
    /// this only draws it. The replay never spawns FPSF projectiles, which would deal damage again.
    /// </summary>
    [RequireComponent(typeof(PlayerLoadout))]
    public class RemoteWeaponView : NetworkBehaviour
    {
        [Tooltip("World model per loadout slot (same order as the Inventory). Inactive by default.")]
        [SerializeField] private GameObject[] slotModels;
        [Tooltip("Muzzle point per slot model (tracer and flash start here). Falls back to the model.")]
        [SerializeField] private Transform[] slotMuzzles;
        [SerializeField] private GameObject muzzleFlashPrefab;
        [SerializeField] private float tracerDuration = 0.05f;
        [SerializeField] private float tracerWidth = 0.02f;
        [SerializeField] private Color tracerColor = new Color(1f, 0.85f, 0.5f, 0.9f);
        [Tooltip("How long a replayed impact decal stays.")]
        [SerializeField] private float impactLifetime = 10f;

        private PlayerLoadout loadout;
        private int shownSlot = -1;
        private Material tracerMaterial;

        private void Awake()
        {
            loadout = GetComponent<PlayerLoadout>();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            Firearm.ShotFired += OnLocalShot;
        }

        public override void OnStopLocalPlayer()
        {
            base.OnStopLocalPlayer();
            Firearm.ShotFired -= OnLocalShot;
        }

        private void OnDestroy()
        {
            Firearm.ShotFired -= OnLocalShot;
            if (tracerMaterial != null)
                Destroy(tracerMaterial);
        }

        private void Update()
        {
            // The owner sees the FPSF viewmodel instead.
            if (isOwned)
                return;

            int slot = loadout.EquippedSlot;
            if (slot != shownSlot)
                Show(slot);
        }

        private void Show(int slot)
        {
            if (slotModels != null)
                for (int i = 0; i < slotModels.Length; i++)
                    if (slotModels[i] != null)
                        slotModels[i].SetActive(i == slot);
            shownSlot = slot;
        }

        // ---------- owner: report the shot ----------

        private void OnLocalShot(Firearm firearm)
        {
            // Every player's FPSF weapons raise the same static event; only ours concern us.
            if (firearm == null || !firearm.transform.IsChildOf(transform))
                return;

            Transform muzzle = firearm._muzzle != null ? firearm._muzzle : firearm.transform;
            float range = firearm.preset != null ? firearm.preset.range : 300f;
            Vector3 origin = muzzle.position;
            Vector3 dir = muzzle.forward;

            bool hit = Physics.Raycast(origin, dir, out RaycastHit info, range, ~0, QueryTriggerInteraction.Ignore);
            Vector3 end = hit ? info.point : origin + dir * range;
            CmdShot(end, hit ? info.normal : Vector3.zero, hit);
        }

        [Command(channel = Channels.Unreliable)]
        private void CmdShot(Vector3 end, Vector3 normal, bool hit)
        {
            RpcShot(end, normal, hit);
        }

        [ClientRpc(includeOwner = false, channel = Channels.Unreliable)]
        private void RpcShot(Vector3 end, Vector3 normal, bool hit)
        {
            int slot = loadout.EquippedSlot;
            Transform muzzle = MuzzleFor(slot);
            Firearm firearm = loadout.GetSlot(slot) != null ? loadout.GetSlot(slot).GetComponent<Firearm>() : null;
            FirearmData preset = firearm != null ? firearm.preset : null;

            if (muzzleFlashPrefab != null && muzzle != null)
            {
                GameObject flash = Instantiate(muzzleFlashPrefab, muzzle.position, muzzle.rotation);
                SetLayer(flash, 0); // the FPSF prefab sits on the viewmodel layer other cameras skip
                Destroy(flash, 2f);
            }

            if (preset != null && preset.fire != null && preset.fire.clip != null && muzzle != null)
                AudioSource.PlayClipAtPoint(preset.fire.clip, muzzle.position, preset.fire.volume);

            if (muzzle != null)
                StartCoroutine(Tracer(muzzle.position, end));

            if (hit && preset != null && preset.defaultDecal != null)
            {
                GameObject decal = Instantiate(preset.defaultDecal, end, Quaternion.LookRotation(normal));
                Destroy(decal, impactLifetime);
            }
        }

        private Transform MuzzleFor(int slot)
        {
            if (slotMuzzles != null && slot >= 0 && slot < slotMuzzles.Length && slotMuzzles[slot] != null)
                return slotMuzzles[slot];
            if (slotModels != null && slot >= 0 && slot < slotModels.Length && slotModels[slot] != null)
                return slotModels[slot].transform;
            return transform;
        }

        private IEnumerator Tracer(Vector3 from, Vector3 to)
        {
            if (tracerMaterial == null)
                tracerMaterial = new Material(Shader.Find("Sprites/Default"));

            var go = new GameObject("Tracer");
            var line = go.AddComponent<LineRenderer>();
            line.material = tracerMaterial;
            line.startColor = line.endColor = tracerColor;
            line.startWidth = line.endWidth = tracerWidth;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            yield return new WaitForSeconds(tracerDuration);
            Destroy(go);
        }

        private static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }
    }
}
