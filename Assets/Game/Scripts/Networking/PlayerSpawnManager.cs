using UnityEngine;

namespace FPSGame.Networking
{
    /// <summary>
    /// Manages player spawn points for multiplayer.
    /// </summary>
    public class PlayerSpawnManager : MonoBehaviour
    {
        [Header("Spawn Points")]
        [SerializeField] private Transform[] spawnPoints;

        [Header("Settings")]
        [SerializeField] private SpawnMode spawnMode = SpawnMode.RoundRobin;
        [SerializeField] private float minSpawnDistance = 5f;

        public enum SpawnMode
        {
            RoundRobin,
            Random,
            FurthestFromPlayers
        }

        private int currentSpawnIndex;

        public Transform[] SpawnPoints => spawnPoints;

        private void Awake()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                spawnPoints = new Transform[] { transform };
            }
        }

        public Transform GetSpawnPoint()
        {
            switch (spawnMode)
            {
                case SpawnMode.RoundRobin:
                    return GetRoundRobinSpawnPoint();
                case SpawnMode.Random:
                    return GetRandomSpawnPoint();
                case SpawnMode.FurthestFromPlayers:
                    return GetFurthestSpawnPoint();
                default:
                    return spawnPoints[0];
            }
        }

        private Transform GetRoundRobinSpawnPoint()
        {
            Transform spawnPoint = spawnPoints[currentSpawnIndex];
            currentSpawnIndex = (currentSpawnIndex + 1) % spawnPoints.Length;
            return spawnPoint;
        }

        private Transform GetRandomSpawnPoint()
        {
            int index = Random.Range(0, spawnPoints.Length);
            return spawnPoints[index];
        }

        private Transform GetFurthestSpawnPoint()
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");

            if (players.Length == 0)
            {
                return GetRandomSpawnPoint();
            }

            Transform bestSpawn = spawnPoints[0];
            float maxMinDistance = 0;

            foreach (Transform spawn in spawnPoints)
            {
                float minDistanceToPlayer = float.MaxValue;

                foreach (GameObject player in players)
                {
                    float distance = Vector3.Distance(spawn.position, player.transform.position);
                    minDistanceToPlayer = Mathf.Min(minDistanceToPlayer, distance);
                }

                if (minDistanceToPlayer > maxMinDistance)
                {
                    maxMinDistance = minDistanceToPlayer;
                    bestSpawn = spawn;
                }
            }

            return bestSpawn;
        }

        public void ResetSpawnIndex()
        {
            currentSpawnIndex = 0;
        }

        private void OnDrawGizmosSelected()
        {
            if (spawnPoints == null)
                return;

            Gizmos.color = Color.green;
            foreach (Transform spawn in spawnPoints)
            {
                if (spawn != null)
                {
                    Gizmos.DrawWireSphere(spawn.position, 0.5f);
                    Gizmos.DrawRay(spawn.position, spawn.forward * 2);
                }
            }
        }
    }
}
