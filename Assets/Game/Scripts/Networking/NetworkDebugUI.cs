using System.Collections;
using Mirror;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using ParrelSync;
#endif

namespace Game.Scripts.Networking
{
    public class NetworkDebugUI : MonoBehaviour
    {
        [SerializeField] private TMP_InputField addressInput;
        [SerializeField] private GameObject panel;

        [Header("Addresses")]
        [Tooltip("Prefilled when running the original project or a build — the host's ZeroTier IP.")]
        [SerializeField] private string remoteAddress = "10.195.66.21";
        [Tooltip("Prefilled inside a ParrelSync clone, where the host is the other editor on this machine.")]
        [SerializeField] private string localAddress = "localhost";

        [Header("ParrelSync")]
        [Tooltip("A ParrelSync clone connects to the original automatically, retrying until the host is up.")]
        [SerializeField] private bool autoConnectInClone = true;
        [Tooltip("The original project starts hosting on play. Leave off for normal single-editor work.")]
        [SerializeField] private bool autoHostInOriginal;
        [SerializeField] private float retryInterval = 1f;
        [SerializeField] private int retryAttempts = 15;

        private NetworkManager _manager;

        /// <summary>ParrelSync only exists in the editor; a build is never a clone.</summary>
        private static bool IsClone
        {
            get
            {
#if UNITY_EDITOR
                return ClonesManager.IsClone();
#else
                return false;
#endif
            }
        }

        private void Start()
        {
            _manager = NetworkManager.singleton;
            if (_manager == null)
            {
                Debug.LogError("NetworkDebugUI: no NetworkManager in the scene.");
                return;
            }

            // Hide the panel if a network session is already running (e.g. after scene reload).
            if (NetworkServer.active || NetworkClient.isConnected)
            {
                panel.SetActive(false);
                return;
            }

            // A clone shares this machine with the host, so loopback — never the ZeroTier peer.
            addressInput.text = IsClone ? localAddress : remoteAddress;

            if (IsClone)
            {
                if (autoConnectInClone)
                    StartCoroutine(AutoConnectClient());
            }
            else if (autoHostInOriginal)
            {
                OnHostButton();
            }
        }

        public void OnHostButton()
        {
            StopAllCoroutines();
            _manager.StartHost();
            panel.SetActive(false);
        }

        public void OnClientButton()
        {
            StopAllCoroutines();
            _manager.networkAddress = addressInput.text;
            _manager.StartClient();
            panel.SetActive(false);
        }

        public void OnServerButton()
        {
            StopAllCoroutines();
            _manager.StartServer();
            panel.SetActive(false);
        }

        /// <summary>
        /// The clone usually enters play mode before the original finishes starting its host,
        /// so a single StartClient() would time out. Retry until the host answers, then hand
        /// the panel back to the user if it never does.
        /// </summary>
        private IEnumerator AutoConnectClient()
        {
            panel.SetActive(false);
            _manager.networkAddress = localAddress;

            for (int attempt = 1; attempt <= retryAttempts; attempt++)
            {
                _manager.StartClient();

                float deadline = Time.time + retryInterval;
                while (Time.time < deadline && !NetworkClient.isConnected)
                    yield return null;

                if (NetworkClient.isConnected)
                {
                    Debug.Log($"NetworkDebugUI: clone connected to {localAddress} on attempt {attempt}.");
                    yield break;
                }

                _manager.StopClient();
                // Mirror tears the client down over a frame; starting again too early is ignored.
                yield return null;
            }

            Debug.LogWarning($"NetworkDebugUI: no host on {localAddress} after {retryAttempts} attempts. Start the host in the original project.");
            panel.SetActive(true);
        }
    }
}
