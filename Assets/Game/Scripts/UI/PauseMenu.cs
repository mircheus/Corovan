using UnityEngine;
using UnityEngine.InputSystem;
using FPSGame.Player;

namespace FPSGame.UI
{
    public class PauseMenu : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject pauseMenuPanel;
        [SerializeField] private PlayerCamera playerCamera;

        private bool isPaused;

        public bool IsPaused => isPaused;

        private void Awake()
        {
            if (pauseMenuPanel != null)
            {
                pauseMenuPanel.SetActive(false);
            }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                TogglePause();
            }
        }

        public void TogglePause()
        {
            if (isPaused)
                Resume();
            else
                Pause();
        }

        public void Pause()
        {
            isPaused = true;
            Time.timeScale = 0f;

            if (pauseMenuPanel != null)
            {
                pauseMenuPanel.SetActive(true);
            }

            if (playerCamera != null)
            {
                playerCamera.UnlockCursor();
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public void Resume()
        {
            isPaused = false;
            Time.timeScale = 1f;

            if (pauseMenuPanel != null)
            {
                pauseMenuPanel.SetActive(false);
            }

            if (playerCamera != null)
            {
                playerCamera.LockCursor();
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void OpenSettings()
        {
            // Placeholder for opening settings menu
            Debug.Log("Settings menu not yet implemented");
        }

        public void Disconnect()
        {
            // Placeholder for network disconnect
            Time.timeScale = 1f;
            Debug.Log("Disconnect not yet implemented - will be added with Mirror networking");
        }

        public void QuitGame()
        {
            Time.timeScale = 1f;

            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            #endif
        }

        private void OnDestroy()
        {
            // Ensure time scale is reset
            Time.timeScale = 1f;
        }
    }
}
