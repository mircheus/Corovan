using Mirror;
using FPSGame.Combat;
using UnityEngine;

namespace FPSGame.Environment
{
    public class DestroyableObject : NetworkBehaviour, IDamageReceiver
    {
        int IDamageReceiver.CurrentHealth => currentHealth;
        int IDamageReceiver.MaxHealth => maxHealth;
        bool IDamageReceiver.IsDead => isDestroyed;
        void IDamageReceiver.TakeDamage(int damage) => TakeDamage(damage);

        [Tooltip("Total weapon damage needed to destroy the object.")]
        [SerializeField, Min(1)] private int maxHealth = 100;

        [Header("Destruction FX (played locally on every client)")]
        [Tooltip("Inactive children (Play On Awake) activated once when the object is destroyed. " +
                 "Any number; empty = none.")]
        [SerializeField] private GameObject[] destructionEffects;

        [SyncVar(hook = nameof(OnDestroyedChanged))]
        private bool isDestroyed;

        [SyncVar]
        private int currentHealth;

        // Cached while the effects are still inactive, so hiding the object never hides them.
        private Renderer[] renderers;
        private Collider[] colliders;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            colliders = GetComponentsInChildren<Collider>();
        }

        public override void OnStartServer()
        {
            currentHealth = maxHealth;
        }

        [Server]
        public void TakeDamage(int damage)
        {
            if (isDestroyed || damage <= 0)
                return;

            currentHealth = Mathf.Max(0, currentHealth - damage);

            if (currentHealth == 0)
            {
                // An RPC (not the hook) so a client joining later doesn't replay the effects.
                RpcPlayDestructionEffects();
                isDestroyed = true;
            }
        }

        [ClientRpc]
        private void RpcPlayDestructionEffects()
        {
            DestructionEffects.Play(destructionEffects);
        }

        private void OnDestroyedChanged(bool oldValue, bool newValue)
        {
            if (!newValue)
                return;

            // Hidden, not deactivated: the object stays active so its child effects can play.
            foreach (Renderer r in renderers) r.enabled = false;
            foreach (Collider c in colliders) c.enabled = false;
        }
    }
}
