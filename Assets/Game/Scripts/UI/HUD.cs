using FPSGame.Networking;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FPSGame.Player;

namespace FPSGame.UI
{
    public class HUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private PlayerShooting playerShooting;
        [SerializeField] private PlayerInputHandler playerInputHandler;

        [Header("Health UI")]
        [SerializeField] private Slider healthBar;
        [SerializeField] private TextMeshProUGUI healthText;
        [SerializeField] private Image healthFill;
        [SerializeField] private Color healthyColor = Color.green;
        [SerializeField] private Color lowHealthColor = Color.red;
        [SerializeField] private float lowHealthThreshold = 0.25f;

        [Header("Ammo UI")]
        [SerializeField] private TextMeshProUGUI ammoText;
        [SerializeField] private TextMeshProUGUI reserveAmmoText;
        [SerializeField] private GameObject reloadingIndicator;

        [Header("Crosshair")]
        [SerializeField] private RectTransform crosshair;
        [SerializeField] private Image[] crosshairLines;
        [SerializeField] private float crosshairBaseSize = 20f;
        [SerializeField] private float crosshairMaxSpread = 60f;

        [Header("Interact Progress")]
        [SerializeField] private GameObject interactProgressRoot;
        [SerializeField] private Slider interactProgressBar;

        [Header("Hit Marker")]
        [SerializeField] private GameObject hitMarker;
        [SerializeField] private float hitMarkerDuration = 0.1f;
        
        [Header("Score")]
        [SerializeField] TextMeshProUGUI scoreText;

        private float hitMarkerTimer;
        private ScoreManager _scoreManager;

        private void Start()
        {
            _scoreManager = ScoreManager.Instance;

            if (_scoreManager != null)
            {
                _scoreManager.OnScoreChanged += OnScoreChanged;
                scoreText.text = _scoreManager.TotalScore.ToString();
            }
            
            if (playerHealth != null)
            {
                playerHealth.OnHealthChanged += UpdateHealthUI;
                UpdateHealthUI(playerHealth.CurrentHealth, playerHealth.CurrentHealth);
            }

            if (playerShooting != null)
            {
                playerShooting.OnAmmoChangedEvent += UpdateAmmoUI;
                playerShooting.OnReloadStarted += ShowReloading;
                playerShooting.OnReloadFinished += HideReloading;
            }

            if (playerInputHandler != null)
            {
                playerInputHandler.OnInteractProgress += UpdateInteractProgress;
                playerInputHandler.OnInteractCancelled += HideInteractProgress;
                playerInputHandler.OnInteractFinished += HideInteractProgress;
            }

            if (hitMarker != null)
                hitMarker.SetActive(false);

            if (reloadingIndicator != null)
                reloadingIndicator.SetActive(false);

            HideInteractProgress();
        }

        private void OnScoreChanged(int amount)
        {
            scoreText.text = _scoreManager.TotalScore.ToString();
        }

        private void Update()
        {
            UpdateHitMarker();
            UpdateCrosshair();
        }

        private void UpdateHealthUI(float previousHealth, float currentHealth)
        {
            if (healthBar != null)
            {
                healthBar.value = playerHealth.HealthPercentage;
            }

            if (healthText != null)
            {
                healthText.text = $"{Mathf.CeilToInt(currentHealth)}";
            }

            if (healthFill != null)
            {
                healthFill.color = playerHealth.HealthPercentage <= lowHealthThreshold
                    ? lowHealthColor
                    : Color.Lerp(lowHealthColor, healthyColor, playerHealth.HealthPercentage);
            }
        }

        private void UpdateAmmoUI(int currentAmmo, int reserveAmmo)
        {
            if (ammoText != null)
            {
                ammoText.text = currentAmmo.ToString();
            }

            if (reserveAmmoText != null)
            {
                reserveAmmoText.text = reserveAmmo.ToString();
            }
        }

        private void UpdateInteractProgress(float t)
        {
            if (interactProgressRoot != null)
                interactProgressRoot.SetActive(true);

            if (interactProgressBar != null)
                interactProgressBar.value = t;
        }

        private void HideInteractProgress()
        {
            if (interactProgressRoot != null)
                interactProgressRoot.SetActive(false);

            if (interactProgressBar != null)
                interactProgressBar.value = 0f;
        }

        private void ShowReloading()
        {
            if (reloadingIndicator != null)
            {
                reloadingIndicator.SetActive(true);
            }
        }

        private void HideReloading()
        {
            if (reloadingIndicator != null)
            {
                reloadingIndicator.SetActive(false);
            }
        }

        private void UpdateCrosshair()
        {
            // Placeholder for dynamic crosshair spread based on movement/shooting
        }

        private void UpdateHitMarker()
        {
            if (hitMarker == null)
                return;

            if (hitMarkerTimer > 0)
            {
                hitMarkerTimer -= Time.deltaTime;
                if (hitMarkerTimer <= 0)
                {
                    hitMarker.SetActive(false);
                }
            }
        }

        public void ShowHitMarker()
        {
            if (hitMarker != null)
            {
                hitMarker.SetActive(true);
                hitMarkerTimer = hitMarkerDuration;
            }
        }

        public void SetPlayerReferences(PlayerHealth health, PlayerShooting shooting, PlayerInputHandler inputHandler = null)
        {
            playerHealth = health;
            playerShooting = shooting;

            if (playerHealth != null)
                playerHealth.OnHealthChanged += UpdateHealthUI;

            if (playerShooting != null)
            {
                playerShooting.OnAmmoChangedEvent += UpdateAmmoUI;
                playerShooting.OnReloadStarted += ShowReloading;
                playerShooting.OnReloadFinished += HideReloading;
            }

            if (inputHandler != null)
            {
                playerInputHandler = inputHandler;
                playerInputHandler.OnInteractProgress += UpdateInteractProgress;
                playerInputHandler.OnInteractCancelled += HideInteractProgress;
            }
        }

        private void OnDestroy()
        {
            if (_scoreManager != null)
            {
                _scoreManager.OnScoreChanged -= OnScoreChanged;
            }
            
            if (playerHealth != null)
                playerHealth.OnHealthChanged -= UpdateHealthUI;

            if (playerShooting != null)
            {
                playerShooting.OnAmmoChangedEvent -= UpdateAmmoUI;
                playerShooting.OnReloadStarted -= ShowReloading;
                playerShooting.OnReloadFinished -= HideReloading;
            }

            if (playerInputHandler != null)
            {
                playerInputHandler.OnInteractProgress -= UpdateInteractProgress;
                playerInputHandler.OnInteractCancelled -= HideInteractProgress;
                playerInputHandler.OnInteractFinished -= HideInteractProgress;
            }
        }
    }
}
