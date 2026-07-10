using FPSGame.Networking;
using Mirror;
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
        [Tooltip("Optional — resolved from the PlayerHealth object when left empty.")]
        [SerializeField] private PlayerRevive playerRevive;

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

        [Header("Downed")]
        [Tooltip("Shown to this player while they are bleeding out.")]
        [SerializeField] private GameObject downedRoot;
        [SerializeField] private Slider bleedoutBar;
        [SerializeField] private TextMeshProUGUI bleedoutText;
        [Tooltip("Shown on top of the downed overlay while a teammate is picking this player up.")]
        [SerializeField] private GameObject beingRevivedRoot;
        [SerializeField] private Slider reviveProgressBar;

        [Header("Hit Marker")]
        [SerializeField] private GameObject hitMarker;
        [SerializeField] private float hitMarkerDuration = 0.1f;
        
        [Header("Score")]
        [SerializeField] TextMeshProUGUI scoreText;

        private float hitMarkerTimer;
        private ScoreManager _scoreManager;

        // This canvas is part of the player prefab, so every spawned player brings a full-screen
        // overlay along. Only the owner's may live: otherwise a teammate's health bar is drawn
        // pixel-for-pixel on top of yours, and their damage reads as yours.
        private bool BelongsToLocalPlayer()
        {
            NetworkIdentity identity = GetComponentInParent<NetworkIdentity>();

            // A HUD placed directly in the scene has no identity above it — leave it alone.
            return identity == null || identity.isOwned;
        }

        private void Start()
        {
            // Mirror assigns ownership while handling the spawn message, which always completes
            // before Unity gets around to Start. Bail out before subscribing to anything.
            if (!BelongsToLocalPlayer())
            {
                gameObject.SetActive(false);
                return;
            }

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

            ResolvePlayerRevive();

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
            UpdateDownedUI();
        }

        private void ResolvePlayerRevive()
        {
            if (playerRevive == null && playerHealth != null)
                playerRevive = playerHealth.GetComponent<PlayerRevive>();
        }

        private void OnScoreChanged(int amount)
        {
            scoreText.text = _scoreManager.TotalScore.ToString();
        }

        private void Update()
        {
            UpdateHitMarker();
            UpdateCrosshair();
            UpdateDownedUI();
        }

        // The bleedout countdown is derived from a synced deadline rather than streamed, so it is
        // recomputed here every frame. It simply stops moving while a revive holds it paused.
        private void UpdateDownedUI()
        {
            if (playerHealth == null)
                return;

            bool downed = playerHealth.IsDowned;

            if (downedRoot != null && downedRoot.activeSelf != downed)
                downedRoot.SetActive(downed);

            if (!downed)
            {
                if (beingRevivedRoot != null && beingRevivedRoot.activeSelf)
                    beingRevivedRoot.SetActive(false);
                return;
            }

            float remaining = playerHealth.BleedoutRemaining;

            if (bleedoutText != null)
                bleedoutText.text = Mathf.CeilToInt(remaining).ToString();

            if (bleedoutBar != null)
            {
                float duration = playerHealth.BleedoutDuration;
                bleedoutBar.value = duration > 0f ? remaining / duration : 0f;
            }

            bool beingRevived = playerRevive != null && playerRevive.IsBeingRevived;

            if (beingRevivedRoot != null && beingRevivedRoot.activeSelf != beingRevived)
                beingRevivedRoot.SetActive(beingRevived);

            if (beingRevived && reviveProgressBar != null)
                reviveProgressBar.value = playerRevive.ReviveProgress;
        }

        private void UpdateHealthUI(int previousHealth, int currentHealth)
        {
            if (healthBar != null)
            {
                healthBar.value = playerHealth.HealthPercentage;
            }

            if (healthText != null)
            {
                healthText.text = $"{currentHealth}";
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

            ResolvePlayerRevive();

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
