using UnityEngine;
using Mirror;

namespace FPSGame.Networking
{
    public class CustomNetworkManager : NetworkManager
    {
        [Header("Spawn Settings")]
        [SerializeField] private Transform[] spawnPoints;

        private int currentSpawnIndex;

        public override void OnStartServer()
        {
            base.OnStartServer();
            Debug.Log("Server started");
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            base.OnServerSceneChanged(sceneName);
            currentSpawnIndex = 0;

            // CustomNetworkManager is DontDestroyOnLoad, so its serialized spawnPoints
            // hold references to the previous scene's objects — they're destroyed after
            // reload. Clear them first, then try to re-acquire from the new scene.
            spawnPoints = null;
            var spawnManager = FindObjectOfType<PlayerSpawnManager>();
            if (spawnManager != null)
                spawnPoints = spawnManager.SpawnPoints;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Debug.Log("Server stopped");
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            Debug.Log("Connected to server");
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            Debug.Log("Disconnected from server");
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            Transform spawnPoint = GetNextSpawnPoint();
            GameObject player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
            NetworkServer.AddPlayerForConnection(conn, player);
            Debug.Log($"Player spawned at: {spawnPoint.position}");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            base.OnServerDisconnect(conn);
            Debug.Log("Player disconnected from server");
        }

        public Transform GetNextSpawnPoint()
        {
            // 1. Custom spawnPoints array — null-safe loop skips destroyed references.
            if (spawnPoints != null)
            {
                for (int i = 0; i < spawnPoints.Length; i++)
                {
                    int idx = (currentSpawnIndex + i) % spawnPoints.Length;
                    if (spawnPoints[idx] != null)
                    {
                        currentSpawnIndex = (idx + 1) % spawnPoints.Length;
                        return spawnPoints[idx];
                    }
                }
            }

            // 2. Mirror's built-in NetworkStartPosition system — auto-populated by
            //    NetworkStartPosition components on spawn point GameObjects in the scene.
            if (startPositions != null && startPositions.Count > 0)
                return GetStartPosition();

            return transform;
        }

        public void ResetSpawnIndex()
        {
            currentSpawnIndex = 0;
        }
    }
}
