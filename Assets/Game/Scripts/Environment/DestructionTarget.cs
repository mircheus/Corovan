using System;
using Mirror;
using FPSGame.Combat;
using UnityEngine;

namespace FPSGame.Environment
{
    public class DestructionTarget : NetworkBehaviour, IDamageReceiver
    {
        int IDamageReceiver.CurrentHealth => currentHealth;
        int IDamageReceiver.MaxHealth => maxHealth;
        bool IDamageReceiver.IsDead => isDestroyed;
        void IDamageReceiver.TakeDamage(int damage) => TakeDamage(damage);

        [Tooltip("Total weapon damage needed to destroy the target.")]
        [SerializeField, Min(1)] private int maxHealth = 100;

        [Header("Destruction FX (played locally on every client)")]
        [Tooltip("Inactive children (Play On Awake) activated once when the target is destroyed. " +
                 "Any number; empty = none.")]
        [SerializeField] private GameObject[] destructionEffects;

        [SyncVar(hook = nameof(OnDestroyedChanged))]
        private bool isDestroyed;

        [SyncVar]
        private int currentHealth;

        private Renderer[] renderers;
        private Collider[] colliders;

        public bool IsDestroyed => isDestroyed;

        // Fired on the server when this target is first destroyed.
        // DestructionObjective subscribes to this in OnStartServer.
        public event Action OnTargetDestroyed;

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
            if (isDestroyed || damage <= 0) return;
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
            if (!newValue) return;

            foreach (var r in renderers) r.enabled = false;
            foreach (var c in colliders) c.enabled = false;

            if (isServer)
                OnTargetDestroyed?.Invoke();
        }
    }
}
