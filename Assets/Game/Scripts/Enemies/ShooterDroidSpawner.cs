using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
using FPSGame.Player;

namespace FPSGame.Enemies
{
    /// <summary>
    /// Server-side spawner for <see cref="ShooterDroid"/>s. Keeps up to <see cref="maxAlive"/> living
    /// droids from this spawner, optionally capped by <see cref="totalToSpawn"/> for the whole match.
    ///
    /// Activation:
    ///   • OnStart — spawns from the moment the server starts.
    ///   • WhilePlayersInZone — spawns only while a player on foot stands inside activationZone.
    ///   • Manual — waits for ServerStartSpawning / ServerStopSpawning, e.g. from an
    ///     InteractableObject's UnityEvent. Those events fire on every client, so both calls are
    ///     silent no-ops anywhere but the server.
    ///
    /// The droid prefab must be in CustomNetworkManager.runtimeSpawnPrefabs. Corpses are removed by
    /// RagdollEnemy itself (respawnDelay 0 → destroyDelay).
    /// </summary>
    public class ShooterDroidSpawner : NetworkBehaviour
    {
        public enum Activation { OnStart, WhilePlayersInZone, Manual }

        [Header("Droid")]
        [SerializeField] private ShooterDroid droidPrefab;

        [Header("Activation")]
        [SerializeField] private Activation activation = Activation.WhilePlayersInZone;
        [Tooltip("Zone for WhilePlayersInZone, and for aggroOnSpawn. Any collider shape; it is only queried, " +
                 "so it can be a trigger on any object.")]
        [SerializeField] private Collider activationZone;

        [Header("Spawning")]
        [Tooltip("Seconds before the first spawn once spawning starts.")]
        [SerializeField] private float initialDelay = 0f;
        [Tooltip("Seconds between spawns while below the alive cap.")]
        [SerializeField] private float spawnInterval = 3f;
        [Tooltip("Max living droids from this spawner at once. Dead ones free their slot.")]
        [SerializeField] private int maxAlive = 4;
        [Tooltip("Droids this spawner may ever create. 0 = unlimited.")]
        [SerializeField] private int totalToSpawn = 0;
        [Tooltip("Spawn points (random pick). Falls back to this spawner's own transform.")]
        [SerializeField] private Transform[] spawnPoints;
        [Tooltip("Radius used to snap a chosen point onto the baked NavMesh.")]
        [SerializeField] private float navSampleRadius = 2f;
        [Tooltip("A point with a living droid closer than this is busy; the spawn waits for a free one. " +
                 "Droids stacked on one spot shoot straight into each other.")]
        [SerializeField] private float spawnClearance = 1.2f;

        [Header("Aggro")]
        [Tooltip("Hand this spawner's droids the nearest player on foot, so they engage at once instead of " +
                 "waiting to see someone. Checked on spawn and on every spawn tick for droids left idle " +
                 "(an OnStart wave spawns before anyone has joined). The player must be inside " +
                 "activationZone if one is set, otherwise within aggroRadius of the droid.")]
        [SerializeField] private bool aggroOnSpawn = true;
        [Tooltip("Without an activationZone: how far from the droid a player may be. 0 = anywhere.")]
        [SerializeField] private float aggroRadius = 30f;

        private readonly List<ShooterDroid> alive = new List<ShooterDroid>();
        private int spawnedTotal;
        private Coroutine spawnRoutine;

        public bool IsSpawning => spawnRoutine != null;
        public bool IsExhausted => totalToSpawn > 0 && spawnedTotal >= totalToSpawn;

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (droidPrefab == null)
                Debug.LogWarning($"[ShooterDroidSpawner] {name}: droidPrefab is not assigned.", this);
            if (activation == Activation.WhilePlayersInZone && activationZone == null)
                Debug.LogWarning($"[ShooterDroidSpawner] {name}: WhilePlayersInZone needs an activationZone.", this);

            if (activation != Activation.Manual)
                ServerStartSpawning();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ServerStopSpawning();
        }

        /// <summary>Start the spawn loop. Safe to call from a UnityEvent on any peer.</summary>
        public void ServerStartSpawning()
        {
            if (!NetworkServer.active || spawnRoutine != null)
                return;
            spawnRoutine = StartCoroutine(SpawnLoop());
        }

