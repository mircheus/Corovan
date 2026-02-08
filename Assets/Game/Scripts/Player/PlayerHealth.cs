using System;
using UnityEngine;
using Mirror;

namespace FPSGame.Player
{
    public class PlayerHealth : NetworkBehaviour
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;

        [SyncVar(hook = nameof(OnHealthChangedHook))]
        private float currentHealth;

        [SyncVar]
        private bool isDead;

        [Header("Regeneration")]
        [SerializeField] private bool enableRegeneration = false;
        [SerializeField] private float regenerationRate = 5f;
        [SerializeField] private float regenerationDelay = 5f;

        private float lastDamageTime;

        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public float HealthPercentage => currentHealth / maxHealth;
        public bool IsDead => isDead;

        public event Action<float, float> OnHealthChanged;
        public event Action OnDeath;
        public event Action OnRespawn;

        public override void OnStartServer()
        {
            base.OnStartServer();
            currentHealth = maxHealth;
            isDead = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            OnHealthChanged?.Invoke(currentHealth, currentHealth);
        }

        private void Update()
        {
            if (!isServer)
                return;

            if (enableRegeneration && !isDead)
            {
                HandleRegeneration();
            }
        }

        private void HandleRegeneration()
        {
            if (currentHealth < maxHealth && Time.time - lastDamageTime >= regenerationDelay)
            {
                currentHealth = Mathf.Min(maxHealth, currentHealth + regenerationRate * Time.deltaTime);
            }
        }

        private void OnHealthChangedHook(float oldHealth, float newHealth)
        {
            OnHealthChanged?.Invoke(oldHealth, newHealth);

            if (newHealth <= 0 && oldHealth > 0)
            {
                OnDeath?.Invoke();
            }
        }

        [Server]
        public void TakeDamage(float damage)
        {
            if (isDead || damage <= 0)
                return;

            lastDamageTime = Time.time;
            currentHealth = Mathf.Max(0, currentHealth - damage);

            if (currentHealth <= 0)
            {
                Die();
            }
        }

        [Server]
        public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection)
        {
            TakeDamage(damage);
        }

        [Server]
        public void Heal(float amount)
        {
            if (isDead || amount <= 0)
                return;

            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        }

        [Server]
        public void SetHealth(float health)
        {
            currentHealth = Mathf.Clamp(health, 0, maxHealth);

            if (currentHealth <= 0 && !isDead)
            {
                Die();
            }
        }

        [Server]
        private void Die()
        {
            if (isDead)
                return;

            isDead = true;
            RpcOnDeath();
            Debug.Log($"{gameObject.name} has died.");
        }

        [ClientRpc]
        private void RpcOnDeath()
        {
            OnDeath?.Invoke();
        }

        [Server]
        public void Respawn()
        {
            isDead = false;
            currentHealth = maxHealth;
            RpcOnRespawn();
        }

        [ClientRpc]
        private void RpcOnRespawn()
        {
            OnRespawn?.Invoke();
        }

        [Server]
        public void ResetHealth()
        {
            currentHealth = maxHealth;
            isDead = false;
        }

        [Command]
        public void CmdTakeDamage(float damage)
        {
            TakeDamage(damage);
        }
    }
}
