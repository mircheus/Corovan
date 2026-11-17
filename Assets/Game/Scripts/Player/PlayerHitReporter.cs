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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterIgnoreHit()
        {
            Firearm.IgnoreHit = HitsShootersOwnVehicle;
        }

        // Owner side (FPSF hook): bullets fly through the car their shooter is riding in, so they reach
        // whatever is behind it. Only colliders that resolve to the car itself — not something else
        // mounted on it with its own receiver.
        private static bool HitsShootersOwnVehicle(Firearm firearm, Collider hit)
        {
            if (firearm == null || hit == null)
                return false;

            PlayerInputHandler input = firearm.GetComponentInParent<PlayerInputHandler>();
            CarController car = input != null ? input.OccupiedVehicle : null;
            return car != null && ReferenceEquals(hit.GetComponentInParent<IDamageReceiver>(), car);
        }

        /// <summary>Owner side: a weapon of ours hit this receiver.</summary>
        public void ReportHit(IDamageReceiver receiver, int damage, Vector3 force)
        {
            if (!(receiver is NetworkBehaviour target))
                return;

            if (isServer)
            {
                if (!ServerRidesIn(target))
                    ServerApplyHit(receiver, damage, force);
            }
            else
                CmdReportHit(target, damage, force);
        }

        [Command]
        private void CmdReportHit(NetworkBehaviour target, int damage, Vector3 force)
        {
            if (target == null || !(target is IDamageReceiver receiver))
                return;

            // Incapacitated players cannot shoot; one's own bullets never hit oneself or the car one rides in.
            if (health.IsIncapacitated || target.gameObject == gameObject || ServerRidesIn(target))
                return;

            if (Vector3.Distance(transform.position, target.transform.position) > maxReportDistance)
                return;

            ServerApplyHit(receiver, Mathf.Clamp(damage, 0, maxDamagePerHit), force);
        }

        // Server view: this player sits in the target car, in the driver seat or a passenger seat.
        [Server]
        private bool ServerRidesIn(NetworkBehaviour target)
        {
            if (!(target is CarController car))
                return false;
            if (car.DriverNetId == netId)
                return true;

            foreach (CarPassengerSeat seat in car.GetComponents<CarPassengerSeat>())
            {
                if (seat.PassengerNetId == netId)
                    return true;
            }
            return false;
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

            if (applied && receiver is Component hitComponent)
            {
                foreach (IAttackerListener listener in hitComponent.GetComponents<IAttackerListener>())
                    listener.ServerOnHitBy(gameObject);
            }

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
