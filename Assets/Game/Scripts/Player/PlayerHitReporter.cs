using UnityEngine;
using Mirror;
using FPSGame.Combat;
using FPSGame.Vehicle;
using Akila.FPSFramework;

namespace FPSGame.Player
{
    /// <summary>
    /// Carries this player's weapon hits to the server and applies the friendly-fire rules.
    /// Hits are client-authoritative (co-op among friends): the shooter's client decides what it hit,
    /// the server only checks that the report is plausible before applying it.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public class PlayerHitReporter : NetworkBehaviour
    {
        [Header("Friendly fire")]
        [Tooltip("Damage multiplier when this player hits another player.")]
        [SerializeField] private float teammateDamageMultiplier = 1f;
        [Tooltip("Damage multiplier when this player hits a vehicle (the convoy car).")]
        [SerializeField] private float vehicleDamageMultiplier = 1f;

        [Header("Report validation (client hits)")]
        [Tooltip("A reported hit farther than this from the shooter is rejected.")]
        [SerializeField] private float maxReportDistance = 300f;
        [Tooltip("A single reported hit is clamped to this much damage.")]
        [SerializeField] private int maxDamagePerHit = 500;

        private PlayerHealth health;

        /// <summary>The local player's reporter (null until it spawns).</summary>
        public static PlayerHitReporter Local { get; private set; }

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            Local = this;
        }

        public override void OnStopLocalPlayer()
        {
            base.OnStopLocalPlayer();
            if (Local == this)
                Local = null;
        }

        /// <summary>Owner side: a weapon of ours hit this receiver.</summary>
        public void ReportHit(IDamageReceiver receiver, int damage, Vector3 force)
        {
            if (!(receiver is NetworkBehaviour target))
                return;

            if (isServer)
                ServerApplyHit(receiver, damage, force);
            else
                CmdReportHit(target, damage, force);
        }

        [Command]
        private void CmdReportHit(NetworkBehaviour target, int damage, Vector3 force)
        {
            if (target == null || !(target is IDamageReceiver receiver))
                return;

            // Incapacitated players cannot shoot; one's own bullets never hit oneself.
            if (health.IsIncapacitated || target.gameObject == gameObject)
                return;

            if (Vector3.Distance(transform.position, target.transform.position) > maxReportDistance)
                return;

            ServerApplyHit(receiver, Mathf.Clamp(damage, 0, maxDamagePerHit), force);
        }

        /// <summary>Server side: apply a hit by this player, with friendly-fire multipliers.</summary>
        [Server]
        public bool ServerApplyHit(IDamageReceiver receiver, int damage, Vector3 force)
        {
            float multiplier = 1f;
            if (receiver is PlayerHealth)
                multiplier = teammateDamageMultiplier;
            else if (receiver is CarController)
                multiplier = vehicleDamageMultiplier;

            bool wasAlive = !receiver.IsDead;
            bool applied = DamageRouter.ServerApplyHit(receiver, Mathf.RoundToInt(damage * multiplier), force);

            // The shooter's own hit marker fires on the spot; the kill highlight waits for the server.
            if (applied && wasAlive && receiver.IsDead && connectionToClient != null)
                TargetKillConfirmed(connectionToClient);
            return applied;
        }

        [TargetRpc]
        private void TargetKillConfirmed(NetworkConnectionToClient target)
        {
            Hitmarker marker = UIManager.Instance != null ? UIManager.Instance.Hitmarker : null;
            if (marker != null)
                marker.Enable(true, true, marker.maxSize);
        }
    }
}
