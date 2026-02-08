using UnityEngine;

namespace FPSGame.Networking
{
    /// <summary>
    /// Placeholder for custom network manager.
    /// Will extend Mirror's NetworkManager in Phase 2.
    /// </summary>
    public class CustomNetworkManager : MonoBehaviour
    {
        [Header("Player Settings")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private Transform[] spawnPoints;

        [Header("Network Settings")]
        [SerializeField] private int maxPlayers = 16;
        [SerializeField] private string networkAddress = "localhost";
        [SerializeField] private ushort networkPort = 7777;

        private int currentSpawnIndex;

        public int MaxPlayers => maxPlayers;
        public string NetworkAddress => networkAddress;
        public ushort NetworkPort => networkPort;

        /// <summary>
        /// Will be called by Mirror when hosting a game.
        /// </summary>
        public void StartHost()
        {
            Debug.Log("StartHost called - Mirror not yet integrated");
            // Placeholder - will call base.StartHost() in Phase 2
        }

        /// <summary>
        /// Will be called by Mirror when joining a game.
        /// </summary>
        public void StartClient()
        {
            Debug.Log($"StartClient called - connecting to {networkAddress}:{networkPort}");
            // Placeholder - will call base.StartClient() in Phase 2
        }

        /// <summary>
        /// Will be called by Mirror when stopping.
        /// </summary>
        public void StopHost()
        {
            Debug.Log("StopHost called");
            // Placeholder - will call base.StopHost() in Phase 2
        }

        /// <summary>
        /// Gets the next spawn point using round-robin selection.
        /// </summary>
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

        /// <summary>
        /// Will be overridden from NetworkManager.OnServerAddPlayer
        /// </summary>
        public void OnServerAddPlayer(/* NetworkConnectionToClient conn */)
        {
            Transform spawnPoint = GetNextSpawnPoint();
            // Placeholder - will instantiate and spawn player with Mirror
            Debug.Log($"Player would spawn at: {spawnPoint.position}");
        }

        /// <summary>
        /// Will be overridden from NetworkManager.OnServerDisconnect
        /// </summary>
        public void OnServerDisconnect(/* NetworkConnectionToClient conn */)
        {
            Debug.Log("Player disconnected from server");
            // Placeholder - will handle cleanup in Phase 2
        }
    }
}
