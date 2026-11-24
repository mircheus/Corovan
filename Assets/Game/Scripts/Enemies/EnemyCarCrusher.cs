using UnityEngine;

namespace FPSGame.Environment
{
    /// <summary>
    /// A body that destroys a live <see cref="EnemyCarController"/> outright on impact. Looked up on the other
    /// Rigidbody by the enemy car's server-side OnCollisionEnter. Implemented by <see cref="EnemyCarCrusher"/>
    /// (a marker on road props) and by <see cref="EnemyCarController"/> itself once it is a flying wreck
    /// (chain reaction).
    /// </summary>
    public interface IEnemyCarCrusher
    {
        /// <summary>Server: may crush right now (off the wagon, not held, not carried...).</summary>
        bool CrushArmed { get; }

        /// <summary>Minimum speed (m/s) of the body relative to the car for the hit to count.</summary>
        float MinImpactSpeed { get; }

        /// <summary>Share of the relative impact velocity the wreck flies off with.</summary>
        float WreckPush { get; }

        /// <summary>Server: the body's velocity before this physics step's collision response.</summary>
        Vector3 PreImpactVelocity { get; }
    }

    /// <summary>
    /// Marker for a <see cref="RoadBody"/> prop (dropped crate, barrel, safe...) that blows up any enemy car it hits:
    /// the car dies at once (full destruction path, wreck launched along the impact). Armed only once the prop is
    /// actually loose — not while it rides the wagon (OnTruck), is held by <see cref="RoadBodyRelease"/> holders, or is
    /// carried by a player. Server only; nothing here is networked.
    /// </summary>
    [RequireComponent(typeof(RoadBody))]
    public class EnemyCarCrusher : MonoBehaviour, IEnemyCarCrusher
    {
        [Tooltip("Minimum speed (m/s) of the prop relative to the enemy car for the impact to destroy it. A prop that " +
                 "just slid off the wagon and is brushed by a car moving alongside doesn't count; one rushing back " +
                 "with the road does. 0 = any contact.")]
        [SerializeField, Min(0f)] private float minImpactSpeed = 5f;
        [Tooltip("Share of the impact velocity (prop relative to the car) the wreck flies off with. 0 = the wreck keeps " +
                 "the car's own momentum.")]
        [SerializeField, Min(0f)] private float wreckPush = 0.5f;

        private RoadBody roadBody;
        private Rigidbody rb;
        private RoadBodyRelease release;
        private CarryableObject carryable;
        private Vector3 lastVelocity;

        public bool CrushArmed =>
            roadBody != null && roadBody.enabled && roadBody.Phase != RoadPhase.OnTruck
            && (release == null || !release.IsHeld)
            && (carryable == null || !carryable.IsCarried);

        public float MinImpactSpeed => minImpactSpeed;
        public float WreckPush => wreckPush;
        public Vector3 PreImpactVelocity => lastVelocity;

        private void Awake()
        {
            roadBody = GetComponent<RoadBody>();
            rb = GetComponent<Rigidbody>();
            release = GetComponent<RoadBodyRelease>();
            carryable = GetComponent<CarryableObject>();
        }

        // FixedUpdate runs before the physics step, collision callbacks after it: this is the approach speed,
        // not what is left after bouncing off the car.
        private void FixedUpdate()
        {
            lastVelocity = rb.isKinematic ? Vector3.zero : rb.linearVelocity;
        }
    }
}
