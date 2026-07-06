using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Mirror;

namespace FPSGame.Hazards
{
    public class BarrelSpawner : NetworkBehaviour
    {
        [Header("Pool")]
        [SerializeField] private Barrel barrelPrefab;
        [SerializeField] private int poolSize = 5;

        [Header("Spawning")]
        [Tooltip("Seconds between barrel spawns while the spawner is active. Independent of how long a barrel lives — a fresh barrel launches every interval as long as the pool has one available.")]
        [FormerlySerializedAs("spawnDelay")]
        [SerializeField] private float spawnInterval = 1.5f;
        [Tooltip("If true, starts spawning immediately on server start. If false, waits for BeginSpawning() — e.g. fired by a BarrelSpawnTrigger when the wagon approaches. Ignored when Manual Control is enabled.")]
        [SerializeField] private bool startOnServerStart = true;
        [Tooltip("When enabled, this spawner never runs its own spawn loop — timing is driven externally via Spawn(), e.g. by a BarrelSpawnTrigger picking spawners at random. Use this for any spawner referenced by a trigger.")]
        [SerializeField] private bool manualControl = false;

        private readonly List<Barrel> pool = new List<Barrel>();
        private readonly Queue<Barrel> available = new Queue<Barrel>();
        private bool spawning;
        private Coroutine spawnRoutine;

        public override void OnStartServer()
        {
            base.OnStartServer();
            InitializePool();
            if (!manualControl && startOnServerStart)
                BeginSpawning();
        }

        /// <summary>
        /// Starts the spawner's own spawn loop. Safe to call multiple times — only the first call takes effect.
        /// No-op while <c>manualControl</c> is enabled; in that case use <see cref="Spawn"/> to drive spawning externally.
        /// </summary>
        [Server]
        public void BeginSpawning()
        {
            if (manualControl || spawning) return;
            spawning = true;
            spawnRoutine = StartCoroutine(SpawnLoop());
        }

        /// <summary>
        /// Stops the spawn loop. Barrels already in flight finish their run and return to the pool normally.
        /// </summary>
        [Server]
        public void StopSpawning()
        {
            if (!spawning) return;
            spawning = false;
            if (spawnRoutine != null)
            {
                StopCoroutine(spawnRoutine);
                spawnRoutine = null;
            }
        }

        private void InitializePool()
        {
            for (int i = 0; i < poolSize; i++)
            {
                Barrel go = Instantiate(barrelPrefab, transform.position, barrelPrefab.transform.rotation);
                NetworkServer.Spawn(go.gameObject);

                Barrel barrel = go.GetComponent<Barrel>();
                barrel.OnReturnToPool = OnBarrelReturned;
                pool.Add(barrel);
                available.Enqueue(barrel);
            }
        }

        /// <summary>
        /// Launches the next available barrel from the pool at this spawner's position.
        /// Returns <c>true</c> if a barrel was spawned, <c>false</c> if the pool is currently empty.
        /// Intended to be called by an external driver (e.g. <c>BarrelSpawnTrigger</c>) in manual-control mode.
        /// </summary>
        [Server]
        public bool Spawn()
        {
            if (available.Count == 0) return false;

            Barrel barrel = available.Dequeue();
            barrel.ServerActivate(transform.position);
            return true;
        }

        private IEnumerator SpawnLoop()
        {
            var wait = new WaitForSeconds(spawnInterval);
            while (spawning)
            {
                Spawn();
                yield return wait;
            }
        }

        // A spent barrel just becomes available again for the next spawn tick.
        private void OnBarrelReturned(Barrel barrel)
        {
            available.Enqueue(barrel);
        }
    }
}
