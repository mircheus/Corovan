using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

namespace FPSGame.Hazards
{
    public class BarrelSpawner : NetworkBehaviour
    {
        [Header("Pool")]
        [SerializeField] private GameObject barrelPrefab;
        [SerializeField] private int poolSize = 5;

        [Header("Spawning")]
        [SerializeField] private float spawnDelay = 1.5f;

        private readonly List<Barrel> pool = new List<Barrel>();
        private readonly Queue<Barrel> available = new Queue<Barrel>();

        public override void OnStartServer()
        {
            base.OnStartServer();
            InitializePool();
            SpawnNext();
        }

        private void InitializePool()
        {
            for (int i = 0; i < poolSize; i++)
            {
                GameObject go = Instantiate(barrelPrefab, transform.position, barrelPrefab.transform.rotation);
                NetworkServer.Spawn(go);

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

        private void OnBarrelReturned(Barrel barrel)
        {
            available.Enqueue(barrel);
            StartCoroutine(SpawnAfterDelay());
        }

        private IEnumerator SpawnAfterDelay()
        {
            yield return new WaitForSeconds(spawnDelay);
            SpawnNext();
        }
    }
}
