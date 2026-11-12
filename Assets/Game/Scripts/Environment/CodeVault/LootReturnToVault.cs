using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    /// <summary>
    /// Prototype stand-in for a missed throw (handoff F6): loot that lands on the road (touches a
    /// <see cref="RoadSurface"/> or enters a <see cref="FallHazardVolume"/>) is put back at its starting
    /// spot in the vault after <see cref="lootReturnDelay"/> seconds — unless someone picks it up or it gets
    /// stowed in a car first. Server only; the teleport replicates through the NetworkTransform.
    /// Put it next to <see cref="CarryableObject"/> on the reward.
    /// </summary>
    [RequireComponent(typeof(CarryableObject))]
    public class LootReturnToVault : NetworkBehaviour
    {
        [SerializeField, Min(0f)] private float lootReturnDelay = 3f;

        [Tooltip("Where the loot goes back to. Empty = its position and rotation when the server started.")]
        [SerializeField] private Transform returnPoint;

        private CarryableObject carryable;
        private Rigidbody rb;
        private RoadBody roadBody;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private float returnAt = -1f;

        private void Awake()
        {
            carryable = GetComponent<CarryableObject>();
            rb = GetComponent<Rigidbody>();
            roadBody = GetComponent<RoadBody>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            homePosition = transform.position;
            homeRotation = transform.rotation;
        }

        [ServerCallback]
        private void OnCollisionEnter(Collision collision)
        {
            if (collision.collider.GetComponent<RoadSurface>() != null)
                ScheduleReturn();
        }

        [ServerCallback]
        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<FallHazardVolume>() != null)
                ScheduleReturn();
        }

        [Server]
        private void ScheduleReturn()
        {
            if (returnAt >= 0f || carryable.IsCarried || carryable.IsStowed)
                return;
            returnAt = Time.time + lootReturnDelay;
        }

        [ServerCallback]
        private void Update()
        {
            if (returnAt < 0f)
                return;

            if (carryable.IsCarried || carryable.IsStowed)
            {
                returnAt = -1f;
                return;
            }

            if (Time.time < returnAt)
                return;

            returnAt = -1f;
            ServerReturnHome();
        }

        [Server]
        private void ServerReturnHome()
        {
            Vector3 pos = returnPoint != null ? returnPoint.position : homePosition;
            Quaternion rot = returnPoint != null ? returnPoint.rotation : homeRotation;

            if (roadBody != null)
                roadBody.ServerResetToTruck(); // also turns replication back on if it had settled

            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.position = pos;
            rb.rotation = rot;

            // Teleport, not a slide: clients drop their interpolation buffer.
            if (carryable.NetTransform != null && carryable.NetTransform.enabled)
                carryable.NetTransform.ServerTeleport(pos, rot);
            else
                transform.SetPositionAndRotation(pos, rot);

            Debug.Log($"[LootReturn] {name} missed the car — returned to the vault.");
        }
    }
}
