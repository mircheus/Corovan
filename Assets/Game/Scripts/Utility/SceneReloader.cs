using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

namespace FPSGame.Utility
{
    public class SceneReloader : MonoBehaviour
    {
        [SerializeField] private KeyCode reloadKey = KeyCode.F9;

        private void Update()
        {
            if (Input.GetKeyDown(reloadKey))
                Restart();
        }

        public void Restart()
        {
            string sceneName = SceneManager.GetActiveScene().name;

            if (NetworkServer.active)
            {
                NetworkManager.singleton.ServerChangeScene(sceneName);
            }
            else if (!NetworkClient.isConnected)
            {
                SceneManager.LoadScene(sceneName);
            }
        }
    }
}
