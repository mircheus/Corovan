using Mirror;
using UnityEngine;
using FPSGame.Hazards;

namespace FPSGame.Environment
{
    /// <summary>
    /// Unarmed enemy car tied to an <see cref="ObstacleLayerZone"/> (a branch of <see cref="ObstacleLayerCarBehaviour"/>):
    /// <list type="number">
    /// <item>it drives to the zone's attack point and drops obstacles from there;</item>
    /// <item>it attacks while a driven player car is inside the zone's collider — damage doesn't interrupt it;</item>
    /// <item>with no player car in the zone (for <c>leaveDelay</c> s) it drives to the safe point, waits <c>waitTime</c> s
    ///   and drives back to the attack point;</item>
    /// <item>a player car entering the zone while the car retreats or waits sends it straight back to the attack point.</item>
    /// </list>
    /// The controller's target doesn't matter: the zone alone decides, so ServerTick and ServerIdle do the same.
    /// Moves along NavMesh paths (the points are fixed), unlike the original's per-frame <c>agent.Move</c>.
    /// </summary>
    public class ObstacleLayerZoneCarBehaviour : EnemyCarBehaviour
    {
        private enum State { ToAttack, Attacking, ToSafe, Waiting }

        [Header("Zone")]
        [Tooltip("Scene zone with the attack point, safe point and trigger. Empty = the nearest one in the scene when the " +
                 "car appears on the server (spawned prefabs can't hold scene references).")]
        [SerializeField] private ObstacleLayerZone zone;
        [Tooltip("Seconds between two checks of the zone for a player car.")]
        [SerializeField, Min(0f)] private float zoneCheckInterval = 0.2f;

        [Header("Movement")]
        [Tooltip("Agent speed (m/s) on the way to the attack point.")]
        [SerializeField, Min(0.1f)] private float attackMoveSpeed = 14f;
        [Tooltip("Agent speed (m/s) on the way to the safe point.")]
        [SerializeField, Min(0.1f)] private float retreatSpeed = 12f;
        [Tooltip("Flat distance (m) to a point that counts as arrived.")]
        [SerializeField, Min(0.1f)] private float arriveDistance = 1.5f;
        [Tooltip("Turn (deg/s) to the point's forward while standing on it. 0 = keep whatever heading the car arrived with.")]
        [SerializeField, Min(0f)] private float alignTurnSpeed = 180f;

        [Header("Timing")]
        [Tooltip("Seconds the zone must stay empty before the car leaves the attack point (also covers a car " +
                 "jittering on the zone's edge, and how long it stays after coming back to an empty zone).")]
        [SerializeField, Min(0f)] private float leaveDelay = 1f;
        [Tooltip("Seconds the car waits at the safe point before it drives back to the attack point.")]
        [SerializeField, Min(0f)] private float waitTime = 5f;

        [Header("Obstacles")]
        [Tooltip("Networked prefab (NetworkIdentity on its root), registered in CustomNetworkManager.runtimeSpawnPrefabs. " +
                 "Spawned facing world -Z, so a flying obstacle travels back toward the player.")]
        [SerializeField] private GameObject obstaclePrefab;
        [Tooltip("Where obstacles appear (at the car's rear). Empty = Rear Offset behind the car's pivot.")]
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private float rearOffset = 4f;
        [SerializeField] private float spawnHeightOffset = 0f;
        [Tooltip("Flight speed of each spawned obstacle (m/s), along world -Z. Overrides the speed set on the obstacle prefab.")]
        [SerializeField, Min(0f)] private float obstacleSpeed = 15f;
        [Tooltip("Seconds between two obstacles.")]
        [SerializeField, Min(0.1f)] private float spawnInterval = 2f;
        [Tooltip("Seconds from reaching the attack point (with a player in the zone) to the first obstacle.")]
        [SerializeField, Min(0f)] private float firstSpawnDelay = 0.5f;
        [Tooltip("Each obstacle is shifted on X by a random value in ±this around the spawn position.")]
        [SerializeField, Min(0f)] private float spawnSpreadX = 4f;

        private State state = State.ToAttack;
        private bool destinationIssued;
        private bool playerInZone;
        private float nextZoneCheck;
        private float lastPlayerSeenTime;
        private float waitEndTime;
        private double nextSpawnTime;
        private bool missingZoneLogged;

        /// <summary>Server: the zone this car works in (e.g. from its spawner). Restarts it from the approach.</summary>
        public void SetZone(ObstacleLayerZone value)
        {
            zone = value;
            missingZoneLogged = false;
            Enter(State.ToAttack);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            state = State.ToAttack;
            destinationIssued = false;
        }

        public override void ServerTick() => Tick();
        public override void ServerIdle() => Tick();

