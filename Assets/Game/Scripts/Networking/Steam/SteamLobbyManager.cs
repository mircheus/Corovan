using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mirror;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace FPSGame.Networking.Steam
{
    public class SteamLobbyManager : MonoBehaviour
    {
        [SerializeField] private string gameName = "Corovan";
        // Note: Facepunch.Steamworks does not expose lobby type via CreateLobbyAsync;
        // this field is reserved for future use with the internal Steam API.
        // [SerializeField] private LobbyType defaultLobbyType = LobbyType.FriendsOnly;

        private Lobby? currentLobby;

        public static SteamLobbyManager Instance { get; private set; }

        public event Action<Lobby> OnLobbyCreated;
        public event Action<Lobby[]> OnLobbiesFound;
        public event Action<Lobby> OnJoinedLobby;
        public event Action OnLeftLobby;
        public event Action<Friend> OnPlayerJoined;
        public event Action<Friend> OnPlayerLeft;
        public event Action<List<Friend>> OnLobbyMembersUpdated;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            if (!SteamClient.IsValid) return;

            SteamMatchmaking.OnLobbyMemberJoined += HandleMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave += HandleMemberLeft;
            SteamMatchmaking.OnLobbyMemberDisconnected += HandleMemberLeft;
            SteamFriends.OnGameLobbyJoinRequested += HandleLobbyJoinRequested;
        }

        private void OnDisable()
        {
            SteamMatchmaking.OnLobbyMemberJoined -= HandleMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave -= HandleMemberLeft;
            SteamMatchmaking.OnLobbyMemberDisconnected -= HandleMemberLeft;
            SteamFriends.OnGameLobbyJoinRequested -= HandleLobbyJoinRequested;
        }

        public async Task<bool> CreateLobby(int maxPlayers = 4)
        {
            if (!SteamClient.IsValid)
            {
                Debug.LogError("[SteamLobbyManager] Steam is not initialized.");
                return false;
            }

            try
            {
                Lobby? lobby = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);
                if (!lobby.HasValue)
                {
                    Debug.LogError("[SteamLobbyManager] Failed to create lobby.");
                    return false;
                }

                currentLobby = lobby;
                currentLobby.Value.SetData("HostSteamId", SteamClient.SteamId.Value.ToString());
                currentLobby.Value.SetData("GameName", gameName);
                currentLobby.Value.SetData("GameVersion", Application.version);

                NetworkManager.singleton.StartHost();

                OnLobbyCreated?.Invoke(currentLobby.Value);
                Debug.Log($"[SteamLobbyManager] Lobby created: {currentLobby.Value.Id}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SteamLobbyManager] Error creating lobby: {e.Message}");
                return false;
            }
        }

        public async Task<Lobby[]> FindLobbies()
        {
            if (!SteamClient.IsValid)
            {
                Debug.LogError("[SteamLobbyManager] Steam is not initialized.");
                return Array.Empty<Lobby>();
            }

            try
            {
                Lobby[] lobbies = await SteamMatchmaking.LobbyList
                    .WithKeyValue("GameName", gameName)
                    .WithKeyValue("GameVersion", Application.version)
                    .WithSlotsAvailable(1)
                    .RequestAsync();

                lobbies ??= Array.Empty<Lobby>();
                OnLobbiesFound?.Invoke(lobbies);
                Debug.Log($"[SteamLobbyManager] Found {lobbies.Length} lobbies.");
                return lobbies;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SteamLobbyManager] Error finding lobbies: {e.Message}");
                return Array.Empty<Lobby>();
            }
        }

        public async Task<bool> JoinLobby(Lobby lobby)
        {
            if (!SteamClient.IsValid)
            {
                Debug.LogError("[SteamLobbyManager] Steam is not initialized.");
                return false;
            }

            try
            {
                RoomEnter result = await lobby.Join();
                if (result != RoomEnter.Success)
                {
                    Debug.LogError($"[SteamLobbyManager] Failed to join lobby: {result}");
                    return false;
                }

                currentLobby = lobby;
                string hostSteamIdString = lobby.GetData("HostSteamId");
                if (string.IsNullOrEmpty(hostSteamIdString))
                {
                    Debug.LogError("[SteamLobbyManager] Could not read HostSteamId from lobby data.");
                    return false;
                }

                NetworkManager.singleton.networkAddress = hostSteamIdString;
                NetworkManager.singleton.StartClient();

                OnJoinedLobby?.Invoke(currentLobby.Value);
                Debug.Log($"[SteamLobbyManager] Joined lobby, connecting to host: {hostSteamIdString}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SteamLobbyManager] Error joining lobby: {e.Message}");
                return false;
            }
        }

        public void LeaveLobby()
        {
            try
            {
                if (NetworkServer.active)
                    NetworkManager.singleton.StopHost();
                else if (NetworkClient.active)
                    NetworkManager.singleton.StopClient();

                currentLobby?.Leave();
                currentLobby = null;

                OnLeftLobby?.Invoke();
                Debug.Log("[SteamLobbyManager] Left lobby.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SteamLobbyManager] Error leaving lobby: {e.Message}");
            }
        }

        public void InviteFriend(SteamId friendId)
        {
            if (!SteamClient.IsValid || !currentLobby.HasValue) return;
            currentLobby.Value.InviteFriend(friendId);
        }

        public void OpenSteamOverlayInvite()
        {
            if (!SteamClient.IsValid || !currentLobby.HasValue) return;
            SteamFriends.OpenGameInviteOverlay(currentLobby.Value.Id);
        }

        public List<Friend> LobbyMembers
        {
            get
            {
                if (!currentLobby.HasValue) return new List<Friend>();
                return currentLobby.Value.Members.ToList();
            }
        }

        private void HandleMemberJoined(Lobby lobby, Friend friend)
        {
            OnPlayerJoined?.Invoke(friend);
            OnLobbyMembersUpdated?.Invoke(LobbyMembers);
        }

        private void HandleMemberLeft(Lobby lobby, Friend friend)
        {
            OnPlayerLeft?.Invoke(friend);
            OnLobbyMembersUpdated?.Invoke(LobbyMembers);
        }

        private async void HandleLobbyJoinRequested(Lobby lobby, SteamId steamId)
        {
            await JoinLobby(lobby);
        }
    }
}
