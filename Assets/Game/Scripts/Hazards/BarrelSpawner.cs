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
        [Tooltip("If true, starts spawning immediately on server start. If false, waits for BeginSpawning() — e.g. fired by a BarrelSpawnTrigger when the wagon approaches.")]
        [SerializeField] private bool startOnServerStart = true;

        private readonly List<Barrel> pool = new List<Barrel>();
        private readonly Queue<Barrel> available = new Queue<Barrel>();
        private bool spawning;
        private Coroutine spawnRoutine;

        public override void OnStartServer()
        {
            base.OnStartServer();
            InitializePool();
            if (startOnServerStart)
                BeginSpawning();
        }

        /// <summary>
        /// Starts the barrel spawn loop. Safe to call multiple times — only the first call takes effect.
        /// Intended to be invoked by a trigger (e.g. <c>BarrelSpawnTrigger</c>) when the wagon draws near.
        /// </summary>
        [Server]
        public void BeginSpawning()
        {
            if (spawning) return;
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

        private void SpawnNext()
        {
            if (available.Count == 0) return;

            Barrel barrel = available.Dequeue();
            barrel.ServerActivate(transform.position);
        }

        private IEnumerator SpawnLoop()
        {
            var wait = new WaitForSeconds(spawnInterval);
            while (spawning)
            {
                SpawnNext();
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
