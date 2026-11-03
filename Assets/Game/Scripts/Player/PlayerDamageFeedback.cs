using UnityEngine;
using Mirror;
using Akila.FPSFramework;
using FPSGame.Vehicle;

namespace FPSGame.Player
{
    /// <summary>
    /// Owner-side "you got hurt" feedback: flashes the FPS Framework damage indicator (red screen
    /// edges, stronger for bigger hits) and shakes the camera, whenever PlayerHealth drops — or,
    /// while driving, whenever the driven car's health drops.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public class PlayerDamageFeedback : NetworkBehaviour
    {
        [Tooltip("Indicator alpha for the smallest hit.")]
        [SerializeField] private float minAlpha = 0.35f;
        [Tooltip("A hit of this fraction of max health or more flashes at full alpha.")]
        [SerializeField] private float fullAlphaFraction = 0.4f;
        [SerializeField] private float cameraShake = 0.3f;

        [Header("Driving")]
        [Tooltip("A car hit of this fraction of the car's max health or more flashes at full alpha.")]
        [SerializeField] private float vehicleFullAlphaFraction = 0.25f;
        [SerializeField] private float vehicleCameraShake = 0.4f;

        private PlayerHealth health;
        private CharacterManager character;
        private PlayerInputHandler inputHandler;
        private CarController watchedVehicle;

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
            character = GetComponent<CharacterManager>();
            inputHandler = GetComponent<PlayerInputHandler>();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            health.OnHealthChanged += OnHealthChanged;
            if (inputHandler != null)
                inputHandler.OnVehicleChanged += WatchVehicle;
        }

        private void OnDestroy()
        {
            if (health != null)
                health.OnHealthChanged -= OnHealthChanged;
            if (inputHandler != null)
                inputHandler.OnVehicleChanged -= WatchVehicle;
            WatchVehicle(null);
        }

        private void WatchVehicle(CarController vehicle)
        {
            if (watchedVehicle != null)
                watchedVehicle.OnVehicleHealthChanged -= OnVehicleHealthChanged;
            watchedVehicle = vehicle;
            if (watchedVehicle != null)
                watchedVehicle.OnVehicleHealthChanged += OnVehicleHealthChanged;
        }

        private void OnHealthChanged(int oldHealth, int newHealth)
        {
            Flash(oldHealth - newHealth, health.MaxHealth, fullAlphaFraction, cameraShake);
        }

        private void OnVehicleHealthChanged(int oldHealth, int newHealth)
        {
            if (watchedVehicle == null)
                return;
            Flash(oldHealth - newHealth, watchedVehicle.MaxVehicleHealth, vehicleFullAlphaFraction, vehicleCameraShake);
        }

        private void Flash(int damage, int maxHealth, float fullFraction, float shake)
        {
            if (damage <= 0 || maxHealth <= 0)
                return;

            float fraction = (float)damage / maxHealth;
            float alpha = Mathf.Lerp(minAlpha, 1f, Mathf.Clamp01(fraction / fullFraction));

            DamageIndicator indicator = UIManager.Instance != null ? UIManager.Instance.DamageIndicator : null;
            if (indicator != null)
                indicator.Show(alpha);

            if (character != null && character.cameraManager != null)
                character.cameraManager.ShakeCameras(shake * alpha);
        }
    }
}
