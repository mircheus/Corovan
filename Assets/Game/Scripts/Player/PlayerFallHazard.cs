using Mirror;
using UnityEngine;
using FPSGame.Environment;

namespace FPSGame.Player
{
    // Owner-side detector for FallHazardVolumes. The convoy's swerve can shove the player off the
    // platform; dropping into a hazard volume costs health, knocks carried loot loose, and snaps
    // the player back to the last point where they stood safely on the convoy.
    //
    // Detection runs on the owning client because only the owner runs the CharacterController that
    // generates trigger events (remote copies are moved by NetworkTransform and fire no triggers).
    // The teleport is applied owner-side (the owner is authoritative over its own transform); the
    // health penalty is applied on the server via Command.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerFallHazard : NetworkBehaviour
    {
        // Where a fall sends the player back to.
        //   LastSafePoint : the last spot they stood safely on the convoy (dynamic).
        //   FixedPoint    : a single scene marker (MotionDirector.FallRespawnPoint).
        public enum RecoveryMode { LastSafePoint, FixedPoint }

        [Header("Recovery")]
        [Tooltip("LastSafePoint = last safe spot on the platform. FixedPoint = the MotionDirector's " +
                 "Fall Respawn Point marker (falls back to last safe spot if that isn't assigned).")]
        [SerializeField] private RecoveryMode recoveryMode = RecoveryMode.LastSafePoint;

        [Header("Safe point tracking")]
        [Tooltip("Vertical offset added above the recorded safe point when respawning.")]
        [SerializeField] private float respawnHeightOffset = 0.5f;
        [Tooltip("Minimum movement before the safe point is re-sampled (metres).")]
        [SerializeField] private float sampleDistance = 0.5f;

        [Header("Retrigger")]
        [Tooltip("Seconds of immunity after a fall so one gap can't chain-trigger.")]
        [SerializeField] private float cooldown = 1f;

        private PlayerController controller;
        private PlayerCarry carry;
        private PlayerHealth health;

        private Vector3 lastSafePoint;
        private bool hasSafePoint;
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
            // Seed with the spawn position so a fall before ever standing on the platform still
            // has somewhere to send the player.
            lastSafePoint = transform.position;
            hasSafePoint = true;
        }

        private void Update()
        {
            if (!isOwned)
                return;

            if (cooldownTimer > 0f)
                cooldownTimer -= Time.deltaTime;

            // Only record a safe point while genuinely standing on the convoy platform, so recovery
            // never sends the player back into the air or off the edge they just fell from.
            if (controller.OnMotionPlatform && controller.IsGrounded)
            {
                if (!hasSafePoint ||
                    (transform.position - lastSafePoint).sqrMagnitude >= sampleDistance * sampleDistance)
                {
                    lastSafePoint = transform.position;
                    hasSafePoint = true;
                }
            }
        }

        // Fires on the owner: the moving CharacterController generates the trigger event.
        private void OnTriggerEnter(Collider other)
        {
            if (!isOwned || cooldownTimer > 0f)
                return;

            FallHazardVolume hazard = other.GetComponentInParent<FallHazardVolume>();
            if (hazard == null)
                return;

            cooldownTimer = cooldown;

            // Knock the loot loose (owner-side; replicates the release through PlayerCarry).
            carry?.ForceDrop();

            // Resolve the recovery spot, then snap back. The owner owns its transform, so this
            // replicates to everyone else via NetworkTransform.
            if (TryGetRecoveryPoint(out Vector3 target))
                controller.TeleportTo(target + Vector3.up * respawnHeightOffset);

            // Apply the health cost on the server.
            CmdApplyFallPenalty(hazard.InstantKill, hazard.FallDamage);
        }

        // Picks the teleport target for the current recovery mode. FixedPoint uses the shared scene
        // marker on MotionDirector, falling back to the last safe spot when it isn't assigned.
        private bool TryGetRecoveryPoint(out Vector3 point)
        {
            if (recoveryMode == RecoveryMode.FixedPoint)
            {
                MotionDirector director = MotionDirector.Instance;
                if (director != null && director.FallRespawnPoint != null)
                {
                    point = director.FallRespawnPoint.position;
                    return true;
                }
            }

            point = lastSafePoint;
            return hasSafePoint;
        }

        // Detection is owner-side (matching this project's owner-authoritative movement), so the
        // penalty values are supplied by the client. Clamp to a sane range so a bad value can't
        // do anything the volume itself couldn't.
        [Command]
        private void CmdApplyFallPenalty(bool instantKill, int damage)
        {
            if (health == null)
                return;

            if (instantKill)
                health.ServerKillInstant();
            else
                health.TakeDamage(Mathf.Clamp(damage, 0, health.MaxHealth));
        }
    }
}
