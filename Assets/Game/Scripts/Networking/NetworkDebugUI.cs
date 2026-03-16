using Mirror;
using TMPro;
using UnityEngine;

namespace Game.Scripts.Networking
{
    public class NetworkDebugUI : MonoBehaviour
    {
        [SerializeField] private TMP_InputField addressInput;
        [SerializeField] private GameObject panel;

        private NetworkManager _manager;

        private void Start()
        {
            _manager = NetworkManager.singleton;
        
            // По умолчанию ставим ZeroTier IP хоста
            addressInput.text = "10.195.66.21";
        }

        public void OnHostButton()
        {
            _manager.StartHost();
            panel.SetActive(false);
        }

        public void OnClientButton()
        {
            _manager.networkAddress = addressInput.text;
            _manager.StartClient();
            panel.SetActive(false);
        }

        public void OnServerButton()
        {
            _manager.StartServer();
            panel.SetActive(false);
        }
    }
}