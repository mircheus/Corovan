using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace FPSGame.Environment
{
    // Place on any GameObject with a NetworkIdentity.
    // Two modes:
    //  - Standard: player holds E for HoldDuration seconds → OnInteracted fires once.
    //  - HoldToActivate: instantly activates on press, deactivates on release / out of range.
    //
    // Found by the player through the All registry (distance + look direction only), never through
    // a physics overlap — surrounding colliders, triggers and enemies can't hide it.
    public class InteractableObject : NetworkBehaviour
    {
        private static readonly List<InteractableObject> all = new List<InteractableObject>();
        /// <summary>Every enabled InteractableObject on this peer.</summary>
        public static IReadOnlyList<InteractableObject> All => all;

        [SerializeField] private float holdDuration = 2f;
        [SerializeField] private float interactRange = 3f;

        [Tooltip("Point the range and the look check are measured to (e.g. the button itself). " +
                 "Empty = range from this object's pivot, look check at its collider's centre.")]
        [SerializeField] private Transform interactPoint;

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

        // Why the object can't be used right now; empty = usable. One string, not a bool + text,
        // so the reason and the state always arrive together.
        [SyncVar]
        private string blockedReason = string.Empty;

        public float HoldDuration => holdDuration;
        public float InteractRange => interactRange;
        public bool HoldToActivate => holdToActivate;
        public bool IsActive => isActive;
        public string PromptText => promptText;

        /// <summary>True while a server-side owner (e.g. PlayerCarSpawner) refuses interactions.
        /// The player sees <see cref="BlockedReason"/> as a warning and can't start the hold.</summary>
        public bool IsBlocked => !string.IsNullOrEmpty(blockedReason);
        public string BlockedReason => blockedReason;

        /// <summary>Where the interaction range is measured to.</summary>
        public Vector3 RangePoint => interactPoint != null ? interactPoint.position : transform.position;

        /// <summary>Where the player has to roughly look to interact.</summary>
        public Vector3 ViewPoint
        {
            get
            {
                if (interactPoint != null)
                    return interactPoint.position;
                if (ownCollider == null)
                    ownCollider = GetComponentInChildren<Collider>();
                return ownCollider != null ? ownCollider.bounds.center : transform.position;
            }
        }

        private Collider ownCollider;

        private void OnEnable()
        {
            if (!all.Contains(this))
                all.Add(this);
        }

        private void OnDisable()
        {
            all.Remove(this);
        }

        // Server-side events. InteractObjective and other listeners subscribe in OnStartServer.
        public event Action<GameObject> OnInteracted;
        public event Action<GameObject> OnActivated;
        public event Action<GameObject> OnDeactivated;

        // Called server-side by PlayerInputHandler after the hold completes and
        // server-side range validation passes (standard mode only).
        [Server]
        public void ServerInteract(GameObject instigator)
        {
            if (holdToActivate || IsBlocked)
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
            if (active && IsBlocked)
                return;
            if (isActive == active)
                return;

            isActive = active;

            if (active)
                OnActivated?.Invoke(instigator);
            else
                OnDeactivated?.Invoke(instigator);
        }

        /// <summary>Server: refuse interactions and show <paramref name="reason"/> to players
        /// (null/empty = usable again). A hold-to-activate object that gets blocked is switched off.</summary>
        [Server]
        public void ServerSetBlocked(string reason)
        {
            reason ??= string.Empty;
            if (blockedReason == reason)
                return;

            blockedReason = reason;
            if (IsBlocked && holdToActivate && isActive)
            {
                isActive = false;
                OnDeactivated?.Invoke(null);
            }
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
