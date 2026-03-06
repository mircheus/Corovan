using UnityEngine;
using Mirror;
using FPSGame.Networking.Steam;

namespace FPSGame.Networking
{
    public class CustomNetworkManager : NetworkManager
    {
        [Header("Spawn Settings")]
        [SerializeField] private Transform[] spawnPoints;

        [Header("Steam")]
        [SerializeField] private SteamLobbyManager steamLobbyManager;

        private int currentSpawnIndex;

        public override void OnStartServer()
        {
            base.OnStartServer();
            Debug.Log("Server started");
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
            steamLobbyManager?.LeaveLobby();
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
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return transform;
            }

            Transform spawnPoint = spawnPoints[currentSpawnIndex];
            currentSpawnIndex = (currentSpawnIndex + 1) % spawnPoints.Length;
            return spawnPoint;
        }

        public void ResetSpawnIndex()
        {
            currentSpawnIndex = 0;
        }
    }
}
