using System;
using UnityEngine;
using Mirror;
using FPSGame.Combat;
using FPSGame.Config;
using FPSGame.Networking;

namespace FPSGame.Player
{
    // Alive -> Downed -> Alive. A player whose health reaches zero goes down instead of dying:
    // a teammate can revive them (PlayerRevive) until the bleedout timer runs out, then they
    // respawn (PlayerInputHandler.ServerRelocateAfterRespawn picks the spot). Dead is reached
    // only by an instant kill (or a bleedout after the match ended) and lasts deadRespawnDelay.
    // Crashed = fell off onto the road (FallHazardVolume): out of the game for deadRespawnDelay, then
    // back with the health the fall left them — not a death, but incapacitated like one.
    public enum LifeState : byte
    {
        Alive = 0,
        Downed = 1,
        Dead = 2,
        Crashed = 3
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
        [Tooltip("Seconds a downed player can be revived. Then they respawn (or die, with Respawn On Bleedout off). " +
                 "Zero or less = no downed state, damage kills outright.")]
        [SerializeField] private float bleedoutDuration = 15f;
        [Tooltip("Fraction of max health restored on revive.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float reviveHealthPercent = 0.5f;

        [Header("Respawn")]
        [Tooltip("On: a bleedout that runs out respawns the player (full health) instead of killing them.")]
        [SerializeField] private bool respawnOnBleedout = true;
        [Tooltip("Seconds a player killed outright (fall, exploding car) stays dead before respawning, " +
                 "and a player who crashed onto the road stays out. Below zero = the dead never respawn " +
                 "(a crash still comes back, at once).")]
        [SerializeField] private float deadRespawnDelay = 15f;

        // NetworkTime.time at which a dead or crashed player respawns; 0 = no respawn scheduled. A
        // deadline, like the bleedout, so clients derive the countdown themselves.
        [SyncVar]
        private double respawnTime;

        // NetworkTime.time at which bleedout runs out. Synced once per transition rather
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
        public bool IsCrashed => lifeState == LifeState.Crashed;

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

        /// <summary>Seconds left before a downed player respawns (or dies). Zero when not downed.</summary>
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

        /// <summary>True while a dead or crashed player is waiting for a scheduled respawn.</summary>
        public bool RespawnPending => (lifeState == LifeState.Dead || lifeState == LifeState.Crashed) && respawnTime > 0;

        /// <summary>Seconds left before a dead or crashed player respawns. Zero when none is scheduled.</summary>
        public float RespawnRemaining => RespawnPending ? Mathf.Max(0f, (float)(respawnTime - NetworkTime.time)) : 0f;

        public event Action<int, int> OnHealthChanged;
        public event Action OnDowned;
        public event Action OnRevived;
        public event Action OnDeath;
        /// <summary>Fell off onto the road. Ends in <see cref="OnRespawn"/>.</summary>
        public event Action OnCrashed;
        /// <summary>Back in the game from Dead or Crashed (from Downed it is <see cref="OnRevived"/>).</summary>
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

            if (lifeState == LifeState.Dead || lifeState == LifeState.Crashed)
            {
                TickRespawn();
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

            if (NetworkTime.time < bleedoutEndTime)
                return;

            // Ending the match doesn't stop the world, but nobody comes back once it's decided —
            // a respawn would hand input back under the end screen.
            if (respawnOnBleedout && !IsMatchOver)
                ServerRespawn();
            else
                Die();
        }

        [Server]
        private void TickRespawn()
        {
            if (respawnTime <= 0 || NetworkTime.time < respawnTime)
                return;

            if (IsMatchOver)
            {
                respawnTime = 0;
                return;
            }

            ServerRespawn();
        }

        private static bool IsMatchOver => GameStateManager.Instance != null && GameStateManager.Instance.IsMatchOver;

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
                case LifeState.Crashed:
                    OnCrashed?.Invoke();
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

        /// <summary>
        /// Fell off onto the road (<see cref="PlayerFallHazard"/>): the fall costs <paramref name="damage"/>
        /// and the player is out (Crashed) for <c>deadRespawnDelay</c>, then <see cref="ServerRespawn"/>
        /// brings them back with the health they had left. A fall that would empty their health, an
        /// instant-kill volume, or a fall while downed is a death instead (full health on the respawn).
        /// </summary>
        [Server]
        public void ServerCrash(int damage, bool instantKill)
        {
            if (lifeState == LifeState.Dead || lifeState == LifeState.Crashed)
                return;

            damage = Mathf.Max(0, damage);
            if (instantKill || lifeState == LifeState.Downed || currentHealth - damage <= 0)
            {
                Die();
                return;
            }

            currentHealth -= damage;
            lastDamageTime = Time.time;
            regenBuffer = 0f;
            respawnTime = IsMatchOver ? 0 : NetworkTime.time + Mathf.Max(0f, deadRespawnDelay);
            SetLifeState(LifeState.Crashed);

            Debug.Log($"{gameObject.name} crashed onto the road.");
        }

        [Server]
        private void Die()
        {
            if (lifeState == LifeState.Dead)
                return;

            currentHealth = 0;
            bleedoutPaused = false;
            bleedoutRemaining = 0f;
            respawnTime = deadRespawnDelay >= 0f && !IsMatchOver ? NetworkTime.time + deadRespawnDelay : 0;
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

        /// <summary>
        /// Back on their feet, from Downed (bleedout ran out) or Dead (respawn delay passed) with full
        /// health, or from Crashed with what the fall left them. Then moved: a free passenger seat in a
        /// car another player drives, else the nearest
        /// <see cref="PlayerRespawnPoint"/>, else a spawn point. The state flips first — a passenger
        /// seat frees itself from an incapacitated occupant.
        /// <para>
        /// From Downed, clients see Downed -> Alive and raise <see cref="OnRevived"/>; from Dead or
        /// Crashed they raise <see cref="OnRespawn"/>. The move itself is the owner's TargetRpc either way.
        /// </para>
        /// </summary>
        [Server]
        public void ServerRespawn()
        {
            if (lifeState == LifeState.Alive)
                return;

            if (lifeState != LifeState.Crashed)
                currentHealth = maxHealth;
            bleedoutPaused = false;
            bleedoutRemaining = 0f;
            respawnTime = 0;
            regenBuffer = 0f;
            lastDamageTime = Time.time;
            SetLifeState(LifeState.Alive);

            if (TryGetComponent(out PlayerInputHandler input))
                input.ServerRelocateAfterRespawn();

            Debug.Log($"{gameObject.name} respawned.");
        }

        [Server]
        public void ResetHealth()
        {
            currentHealth = maxHealth;
            bleedoutPaused = false;
            bleedoutRemaining = 0f;
            respawnTime = 0;
            SetLifeState(LifeState.Alive);
        }

        [Command]
        public void CmdTakeDamage(int damage)
        {
            TakeDamage(damage);
        }
    }
}
