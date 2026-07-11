using System;
using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace FPSGame.Environment
{
    // Place on any GameObject with a NetworkIdentity.
    // Two modes:
    //  - Standard: player holds E for HoldDuration seconds → OnInteracted fires once.
    //  - HoldToActivate: instantly activates on press, deactivates on release / out of range.
    public class InteractableObject : NetworkBehaviour
    {
        [SerializeField] private float holdDuration = 2f;
        [SerializeField] private float interactRange = 3f;

        [Tooltip("If true, the object is active only while the player holds the button and stays in range. Standard hold-to-complete is disabled.")]
        [SerializeField] private bool holdToActivate = false;

        [Tooltip("Verb shown in the \"[E] …\" prompt when a player is in range, e.g. \"Открыть\".")]
        [SerializeField] private string promptText = "Взаимодействовать";

        [Space]
        [SerializeField] private UnityEvent onInteracted;
        [SerializeField] private UnityEvent onActivated;
        [SerializeField] private UnityEvent onDeactivated;

        [SyncVar(hook = nameof(OnActiveChanged))]
        private bool isActive;

        public float HoldDuration => holdDuration;
        public float InteractRange => interactRange;
        public bool HoldToActivate => holdToActivate;
        public bool IsActive => isActive;
        public string PromptText => promptText;

        // Server-side events. InteractObjective and other listeners subscribe in OnStartServer.
        public event Action<GameObject> OnInteracted;
        public event Action<GameObject> OnActivated;
        public event Action<GameObject> OnDeactivated;

        // Called server-side by PlayerInputHandler after the hold completes and
        // server-side range validation passes (standard mode only).
        [Server]
        public void ServerInteract(GameObject instigator)
        {
            if (holdToActivate)
                return;

            OnInteracted?.Invoke(instigator);
            RpcOnInteracted();
        }

        // Called server-side while the player holds the button (hold-to-activate mode).
        [Server]
        public void ServerSetActive(bool active, GameObject instigator)
        {
            if (!holdToActivate)
                return;
            if (isActive == active)
                return;

            isActive = active;

            if (active)
                OnActivated?.Invoke(instigator);
            else
                OnDeactivated?.Invoke(instigator);
        }

        // Fires on all clients so every player sees the result (door opens, light turns
        // on, etc.). The UnityEvent is wired up per-object in the Inspector.
        [ClientRpc]
        private void RpcOnInteracted()
        {
            onInteracted?.Invoke();
        }

        private void OnActiveChanged(bool oldValue, bool newValue)
        {
            if (newValue)
                onActivated?.Invoke();
            else
                onDeactivated?.Invoke();
        }
    }
}
