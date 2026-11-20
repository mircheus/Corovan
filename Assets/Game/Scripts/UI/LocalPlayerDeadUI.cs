using FPSGame.Networking;
using FPSGame.Player;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace FPSGame.UI
{
    /// <summary>
    /// Shown when THIS client's own player dies while the match is still going — a teammate may yet
    /// revive the squad's fortunes, win the extraction, or lose it. Purely local: it reacts to the
    /// local player's <see cref="PlayerHealth"/>, not to the match state.
    /// <para>
    /// Distinct from <see cref="MatchEndUI"/>, which announces the whole match's outcome. If the
    /// local death is itself what wipes the squad, <see cref="GameStateManager"/> ends the match and
    /// this overlay steps aside so the end screen owns the view.
    /// </para>
    /// </summary>
    public class LocalPlayerDeadUI : MonoBehaviour
    {
        [Tooltip("Panel shown while the local player is dead and the match is still in progress.")]
        [FormerlySerializedAs("gameOverUI")]
        [SerializeField] private GameObject root;

        [Tooltip("Optional — countdown to the respawn (PlayerHealth.deadRespawnDelay). Hidden when none is scheduled.")]
        [SerializeField] private TextMeshProUGUI respawnText;
        [Tooltip("{0} is replaced by the whole seconds left.")]
        [SerializeField] private string respawnFormat = "Возрождение через {0}";

        // The local player spawns after the scene loads, so the binding can't be a serialized
        // reference — it's resolved from NetworkClient.localPlayer once that exists.
        private PlayerHealth localHealth;
        private GameStateManager gameState;

        private void Start()
        {
            if (root != null)
                root.SetActive(false);

            gameState = GameStateManager.Instance;
            if (gameState != null)
                gameState.OnMatchEnded += OnMatchEnded;
        }

        private void OnDestroy()
        {
            if (gameState != null)
                gameState.OnMatchEnded -= OnMatchEnded;

            Unbind();
        }

        private void Update()
        {
            if (localHealth == null)
                TryBind();

            UpdateRespawnText();
        }

        // Derived from the synced deadline every frame, like the bleedout countdown.
        private void UpdateRespawnText()
        {
            if (respawnText == null || root == null || !root.activeSelf)
                return;

            bool pending = localHealth != null && localHealth.RespawnPending;
            if (respawnText.gameObject.activeSelf != pending)
                respawnText.gameObject.SetActive(pending);
            if (pending)
                respawnText.text = string.Format(respawnFormat, Mathf.CeilToInt(localHealth.RespawnRemaining));
        }

        private void TryBind()
        {
            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer == null)
                return;

            localHealth = localPlayer.GetComponent<PlayerHealth>();
            if (localHealth == null)
                return;

            localHealth.OnDeath += OnLocalDeath;
            localHealth.OnRevived += OnLocalRecovered;
            localHealth.OnRespawn += OnLocalRecovered;

            // Bound late (this UI can outlive a respawn): reflect a death that already happened.
            if (localHealth.IsDead)
                OnLocalDeath();
        }

        private void Unbind()
        {
            if (localHealth == null)
                return;

            localHealth.OnDeath -= OnLocalDeath;
            localHealth.OnRevived -= OnLocalRecovered;
            localHealth.OnRespawn -= OnLocalRecovered;
            localHealth = null;
        }

        private void OnLocalDeath()
        {
            // The match-end SyncVar and the life-state SyncVar arrive independently on a remote
            // client, so the match may already be decided by the time this fires. If so, MatchEndUI
            // owns the screen — don't stack under it.
            if (gameState != null && gameState.IsMatchOver)
                return;

            Show(true);
        }

        private void OnLocalRecovered() => Show(false);

        private void OnMatchEnded(MatchResult result) => Show(false);

        private void Show(bool visible)
        {
            if (root != null)
                root.SetActive(visible);
        }
    }
}
