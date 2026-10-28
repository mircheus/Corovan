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
        [SerializeField] private float speed = 15f;
        [Tooltip("Distance flown before the trigger disappears.")]
        [SerializeField] private float travelDistance = 60f;
        [Tooltip("Start flying by itself when the server starts. Off = wait for ServerLaunch (e.g. from a UnityEvent).")]
        [SerializeField] private bool launchOnStart = true;
        [Tooltip("Seconds to wait before flying when Launch On Start is on.")]
        [SerializeField] private float startDelay = 2f;

        [Header("Ejection")]
        [Tooltip("Off = the car's own Ejection speeds are used.")]
        [SerializeField] private bool overrideLaunch;
        [SerializeField] private float forwardSpeed = 14f;
        [SerializeField] private float upSpeed = 7f;
        [Tooltip("Disappear after throwing a driver out instead of flying on.")]
        [SerializeField] private bool disappearOnHit = true;

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
        private bool flying;

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
            if (!isServer || !flying || !visible)
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
