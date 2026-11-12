using FPSGame.Networking;
using Mirror;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FPSGame.Player;
using FPSGame.Vehicle;
using Akila.FPSFramework;

namespace FPSGame.UI
{
    public class HUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerHealth playerHealth;
        [Tooltip("Optional — resolved from the PlayerHealth object when left empty.")]
        [SerializeField] private PlayerLoadout loadout;
        [SerializeField] private PlayerInputHandler playerInputHandler;
        [Tooltip("Optional — resolved from the PlayerHealth object when left empty.")]
        [SerializeField] private PlayerRevive playerRevive;
        [Tooltip("Optional — resolved from the PlayerHealth object when left empty.")]
        [SerializeField] private PlayerCarry playerCarry;
        [Tooltip("Optional — weapon selection panel (owned weapons + the one in hand).")]
        [SerializeField] private WeaponPanelUI weaponPanel;

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

        [Header("Vehicle Gun")]
        [Tooltip("Shown while driving a car that has a CarMachineGun.")]
        [SerializeField] private GameObject vehicleAmmoRoot;
        [SerializeField] private TextMeshProUGUI vehicleAmmoText;
        [Tooltip("{0} = ammo left, {1} = max ammo.")]
        [SerializeField] private string vehicleAmmoFormat = "{0}";

        [Header("Crosshair")]
        [SerializeField] private RectTransform crosshair;
        [SerializeField] private Image[] crosshairLines;
        [SerializeField] private float crosshairBaseSize = 20f;
        [SerializeField] private float crosshairMaxSpread = 60f;

        [Header("Interact Progress")]
        [SerializeField] private GameObject interactProgressRoot;
        [SerializeField] private Slider interactProgressBar;

        [Header("Interact Prompt")]
        [Tooltip("Shown while a nearby object can be interacted with; hidden otherwise.")]
        [SerializeField] private GameObject interactPromptRoot;
        [SerializeField] private TextMeshProUGUI interactPromptText;
        [Tooltip("{0} is replaced by the action verb, e.g. \"Взять\".")]
        [SerializeField] private string interactPromptFormat = "[E] {0}";
        [Tooltip("Colour for an actionable \"[E] …\" prompt.")]
        [SerializeField] private Color promptActionColor = Color.white;
        [Tooltip("Colour for a blocked-requirement warning, e.g. \"Требуется красная карта\".")]
        [SerializeField] private Color promptBlockedColor = new Color(1f, 0.55f, 0.2f);

        [Header("Carry Hint")]
        [Tooltip("Shown while carrying loot; lists the available drop/throw controls.")]
        [SerializeField] private GameObject carryHintRoot;
        [SerializeField] private TextMeshProUGUI carryHintText;
        [SerializeField] private string carryDropHint = "ПКМ — положить";
        [SerializeField] private string carryThrowHint = "ЛКМ — бросить";

        [Header("Downed")]
        [Tooltip("Shown to this player while they are bleeding out.")]
        [SerializeField] private GameObject downedRoot;
        [SerializeField] private Slider bleedoutBar;
        [SerializeField] private TextMeshProUGUI bleedoutText;
        [Tooltip("Shown on top of the downed overlay while a teammate is picking this player up.")]
        [SerializeField] private GameObject beingRevivedRoot;
        [SerializeField] private Slider reviveProgressBar;

        [Header("FPS Framework")]
        [Tooltip("FPSF hit marker + damage indicator (UIManager). Instantiated for the local player only.")]
        [SerializeField] private GameObject fpsfCombatHudPrefab;

        [Header("Hit Marker")]
        [SerializeField] private GameObject hitMarker;
        [SerializeField] private float hitMarkerDuration = 0.1f;
        
        [Header("Score")]
        [SerializeField] TextMeshProUGUI scoreText;

        private float hitMarkerTimer;
        private int shownVehicleAmmo = int.MinValue;
        private GameObject fpsfCombatHud;
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

            // FPSF's UIManager is a singleton the FPSF weapons talk to (hit marker, damage flash):
            // one per client, the local player's.
            if (fpsfCombatHudPrefab != null && fpsfCombatHud == null)
                fpsfCombatHud = Instantiate(fpsfCombatHudPrefab);

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

            if (loadout == null && playerHealth != null)
                loadout = playerHealth.GetComponent<PlayerLoadout>();

            if (weaponPanel == null)
                weaponPanel = GetComponentInChildren<WeaponPanelUI>(true);

            if (loadout != null)
            {
                loadout.OnAmmoChangedEvent += UpdateAmmoUI;
                loadout.OnEquippedItemChanged += ApplyWeaponHud;
                ApplyWeaponHud(loadout.EquippedItem);
                if (weaponPanel != null)
                    weaponPanel.Bind(loadout);
            }

            if (playerInputHandler != null)
            {
                playerInputHandler.OnInteractProgress += UpdateInteractProgress;
                playerInputHandler.OnInteractCancelled += HideInteractProgress;
                playerInputHandler.OnInteractFinished += HideInteractProgress;
                playerInputHandler.OnInteractPromptChanged += UpdateInteractPrompt;
            }

            ResolvePlayerCarry();

            if (playerCarry != null)
                playerCarry.OnCarryingChanged += UpdateCarryHint;

            // Instant death doesn't force-drop loot, so hide the hint on death as well.
            if (playerHealth != null)
                playerHealth.OnDeath += HideCarryHint;

            if (hitMarker != null)
                hitMarker.SetActive(false);

