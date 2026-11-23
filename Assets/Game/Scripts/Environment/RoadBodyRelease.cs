using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace FPSGame.Environment
{
    /// <summary>
    /// Keeps a <see cref="RoadBody"/> in place (kinematic, OnTruck — e.g. strapped to a wagon) until every
    /// holder is shot away, then drops it onto the road the way <c>startDetached</c> does (InAir → OnRoad),
    /// with an optional push and spin.
    ///
    /// Holders are <see cref="DestructionTarget"/>s on CHILD objects of this body, without a NetworkIdentity of
    /// their own: Mirror picks up NetworkBehaviours on children, so weapons hit them through the usual
    /// IDamageReceiver path (<c>PlayerHitReporter.CmdReportHit</c> takes a NetworkBehaviour). A destroyed holder
    /// hides its renderers/colliders and plays its own effects; nothing happens to the body until the last one.
    ///
    /// State is the <see cref="released"/> SyncVar (late joiners detach too); the one-shot
    /// <see cref="onReleased"/> event is an RPC so it is not replayed for them.
    /// </summary>
    [RequireComponent(typeof(RoadBody))]
    public class RoadBodyRelease : NetworkBehaviour
    {
        [Tooltip("DestructionTargets (children of this object, no NetworkIdentity) holding the body. " +
                 "The body drops when ALL are destroyed. Empty = every DestructionTarget under this object.")]
        [SerializeField] private DestructionTarget[] holders;

        [Header("Launch")]
        [Tooltip("Self = velocity in this object's local axes (follows how it is placed); World = world axes.")]
        [SerializeField] private Space launchSpace = Space.World;
        [Tooltip("Initial velocity (m/s) when the body is released. Zero = plain drop, like startDetached.")]
        [SerializeField] private Vector3 launchVelocity = new Vector3(0f, 1.5f, 0f);
        [Tooltip("Random ± added per axis (same space as launchVelocity).")]
        [SerializeField] private Vector3 launchVelocityRandom = new Vector3(1f, 0.5f, 1f);
        [Tooltip("Spin around a random axis on release (rad/s). 0 = none.")]
        [SerializeField, Min(0f)] private float launchSpin = 2f;

        [Header("Hierarchy")]
        [Tooltip("Unparent the body on release (every peer), so it no longer follows the wagon it was attached to.")]
        [SerializeField] private bool detachFromParent = true;

        [Header("Events")]
        [Tooltip("Fired once on every client when the body is released (sound, effects). Not replayed for late joiners.")]
        [SerializeField] private UnityEvent onReleased;

        [SyncVar(hook = nameof(OnReleasedChanged))]
        private bool released;

        private RoadBody roadBody;
        private Rigidbody rb;
        private NetworkTransformBase netSync;

        /// <summary>True while holders keep the body in place (it can't be picked up, stays kinematic).</summary>
        public bool IsHeld => !released;

        private void Awake()
        {
            roadBody = GetComponent<RoadBody>();
            rb = GetComponent<Rigidbody>();
            netSync = GetComponent<NetworkTransformBase>();

            if (holders == null || holders.Length == 0)
                holders = GetComponentsInChildren<DestructionTarget>(true);

            // Held from the very first frame, on every peer (clients are kinematic proxies anyway).
            if (!released)
                rb.isKinematic = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (released)
                return;

            int count = 0;
            foreach (DestructionTarget holder in holders)
            {
                if (holder == null) continue;
                holder.OnTargetDestroyed += ServerCheckHolders;
                count++;
            }

            if (count == 0)
            {
                Debug.LogWarning($"{name}: RoadBodyRelease has no holders (DestructionTarget children); releasing at once.", this);
                ServerRelease();
                return;
            }

            rb.isKinematic = true;
        }

        public override void OnStopServer()
        {
            foreach (DestructionTarget holder in holders)
            {
                if (holder != null)
                    holder.OnTargetDestroyed -= ServerCheckHolders;
            }
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (released)
                Detach(); // late joiner: the hook doesn't run for the initial state
        }

        [Server]
        private void ServerCheckHolders()
        {
            if (released)
                return;

            foreach (DestructionTarget holder in holders)
            {
                if (holder != null && !holder.IsDestroyed)
                    return;
            }

            ServerRelease();
        }

        /// <summary>
        /// Drops the body now, whatever the holders' state. Safe to call from a UnityEvent on any peer
        /// (no-op off the server).
        /// </summary>
        public void Release()
        {
            if (isServer)
                ServerRelease();
        }

        [Server]
        private void ServerRelease()
        {
            if (released)
                return;

            // Launch direction is resolved before unparenting; the world rotation doesn't change anyway.
            Vector3 v = launchVelocity + new Vector3(
                Random.Range(-launchVelocityRandom.x, launchVelocityRandom.x),
                Random.Range(-launchVelocityRandom.y, launchVelocityRandom.y),
                Random.Range(-launchVelocityRandom.z, launchVelocityRandom.z));
            if (launchSpace == Space.Self)
                v = transform.TransformDirection(v);

            released = true;
            Detach();

            roadBody.ServerLaunch(v); // dynamic + InAir: gravity, airstream, then the road takes over
            if (launchSpin > 0f)
                rb.angularVelocity = Random.onUnitSphere * launchSpin;

            RpcReleased();
        }

        [ClientRpc]
        private void RpcReleased()
        {
            onReleased?.Invoke();
        }

        private void OnReleasedChanged(bool oldValue, bool newValue)
        {
            if (!newValue || isServer)
                return;

            Detach();
            // Snapshots buffered before the unparent are in the old parent's space (Local sync) — drop them.
            if (netSync != null)
                netSync.ResetState();
        }

        private void Detach()
        {
            if (detachFromParent && transform.parent != null)
                transform.SetParent(null, true);
        }

        private void OnDrawGizmosSelected()
        {
            if (launchVelocity == Vector3.zero)
                return;

            Vector3 v = launchSpace == Space.Self ? transform.TransformDirection(launchVelocity) : launchVelocity;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, transform.position + v * 0.5f);
            Gizmos.DrawWireSphere(transform.position + v * 0.5f, 0.05f);
        }
    }
}
