using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
using FPSGame.Player;

namespace FPSGame.Enemies
{
    /// <summary>
    /// Shared server spawn loop for one enemy prefab (<see cref="ShooterDroidSpawner"/>,
    /// <see cref="MeleeZombieSpawner"/>, <see cref="EnemyCarSpawner"/>). Keeps up to <see cref="maxAlive"/>
    /// living enemies from this spawner.
    ///
    /// Two sources of spawns share one tick (every <see cref="spawnInterval"/>, at most one spawn per tick)
    /// and the same alive cap:
    ///   • its own loop, by <see cref="activation"/> — OnStart / WhilePlayersInZone / Manual
    ///     (ServerStartSpawning / ServerStopSpawning, safe from UnityEvents on any peer), capped by
    ///     <see cref="totalToSpawn"/>;
    ///   • requests from <see cref="EnemyDirector"/> (<see cref="ServerRequest"/>): "spawn N more",
    ///     served first, ignoring the activation gate and totalToSpawn. A spawner driven only by the
    ///     director should be set to Manual.
    ///
    /// The prefab must be in CustomNetworkManager.runtimeSpawnPrefabs.
    /// </summary>
    public abstract class EnemySpawner : NetworkBehaviour
    {
        public enum Activation { OnStart, WhilePlayersInZone, Manual }

        /// <summary>
        /// A director's order: <see cref="Requested"/> enemies from this spawner. The spawner fills it over
        /// time; the director reads the counters to decide when the wave is over. Server only.
        /// </summary>
        public sealed class SpawnRequest
        {
            public int Requested { get; }
            public int Spawned { get; internal set; }
            public bool Cancelled { get; internal set; }
            internal readonly List<NetworkBehaviour> members = new List<NetworkBehaviour>();
            internal EnemySpawner owner;

            internal SpawnRequest(int requested, EnemySpawner owner)
            {
                Requested = requested;
                this.owner = owner;
            }

            /// <summary>Nothing left to spawn (filled or cancelled).</summary>
            public bool AllSpawned => Cancelled || Spawned >= Requested;

            /// <summary>Living enemies spawned for this request.</summary>
            public int Alive
            {
                get
                {
                    int count = 0;
                    foreach (NetworkBehaviour enemy in members)
                        if (enemy != null && !owner.IsEnemyDead(enemy))
                            count++;
                    return count;
                }
            }

            /// <summary>Everything spawned and every one of them is dead (or gone).</summary>
            public bool AllDead => AllSpawned && Alive == 0;
        }

        [Header("Activation")]
        [SerializeField] protected Activation activation = Activation.WhilePlayersInZone;
        [Tooltip("Zone for WhilePlayersInZone, and for aggroOnSpawn. Any collider shape; it is only queried, " +
                 "so it can be a trigger on any object.")]
        [SerializeField] protected Collider activationZone;

        [Header("Spawning")]
        [Tooltip("Seconds before the first spawn of the spawner's own loop once it starts. Director requests don't wait for it.")]
        [SerializeField] protected float initialDelay = 0f;
        [Tooltip("Seconds between spawns while below the alive cap (own loop and director requests alike).")]
        [SerializeField] protected float spawnInterval = 3f;
        [Tooltip("Max living enemies from this spawner at once. Dead ones free their slot. Director requests obey it too.")]
        [SerializeField] protected int maxAlive = 4;
        [Tooltip("Enemies the spawner's own loop may ever create. 0 = unlimited. Director requests don't count.")]
        [SerializeField] protected int totalToSpawn = 0;
        [Tooltip("Spawn points (random pick). Falls back to this spawner's own transform.")]
        [SerializeField] protected Transform[] spawnPoints;
        [Tooltip("Radius used to snap a chosen point onto the baked NavMesh.")]
        [SerializeField] protected float navSampleRadius = 2f;
        [Tooltip("A point with a living enemy of this spawner closer than this is busy; the spawn waits for a free one.")]
        [SerializeField] protected float spawnClearance = 1.2f;

