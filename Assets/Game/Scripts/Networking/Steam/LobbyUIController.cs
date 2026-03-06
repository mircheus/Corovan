using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FPSGame.Networking.Steam
{
    public class LobbyUIController : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button createLobbyButton;
        [SerializeField] private Button findLobbiesButton;
        [SerializeField] private Button joinLobbyButton;
        [SerializeField] private Button leaveLobbyButton;
        [SerializeField] private Button inviteButton;

        [Header("Containers")]
        [SerializeField] private Transform lobbyListContainer;
        [SerializeField] private Transform playerListContainer;

        [Header("Prefabs")]
        [SerializeField] private GameObject lobbyEntryPrefab;
        [SerializeField] private GameObject playerEntryPrefab;

        [Header("Status")]
        [SerializeField] private TMP_Text connectionStatusText;

        private Lobby? selectedLobby;

        private void Start()
        {
            if (SteamLobbyManager.Instance == null)
            {
                Debug.LogWarning("[LobbyUIController] SteamLobbyManager.Instance is null.");
                return;
            }

            SteamLobbyManager.Instance.OnLobbiesFound += HandleLobbiesFound;
            SteamLobbyManager.Instance.OnJoinedLobby += HandleJoinedLobby;
            SteamLobbyManager.Instance.OnLeftLobby += HandleLeftLobby;
            SteamLobbyManager.Instance.OnLobbyMembersUpdated += HandleMembersUpdated;

            createLobbyButton?.onClick.AddListener(() => _ = SteamLobbyManager.Instance.CreateLobby());
            findLobbiesButton?.onClick.AddListener(() => _ = SteamLobbyManager.Instance.FindLobbies());
            joinLobbyButton?.onClick.AddListener(() =>
            {
                if (selectedLobby.HasValue)
                    _ = SteamLobbyManager.Instance.JoinLobby(selectedLobby.Value);
            });
            leaveLobbyButton?.onClick.AddListener(SteamLobbyManager.Instance.LeaveLobby);
            inviteButton?.onClick.AddListener(SteamLobbyManager.Instance.OpenSteamOverlayInvite);
        }

        private void OnDestroy()
        {
            if (SteamLobbyManager.Instance == null) return;

            SteamLobbyManager.Instance.OnLobbiesFound -= HandleLobbiesFound;
            SteamLobbyManager.Instance.OnJoinedLobby -= HandleJoinedLobby;
            SteamLobbyManager.Instance.OnLeftLobby -= HandleLeftLobby;
            SteamLobbyManager.Instance.OnLobbyMembersUpdated -= HandleMembersUpdated;
        }

        private void HandleLobbiesFound(Lobby[] lobbies)
        {
            if (lobbyListContainer == null || lobbyEntryPrefab == null) return;

            foreach (Transform child in lobbyListContainer)
                Destroy(child.gameObject);

            foreach (Lobby lobby in lobbies)
            {
                GameObject entry = Instantiate(lobbyEntryPrefab, lobbyListContainer);

                TMP_Text label = entry.GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.text = $"{lobby.GetData("GameName")} ({lobby.MemberCount}/{lobby.MaxMembers})";

                Button btn = entry.GetComponent<Button>();
                if (btn != null)
                {
                    Lobby captured = lobby;
                    btn.onClick.AddListener(() => selectedLobby = captured);
                }
            }
        }

        private void HandleJoinedLobby(Lobby lobby)
        {
            if (connectionStatusText != null)
                connectionStatusText.text = "In Lobby";
        }

        private void HandleLeftLobby()
        {
            if (connectionStatusText != null)
                connectionStatusText.text = "Disconnected";

            selectedLobby = null;

            if (lobbyListContainer != null)
                foreach (Transform child in lobbyListContainer)
                    Destroy(child.gameObject);

            if (playerListContainer != null)
                foreach (Transform child in playerListContainer)
                    Destroy(child.gameObject);
        }

        private void HandleMembersUpdated(List<Friend> members)
        {
            if (playerListContainer == null || playerEntryPrefab == null) return;

            foreach (Transform child in playerListContainer)
                Destroy(child.gameObject);

            foreach (Friend member in members)
            {
                GameObject entry = Instantiate(playerEntryPrefab, playerListContainer);
                TMP_Text label = entry.GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.text = member.Name;
            }
        }
    }
}
