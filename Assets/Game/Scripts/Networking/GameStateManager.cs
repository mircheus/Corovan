using System;
using System.Collections.Generic;
using FPSGame.Player;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPSGame.Networking
{
    /// <summary>
    /// Outcome of the current match. A single enum rather than a phase flag plus a result flag:
    /// two SyncVars would arrive as two hooks, and a client could act on the outcome before the
    /// phase caught up (the same trap <see cref="LifeState"/> avoids on the player).
    /// </summary>
    public enum MatchResult : byte
    {
        InProgress = 0,
        Victory = 1,
        Defeat = 2
    }

    /// <summary>
    /// Server-authoritative match arbiter. Watches every connected player's life state and calls the
    /// match lost once nobody is left on their feet; polls <see cref="IWinCondition"/>s for the win.
    /// <para>
    /// Ending the match does not stop the world — turrets keep firing, barrels keep spawning,
    /// bleedout keeps ticking. It only flips a synced flag; clients react by locking input and
    /// showing <c>MatchEndUI</c>.
    /// </para>
    /// </summary>
    public class GameStateManager : NetworkBehaviour
    {
        public static GameStateManager Instance { get; private set; }

        [Header("Win Conditions")]
        [Tooltip("Components implementing IWinCondition. Empty = the match can never be won (stub).")]
        [SerializeField] private MonoBehaviour[] winConditionSources;

        [Tooltip("On: every condition must be met. Off: any single one wins the match.")]
        [SerializeField] private bool requireAllWinConditions = true;

        [Tooltip("Seconds between win-condition polls on the server.")]
        [SerializeField] private float winCheckInterval = 0.5f;

        [SyncVar(hook = nameof(OnMatchResultChangedHook))]
        private MatchResult matchResult = MatchResult.InProgress;

        // Server-only. Rebuilt from scratch on scene reload, since this is a scene object.
        private readonly List<PlayerHealth> serverPlayers = new List<PlayerHealth>();
        private IWinCondition[] winConditions = Array.Empty<IWinCondition>();
        private float winCheckTimer;

        public MatchResult Result => matchResult;
        public bool IsMatchActive => matchResult == MatchResult.InProgress;
        public bool IsMatchOver => matchResult != MatchResult.InProgress;

        /// <summary>Fires once on every client (and on a dedicated server) when the match ends.</summary>
        public event Action<MatchResult> OnMatchEnded;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            matchResult = MatchResult.InProgress;
            CacheWinConditions();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();

            foreach (PlayerHealth player in serverPlayers)
                UnsubscribeFrom(player);
            serverPlayers.Clear();
        }

        private void CacheWinConditions()
        {
            if (winConditionSources == null || winConditionSources.Length == 0)
            {
                winConditions = Array.Empty<IWinCondition>();
                return;
            }

            var resolved = new List<IWinCondition>(winConditionSources.Length);
            foreach (MonoBehaviour source in winConditionSources)
            {
                if (source == null)
                    continue;

                if (source is IWinCondition condition)
                    resolved.Add(condition);
                else
                    Debug.LogError($"[GameStateManager] '{source.GetType().Name}' does not implement IWinCondition — ignored.", source);
            }

            winConditions = resolved.ToArray();
        }

        // ----- Player roster -------------------------------------------------
        // Driven by CustomNetworkManager's add/disconnect callbacks rather than by the players
        // themselves, so a player who never finished spawning can't leave a dangling subscription.

        [Server]
        public void ServerRegisterPlayer(PlayerHealth player)
        {
            if (player == null || serverPlayers.Contains(player))
                return;

            serverPlayers.Add(player);

            // Both events fire exactly once on the server: via the SyncVar hook on a host, and via
            // PlayerHealth's explicit isServerOnly path on a dedicated server.
            player.OnDowned += ServerReevaluateDefeat;
            player.OnDeath += ServerReevaluateDefeat;
        }

        [Server]
        public void ServerUnregisterPlayer(PlayerHealth player)
        {
            if (player == null)
                return;

            UnsubscribeFrom(player);

            // A player quitting can itself lose the match — they may have been the last one standing
            // over a field of downed teammates.
            if (serverPlayers.Remove(player))
                ServerReevaluateDefeat();
        }

        private void UnsubscribeFrom(PlayerHealth player)
        {
            if (player == null)
                return;

            player.OnDowned -= ServerReevaluateDefeat;
            player.OnDeath -= ServerReevaluateDefeat;
        }

        // ----- Defeat --------------------------------------------------------

        [Server]
        private void ServerReevaluateDefeat()
        {
            if (!IsMatchActive)
                return;

            // An empty server has not lost anything. Guards the moment between scene load and the
            // first player joining, and the moment the last player disconnects.
            if (serverPlayers.Count == 0)
                return;

            foreach (PlayerHealth player in serverPlayers)
            {
                // Alive means able to fight — and, crucially, able to revive everyone else.
                if (player != null && !player.IsIncapacitated)
                    return;
            }

            ServerEndMatch(MatchResult.Defeat);
        }

        // ----- Victory -------------------------------------------------------

        [ServerCallback]
        private void Update()
        {
            if (!IsMatchActive || winConditions.Length == 0)
                return;

            winCheckTimer -= Time.deltaTime;
            if (winCheckTimer > 0f)
                return;

            winCheckTimer = winCheckInterval;

            if (AreWinConditionsMet())
                ServerEndMatch(MatchResult.Victory);
        }

        [Server]
        private bool AreWinConditionsMet()
        {
            foreach (IWinCondition condition in winConditions)
            {
                if (condition.IsMet)
                {
                    if (!requireAllWinConditions)
                        return true;
                }
                else if (requireAllWinConditions)
                {
                    return false;
                }
            }

            return requireAllWinConditions;
        }

        // ----- Transition ----------------------------------------------------

        /// <summary>
        /// Ends the match. Public so an objective can force an outcome without registering as a
        /// polled win condition. Ignored once the match is already over — the first result sticks.
        /// </summary>
        [Server]
        public void ServerEndMatch(MatchResult result)
        {
            if (result == MatchResult.InProgress || !IsMatchActive)
                return;

            matchResult = result;

            // The hook covers clients and the host; a dedicated server has no hook of its own.
            if (isServerOnly)
                OnMatchEnded?.Invoke(result);

            Debug.Log($"[GameStateManager] Match over: {result}");
        }

        /// <summary>Inspector/UnityEvent-friendly shorthand for <see cref="ServerEndMatch"/>.</summary>
        [Server]
        public void ServerTriggerVictory() => ServerEndMatch(MatchResult.Victory);

        [Server]
        public void ServerTriggerDefeat() => ServerEndMatch(MatchResult.Defeat);

        /// <summary>
        /// Reloads the active scene for every connected client. This object is a scene object, so it
        /// is destroyed and rebuilt too — the roster and the result reset themselves.
        /// </summary>
        [Server]
        public void ServerRestartMatch()
        {
            NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name);
        }

        private void OnMatchResultChangedHook(MatchResult oldResult, MatchResult newResult)
        {
            if (newResult == MatchResult.InProgress)
                return;

            OnMatchEnded?.Invoke(newResult);
        }
    }
}
