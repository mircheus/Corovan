using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FPSGame.UI
{
    /// <summary>
    /// Local full-screen blackout with a message and a countdown, for a player who crashed onto the road
    /// (<see cref="FPSGame.Player.PlayerFallHazard"/>). Built in code on first use, so no scene needs it
    /// placed; it sits above the HUD and the "you died" panel and is hidden when the match ends.
    /// </summary>
    public class RoadCrashOverlay : MonoBehaviour
    {
        private const int SortingOrder = 500;

        private CanvasGroup group;
        private TextMeshProUGUI messageText;
        private TextMeshProUGUI countdownText;

        private float targetAlpha;
        private float fadeSpeed;

        public bool IsShown => targetAlpha > 0f;

        public static RoadCrashOverlay Create()
        {
            var root = new GameObject("RoadCrashOverlay", typeof(RectTransform));
            DontDestroyOnLoad(root);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var overlay = root.AddComponent<RoadCrashOverlay>();
            overlay.group = root.AddComponent<CanvasGroup>();
            overlay.group.alpha = 0f;
            overlay.group.blocksRaycasts = false;
            overlay.group.interactable = false;

            var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
            black.transform.SetParent(root.transform, false);
            Stretch((RectTransform)black.transform);
            var image = black.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;

            overlay.messageText = CreateText(root.transform, "Message", 64f, new Vector2(0f, 40f));
            overlay.countdownText = CreateText(root.transform, "Countdown", 36f, new Vector2(0f, -50f));
            overlay.countdownText.color = new Color(1f, 1f, 1f, 0.7f);

            root.SetActive(false);
            return overlay;
        }

        /// <summary>Fades to black over <paramref name="fade"/> seconds (0 = at once) and shows the message.</summary>
        public void Show(string message, float fade)
        {
            gameObject.SetActive(true);
            messageText.text = message;
            countdownText.text = string.Empty;
            FadeTo(1f, fade);
        }

        public void Hide(float fade)
        {
            if (!gameObject.activeSelf)
                return;
            FadeTo(0f, fade);
        }

        public void SetCountdown(string text)
        {
            if (countdownText.text != text)
                countdownText.text = text;
        }

        private void FadeTo(float alpha, float duration)
        {
            targetAlpha = alpha;
            fadeSpeed = duration > 0f ? 1f / duration : float.PositiveInfinity;
            if (float.IsPositiveInfinity(fadeSpeed))
                ApplyAlpha(alpha);
        }

        private void Update()
        {
            if (!Mathf.Approximately(group.alpha, targetAlpha))
                ApplyAlpha(Mathf.MoveTowards(group.alpha, targetAlpha, fadeSpeed * Time.unscaledDeltaTime));
        }

        private void ApplyAlpha(float alpha)
        {
            group.alpha = alpha;
            if (alpha <= 0f && targetAlpha <= 0f)
                gameObject.SetActive(false);
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, float size, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(0f, size * 1.6f);
            rect.anchoredPosition = offset;

            var text = go.GetComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