        [Header("Aggro")]
        [Tooltip("Hand this spawner's idle enemies a target at once instead of waiting for them to notice someone. " +
                 "Checked on every spawn tick. The target must be inside activationZone if one is set, " +
                 "otherwise within aggroRadius of the enemy.")]
        [SerializeField] protected bool aggroOnSpawn = true;
        [Tooltip("Without an activationZone: how far from the enemy a target may be. 0 = anywhere.")]
        [SerializeField] protected float aggroRadius = 30f;

        protected readonly List<NetworkBehaviour> alive = new List<NetworkBehaviour>();
        private readonly List<SpawnRequest> requests = new List<SpawnRequest>();
        private int spawnedTotal;
        private bool ownLoopActive;
        private float ownLoopStartTime;
        private Coroutine tickRoutine;

        /// <summary>The spawner's own loop is running and may still spawn.</summary>
        public bool IsSpawning => ownLoopActive && !IsExhausted;
        public bool IsExhausted => totalToSpawn > 0 && spawnedTotal >= totalToSpawn;
        public int AliveCount { get { PruneAlive(); return alive.Count; } }

        // ── Per-type hooks ──────────────────────────────────────────────────────────────

        /// <summary>The enemy prefab's component (its GameObject is what gets spawned).</summary>
        protected abstract NetworkBehaviour Prefab { get; }
        protected abstract bool IsEnemyDead(NetworkBehaviour enemy);
        /// <summary>Give one idle (target-less) enemy a target, if there is an eligible one.</summary>
        protected abstract void AggroIfIdle(NetworkBehaviour enemy);

        /// <summary>The WhilePlayersInZone gate. Default: a targetable player on foot inside the zone.</summary>
        protected virtual bool ZoneGateOpen() => FindPlayerInZone(transform.position) != null;

        /// <summary>Snap a spawn point onto the NavMesh the enemy walks on.</summary>
        protected virtual Vector3 SnapToNavMesh(Vector3 point) =>
            NavMesh.SamplePosition(point, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas) ? hit.position : point;

        protected virtual Color GizmoColor => new Color(1f, 0.3f, 0.3f, 1f);

        // ── Lifecycle / public API ──────────────────────────────────────────────────────

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (Prefab == null)
                Debug.LogWarning($"[{GetType().Name}] {name}: enemy prefab is not assigned.", this);
            if (activation == Activation.WhilePlayersInZone && activationZone == null)
                Debug.LogWarning($"[{GetType().Name}] {name}: WhilePlayersInZone needs an activationZone.", this);

            tickRoutine = StartCoroutine(TickLoop());
            if (activation != Activation.Manual)
                ServerStartSpawning();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (tickRoutine != null)
                StopCoroutine(tickRoutine);
            tickRoutine = null;
            ownLoopActive = false;
            requests.Clear();
        }

        /// <summary>Start the spawner's own loop. Safe to call from a UnityEvent on any peer.</summary>
        public void ServerStartSpawning()
        {
            if (!NetworkServer.active || ownLoopActive)
                return;
            ownLoopActive = true;
            ownLoopStartTime = Time.time + initialDelay;
        }

        /// <summary>Stop the spawner's own loop; living enemies keep fighting, director requests are still served. Safe on any peer.</summary>
        public void ServerStopSpawning()
        {
            if (!NetworkServer.active)
                return;
            ownLoopActive = false;
        }

        /// <summary>Order <paramref name="count"/> more enemies (served in order, before the own loop). Server only.</summary>
        public SpawnRequest ServerRequest(int count)
        {
            var request = new SpawnRequest(Mathf.Max(0, count), this);
            if (NetworkServer.active && count > 0 && Prefab != null)
                requests.Add(request);
            else
                request.Cancelled = true;
            return request;
        }

        /// <summary>Drop what is left of a request; enemies already spawned stay. Server only.</summary>
        public void ServerCancel(SpawnRequest request)
        {
            if (request == null)
                return;
            request.Cancelled = true;
            requests.Remove(request);
        }

        // ── Loop ────────────────────────────────────────────────────────────────────────

        private IEnumerator TickLoop()
        {
            // Read every tick so tweaking spawnInterval in Play mode takes effect.
            while (true)
            {
                Tick();
                yield return new WaitForSeconds(Mathf.Max(0.05f, spawnInterval));
            }
        }

