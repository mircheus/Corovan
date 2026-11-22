using Mirror;
using UnityEngine;
using FPSGame.Environment;
using FPSGame.Networking;
using FPSGame.UI;

namespace FPSGame.Player
{
    // Owner-side detector for FallHazardVolumes. The convoy's swerve can shove the player off the
    // platform; dropping into a hazard volume is a crash onto the road: carried loot is knocked loose,
    // the body is frozen where it fell, the screen fades to black with the volume's message, and the
    // server (PlayerHealth.ServerCrash) takes the fall damage and keeps the player out for
    // deadRespawnDelay. The respawn then puts them in a teammate's car or at a PlayerRespawnPoint
    // (PlayerInputHandler.ServerRelocateAfterRespawn) and the screen fades back in.
    //
    // Detection runs on the owning client because only the owner runs the CharacterController that
    // generates trigger events (remote copies are moved by NetworkTransform and fire no triggers).
    // The damage and the state change are the server's, via Command.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerFallHazard : NetworkBehaviour
    {
        [Header("Retrigger")]
        [Tooltip("Seconds of immunity after a fall, and after the respawn that ends it, so one gap can't chain-trigger.")]
        [SerializeField] private float cooldown = 1f;

        [Header("Crash screen")]
        [Tooltip("Seconds the screen takes to go black after the fall, and to come back after the respawn.")]
        [SerializeField, Min(0f)] private float fadeDuration = 0.6f;
        [Tooltip("{0} is replaced by the whole seconds left before the respawn.")]
        [SerializeField] private string countdownFormat = "Возвращение через {0}";

        private PlayerController controller;
        private PlayerCarry carry;
        private PlayerHealth health;
        private RoadCrashOverlay overlay;

        private float cooldownTimer;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            carry = GetComponent<PlayerCarry>();
            health = GetComponent<PlayerHealth>();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            if (health != null)
                health.OnRespawn += OnRespawned;
            if (GameStateManager.Instance != null)
                GameStateManager.Instance.OnMatchEnded += OnMatchEnded;
        }

        private void OnDestroy()
        {
            if (health != null)
                health.OnRespawn -= OnRespawned;
            if (GameStateManager.Instance != null)
                GameStateManager.Instance.OnMatchEnded -= OnMatchEnded;
            if (overlay != null)
                Destroy(overlay.gameObject);
        }

        private void Update()
        {
            if (!isOwned)
                return;

            if (cooldownTimer > 0f)
                cooldownTimer -= Time.deltaTime;

            if (overlay != null && overlay.IsShown && health != null)
            {
                overlay.SetCountdown(health.RespawnPending
                    ? string.Format(countdownFormat, Mathf.CeilToInt(health.RespawnRemaining))
                    : string.Empty);
            }
        }

        // Fires on the owner: the moving CharacterController generates the trigger event.
        private void OnTriggerEnter(Collider other)
        {
            if (!isOwned || cooldownTimer > 0f || health == null || health.IsCrashed)
                return;

            FallHazardVolume hazard = other.GetComponentInParent<FallHazardVolume>();
            if (hazard == null)
                return;

            cooldownTimer = cooldown;

            // Knock the loot loose (owner-side; replicates the release through PlayerCarry) and hold
            // the body here until the respawn moves it — otherwise it falls forever.
            carry?.ForceDrop();
            controller.SetFrozen(true);

            // A body that is already dead (e.g. thrown out of an exploding car) only stops falling;
            // its own respawn is already scheduled. Once the match is decided the end screen owns the view.
            bool matchOver = GameStateManager.Instance != null && GameStateManager.Instance.IsMatchOver;
            if (health.IsDead || matchOver)
                return;

            GetOverlay().Show(hazard.CrashMessage, fadeDuration);
            CmdCrash(hazard.InstantKill, hazard.FallDamage);
        }

        // Back in the game (Dead / Crashed -> Alive). PlayerInputHandler unfreezes the body; on a host
        // that happens before the move RPC, at the fall spot, still inside the volume — the cooldown
        // keeps that from counting as a new fall.
        private void OnRespawned()
        {
            cooldownTimer = cooldown;
            controller.SetFrozen(false);
            if (overlay != null)
                overlay.Hide(fadeDuration);
        }

        private void OnMatchEnded(MatchResult result)
        {
            if (overlay != null)
                overlay.Hide(0f);
        }

        private RoadCrashOverlay GetOverlay()
        {
            if (overlay == null)
                overlay = RoadCrashOverlay.Create();
            return overlay;
        }

        // Detection is owner-side (matching this project's owner-authoritative movement), so the
        // penalty values are supplied by the client. Clamp to a sane range so a bad value can't
        // do anything the volume itself couldn't.
        [Command]
        private void CmdCrash(bool instantKill, int damage)
        {
            if (health == null)
                return;

            health.ServerCrash(Mathf.Clamp(damage, 0, health.MaxHealth), instantKill);
        }
    }
}