            if (reloadingIndicator != null)
                reloadingIndicator.SetActive(false);

            HideInteractProgress();
            UpdateInteractPrompt(InteractPrompt.None);
            UpdateCarryHint(false);
            UpdateDownedUI();
        }

        private void ResolvePlayerRevive()
        {
            if (playerRevive == null && playerHealth != null)
                playerRevive = playerHealth.GetComponent<PlayerRevive>();
        }

        private void ResolvePlayerCarry()
        {
            if (playerCarry == null && playerHealth != null)
                playerCarry = playerHealth.GetComponent<PlayerCarry>();
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
            UpdateVehicleGunUI();
        }

        // Car ammo is the car's SyncVar (plus the driver's shots in flight): polled while driving.
        private void UpdateVehicleGunUI()
        {
            CarMachineGun gun = playerInputHandler != null ? playerInputHandler.CurrentVehicleGun : null;
            bool show = gun != null;

            if (vehicleAmmoRoot != null && vehicleAmmoRoot.activeSelf != show)
                vehicleAmmoRoot.SetActive(show);

            if (!show)
            {
                shownVehicleAmmo = int.MinValue;
                return;
            }

            int ammo = gun.DisplayAmmo;
            if (ammo == shownVehicleAmmo || vehicleAmmoText == null)
                return;
            shownVehicleAmmo = ammo;
            vehicleAmmoText.text = string.Format(vehicleAmmoFormat, ammo, gun.MaxAmmo);
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

        // A firearm in hand brings FPSF's Weapon HUD (ammo, dynamic crosshair, alerts): hide ours then.
        // The grenade count uses our ammo line; melee and tools show no ammo.
        private void ApplyWeaponHud(LoadoutItem item)
        {
            bool firearm = item != null && item.GetComponent<Firearm>() != null;
            bool throwable = item != null && item.GetComponent<Throwable>() != null;

            if (crosshair != null)
                crosshair.gameObject.SetActive(!firearm);
            if (ammoText != null)
                ammoText.gameObject.SetActive(throwable);
            if (reserveAmmoText != null)
                reserveAmmoText.gameObject.SetActive(false);
            if (reloadingIndicator != null)
                reloadingIndicator.SetActive(false);
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

        // No prompt hides the line; an action shows "[E] <verb>"; a blocked requirement shows its
        // text as-is in the warning colour.
        private void UpdateInteractPrompt(InteractPrompt prompt)
        {
            if (interactPromptRoot != null)
                interactPromptRoot.SetActive(prompt.HasValue);

            if (!prompt.HasValue || interactPromptText == null)
                return;

            interactPromptText.text = prompt.Blocked
                ? prompt.Text
                : string.Format(interactPromptFormat, prompt.Text);
            interactPromptText.color = prompt.Blocked ? promptBlockedColor : promptActionColor;
        }

        // While carrying, list whichever controls this carrier allows (drop / throw).
        private void UpdateCarryHint(bool carrying)
        {
            if (carryHintRoot != null)
                carryHintRoot.SetActive(carrying);

            if (!carrying || carryHintText == null || playerCarry == null)
                return;

            string text = "";
            if (playerCarry.AllowDrop)
                text = carryDropHint;
            if (playerCarry.AllowThrow)
                text = string.IsNullOrEmpty(text) ? carryThrowHint : text + "\n" + carryThrowHint;

            carryHintText.text = text;
        }

        private void HideCarryHint() => UpdateCarryHint(false);

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

        public void SetPlayerReferences(PlayerHealth health, PlayerLoadout playerLoadout, PlayerInputHandler inputHandler = null)
        {
            playerHealth = health;
            loadout = playerLoadout;

            if (playerHealth != null)
                playerHealth.OnHealthChanged += UpdateHealthUI;

            ResolvePlayerRevive();

            if (loadout != null)
            {
                loadout.OnAmmoChangedEvent += UpdateAmmoUI;
                loadout.OnEquippedItemChanged += ApplyWeaponHud;
                ApplyWeaponHud(loadout.EquippedItem);
                if (weaponPanel != null)
                    weaponPanel.Bind(loadout);
            }

            if (inputHandler != null)
            {
                playerInputHandler = inputHandler;
                playerInputHandler.OnInteractProgress += UpdateInteractProgress;
                playerInputHandler.OnInteractCancelled += HideInteractProgress;
                playerInputHandler.OnInteractPromptChanged += UpdateInteractPrompt;
            }
        }

        private void OnDestroy()
        {
            if (_scoreManager != null)
            {
                _scoreManager.OnScoreChanged -= OnScoreChanged;
            }
            
            if (playerHealth != null)
            {
                playerHealth.OnHealthChanged -= UpdateHealthUI;
                playerHealth.OnDeath -= HideCarryHint;
            }

            if (playerCarry != null)
                playerCarry.OnCarryingChanged -= UpdateCarryHint;

            if (loadout != null)
            {
                loadout.OnAmmoChangedEvent -= UpdateAmmoUI;
                loadout.OnEquippedItemChanged -= ApplyWeaponHud;
            }

            if (fpsfCombatHud != null)
                Destroy(fpsfCombatHud);

            if (playerInputHandler != null)
            {
                playerInputHandler.OnInteractProgress -= UpdateInteractProgress;
                playerInputHandler.OnInteractCancelled -= HideInteractProgress;
                playerInputHandler.OnInteractFinished -= HideInteractProgress;
                playerInputHandler.OnInteractPromptChanged -= UpdateInteractPrompt;
            }
        }
    }
}