        private void Tick()
        {
            if (!ResolveZone())
                return;

            UpdatePlayerInZone();

            switch (state)
            {
                case State.ToAttack:
                    if (Drive(zone.AttackPoint, attackMoveSpeed))
                        Enter(State.Attacking);
                    break;

                case State.Attacking:
                    Align(zone.AttackPoint);
                    if (playerInZone)
                    {
                        lastPlayerSeenTime = Time.time;
                        TrySpawnObstacle();
                    }
                    else
                    {
                        // Keep the first obstacle a full delay away once a player shows up again.
                        nextSpawnTime = NetworkTime.time + firstSpawnDelay;
                        if (Time.time - lastPlayerSeenTime >= leaveDelay)
                            Enter(State.ToSafe);
                    }
                    break;

                case State.ToSafe:
                    if (playerInZone)
                        Enter(State.ToAttack);
                    else if (Drive(zone.SafePoint, retreatSpeed))
                        Enter(State.Waiting);
                    break;

                case State.Waiting:
                    Align(zone.SafePoint);
                    if (playerInZone || Time.time >= waitEndTime)
                        Enter(State.ToAttack);
                    break;
            }
        }

        private void Enter(State next)
        {
            state = next;
            destinationIssued = false;
            switch (next)
            {
                case State.Attacking:
                    lastPlayerSeenTime = Time.time;
                    nextSpawnTime = NetworkTime.time + firstSpawnDelay;
                    break;
                case State.Waiting:
                    waitEndTime = Time.time + waitTime;
                    break;
            }
        }

        private bool ResolveZone()
        {
            if (zone == null)
                zone = ObstacleLayerZone.FindNearest(transform.position);
            if (zone != null && zone.IsValid)
                return true;

            if (!missingZoneLogged)
            {
                missingZoneLogged = true;
                Debug.LogError($"[EnemyCar] {name}: no valid ObstacleLayerZone (attack point, safe point, collider) — the car stays put.", this);
            }
            return false;
        }

        private void UpdatePlayerInZone()
        {
            if (Time.time < nextZoneCheck)
                return;
            nextZoneCheck = Time.time + zoneCheckInterval;
            playerInZone = zone.HasPlayerCarInside();
        }

        // Paths to the point; true once the car stands on it (the path is then dropped so the agent stops).
        private bool Drive(Transform point, float speed)
        {
            var agent = Car.Agent;
            if (!agent.isOnNavMesh)
                return false;

            Vector3 offset = point.position - transform.position;
            offset.y = 0f;
            bool arrived = offset.sqrMagnitude <= arriveDistance * arriveDistance
                           || (destinationIssued && !agent.pathPending && agent.hasPath
                               && agent.remainingDistance <= arriveDistance);
            if (arrived)
            {
                if (agent.hasPath || agent.pathPending)
                    agent.ResetPath();
                return true;
            }

            // (Re)issue on entering the state, or if the agent lost its path on the way.
            if (!destinationIssued || (!agent.pathPending && !agent.hasPath))
            {
                agent.speed = speed;
                agent.stoppingDistance = arriveDistance * 0.5f;
                agent.autoBraking = true;
                agent.SetDestination(point.position);
                destinationIssued = true;
            }
            return false;
        }

        private void Align(Transform point)
        {
            if (alignTurnSpeed <= 0f)
                return;
            Vector3 forward = point.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return;
            Quaternion wanted = Quaternion.LookRotation(forward, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, alignTurnSpeed * Time.deltaTime);
        }

        private void TrySpawnObstacle()
        {
            if (NetworkTime.time < nextSpawnTime)
                return;
            nextSpawnTime = NetworkTime.time + spawnInterval;

            if (obstaclePrefab == null)
            {
                Debug.LogError($"[EnemyCar] {name}: obstaclePrefab is not assigned!", this);
                return;
            }

            Vector3 origin = SpawnOrigin();
            origin.x += Random.Range(-spawnSpreadX, spawnSpreadX);

            GameObject obstacle = Instantiate(obstaclePrefab, origin, Quaternion.LookRotation(Vector3.back));
            if (obstacle.TryGetComponent(out CarEjectionTrigger flight))
                flight.SetSpeed(obstacleSpeed);
            else
                Debug.LogWarning($"[EnemyCar] {name}: obstacle prefab has no CarEjectionTrigger, Obstacle Speed is ignored.", this);

            NetworkServer.Spawn(obstacle);
        }

        private Vector3 SpawnOrigin()
        {
            Vector3 origin = spawnPoint != null
                ? spawnPoint.position
                : transform.position - transform.forward * rearOffset;
            origin.y += spawnHeightOffset;
            return origin;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
            Vector3 origin = SpawnOrigin();
            Gizmos.DrawLine(origin + Vector3.left * spawnSpreadX, origin + Vector3.right * spawnSpreadX);

            if (zone != null && zone.AttackPoint != null)
            {
                Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.6f);
                Gizmos.DrawLine(transform.position, zone.AttackPoint.position);
            }
        }
    }
}
