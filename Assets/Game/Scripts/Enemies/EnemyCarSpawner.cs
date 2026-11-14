using Mirror;
using UnityEngine;
using UnityEngine.AI;
using FPSGame.Environment;
using FPSGame.Vehicle;

namespace FPSGame.Enemies
{
    /// <summary>
    /// Spawns enemy cars (<see cref="EnemyCarController"/> prefabs — gunner, obstacle layer, …). The loop,
    /// activation modes, alive cap and director requests live in <see cref="EnemySpawner"/>.
    ///
    /// Differences from the on-foot spawners: spawn points snap onto the NavMesh of the prefab's own agent
    /// type (VehicleAgent), the WhilePlayersInZone gate and aggro look for driven, alive player cars instead of
    /// players on foot, and aggro registers the car with <see cref="EnemyCarController.RegisterTarget"/>.
    /// Behaviours that measure from where the car started (ObstacleLayer's initial point) use the spawn point.
    /// Wrecks remove themselves (RoadBody despawn).
    /// </summary>
    public class EnemyCarSpawner : EnemySpawner
    {
        [Header("Enemy car")]
        [SerializeField] private EnemyCarController carPrefab;

        protected override NetworkBehaviour Prefab => carPrefab;
        protected override bool IsEnemyDead(NetworkBehaviour enemy) => ((EnemyCarController)enemy).IsDead;
        protected override Color GizmoColor => new Color(1f, 0.6f, 0.1f, 1f);

        protected override void AggroIfIdle(NetworkBehaviour enemy)
        {
            var car = (EnemyCarController)enemy;
            if (car.HasTarget)
                return;
            CarController target = FindPlayerCar(car.transform.position);
            if (target != null)
                car.RegisterTarget(target);
        }

        protected override bool ZoneGateOpen() => FindPlayerCar(transform.position, zoneOnly: true) != null;

        protected override Vector3 SnapToNavMesh(Vector3 point)
        {
            NavMeshAgent agent = carPrefab != null ? carPrefab.GetComponent<NavMeshAgent>() : null;
            if (agent == null)
                return base.SnapToNavMesh(point);
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
            return NavMesh.SamplePosition(point, out NavMeshHit hit, navSampleRadius, filter) ? hit.position : point;
        }

        // Nearest driven, alive player car: inside activationZone if one is set (or zoneOnly),
        // otherwise within aggroRadius (0 = anywhere).
        private CarController FindPlayerCar(Vector3 nearTo, bool zoneOnly = false)
        {
            bool useZone = zoneOnly || activationZone != null;
            if (useZone && activationZone == null)
                return null;

            CarController nearest = null;
            float bestSqr = !useZone && aggroRadius > 0f ? aggroRadius * aggroRadius : float.MaxValue;
            foreach (CarController car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (!car.VehicleIsAlive || !car.HasDriver)
                    continue;
                Vector3 p = car.transform.position;
                if (useZone && !IsInZone(p))
                    continue;
                float sqr = (p - nearTo).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    nearest = car;
                }
            }
            return nearest;
        }

        private void Reset()
        {
            spawnInterval = 4f;
            maxAlive = 2;
            navSampleRadius = 4f;
            spawnClearance = 8f;
            aggroRadius = 0f;
        }
    }
}
