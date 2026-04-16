using System;
using FPSGame.Player;
using FPSGame.Utility;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace FPSGame.UI
{
    public class GameOverUI : MonoBehaviour
    {
        [SerializeField] PlayerHealth playerHealth;
        [SerializeField] GameObject gameOverUI;
        [SerializeField] Button restartButton;
        
        private void Awake()
        {
            gameOverUI.SetActive(false);
        }

        private void OnEnable()
        {
            playerHealth.OnDeath += EnableGameOverUI;
            restartButton.onClick.AddListener(OnRestartButtonClick);
        }

        private void OnDisable()
        {
            playerHealth.OnDeath -= EnableGameOverUI;
            restartButton.onClick.RemoveListener(OnRestartButtonClick);
        }

        private void OnRestartButtonClick()
        {
            // SceneReloader.R
        }

        private void EnableGameOverUI()
        {
            Cursor.visible = true;
            gameOverUI.SetActive(true);
        }
    }
}