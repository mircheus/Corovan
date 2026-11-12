using FPSGame.Environment;
using FPSGame.Player;
using Mirror;
using UnityEngine;

namespace FPSGame.UI
{
    /// <summary>
    /// The driver's code readout (handoff F3): revealed symbols with placeholders, the current symbol's
    /// progress, the status line and how far the partner has typed. Drawn only on the client whose local
    /// player is at the wheel right now — everyone receives the code, hiding it is purely this view's job.
    ///
    /// Prototype view on IMGUI (no Canvas to author); reads the controller's SyncVars every frame.
    /// </summary>
    public class DriverCodeHUD : MonoBehaviour
    {
        [Tooltip("Empty = the first CodeVaultController in the scene.")]
        [SerializeField] private CodeVaultController vault;

        [SerializeField] private float topMargin = 24f;
        [SerializeField] private int digitFontSize = 44;
        [SerializeField] private int statusFontSize = 20;

        [Header("Colors")]
        [SerializeField] private Color revealedColor = Color.white;
        [SerializeField] private Color enteredColor = new Color(0.35f, 1f, 0.45f);
        [SerializeField] private Color nextColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color hiddenColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] private Color pausedColor = new Color(1f, 0.35f, 0.25f);

        private PlayerInputHandler localInput;
        private GUIStyle digitStyle, statusStyle;
        private Texture2D pixel;

        private void Start()
        {
            if (vault == null)
                vault = FindFirstObjectByType<CodeVaultController>();
        }

        private bool LocalPlayerIsDriving()
        {
            if (localInput == null)
            {
                NetworkIdentity local = NetworkClient.localPlayer;
                if (local == null)
                    return false;
                localInput = local.GetComponent<PlayerInputHandler>();
                if (localInput == null)
                    return false;
            }
            return localInput.IsInVehicle;
        }

        private void OnGUI()
        {
            if (vault == null || !LocalPlayerIsDriving())
                return;

            EnsureStyles();

            string code = vault.Code;
            int length = vault.CodeLength;
            int revealed = vault.RevealedCount;
            int entered = vault.EnteredCount;
            bool solved = vault.IsSolved;

            const float cell = 54f, gap = 8f;
            float panelWidth = Mathf.Max(360f, length * (cell + gap) + 40f);
            float x0 = (Screen.width - panelWidth) * 0.5f;
            float y = topMargin;

            DrawRect(new Rect(x0, y, panelWidth, 150f), new Color(0f, 0f, 0f, 0.55f));
            y += 10f;

            // Digits: revealed (white), already typed by the partner (green), the one to dictate next (yellow).
            float rowWidth = length * cell + (length - 1) * gap;
            float x = (Screen.width - rowWidth) * 0.5f;
            for (int i = 0; i < length; i++)
            {
                bool isRevealed = i < revealed && i < code.Length;
                string ch = isRevealed ? code[i].ToString() : "_";
                Color c = !isRevealed ? hiddenColor
                        : solved || i < entered ? enteredColor
                        : i == entered ? nextColor
                        : revealedColor;

                Rect r = new Rect(x + i * (cell + gap), y, cell, cell);
                DrawRect(r, new Color(1f, 1f, 1f, i == entered && isRevealed && !solved ? 0.18f : 0.07f));
                digitStyle.normal.textColor = c;
                GUI.Label(r, ch, digitStyle);
            }
            y += cell + 8f;

            // Current symbol progress.
            Rect bar = new Rect(x, y, rowWidth, 8f);
            DrawRect(bar, new Color(1f, 1f, 1f, 0.15f));
            float progress = revealed >= length ? 1f : vault.SymbolProgress;
            bool paused = vault.State == CodeVaultState.Paused;
            DrawRect(new Rect(bar.x, bar.y, bar.width * progress, bar.height), paused ? pausedColor : nextColor);
            y += 16f;

            statusStyle.normal.textColor = paused ? pausedColor : Color.white;
            GUI.Label(new Rect(x0, y, panelWidth, 26f), StatusText(), statusStyle);
            y += 26f;

            statusStyle.normal.textColor = enteredColor;
            GUI.Label(new Rect(x0, y, panelWidth, 26f), $"Напарник ввёл: {entered}/{length}", statusStyle);
        }

        private string StatusText()
        {
            switch (vault.State)
            {
                case CodeVaultState.Idle:
                    return "Встань в зону стоянки";
                case CodeVaultState.Decrypting:
                    return "Расшифровка";
                case CodeVaultState.Paused:
                    return "ПАУЗА: " + PauseReason(vault.Parking);
                case CodeVaultState.InputWindow:
                    float left = vault.WindowRemaining;
                    return left > 0f || vault.RotatesCode
                        ? $"Окно ввода: {Mathf.CeilToInt(left)} с"
                        : "Окно ввода истекло — можно вводить";
                case CodeVaultState.Solved:
                    return "Открыто";
            }
            return string.Empty;
        }

        private static string PauseReason(ParkingStatus status)
        {
            switch (status)
            {
                case ParkingStatus.Dashing: return "без рывков";
                case ParkingStatus.TooFar: return "вернись к точке стоянки";
                default: return "вернись в зону";
            }
        }

        private void EnsureStyles()
        {
            if (digitStyle != null)
                return;

            digitStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = digitFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = statusFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            pixel = Texture2D.whiteTexture;
        }

        private void DrawRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, pixel);
            GUI.color = prev;
        }
    }
}
