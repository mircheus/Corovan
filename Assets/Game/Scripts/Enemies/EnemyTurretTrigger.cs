using System.Collections.Generic;
using Mirror;
using UnityEngine;
using FPSGame.Player;
using FPSGame.Vehicle;

namespace FPSGame.Environment
{
    /// <summary>
    /// Shared detection sensor for one or more <see cref="EnemyTurret"/>s. Lives on a dedicated
    /// GameObject whose ONLY collider is the trigger detection volume — that isolation is the whole
    /// point: OnTrigger* messages then arrive exclusively from this collider, never from a turret's
    /// solid body collider or from any other trigger in the scene.
    ///
    /// The sensor owns acquisition entirely and pushes the result to every idle turret via
    /// <see cref="EnemyTurret.ServerAcquireTarget(PlayerHealth)"/> / clear. Each turret keeps its own
    /// target, tracking, aiming and firing — they merely share the same detection volume.
    ///
    /// Turrets may accept different <see cref="TurretTargets"/>, so acquisition is per kind: a player
    /// found in the volume is offered only to the turrets that engage players, and the search for a
    /// vehicle continues for the rest.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class EnemyTurretTrigger : MonoBehaviour
    {
        [Tooltip("Turrets fed by this sensor. If left empty, the nearest EnemyTurret in the parents is used.")]
        [SerializeField] private List<EnemyTurret> turrets = new();

        [Tooltip("The trigger volume. Falls back to the Collider on this GameObject.")]
        [SerializeField] private Collider detectionCollider;

        [Tooltip("If set, turrets keep their target even after it leaves this volume.")]
        [SerializeField] private bool keepAttackAfterTriggerExit;

        [Tooltip("How often the volume is re-swept while any turret has no target. Catches a target " +
                 "that became valid without crossing the boundary — e.g. a revived teammate who never " +
                 "left the volume.")]
        [SerializeField] private float retargetInterval = 0.5f;

        private const TurretTargets AnyKind = TurretTargets.Players | TurretTargets.Vehicles;

        // The sweep queries every layer, so the buffer competes with road tiles, walls and props: too
        // small and the car parked in the volume simply never appears in the results.
        private static readonly Collider[] overlapBuffer = new Collider[64];
        private float retargetTimer;

        private void Awake()
        {
            if (detectionCollider == null)
                detectionCollider = GetComponent<Collider>();
            if (detectionCollider != null)
                detectionCollider.isTrigger = true;
            if (turrets.Count == 0)
            {
                var parentTurret = GetComponentInParent<EnemyTurret>();
                if (parentTurret != null)
                    turrets.Add(parentTurret);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Detection is a server-authoritative decision; clients only render the result.
            if (!NetworkServer.active || !AnyIdleTurret(AnyKind))
                return;

            // Validity is the turret's call (EnemyTurret.CanEngage) — the sensor only offers
            // candidates. Fall through to the vehicle check when the player was found but nobody took
            // it: an anti-vehicle turret must still react to the car this collider may belong to.
            var ph = other.GetComponentInParent<PlayerHealth>();
            if (ph != null && AssignToIdleTurrets(ph))
                return;

            var car = other.GetComponentInParent<CarController>();
            if (car != null)
                AssignToIdleTurrets(car);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!NetworkServer.active || keepAttackAfterTriggerExit)
                return;

            var ph = other.GetComponentInParent<PlayerHealth>();
            if (ph != null)
            {
                ClearTurretsTargeting(ph, null);
                return;
            }

            var car = other.GetComponentInParent<CarController>();
            if (car != null)
                ClearTurretsTargeting(null, car);
        }

        private void Update()
        {
            // OnTriggerEnter only fires on a boundary crossing. A player who went down inside the
            // volume stops being a target, and once revived never re-enters — so re-sweep while any
            // turret is idle to re-acquire them.
            if (!NetworkServer.active || detectionCollider == null || !AnyIdleTurret(AnyKind))
                return;

            retargetTimer -= Time.deltaTime;
            if (retargetTimer > 0f)
                return;

            retargetTimer = retargetInterval;
            Resweep();
        }

        private void Resweep()
        {
            // The AABB is a superset of the trigger shape; IsInsideDetection then rejects anything
            // inside the box but outside the actual collider.
            Bounds bounds = detectionCollider.bounds;
            int count = Physics.OverlapBoxNonAlloc(
                bounds.center, bounds.extents, overlapBuffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

            // Both passes always run: filling the player-hunting turrets says nothing about the
            // anti-vehicle ones, which still need a car of their own.
            bool wantPlayer = AnyIdleTurret(TurretTargets.Players);
            bool wantCar = AnyIdleTurret(TurretTargets.Vehicles);

            for (int i = 0; i < count && wantPlayer; i++)
            {
                var ph = overlapBuffer[i].GetComponentInParent<PlayerHealth>();
                if (ph != null && IsInsideDetection(overlapBuffer[i]) && AssignToIdleTurrets(ph))
                    wantPlayer = false;
            }

            for (int i = 0; i < count && wantCar; i++)
            {
                var car = overlapBuffer[i].GetComponentInParent<CarController>();
                if (car != null && IsInsideDetection(overlapBuffer[i]) && AssignToIdleTurrets(car))
                    wantCar = false;
            }
        }

        // Returns true if at least one turret took the target, so callers can tell "nobody engages
        // this kind" apart from "assigned".
        private bool AssignToIdleTurrets(PlayerHealth player)
        {
            bool assigned = false;
            foreach (var turret in turrets)
            {
                if (turret != null && turret.IsAlive && !turret.HasTarget && turret.CanEngage(player))
                {
                    turret.ServerAcquireTarget(player);
                    assigned = true;
                }
            }
            return assigned;
        }

        private bool AssignToIdleTurrets(CarController car)
        {
            bool assigned = false;
            foreach (var turret in turrets)
            {
                if (turret != null && turret.IsAlive && !turret.HasTarget && turret.CanEngage(car))
                {
                    turret.ServerAcquireTarget(car);
                    assigned = true;
                }
            }
            return assigned;
        }

        // Clears only the turrets currently locked onto the object that left the volume, so a turret
        // aiming at a different target in the shared zone is left untouched.
        private void ClearTurretsTargeting(PlayerHealth player, CarController car)
        {
            foreach (var turret in turrets)
            {
                if (turret == null)
                    continue;
                if ((player != null && turret.CurrentPlayerTarget == player) ||
                    (car != null && turret.CurrentCarTarget == car))
                    turret.ServerClearTarget();
            }
        }

        // kind may be a mask: AnyKind answers "is any turret idle at all".
        private bool AnyIdleTurret(TurretTargets kind)
        {
            foreach (var turret in turrets)
            {
                if (turret != null && turret.IsAlive && !turret.HasTarget && turret.Targets(kind))
                    return true;
            }
            return false;
        }

        // Tests the found collider itself, not its owner's pivot: a car is metres long and its root
        // transform is regularly outside a volume its body is sitting in. Takes the point on that
        // collider nearest the volume's centre and asks whether the volume contains it.
        private bool IsInsideDetection(Collider candidate)
        {
            Vector3 nearest = candidate.ClosestPoint(detectionCollider.bounds.center);
            return (detectionCollider.ClosestPoint(nearest) - nearest).sqrMagnitude < 0.01f;
        }

        private void OnDrawGizmosSelected()
        {
            var col = detectionCollider != null ? detectionCollider : GetComponent<Collider>();
            if (col == null)
                return;

            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 1f);
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
    }
}
