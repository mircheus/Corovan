using System;
using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace FPSGame.Environment
{
    // Place on any GameObject with a NetworkIdentity.
    // The player must stand within InteractRange and hold E for HoldDuration seconds.
    public class InteractableObject : NetworkBehaviour
    {
        [SerializeField] private float holdDuration = 2f;
        [SerializeField] private float interactRange = 3f;

        [Space]
        [SerializeField] private UnityEvent onInteracted;

        public float HoldDuration => holdDuration;
        public float InteractRange => interactRange;

        // Fired on the server every time a successful interaction occurs.
        // InteractObjective subscribes to this in OnStartServer.
        public event Action<GameObject> OnInteracted;

        // Called server-side by PlayerInputHandler after the hold completes and
        // server-side range validation passes.
        [Server]
        public void ServerInteract(GameObject instigator)
        {
            OnInteracted?.Invoke(instigator);
            RpcOnInteracted();
        }

        // Fires on all clients so every player sees the result (door opens, light turns
        // on, etc.). The UnityEvent is wired up per-object in the Inspector.
        [ClientRpc]
        private void RpcOnInteracted()
        {
            onInteracted?.Invoke();
        }
    }
}