        [Server]
        private void Tick()
        {
            requests.RemoveAll(r => r.AllSpawned);

            if (requests.Count > 0)
            {
                SpawnRequest request = requests[0];
                NetworkBehaviour enemy = TrySpawn();
                if (enemy != null)
                {
                    request.Spawned++;
                    request.members.Add(enemy);
                }
            }
            // The zone gate is polled rather than trigger-driven: a player who goes down inside it
            // pauses spawning and resumes it once revived, without ever crossing the boundary.
            else if (ownLoopActive && !IsExhausted && Time.time >= ownLoopStartTime
                     && (activation != Activation.WhilePlayersInZone || ZoneGateOpen()))
            {
                if (TrySpawn() != null)
                    spawnedTotal++;
            }

            if (aggroOnSpawn)
            {
                foreach (NetworkBehaviour enemy in alive)
                    if (enemy != null && !IsEnemyDead(enemy))
                        AggroIfIdle(enemy);
            }
        }

        private void PruneAlive() => alive.RemoveAll(e => e == null || IsEnemyDead(e));

        // Spawns one enemy, or returns null (cap reached / every point occupied — try again next tick).
        [Server]
        private NetworkBehaviour TrySpawn()
        {
            PruneAlive();
            NetworkBehaviour prefab = Prefab;
            if (prefab == null || alive.Count >= maxAlive)
                return null;

            if (!TryGetSpawnPose(out Vector3 position, out Quaternion rotation))
                return null;
            NetworkBehaviour enemy = Instantiate(prefab, position, rotation);
            NetworkServer.Spawn(enemy.gameObject);
            alive.Add(enemy);
            return enemy;
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

                Vector3 candidate = SnapToNavMesh(point.position);
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
            foreach (NetworkBehaviour enemy in alive)
            {
                if (enemy != null && (enemy.transform.position - position).sqrMagnitude < spawnClearance * spawnClearance)
                    return true;
            }
            return false;
        }

        // ── Player helpers (on-foot enemies) ────────────────────────────────────────────

        protected bool IsInZone(Vector3 p) =>
            activationZone != null && (activationZone.ClosestPoint(p) - p).sqrMagnitude <= 0.01f;

        /// <summary>The target an idle on-foot enemy at <paramref name="position"/> should be handed, or null.</summary>
        protected PlayerHealth FindAggroPlayer(Vector3 position) =>
            activationZone != null ? FindPlayerInZone(position) : FindPlayerNear(position);

        // Nearest player on foot within aggroRadius (0 = any distance), or null.
        protected PlayerHealth FindPlayerNear(Vector3 position)
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
        protected PlayerHealth FindPlayerInZone(Vector3 nearTo)
        {
            if (activationZone == null)
                return null;

            PlayerHealth nearest = null;
            float bestSqr = float.MaxValue;
            foreach (PlayerHealth player in ShooterDroid.ServerPlayers())
            {
                if (!player.IsTargetable || !IsInZone(player.transform.position))
                    continue;

                float sqr = (player.transform.position - nearTo).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = player;
                }
            }
            return nearest;
        }

        // ── Gizmos ──────────────────────────────────────────────────────────────────────

        protected virtual void OnDrawGizmosSelected()
        {
            Color wire = GizmoColor;
            Color fill = new Color(wire.r, wire.g, wire.b, 0.45f);
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                DrawSpawnPoint(transform.position, transform.forward, fill, wire);
                return;
            }
            foreach (Transform p in spawnPoints)
            {
                if (p == null)
                    continue;
                DrawSpawnPoint(p.position, p.forward, fill, wire);
                Gizmos.color = wire;
                Gizmos.DrawLine(transform.position, p.position);
            }
        }

        // Solid translucent sphere + wire outline + a short arrow showing the spawn facing.
        private static void DrawSpawnPoint(Vector3 position, Vector3 forward, Color fill, Color wire)
        {
            const float radius = 0.5f;
            Gizmos.color = fill;
            Gizmos.DrawSphere(position, radius);
            Gizmos.color = wire;
            Gizmos.DrawWireSphere(position, radius);
            Gizmos.DrawLine(position, position + forward * (radius * 2f));
        }
    }
}
