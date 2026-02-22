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

        [SyncVar(hook = nameof(OnCarriedChanged))]
        private bool isCarried;

        private Rigidbody rb;

        public bool IsCarried => isCarried;
        public float PickupRange => pickupRange;
        public float ThrowForce => throwForce;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        private void OnCarriedChanged(bool oldValue, bool newValue)
        {
            rb.isKinematic = newValue;
        }

        // Called every FixedUpdate by the authority client (the carrier) via PlayerCarry.
        public void MoveToPosition(Vector3 targetPosition, float speed)
        {
            rb.MovePosition(Vector3.Lerp(rb.position, targetPosition, speed * Time.fixedDeltaTime));
        }

        [Server]
        public void ServerPickup(NetworkConnectionToClient conn)
        {
            if (isCarried)
                return;

            isCarried = true;
            rb.isKinematic = true; // Apply immediately on server (hook fires on clients)
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
