using UnityEngine;
using Mirror;
using FPSGame.Vehicle;

namespace FPSGame.Hazards
{
    /// <summary>
    /// Stand-in for a hard crash: a trigger that flies once from its start point along its local Z
    /// (blue arrow) and throws the driver of any car it touches out through the windshield.
    /// Server-moved; put a NetworkIdentity and a NetworkTransform (Server To Client) on the object,
    /// plus a trigger Collider and a kinematic Rigidbody.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarEjectionTrigger : NetworkBehaviour
    {
        [Header("Movement")]
        [Tooltip("Where the flight starts; its forward (blue arrow) is the flight direction. Empty = this object's own scene position and forward.")]
        [SerializeField] private Transform startPoint;
        [Tooltip("Flight speed, units/sec.")]
        [SerializeField, SyncVar] private float speed = 15f;
        [Tooltip("Distance flown before the trigger disappears.")]
        [SerializeField] private float travelDistance = 60f;
        [Tooltip("Start flying by itself when the server starts. Off = wait for ServerLaunch (e.g. from a UnityEvent).")]
        [SerializeField] private bool launchOnStart = true;
        [Tooltip("Seconds to wait before flying when Launch On Start is on.")]
        [SerializeField] private float startDelay = 2f;

        [Header("Ejection")]
        [Tooltip("Throw the driver of a touched car out through the windshield. Off = the trigger only flies (e.g. a CarPushTrigger " +
                 "on the same object shoves cars instead).")]
        [SerializeField] private bool ejectDriver = true;
        [Tooltip("Off = the car's own Ejection speeds are used.")]
        [SerializeField] private bool overrideLaunch;
        [SerializeField] private float forwardSpeed = 14f;
        [SerializeField] private float upSpeed = 7f;
        [Tooltip("Disappear after throwing a driver out instead of flying on.")]
        [SerializeField] private bool disappearOnHit = true;
        [Tooltip("Despawn the networked object when the flight is over or it hit a car, instead of just hiding it. " +
                 "For runtime-spawned obstacles; leave off for a scene object that is launched again.")]
        [SerializeField] private bool destroyOnFinish;

        // Visible from spawn until the flight is over; the flight happens once.
        [SyncVar(hook = nameof(OnVisibleChanged))]
        private bool visible = true;

        private Rigidbody rb;
        private Renderer[] renderers;
        private Collider[] colliders;
        private Vector3 startPosition;
        private Vector3 direction;
        private float travelled;
        private float launchTime = float.PositiveInfinity;
        // Synced so every peer knows whether the trigger is moving (a standing wall must not push anything).
        [SyncVar] private bool flying;

        /// <summary>
        /// Current velocity on every peer: along this object's forward at the flight speed while flying, else zero.
        /// Assumes Start Point is empty (the flight runs along the object's own forward).
        /// </summary>
        public Vector3 Velocity => flying && visible ? transform.forward * speed : Vector3.zero;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            Transform from = startPoint != null ? startPoint : transform;
            startPosition = from.position;
            direction = from.forward;
            rb.position = startPosition;
            transform.position = startPosition;

            if (launchOnStart)
                launchTime = Time.time + startDelay;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyVisible(visible);
        }

        /// <summary>Overrides the flight speed (units/sec). For a spawner, call before <c>NetworkServer.Spawn</c>; only the server moves the trigger.</summary>
        public void SetSpeed(float value) => speed = Mathf.Max(0f, value);

        /// <summary>Starts the one flight now. Safe to call from a UnityEvent on any peer — only the server acts.</summary>
        public void ServerLaunch()
        {
            if (!NetworkServer.active || flying || !visible)
                return;
            launchTime = Time.time;
        }

        private void FixedUpdate()
        {
            if (!isServer || !visible)
                return;

            if (!flying)
            {
                if (Time.time < launchTime)
                    return;
                flying = true;
            }

            float step = speed * Time.fixedDeltaTime;
            travelled += step;
            rb.MovePosition(startPosition + direction * Mathf.Min(travelled, travelDistance));

            if (travelled >= travelDistance)
                Finish();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isServer || !ejectDriver || !flying || !visible)
                return;

            CarController car = other.GetComponentInParent<CarController>();
            if (car == null || !car.HasDriver)
                return;

            bool ejected = overrideLaunch
                ? car.ServerEjectDriver(forwardSpeed, upSpeed)
                : car.ServerEjectDriver();

            if (ejected && disappearOnHit)
                Finish();
        }

        [Server]
        private void Finish()
        {
            flying = false;
            visible = false;
            ApplyVisible(false);

            if (destroyOnFinish)
                NetworkServer.Destroy(gameObject);
        }

        private void OnVisibleChanged(bool oldValue, bool newValue) => ApplyVisible(newValue);

        private void ApplyVisible(bool value)
        {
            foreach (var r in renderers)
                r.enabled = value;
            foreach (var c in colliders)
                c.enabled = value;
        }

        // A scene object has no owning client, so a Client To Server NetworkTransform would never
        // send the server's movement to anyone.
        protected override void OnValidate()
        {
            base.OnValidate();
            var nt = GetComponent<NetworkTransformBase>();
            if (nt != null && nt.syncDirection != SyncDirection.ServerToClient)
                nt.syncDirection = SyncDirection.ServerToClient;
        }

        private void OnDrawGizmos()
        {
            Transform from = startPoint != null ? startPoint : transform;
            Vector3 origin = Application.isPlaying && isServer ? startPosition : from.position;
            Vector3 dir = Application.isPlaying && isServer ? direction : from.forward;

            Gizmos.color = new Color(1f, 0.4f, 0f);
            Gizmos.DrawLine(origin, origin + dir * travelDistance);
            Gizmos.DrawWireSphere(origin + dir * travelDistance, 0.3f);
        }
    }
}
