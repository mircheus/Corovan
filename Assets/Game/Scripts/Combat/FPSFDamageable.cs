using UnityEngine;
using Mirror;
using Akila.FPSFramework;
using FPSGame.Player;

namespace FPSGame.Combat
{
    /// <summary>
    /// Presents one of our IDamageReceivers to the FPS Framework as an IDamageable, so FPSF weapons,
    /// hit boxes (HealthGroup) and HUD can see it. The receiver stays the single authority: damage is
    /// int, applied on the server only, through its own rules (LifeState, vehicle health, ...).
    ///
    /// Put it explicitly on objects whose FPSF HealthGroups look it up in Start (the ragdoll bots);
    /// everything else gets one added on first hit through Firearm.ResolveDamageable.
    /// </summary>
    public class FPSFDamageable : MonoBehaviour, IDamageable
    {
        private IDamageReceiver receiver;
        private Actor actor;
        private Ragdoll ragdoll;

        public bool deadConfirmed { get; set; }
        // Written by FPSF weapons just before Damage(); travels with the hit to the server.
        public Vector3 deathForce { get; set; }

        public float MaxHealth
        {
            get => receiver != null ? receiver.MaxHealth : 0;
            set { } // the receiver owns its max health
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterResolver()
        {
            Firearm.ResolveDamageable = Resolve;
        }

        // Finds the receiver above a hit collider and hands FPSF an adapter for it.
        private static IDamageable Resolve(Transform hit)
        {
            var found = hit.GetComponentInParent<IDamageReceiver>() as Component;
            if (found == null)
                return null;

            var adapter = found.GetComponent<FPSFDamageable>();
            return adapter != null ? adapter : found.gameObject.AddComponent<FPSFDamageable>();
        }

        private void Awake()
        {
            receiver = GetComponentInParent<IDamageReceiver>();
            actor = GetComponent<Actor>();
            ragdoll = GetComponentInParent<Ragdoll>();
        }

        public Actor GetActor() => actor;
        public float GetHealth() => receiver != null ? receiver.CurrentHealth : 0;
        public bool IsDead() => receiver == null || receiver.IsDead;
        public int GetGroupsCount() => 0;
        public Ragdoll GetRagdoll() => ragdoll;

        public void Damage(float amount, Actor damageSource)
        {
            if (receiver == null)
                return;

            int damage = Mathf.RoundToInt(amount);
            Vector3 force = deathForce;
            deathForce = Vector3.zero;

            PlayerHitReporter attacker = damageSource != null ? damageSource.GetComponent<PlayerHitReporter>() : null;

            // On the server (an explosion, or the host's own weapon): apply directly, still through the
            // attacker's friendly-fire rules when a player is behind it.
            if (NetworkServer.active)
            {
                if (attacker != null)
                    attacker.ServerApplyHit(receiver, damage, force);
                else
                    DamageRouter.ServerApplyHit(receiver, damage, force);
                return;
            }

            // A client: only our own player's FPSF weapons run here (remote players' FPSF never does).
            // Report through the player so the server validates it.
            if (attacker != null && attacker == PlayerHitReporter.Local)
                attacker.ReportHit(receiver, damage, force);
        }
    }
}
