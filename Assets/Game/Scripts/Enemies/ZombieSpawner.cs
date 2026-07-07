using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
using FPSGame.Player;

namespace FPSGame.Environment
{
    // Server-authoritative zombie spawner gated by a BreachConsole. Spawns waves only
    // while the console vault is Open (console.IsVaultOpen) — the reward window comes
    // with a cost. Closing / overheating the console stops the spawn loop; existing
    // zombies keep hunting unless despawnOnVaultClose is set.
    //
    // Subscribes to the console's §5 contract (OnStateChanged); the console itself knows
    // nothing about zombies. All instantiation runs on the server and replicates via
    // NetworkServer.Spawn — the zombie prefab must be registered in the NetworkManager
    // spawnable prefabs list (same as barrels / projectiles).
    public class ZombieSpawner : NetworkBehaviour
    {
        [Header("Console")]
        [Tooltip("Spawning runs only while this console's vault is Open.")]
        [SerializeField] private BreachConsole console;

        [Header("Zombie")]
        [SerializeField] private ZombieEnemy zombiePrefab;

        [Header("Spawning")]
        [Tooltip("Seconds between spawns while the vault is open and below the alive cap.")]
        [SerializeField] private float spawnInterval = 2f;
        [Tooltip("Max living zombies from this spawner at once. Dead ones free their slot.")]
        [SerializeField] private int maxAlive = 8;
        [Tooltip("Spawn points (random pick). Falls back to this spawner's own transform.")]
        [SerializeField] private Transform[] spawnPoints;
        [Tooltip("Radius used to snap a chosen point onto the baked NavMesh.")]
        [SerializeField] private float navSampleRadius = 2f;

        [Header("Aggro")]
        [Tooltip("Trigger volume. A player standing inside it is handed to each zombie " +
                 "as its target the moment it spawns, so zombies charge in immediately " +
                 "instead of idling until the player wanders into their own detection radius.")]
        [SerializeField] private Collider aggroTrigger;
        [Tooltip("Which layers count as players for the aggro-zone query.")]
        [SerializeField] private LayerMask playerLayerMask = ~0;

        [Header("Cleanup")]
        [Tooltip("Destroy living zombies the moment the vault closes.")]
        [SerializeField] private bool despawnOnVaultClose = false;
        [Tooltip("Destroy a corpse this many seconds after death (0 = keep it forever).")]
        [SerializeField] private float despawnDeadDelay = 5f;

        // Living zombies counted against maxAlive. Dead / destroyed ones are pruned.
        private readonly List<ZombieEnemy> alive = new List<ZombieEnemy>();
        private bool spawning;
        private Coroutine spawnRoutine;

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (console != null)
            {
                console.OnStateChanged += OnConsoleStateChanged;
                // Handle a console that is somehow already open at startup.
                if (console.IsVaultOpen)
                    BeginSpawning();
            }
            else
            {
                Debug.LogWarning($"[ZombieSpawner] {name} has no BreachConsole assigned.", this);
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (console != null)
                console.OnStateChanged -= OnConsoleStateChanged;
            StopSpawning();
        }

        // §5: react to vault open/close. Overheat and every non-Open state read as closed.
        [Server]
        private void OnConsoleStateChanged(ConsoleState state)
        {
            if (console.IsVaultOpen)
            {
                BeginSpawning();
            }
            else
            {
                StopSpawning();
                if (despawnOnVaultClose)
                    DespawnAll();
            }
        }

        [Server]
        private void BeginSpawning()
        {
            if (spawning)
                return;
            spawning = true;
            spawnRoutine = StartCoroutine(SpawnLoop());
        }

        [Server]
        private void StopSpawning()
        {
            if (!spawning)
                return;
            spawning = false;
            if (spawnRoutine != null)
            {
                StopCoroutine(spawnRoutine);
                spawnRoutine = null;
            }
        }

