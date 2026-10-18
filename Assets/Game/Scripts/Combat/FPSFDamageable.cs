using UnityEngine;
using Mirror;
using Akila.FPSFramework;

namespace FPSGame.Player
{
    /// <summary>
    /// Presents PlayerHealth to the FPS Framework as an IDamageable, so FPSF's Actor, HUD and weapons
    /// can see the player. PlayerHealth stays the single authority: damage is int, server-side, and
    /// goes through its LifeState (down / bleedout) rules.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public class PlayerDamageable : MonoBehaviour, IDamageable
    {
        private PlayerHealth health;
        private Actor actor;

        public bool deadConfirmed { get; set; }
        // Recorded by FPSF weapons on the killing hit; drives the death ragdoll's push.
        public Vector3 deathForce { get; set; }

        public float MaxHealth
        {
            get => health.MaxHealth;
            set { } // PlayerHealth owns max health; FPSF only writes this from its own HealthSystem.
        }

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
            actor = GetComponent<Actor>();
        }

        public Actor GetActor() => actor;
        public float GetHealth() => health.CurrentHealth;
        public bool IsDead() => health.IsDead;
        public int GetGroupsCount() => 0;
        public Ragdoll GetRagdoll() => null;

        // Only the server may change health. Client-side FPSF hits are routed to the server by the
        // weapon bridge instead of landing here.
        public void Damage(float amount, Actor damageSource)
        {
            if (!NetworkServer.active)
                return;

            health.TakeDamage(Mathf.RoundToInt(amount));
        }
    }
}
