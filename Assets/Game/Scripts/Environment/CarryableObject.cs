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

        // Stowed in a car's cargo bed (CargoTrigger in Stow mode): pinned to the car on every peer at a
        // fixed local pose. Declared before the hooked id so the pose is in place when the hook runs.
        [SyncVar] private Vector3 stowedLocalPos;
        [SyncVar] private Quaternion stowedLocalRot = Quaternion.identity;
        [SyncVar(hook = nameof(OnStowedChanged))]
        private uint stowedInNetId;

        private Rigidbody rb;
        private BoxCollider[] colliders;
        private NetworkTransformBase netTransform;
        private RoadBody roadBody;
        private Transform stowedIn;

        public bool IsCarried => isCarried;
        public bool IsStowed => stowedInNetId != 0;
        public uint StowedInNetId => stowedInNetId;
        public NetworkTransformBase NetTransform => netTransform;
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
            netTransform = GetComponent<NetworkTransformBase>();
            roadBody = GetComponent<RoadBody>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Sync the physics/authority state to whatever the object spawned in as, so a
            // late-joining client sets kinematic + syncDirection correctly for free/carried loot.
            ApplyCarriedState(isCarried);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ApplyCarriedState(isCarried);
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
            bool stowed = IsStowed;

            // Physics authority: the server simulates free loot (so MotionDirector's lateral
            // push is real and replicates), while every non-server peer keeps it kinematic and
            // just follows the NetworkTransform. Carried loot is kinematic everywhere — it rides
            // the carrier's hand and its world position is streamed by the owner. Stowed loot is
            // kinematic too: every peer pins it to its own view of the car (LateUpdate).
            rb.isKinematic = carried || stowed || !isServer;

            foreach (var col in colliders)
                col.enabled = !carried && !stowed;

            // No snapshots while stowed: the car's position is already replicated, the loot follows it.
            // (Re-enabled in RestoreNetTransform; otherwise RoadBody owns this switch.)
            if (netTransform != null && stowed)
                netTransform.enabled = false;

            // While carried, the owning client drives the transform (ClientToServer). While free,
            // the server drives it (ServerToClient) so server-side physics reaches all clients.
            if (netTransform != null)
                netTransform.syncDirection = carried ? SyncDirection.ClientToServer
                                                     : SyncDirection.ServerToClient;
        }

        [Server]
        public void ServerPickup(NetworkConnectionToClient conn)
        {
            if (isCarried)
                return;

            isCarried = true;
            // A Settled RoadBody has its NetworkTransform switched off; lift it off the road
            // first, or the carrier's movement never reaches the server or anyone else.
            if (roadBody != null)
                roadBody.ServerLift();
            netIdentity.AssignClientAuthority(conn);
            ApplyCarriedState(true);
        }

        [Server]
        public void ServerDrop()
        {
            netIdentity.RemoveClientAuthority();
            isCarried = false;
            ApplyCarriedState(false);
        }

        [Server]
        public void ServerThrow(Vector3 impulse)
        {
            netIdentity.RemoveClientAuthority();
            isCarried = false;
            ApplyCarriedState(false);
            rb.AddForce(impulse, ForceMode.Impulse);
        }

        // ── Stowing (cargo bed) ───────────────────────────────────────────

        /// <summary>
        /// Server: attach free loot to <paramref name="holder"/> (a car) at <paramref name="worldPose"/>.
        /// It stays kinematic, collider-less and pinned to the holder on every peer until <see cref="ServerUnstow"/>.
        /// </summary>
        [Server]
        public void ServerStow(NetworkIdentity holder, Vector3 worldPosition, Quaternion worldRotation)
        {
            if (holder == null || isCarried || IsStowed)
                return;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            if (roadBody != null)
                roadBody.ServerResetToTruck(); // no airstream / road state while it rides the car

            Transform frame = holder.transform;
            stowedLocalPos = frame.InverseTransformPoint(worldPosition);
            stowedLocalRot = Quaternion.Inverse(frame.rotation) * worldRotation;
            stowedInNetId = holder.netId;
            stowedIn = frame;
            ApplyCarriedState(false);
            PinToHolder();
        }

        /// <summary>Server: release stowed loot where it is (free physics again).</summary>
        [Server]
        public void ServerUnstow()
        {
            if (!IsStowed)
                return;

            stowedInNetId = 0;
            stowedIn = null;
            ApplyCarriedState(isCarried);
            RestoreNetTransform();
        }

        private void OnStowedChanged(uint oldId, uint newId)
        {
            stowedIn = null;
            ApplyCarriedState(isCarried);
            if (newId == 0)
                RestoreNetTransform();
        }

        private void RestoreNetTransform()
        {
            if (netTransform != null)
                netTransform.enabled = roadBody == null || roadBody.Phase != RoadPhase.Settled;
        }

        private void LateUpdate()
        {
            if (IsStowed)
                PinToHolder();
        }

        private void PinToHolder()
        {
            if (stowedIn == null)
            {
                var spawned = isServer ? NetworkServer.spawned : NetworkClient.spawned;
                if (!spawned.TryGetValue(stowedInNetId, out NetworkIdentity holder) || holder == null)
                    return;
                stowedIn = holder.transform;
            }

            // RoadBody re-enables the sync component on its own phase hook; keep it off while stowed.
            if (netTransform != null && netTransform.enabled)
                netTransform.enabled = false;

            transform.SetPositionAndRotation(stowedIn.TransformPoint(stowedLocalPos),
                                             stowedIn.rotation * stowedLocalRot);
        }
    }
}
