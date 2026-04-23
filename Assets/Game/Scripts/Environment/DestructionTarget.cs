using System;
using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    public class DestructionTarget : NetworkBehaviour
    {
        [SerializeField] private int hitsToDestroy = 3;

        [SyncVar(hook = nameof(OnDestroyedChanged))]
        private bool isDestroyed;

        private int currentHits;
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

        [Server]
        public void TakeHit()
        {
            if (isDestroyed) return;
            currentHits++;
            if (currentHits >= hitsToDestroy)
            {
                isDestroyed = true;
            }
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
