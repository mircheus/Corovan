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

        private static readonly Collider[] overlapBuffer = new Collider[16];
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
            if (!NetworkServer.active || !AnyTurretIdle())
                return;

            var ph = other.GetComponentInParent<PlayerHealth>();
            if (ph != null && ph.IsTargetable)
            {
                AssignToIdleTurrets(ph);
                return;
            }

            var car = other.GetComponentInParent<CarController>();
            if (car != null && car.HasDriver && car.VehicleIsAlive)
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
            if (!NetworkServer.active || detectionCollider == null || !AnyTurretIdle())
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

            for (int i = 0; i < count; i++)
            {
                var ph = overlapBuffer[i].GetComponentInParent<PlayerHealth>();
                if (ph != null && ph.IsTargetable && IsInsideDetection(ph.transform.position))
                {
                    AssignToIdleTurrets(ph);
                    return;
                }
            }

            for (int i = 0; i < count; i++)
            {
                var car = overlapBuffer[i].GetComponentInParent<CarController>();
                if (car != null && car.HasDriver && car.VehicleIsAlive && IsInsideDetection(car.transform.position))
                {
                    AssignToIdleTurrets(car);
                    return;
                }
            }
        }

        private void AssignToIdleTurrets(PlayerHealth player)
        {
            foreach (var turret in turrets)
            {
                if (turret != null && turret.IsAlive && !turret.HasTarget)
                    turret.ServerAcquireTarget(player);
            }
        }

        private void AssignToIdleTurrets(CarController car)
        {
            foreach (var turret in turrets)
            {
                if (turret != null && turret.IsAlive && !turret.HasTarget)
                    turret.ServerAcquireTarget(car);
            }
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

        private bool AnyTurretIdle()
        {
            foreach (var turret in turrets)
            {
                if (turret != null && turret.IsAlive && !turret.HasTarget)
                    return true;
            }
            return false;
        }

        private bool IsInsideDetection(Vector3 worldPosition)
        {
            return (detectionCollider.ClosestPoint(worldPosition) - worldPosition).sqrMagnitude < 0.01f;
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
