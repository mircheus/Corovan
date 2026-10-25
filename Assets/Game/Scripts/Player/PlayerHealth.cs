using System;
using UnityEngine;
using Mirror;
using FPSGame.Combat;
using FPSGame.Config;

namespace FPSGame.Player
{
    // Alive -> Downed -> Dead. A player whose health reaches zero goes down instead of dying:
    // a teammate can revive them (PlayerRevive) until the bleedout timer runs out.
    public enum LifeState : byte
    {
        Alive = 0,
        Downed = 1,
        Dead = 2
    }

    public class PlayerHealth : NetworkBehaviour, IDamageReceiver
    {
        [Header("Health Settings")]
        [Tooltip("Default; the scene's BalanceConfig overrides it on the server. Synced so client HUDs divide by the same value.")]
        [SyncVar]
        [SerializeField] private int maxHealth = 100;

        [SyncVar(hook = nameof(OnHealthChangedHook))]
        private int currentHealth;

        // Single source of truth for alive/downed/dead. One SyncVar (rather than two bools) keeps
        // the client-side transitions unambiguous — hook order can never report "revived" when the
        // player was actually finished off while down.
        [SyncVar(hook = nameof(OnLifeStateChangedHook))]
        private LifeState lifeState = LifeState.Alive;

        [Header("Downed State")]
        [Tooltip("Seconds a downed player survives before dying for good. Zero or less = no downed state, damage kills outright.")]
        [SerializeField] private float bleedoutDuration = 30f;
        [Tooltip("Fraction of max health restored on revive.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float reviveHealthPercent = 0.5f;

        // NetworkTime.time at which bleedout kills the player. Synced once per transition rather
        // than ticking a float every frame; clients derive the countdown themselves.
        [SyncVar]
        private double bleedoutEndTime;

        // A revive in progress freezes the countdown. The frozen remainder lives here.
        [SyncVar]
        private bool bleedoutPaused;

        [SyncVar]
        private float bleedoutRemaining;

        // Owned by CarController on the server (not by the client-side visual swap in
        // SetDriverVisible), so enemy targeting reads the same truth as HasDriver does.
        [SyncVar]
        private bool isDriving;

        [Header("Regeneration")]
        [SerializeField] private bool enableRegeneration = false;
        [SerializeField] private float regenerationRate = 5f;
        [SerializeField] private float regenerationDelay = 5f;

        private float lastDamageTime;
        private float regenBuffer;

        public int MaxHealth => maxHealth;
        public int CurrentHealth => currentHealth;
        public float HealthPercentage => (float)currentHealth / maxHealth;

        public LifeState State => lifeState;
        public bool IsDead => lifeState == LifeState.Dead;
        public bool IsDowned => lifeState == LifeState.Downed;

        /// <summary>Down or dead — out of the fight either way. Use for "can this player act / hold a console" checks.</summary>
        public bool IsIncapacitated => lifeState != LifeState.Alive;

        /// <summary>
        /// Enemies only acquire and keep targets that are alive and on their feet — a driver is
        /// neither. While driving, the player object is frozen at the spot they entered the car with
        /// its renderers and CharacterController off; an enemy locked onto it would shoot a ghost and,
        /// worse, never go idle again, so it would never re-acquire the car the player is actually in.
        /// The car is the target instead.
        /// </summary>
        public bool IsTargetable => lifeState == LifeState.Alive && !isDriving;

        /// <summary>Server-authoritative seat state, set by <c>CarController.ServerEnter/ServerExit</c>.</summary>
        public bool IsDriving => isDriving;

        [Server]
        public void ServerSetDriving(bool driving)
        {
            isDriving = driving;
        }

        public float BleedoutDuration => bleedoutDuration;
        public bool BleedoutPaused => bleedoutPaused;

        /// <summary>Seconds left before a downed player dies. Zero when not downed.</summary>
        public float BleedoutRemaining
        {
            get
            {
                if (lifeState != LifeState.Downed)
                    return 0f;
                if (bleedoutPaused)
                    return Mathf.Max(0f, bleedoutRemaining);
                return Mathf.Max(0f, (float)(bleedoutEndTime - NetworkTime.time));
            }
        }

        public event Action<int, int> OnHealthChanged;
        public event Action OnDowned;
        public event Action OnRevived;
        public event Action OnDeath;
        public event Action OnRespawn;

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (SceneBalance.Current != null)
                maxHealth = SceneBalance.Current.player.maxHealth;
            currentHealth = maxHealth;
            lifeState = LifeState.Alive;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            OnHealthChanged?.Invoke(currentHealth, currentHealth);
            // Life-state listeners subscribe after this runs, so a client that joins while someone
            // is already down reads IsDowned directly instead of waiting for a transition event.
        }

        private void Update()
        {
            if (!isServer)
                return;

            if (lifeState == LifeState.Downed)
            {
                TickBleedout();
                return;
            }

            if (enableRegeneration && lifeState == LifeState.Alive)
            {
                HandleRegeneration();
            }
        }

        [Server]
        private void TickBleedout()
        {
            if (bleedoutPaused)
                return;

            if (NetworkTime.time >= bleedoutEndTime)
                Die();
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
        }

        private void OnLifeStateChangedHook(LifeState oldState, LifeState newState)
        {
            RaiseLifeStateEvents(oldState, newState);
        }

        // Hooks run on clients (host included). A dedicated server has no hook, so the setter
        // raises the events there instead — exactly once, either way.
        private void RaiseLifeStateEvents(LifeState oldState, LifeState newState)
        {
            if (oldState == newState)
                return;

            switch (newState)
            {
                case LifeState.Downed:
                    OnDowned?.Invoke();
                    break;
                case LifeState.Dead:
                    OnDeath?.Invoke();
                    break;
                case LifeState.Alive:
                    if (oldState == LifeState.Downed)
                        OnRevived?.Invoke();
                    else
                        OnRespawn?.Invoke();
                    break;
            }
        }

        [Server]
        private void SetLifeState(LifeState newState)
        {
            LifeState oldState = lifeState;
            if (oldState == newState)
                return;

            lifeState = newState;

            if (isServerOnly)
                RaiseLifeStateEvents(oldState, newState);
        }

        [Server]
        public void TakeDamage(int damage)
        {
            // A downed player's fate is the bleedout timer alone — stray splash damage from barrels
            // and projectiles must not shorten it.
            if (lifeState != LifeState.Alive || damage <= 0)
                return;

            lastDamageTime = Time.time;
            currentHealth = Mathf.Max(0, currentHealth - damage);

            if (currentHealth <= 0)
            {
                GoDown();
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
            if (lifeState != LifeState.Alive || amount <= 0)
                return;

            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        }

        [Server]
        public void SetHealth(int health)
        {
            if (lifeState != LifeState.Alive)
                return;

            currentHealth = Mathf.Clamp(health, 0, maxHealth);

            if (currentHealth <= 0)
            {
                GoDown();
            }
        }

        // Enters the downed state, or dies outright when the downed state is switched off.
        [Server]
        private void GoDown()
        {
            if (lifeState != LifeState.Alive)
                return;

            if (bleedoutDuration <= 0f)
            {
                Die();
                return;
            }

            regenBuffer = 0f;
            bleedoutPaused = false;
            bleedoutRemaining = bleedoutDuration;
            bleedoutEndTime = NetworkTime.time + bleedoutDuration;
            SetLifeState(LifeState.Downed);

            Debug.Log($"{gameObject.name} is down.");
        }

        /// <summary>
        /// Kills the player outright, skipping the downed state. For hazards where being revived
        /// makes no sense — bottomless falls, dying inside an exploding car.
        /// </summary>
        [Server]
        public void ServerKillInstant()
        {
            if (lifeState == LifeState.Dead)
                return;

            Die();
        }

        [Server]
        private void Die()
        {
            if (lifeState == LifeState.Dead)
                return;

            currentHealth = 0;
            bleedoutPaused = false;
            bleedoutRemaining = 0f;
            SetLifeState(LifeState.Dead);

            Debug.Log($"{gameObject.name} has died.");
        }

        /// <summary>Called by PlayerRevive once a teammate finishes the hold. Restores a fraction of max health.</summary>
        [Server]
        public void ServerRevive()
        {
            if (lifeState != LifeState.Downed)
                return;

            bleedoutPaused = false;
            bleedoutRemaining = 0f;
            currentHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * reviveHealthPercent));
            lastDamageTime = Time.time;
            SetLifeState(LifeState.Alive);
        }

        /// <summary>
        /// Freezes / resumes the bleedout countdown while a revive is being held, so a player can't
        /// die in the last moments of a hold that was going to save them.
        /// </summary>
        [Server]
        public void ServerSetBleedoutPaused(bool paused)
        {
            if (lifeState != LifeState.Downed || bleedoutPaused == paused)
                return;

            if (paused)
            {
                bleedoutRemaining = Mathf.Max(0f, (float)(bleedoutEndTime - NetworkTime.time));
            }
            else
            {
                bleedoutEndTime = NetworkTime.time + bleedoutRemaining;
            }

            bleedoutPaused = paused;
        }

        [Server]
        public void Respawn()
        {
            currentHealth = maxHealth;
            bleedoutPaused = false;
            bleedoutRemaining = 0f;
            SetLifeState(LifeState.Alive);
        }

        [Server]
        public void ResetHealth()
        {
            currentHealth = maxHealth;
            bleedoutPaused = false;
            bleedoutRemaining = 0f;
            SetLifeState(LifeState.Alive);
        }

        [Command]
        public void CmdTakeDamage(int damage)
        {
            TakeDamage(damage);
        }
    }
}
