using FPSGame.Networking;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FPSGame.UI
{
    /// <summary>
    /// Victory / defeat screen, driven by <see cref="GameStateManager"/>. Distinct from GameOverUI,
    /// which reacts to this player's own death while the match may well still be running.
    /// </summary>
    public class MatchEndUI : MonoBehaviour
    {
        [Header("Panel")]
        [Tooltip("Container enabled when the match ends. Leave this component itself always active.")]
        [SerializeField] private GameObject root;
        [SerializeField] private TextMeshProUGUI outcomeText;
        [SerializeField] private string victoryMessage = "VICTORY";
        [SerializeField] private string defeatMessage = "DEFEAT";

        [Header("Restart")]
        [Tooltip("Restarting reloads the scene for everyone, so only the host gets the button.")]
        [SerializeField] private Button restartButton;
        [Tooltip("Optional — shown to non-host clients in place of the restart button.")]
        [SerializeField] private GameObject waitingForHostLabel;

        private GameStateManager gameState;

        private void Start()
        {
            if (root != null)
                root.SetActive(false);

            if (restartButton != null)
                restartButton.onClick.AddListener(OnRestartClicked);

            // Both are scene objects, so every Awake has run by now and Instance is set.
            gameState = GameStateManager.Instance;
            if (gameState == null)
            {
                Debug.LogError("[MatchEndUI] No GameStateManager in the scene — the end screen will never show.", this);
                return;
            }

            gameState.OnMatchEnded += Show;

            // Joined after the match had already been decided.
            if (gameState.IsMatchOver)
                Show(gameState.Result);
        }

        private void OnDestroy()
        {
            if (gameState != null)
                gameState.OnMatchEnded -= Show;

            if (restartButton != null)
                restartButton.onClick.RemoveListener(OnRestartClicked);
        }

        private void Show(MatchResult result)
        {
            if (root != null)
                root.SetActive(true);

            if (outcomeText != null)
                outcomeText.text = result == MatchResult.Victory ? victoryMessage : defeatMessage;

            bool isHost = NetworkServer.active;

            if (restartButton != null)
                restartButton.gameObject.SetActive(isHost);

            if (waitingForHostLabel != null)
                waitingForHostLabel.SetActive(!isHost);
        }

        private void OnRestartClicked()
        {
            // The button is hidden on clients, but a stray call must not reach a [Server] method.
            if (!NetworkServer.active || gameState == null)
                return;

            gameState.ServerRestartMatch();
        }
    }
}
