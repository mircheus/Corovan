using System;
using UnityEngine;
using Mirror;

namespace FPSGame.Player
{
    public class PlayerHealth : NetworkBehaviour
    {
        [Header("Health Settings")]
        [SerializeField] private int maxHealth = 100;

        [SyncVar(hook = nameof(OnHealthChangedHook))]
        private int currentHealth;

        [SyncVar]
        private bool isDead;

        [Header("Regeneration")]
        [SerializeField] private bool enableRegeneration = false;
        [SerializeField] private float regenerationRate = 5f;
        [SerializeField] private float regenerationDelay = 5f;

        private float lastDamageTime;
        private float regenBuffer;

        public int MaxHealth => maxHealth;
        public int CurrentHealth => currentHealth;
        public float HealthPercentage => (float)currentHealth / maxHealth;
        public bool IsDead => isDead;

        public event Action<int, int> OnHealthChanged;
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
                // Health is int; accumulate fractional regen and apply whole points.
                regenBuffer += regenerationRate * Time.deltaTime;
                int wholePoints = Mathf.FloorToInt(regenBuffer);
                if (wholePoints > 0)
                {
                    regenBuffer -= wholePoints;
                    currentHealth = Mathf.Min(maxHealth, currentHealth + wholePoints);
                }
            }
            else
            {
                regenBuffer = 0f;
            }
        }

        private void OnHealthChangedHook(int oldHealth, int newHealth)
        {
            OnHealthChanged?.Invoke(oldHealth, newHealth);

            if (newHealth <= 0 && oldHealth > 0)
            {
                OnDeath?.Invoke();
            }
        }

        [Server]
        public void TakeDamage(int damage)
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
        public void TakeDamage(int damage, Vector3 hitPoint, Vector3 hitDirection)
        {
            TakeDamage(damage);
        }

        [Server]
        public void Heal(int amount)
        {
            if (isDead || amount <= 0)
                return;

            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        }

        [Server]
        public void SetHealth(int health)
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
        public void CmdTakeDamage(int damage)
        {
            TakeDamage(damage);
        }
    }
}