        /// <summary>Stop the spawn loop; living droids keep fighting. Safe to call on any peer.</summary>
        public void ServerStopSpawning()
        {
            if (!NetworkServer.active || spawnRoutine == null)
                return;
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }

        private IEnumerator SpawnLoop()
        {
            if (initialDelay > 0f)
                yield return new WaitForSeconds(initialDelay);

            var wait = new WaitForSeconds(spawnInterval);
            while (!IsExhausted)
            {
                // The zone gate is polled rather than trigger-driven: a player who goes down inside it
                // pauses spawning and resumes it once revived, without ever crossing the boundary.
                if (activation != Activation.WhilePlayersInZone || FindPlayerInZone(transform.position) != null)
                    TrySpawn();
                if (aggroOnSpawn)
                    AggroIdleDroids();
                yield return wait;
            }
            spawnRoutine = null;
        }

        [Server]
        private void TrySpawn()
        {
            alive.RemoveAll(d => d == null || d.IsDead);
            if (droidPrefab == null || alive.Count >= maxAlive)
                return;

            if (!TryGetSpawnPose(out Vector3 position, out Quaternion rotation))
                return; // every point is occupied; try again next tick
            ShooterDroid droid = Instantiate(droidPrefab, position, rotation);
            NetworkServer.Spawn(droid.gameObject);
            alive.Add(droid);
            spawnedTotal++;
        }

        // Hand every idle droid of this spawner the nearest eligible player, if there is one.
        [Server]
        private void AggroIdleDroids()
        {
            foreach (ShooterDroid droid in alive)
            {
                if (droid == null || droid.IsDead || droid.Target != null)
                    continue;

                PlayerHealth target = activationZone != null
                    ? FindPlayerInZone(droid.transform.position)
                    : FindPlayerNear(droid.transform.position);
                if (target != null)
                    droid.ServerSetTarget(target);
            }
        }

        // Nearest player on foot within aggroRadius (0 = any distance), or null.
        private PlayerHealth FindPlayerNear(Vector3 position)
        {
            PlayerHealth nearest = null;
            float bestSqr = aggroRadius > 0f ? aggroRadius * aggroRadius : float.MaxValue;
            foreach (PlayerHealth player in ShooterDroid.ServerPlayers())
            {
                if (!player.IsTargetable)
                    continue;
                float sqr = (player.transform.position - position).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    nearest = player;
                }
            }
            return nearest;
        }

        // Nearest player on foot inside the activation zone, or null.
        private PlayerHealth FindPlayerInZone(Vector3 nearTo)
        {
            if (activationZone == null)
                return null;

            PlayerHealth nearest = null;
            float bestSqr = float.MaxValue;
            foreach (PlayerHealth player in ShooterDroid.ServerPlayers())
            {
                if (!player.IsTargetable)
                    continue;

                Vector3 p = player.transform.position;
                if ((activationZone.ClosestPoint(p) - p).sqrMagnitude > 0.01f)
                    continue;

                float sqr = (p - nearTo).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = player;
                }
            }
            return nearest;
        }

        // Random free spawn point (the spawner itself when none are set). False if all are occupied.
        private bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            int count = spawnPoints != null ? spawnPoints.Length : 0;
            int start = Random.Range(0, Mathf.Max(1, count));

            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                Transform point = count > 0 ? spawnPoints[(start + i) % count] : transform;
                if (point == null)
                    continue; // empty slot left in the array

                // Snap onto the NavMesh so the droid's NavMeshAgent starts on it.
                Vector3 candidate = NavMesh.SamplePosition(point.position, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas)
                    ? hit.position
                    : point.position;

                if (IsOccupied(candidate))
                    continue;

                position = candidate;
                rotation = point.rotation;
                return true;
            }

            position = default;
            rotation = default;
            return false;
        }

        private bool IsOccupied(Vector3 position)
        {
            foreach (ShooterDroid droid in alive)
            {
                if (droid != null && (droid.transform.position - position).sqrMagnitude < spawnClearance * spawnClearance)
                    return true;
            }
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 1f);
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Gizmos.DrawWireSphere(transform.position, 0.5f);
                return;
            }
            foreach (Transform p in spawnPoints)
            {
                if (p == null)
                    continue;
                Gizmos.DrawWireSphere(p.position, 0.5f);
                Gizmos.DrawLine(transform.position, p.position);
            }
        }
    }
}
