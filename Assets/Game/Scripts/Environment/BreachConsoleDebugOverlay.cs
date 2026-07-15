using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    // Throwaway on-screen debug for validating BreachConsole on a scene. Reads the
    // replicated State / HeatNormalized (cosmetic-only, so it works on every client)
    // and logs transitions through OnStateChanged to prove the §5 event contract fires.
    // Remove before shipping — no gameplay logic lives here.
    public class BreachConsoleDebugOverlay : MonoBehaviour
    {
        [SerializeField] private BreachConsole console;
        [Tooltip("Padding of the overlay box from the bottom-left screen corner, in pixels.")]
        [SerializeField] private Vector2 screenOffset = new Vector2(16f, 16f);
        [Tooltip("Also print each state transition to the Console log.")]
        [SerializeField] private bool logTransitions = true;
        [Tooltip("Hotkey that force-breaches the console with no key-card (server/host only).")]
        [SerializeField] private KeyCode debugBreachKey = KeyCode.F8;

        private static readonly Color IdleColor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color BreachingColor = new Color(1f, 0.85f, 0.2f);
        private static readonly Color OpenColor = new Color(0.3f, 1f, 0.4f);
        private static readonly Color OverheatColor = new Color(1f, 0.3f, 0.25f);

        private GUIStyle labelStyle;
        private Texture2D pixel;

        private void Awake()
        {
            if (console == null)
                console = GetComponent<BreachConsole>();
            if (console == null)
                console = GetComponentInParent<BreachConsole>();
        }

        private void OnEnable()
        {
            if (console != null)
                console.OnStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (console != null)
                console.OnStateChanged -= HandleStateChanged;
        }

        private void Update()
        {
            if (console == null || !Input.GetKeyDown(debugBreachKey))
                return;

            // SetCardInSlot / DebugToggleBreach are server-authoritative, so this only bites
            // on the host or a headless server. Warn instead of silently doing nothing on a
            // pure client.
            if (!NetworkServer.active)
            {
                Debug.LogWarning($"[BreachConsole] {name}: {debugBreachKey} test-breach ignored — not the server/host.", this);
                return;
            }

            console.DebugToggleBreach();
        }

        private void HandleStateChanged(ConsoleState state)
        {
            if (logTransitions)
                Debug.Log($"[BreachConsole] {name} -> {state}", this);
        }

        private void OnGUI()
        {
            if (console == null)
                return;

            if (pixel == null)
            {
                pixel = new Texture2D(1, 1);
                pixel.SetPixel(0, 0, Color.white);
                pixel.Apply();
            }
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
            }

            const float width = 420f;
            const float height = 150f;
            const float pad = 16f;
            // Anchor to the bottom-left corner.
            var box = new Rect(screenOffset.x, Screen.height - height - screenOffset.y, width, height);

            DrawRect(box, new Color(0f, 0f, 0f, 0.7f));

            ConsoleState state = console.State;
            float heat = console.HeatNormalized;

            labelStyle.fontSize = 26;
            labelStyle.normal.textColor = StateColor(state);
            GUI.Label(new Rect(box.x + pad, box.y + pad, width - 2f * pad, 34f),
                $"{name}: {state}", labelStyle);

            // Heat bar.
            var barBg = new Rect(box.x + pad, box.y + 62f, width - 2f * pad, 30f);
            DrawRect(barBg, new Color(0.15f, 0.15f, 0.15f, 1f));
            var barFill = new Rect(barBg.x, barBg.y, barBg.width * Mathf.Clamp01(heat), barBg.height);
            DrawRect(barFill, HeatColor(heat));

            labelStyle.fontSize = 22;
            labelStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(box.x + pad, box.y + 104f, width - 2f * pad, 30f),
                $"heat {heat:0.00}", labelStyle);
        }

        private void DrawRect(Rect rect, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, pixel);
            GUI.color = prev;
        }

        private static Color StateColor(ConsoleState state) => state switch
        {
            ConsoleState.Breaching => BreachingColor,
            ConsoleState.Open => OpenColor,
            ConsoleState.Overheat => OverheatColor,
            _ => IdleColor
        };

        // Green -> yellow -> red as heat climbs to the threshold.
        private static Color HeatColor(float heat) =>
            Color.Lerp(OpenColor, OverheatColor, Mathf.Clamp01(heat));
    }
}
