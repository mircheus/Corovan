using System;
using UnityEngine;

namespace FPSGame.Player
{
    public class PlayerHealth : MonoBehaviour
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth;

        [Header("Regeneration")]
        [SerializeField] private bool enableRegeneration = false;
        [SerializeField] private float regenerationRate = 5f;
        [SerializeField] private float regenerationDelay = 5f;

        private float lastDamageTime;
        private bool isDead;

        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public float HealthPercentage => currentHealth / maxHealth;
        public bool IsDead => isDead;

        public event Action<float, float> OnHealthChanged;
        public event Action OnDeath;
        public event Action OnRespawn;

        private void Awake()
        {
            currentHealth = maxHealth;
        }

        private void Update()
        {
            if (enableRegeneration && !isDead)
            {
                HandleRegeneration();
            }
        }

        private void HandleRegeneration()
        {
            if (currentHealth < maxHealth && Time.time - lastDamageTime >= regenerationDelay)
            {
                Heal(regenerationRate * Time.deltaTime);
            }
        }

        public void TakeDamage(float damage)
        {
            if (isDead || damage <= 0)
                return;

            lastDamageTime = Time.time;
            float previousHealth = currentHealth;
            currentHealth = Mathf.Max(0, currentHealth - damage);

            OnHealthChanged?.Invoke(previousHealth, currentHealth);

            if (currentHealth <= 0)
            {
                Die();
            }
        }

        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
        {
            TakeDamage(damage);
        }

        public void Heal(float amount)
        {
            if (isDead || amount <= 0)
                return;

            float previousHealth = currentHealth;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);

            if (currentHealth != previousHealth)
            {
                OnHealthChanged?.Invoke(previousHealth, currentHealth);
            }
        }

        public void SetHealth(float health)
        {
            float previousHealth = currentHealth;
            currentHealth = Mathf.Clamp(health, 0, maxHealth);

            OnHealthChanged?.Invoke(previousHealth, currentHealth);

            if (currentHealth <= 0 && !isDead)
            {
                Die();
            }
        }

        private void Die()
        {
            if (isDead)
                return;

            isDead = true;
            OnDeath?.Invoke();

            Debug.Log($"{gameObject.name} has died.");
        }

        public void Respawn()
        {
            isDead = false;
            currentHealth = maxHealth;

            OnHealthChanged?.Invoke(0, currentHealth);
            OnRespawn?.Invoke();
        }

        public void ResetHealth()
        {
            currentHealth = maxHealth;
            isDead = false;
            OnHealthChanged?.Invoke(0, currentHealth);
        }
    }
}
