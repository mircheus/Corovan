using UnityEngine;
using Mirror;
using Akila.FPSFramework;

namespace FPSGame.Player
{
    /// <summary>
    /// Owner-side "you got hurt" feedback: flashes the FPS Framework damage indicator (red screen
    /// edges, stronger for bigger hits) and shakes the camera, whenever PlayerHealth drops.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public class PlayerDamageFeedback : NetworkBehaviour
    {
        [Tooltip("Indicator alpha for the smallest hit.")]
        [SerializeField] private float minAlpha = 0.35f;
        [Tooltip("A hit of this fraction of max health or more flashes at full alpha.")]
        [SerializeField] private float fullAlphaFraction = 0.4f;
        [SerializeField] private float cameraShake = 0.3f;

        private PlayerHealth health;
        private CharacterManager character;

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
            character = GetComponent<CharacterManager>();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            health.OnHealthChanged += OnHealthChanged;
        }

        private void OnDestroy()
        {
            if (health != null)
                health.OnHealthChanged -= OnHealthChanged;
        }

        private void OnHealthChanged(int oldHealth, int newHealth)
        {
            int damage = oldHealth - newHealth;
            if (damage <= 0 || health.MaxHealth <= 0)
                return;

            float fraction = (float)damage / health.MaxHealth;
            float alpha = Mathf.Lerp(minAlpha, 1f, Mathf.Clamp01(fraction / fullAlphaFraction));

            DamageIndicator indicator = UIManager.Instance != null ? UIManager.Instance.DamageIndicator : null;
            if (indicator != null)
                indicator.Show(alpha);

            if (character != null && character.cameraManager != null)
                character.cameraManager.ShakeCameras(cameraShake * alpha);
        }
    }
}