        private IEnumerator SpawnLoop()
        {
            var wait = new WaitForSeconds(spawnInterval);
            while (spawning)
            {
                TrySpawn();
                yield return wait;
            }
        }

        [Server]
        private void TrySpawn()
        {
            PruneAndCleanup();

            if (zombiePrefab == null)
            {
                Debug.LogError($"[ZombieSpawner] {name}: zombiePrefab is not assigned!", this);
                return;
            }

            if (alive.Count >= maxAlive)
                return;

            GetSpawnPose(out Vector3 pos, out Quaternion rot);
            ZombieEnemy zombie = Instantiate(zombiePrefab, pos, rot);
            NetworkServer.Spawn(zombie.gameObject);
            alive.Add(zombie);

            // If a player is standing in the aggro zone, hand them to the fresh zombie so
            // it charges in right away instead of waiting to detect them on its own.
            PlayerHealth target = FindTargetInZone(pos);
            if (target != null)
                zombie.ServerSetTarget(target);
        }

        // Nearest living player currently inside the aggro trigger, or null.
        [Server]
        private PlayerHealth FindTargetInZone(Vector3 nearTo)
        {
            if (aggroTrigger == null)
                return null;

            // Broad-phase with a sphere enclosing the trigger's AABB, then confirm each
            // candidate is actually inside the real collider shape via ClosestPoint.
            Bounds b = aggroTrigger.bounds;
            Collider[] hits = Physics.OverlapSphere(
                b.center, b.extents.magnitude, playerLayerMask, QueryTriggerInteraction.Ignore);

            PlayerHealth nearest = null;
            float bestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                var ph = hit.GetComponentInParent<PlayerHealth>();
                if (ph == null || ph.IsDead)
                    continue;

                Vector3 p = ph.transform.position;
                if ((aggroTrigger.ClosestPoint(p) - p).sqrMagnitude > 0.01f)
                    continue; // outside the actual trigger volume

                float d = (p - nearTo).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    nearest = ph;
                }
            }

            return nearest;
        }

        // Remove destroyed and dead zombies from the live cap. Corpses are optionally
        // scheduled for destruction so repeated bursts don't pile up network objects.
        [Server]
        private void PruneAndCleanup()
        {
            for (int i = alive.Count - 1; i >= 0; i--)
            {
                ZombieEnemy z = alive[i];
                if (z == null)
                {
                    alive.RemoveAt(i);
                    continue;
                }

                if (!z.IsAlive)
                {
                    alive.RemoveAt(i); // frees a slot; corpse may linger for the animation
                    if (despawnDeadDelay > 0f)
                        StartCoroutine(DestroyAfter(z, despawnDeadDelay));
                }
            }
        }

        [Server]
        private void DespawnAll()
        {
            for (int i = 0; i < alive.Count; i++)
            {
                if (alive[i] != null)
                    NetworkServer.Destroy(alive[i].gameObject);
            }
            alive.Clear();
        }

        private IEnumerator DestroyAfter(ZombieEnemy zombie, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (zombie != null)
                NetworkServer.Destroy(zombie.gameObject);
        }

        private void GetSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            Transform point = null;
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                // Ignore empty slots left in the array.
                int guard = spawnPoints.Length;
                do { point = spawnPoints[Random.Range(0, spawnPoints.Length)]; }
                while (point == null && --guard > 0);
            }

            Vector3 basePos = point != null ? point.position : transform.position;
            rotation = point != null ? point.rotation : transform.rotation;

            // Snap onto the NavMesh so the ZombieEnemy's NavMeshAgent has a valid start.
            if (NavMesh.SamplePosition(basePos, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas))
                position = hit.position;
            else
                position = basePos;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 1f, 0.3f, 1f);
            if (spawnPoints != null)
            {
                foreach (var p in spawnPoints)
                {
                    if (p == null)
                        continue;
                    Gizmos.DrawWireSphere(p.position, 0.5f);
                    Gizmos.DrawLine(transform.position, p.position);
                }
            }
        }
    }
}
