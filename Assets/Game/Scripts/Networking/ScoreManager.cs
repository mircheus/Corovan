using System;
using Mirror;
using UnityEngine;

namespace FPSGame.Networking
{
    public class ScoreManager : NetworkBehaviour
    {
        public static ScoreManager Instance { get; private set; }

        [SyncVar(hook = nameof(OnScoreChangedHook))]
        private int totalScore;

        public int TotalScore => totalScore;

        // Fires on all clients (and host) when the score changes.
        public event Action<int> OnScoreChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        [Server]
        public void AddScore(int amount)
        {
            if (amount <= 0)
                return;
            totalScore += amount;
        }

        private void OnScoreChangedHook(int oldValue, int newValue)
        {
            OnScoreChanged?.Invoke(newValue);
        }
    }
}
