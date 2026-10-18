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

            // Our own player's FPSF weapon: report through the player so the server validates it
            // and applies friendly-fire rules. (Remote players' FPSF never runs, so a client only ever
            // sees its own player's hits here.)
            PlayerHitReporter local = PlayerHitReporter.Local;
            if (local != null && damageSource != null && damageSource.gameObject == local.gameObject)
            {
                local.ReportHit(receiver, damage, force);
                return;
            }

            // Server-side FPSF damage with no player behind it (e.g. an explosion simulated on the server).
            if (NetworkServer.active)
                DamageRouter.ServerApplyHit(receiver, damage, force);
        }
    }
}
