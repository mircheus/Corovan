using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    // Attach to any pickupable prop (requires Rigidbody and NetworkIdentity).
    // Also add NetworkTransformUnreliable so position syncs to all clients while carried.
    [RequireComponent(typeof(Rigidbody))]
    public class CarryableObject : NetworkBehaviour
    {
        [SerializeField] private float pickupRange = 2.5f;
        [SerializeField] private float throwForce = 10f;
        [SerializeField] private int value = 1;

        [Header("Movement")]
        [Tooltip("If enabled, carrying this object slows the player's movement.")]
        [SerializeField] private bool slowsPlayer = false;
        [Tooltip("Speed multiplier applied to the carrier while holding this object (1 = no change).")]
        [Range(0.1f, 1f)]
        [SerializeField] private float carrySpeedMultiplier = 0.5f;

        [SyncVar(hook = nameof(OnCarriedChanged))]
        private bool isCarried;

        private Rigidbody rb;
        private BoxCollider[] colliders;

        public bool IsCarried => isCarried;
        public float PickupRange => pickupRange;
        public float ThrowForce => throwForce;
        public int Value => value;

        // Speed multiplier the carrier should apply while holding this object.
        // Returns 1 (no slow) when slowing is disabled for this object.
        public float MovementSpeedMultiplier => slowsPlayer ? carrySpeedMultiplier : 1f;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            colliders = GetComponentsInChildren<BoxCollider>();
        }

        private void OnCarriedChanged(bool oldValue, bool newValue)
        {
            ApplyCarriedState(newValue);
        }

        // Call on the local carrying client immediately for lag-free feedback.
        // The SyncVar hook confirms the state on all other clients.
        public void SetCarriedLocally(bool carried)
        {
            ApplyCarriedState(carried);
        }

        private void ApplyCarriedState(bool carried)
        {
            rb.isKinematic = carried;
            foreach (var col in colliders)
                col.enabled = !carried;
        }

        [Server]
        public void ServerPickup(NetworkConnectionToClient conn)
        {
            if (isCarried)
                return;

            isCarried = true;
            rb.isKinematic = true;
            netIdentity.AssignClientAuthority(conn);
        }

        [Server]
        public void ServerDrop()
        {
            netIdentity.RemoveClientAuthority();
            isCarried = false;
            rb.isKinematic = false;
        }

        [Server]
        public void ServerThrow(Vector3 impulse)
        {
            netIdentity.RemoveClientAuthority();
            isCarried = false;
            rb.isKinematic = false;
            rb.AddForce(impulse, ForceMode.Impulse);
        }
    }
}
