using UnityEngine;
using Mirror;

namespace FPSGame.Tools
{
    [RequireComponent(typeof(Rigidbody))]
    public class Safe : NetworkBehaviour
    {
        [SerializeField] LayerMask floorLayers;
        [SerializeField] LayerMask groundLayers;
        [SerializeField] float groundScrollSpeed = 20f;
        [SerializeField] Collider baseCollider;
        [SerializeField] float groundCheckDistance = 0.15f;
        [SerializeField] float groundCheckSkin = 0.05f;

        Rigidbody rb;

        [SyncVar] uint attachedCarNetId;
        Transform attachAnchor;

        public bool IsAttached => attachedCarNetId != 0;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (baseCollider == null) baseCollider = GetComponent<Collider>();
        }

        [Server]
        public void ServerAttach(uint carNetId, Transform anchor)
        {
            if (IsAttached || anchor == null) return;
            attachedCarNetId = carNetId;
            attachAnchor = anchor;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.MovePosition(anchor.position);
            rb.MoveRotation(anchor.rotation);
        }

        [Server]
        public void ServerDetach()
        {
            if (!IsAttached) return;
            attachedCarNetId = 0;
            attachAnchor = null;
            rb.isKinematic = false;
        }

        void FixedUpdate()
        {
            if (!isServer) return;

            if (IsAttached)
            {
                if (attachAnchor != null)
                {
                    rb.MovePosition(attachAnchor.position);
                    rb.MoveRotation(attachAnchor.rotation);
                }
                return;
            }

            if (rb.isKinematic) return;

            Bounds b = baseCollider.bounds;
            Vector3 origin = new Vector3(b.center.x, b.min.y + groundCheckSkin, b.center.z);
            float dist = groundCheckDistance + groundCheckSkin;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, dist, groundLayers | floorLayers, QueryTriggerInteraction.Ignore))
            {
                int layerBit = 1 << hit.collider.gameObject.layer;
                if ((groundLayers.value & layerBit) != 0)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.MovePosition(rb.position + Vector3.back * groundScrollSpeed * Time.fixedDeltaTime);
                }
            }
        }
    }
}
