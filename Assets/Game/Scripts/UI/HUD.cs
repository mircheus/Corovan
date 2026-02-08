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

        [Header("Hit Marker")]
        [SerializeField] private GameObject hitMarker;
        [SerializeField] private float hitMarkerDuration = 0.1f;

        private float hitMarkerTimer;

        private void Start()
        {
            if (playerHealth != null)
            {
                playerHealth.OnHealthChanged += UpdateHealthUI;
                UpdateHealthUI(playerHealth.CurrentHealth, playerHealth.CurrentHealth);
            }

            if (playerShooting != null)
            {
                playerShooting.OnAmmoChanged += UpdateAmmoUI;
                playerShooting.OnReloadStarted += ShowReloading;
                playerShooting.OnReloadFinished += HideReloading;
            }

            if (hitMarker != null)
            {
                hitMarker.SetActive(false);
            }

            if (reloadingIndicator != null)
            {
                reloadingIndicator.SetActive(false);
            }
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

        public void SetPlayerReferences(PlayerHealth health, PlayerShooting shooting)
        {
            playerHealth = health;
            playerShooting = shooting;

            if (playerHealth != null)
            {
                playerHealth.OnHealthChanged += UpdateHealthUI;
            }

            if (playerShooting != null)
            {
                playerShooting.OnAmmoChanged += UpdateAmmoUI;
                playerShooting.OnReloadStarted += ShowReloading;
                playerShooting.OnReloadFinished += HideReloading;
            }
        }

        private void OnDestroy()
        {
            if (playerHealth != null)
            {
                playerHealth.OnHealthChanged -= UpdateHealthUI;
            }

            if (playerShooting != null)
            {
                playerShooting.OnAmmoChanged -= UpdateAmmoUI;
                playerShooting.OnReloadStarted -= ShowReloading;
                playerShooting.OnReloadFinished -= HideReloading;
            }
        }
    }
}
