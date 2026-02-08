using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSGame.UI
{
    public class ScoreboardUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject scoreboardPanel;

        private bool isShowing;

        private void Awake()
        {
            if (scoreboardPanel != null)
            {
                scoreboardPanel.SetActive(false);
            }
        }

        private void Update()
        {
            // Tab key hold to show scoreboard
            if (Keyboard.current != null)
            {
                bool tabHeld = Keyboard.current.tabKey.isPressed;

                if (tabHeld != isShowing)
                {
                    isShowing = tabHeld;
                    if (scoreboardPanel != null)
                    {
                        scoreboardPanel.SetActive(isShowing);
                    }
                }
            }
        }

        public void Show()
        {
            isShowing = true;
            if (scoreboardPanel != null)
            {
                scoreboardPanel.SetActive(true);
            }
        }

        public void Hide()
        {
            isShowing = false;
            if (scoreboardPanel != null)
            {
                scoreboardPanel.SetActive(false);
            }
        }

        public void Toggle()
        {
            if (isShowing)
                Hide();
            else
                Show();
        }
    }
}
