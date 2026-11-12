using System.Text;
using FPSGame.Player;
using Mirror;
using TMPro;
using UnityEngine;

namespace FPSGame.Environment
{
    /// <summary>
    /// World-space keypad for the code vault (handoff F4). Keys are <see cref="CodeConsoleButton"/> children
    /// pressed with E through <c>PlayerInputHandler</c> — no modal UI, the partner can turn away and shoot at
    /// any moment. The press is a <c>Command(requiresAuthority = false)</c>; the server validates the presser
    /// and asks <see cref="CodeVaultController.ServerPressDigit"/>, then every client gets the feedback.
    ///
    /// The screen shows the entered digits (from the controller's SyncVar) to both players.
    /// </summary>
    public class CodeConsole : NetworkBehaviour
    {
        [SerializeField] private CodeVaultController vault;

        [Tooltip("Max distance from the presser to the console, metres. The server allows +1 m for latency.")]
        [SerializeField, Min(0.5f)] private float pressRange = 3f;

        [Header("Screen")]
        [SerializeField] private TMP_Text screenText;
        [Tooltip("Optional: tinted on feedback (base color = its material color at start).")]
        [SerializeField] private Renderer screenRenderer;
        [SerializeField] private Color acceptColor = new Color(0.2f, 0.9f, 0.3f);
        [SerializeField] private Color errorColor = new Color(1f, 0.15f, 0.1f);
        [SerializeField] private Color notYetColor = new Color(1f, 0.75f, 0.1f);
        [SerializeField, Min(0.05f)] private float flashDuration = 0.35f;

        [Header("Sound (optional)")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip acceptClip;
        [SerializeField] private AudioClip errorClip;
        [SerializeField] private AudioClip notYetClip;
        [SerializeField] private AudioClip solvedClip;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock block;
        private Color screenBaseColor = Color.black;
        private float flashUntil;
        private Color flashColor;
        private string flashMessage;
        private readonly StringBuilder sb = new StringBuilder(32);

        public float PressRange => pressRange;

        private void Awake()
        {
            if (vault == null)
                vault = GetComponentInParent<CodeVaultController>();

            if (screenRenderer != null)
            {
                block = new MaterialPropertyBlock();
                Material mat = screenRenderer.sharedMaterial;
                if (mat != null)
                    screenBaseColor = mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId)
                                    : mat.HasProperty(ColorId) ? mat.GetColor(ColorId)
                                    : Color.black;
            }
        }

        // ── Press ─────────────────────────────────────────────────────────

        /// <summary>Client: the local player pressed a key of this console.</summary>
        public void RequestPress(CodeConsoleButton button)
        {
            if (button == null)
                return;
            button.PlayPress();
            CmdPress((byte)button.Digit);
        }

        [Command(requiresAuthority = false)]
        private void CmdPress(byte digit, NetworkConnectionToClient sender = null)
        {
            if (vault == null || digit > 9 || sender == null || sender.identity == null)
                return;

            var health = sender.identity.GetComponent<PlayerHealth>();
            if (health != null && health.IsIncapacitated)
                return;

            float dist = Vector3.Distance(sender.identity.transform.position, transform.position);
            if (dist > pressRange + 1f)
                return;

            CodeInputResult result = vault.ServerPressDigit(digit);
            RpcFeedback(result);
        }

        [ClientRpc]
        private void RpcFeedback(CodeInputResult result)
        {
            switch (result)
            {
                case CodeInputResult.Accepted:
                    Flash(acceptColor, null, acceptClip);
                    break;
                case CodeInputResult.Solved:
                    Flash(acceptColor, "ОТКРЫТО", solvedClip != null ? solvedClip : acceptClip);
                    break;
                case CodeInputResult.Wrong:
                    Flash(errorColor, "ОШИБКА", errorClip);
                    break;
                case CodeInputResult.NotYetDecrypted:
                    Flash(notYetColor, "ЕЩЁ НЕ РАСШИФРОВАНО", notYetClip);
                    break;
            }
        }

        private void Flash(Color color, string message, AudioClip clip)
        {
            flashColor = color;
            flashMessage = message;
            flashUntil = Time.time + flashDuration;
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }

        // ── Screen ────────────────────────────────────────────────────────

        private void Update()
        {
            if (vault == null)
                return;

            bool flashing = Time.time < flashUntil;
            UpdateScreenColor(flashing);

            if (screenText == null)
                return;

            sb.Clear();
            string entered = vault.Entered;
            int length = vault.CodeLength;
            for (int i = 0; i < length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(i < entered.Length ? entered[i] : '_');
            }

            if (flashing && !string.IsNullOrEmpty(flashMessage))
                sb.Append('\n').Append(flashMessage);
            else if (vault.IsSolved)
                sb.Append("\nОТКРЫТО");

            screenText.text = sb.ToString();
            screenText.color = flashing ? flashColor : (vault.IsSolved ? acceptColor : Color.white);
        }

        private void UpdateScreenColor(bool flashing)
        {
            if (screenRenderer == null)
                return;

            Color c = flashing ? Color.Lerp(screenBaseColor, flashColor, 0.6f) : screenBaseColor;
            screenRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(ColorId, c);
            screenRenderer.SetPropertyBlock(block);
        }
    }
}
