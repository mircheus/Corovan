using UnityEngine;

namespace FPSGame.Enemies
{
    /// <summary>
    /// Drives the droid's Idle/Walk blend tree from how fast its root actually moves. Runs on every
    /// peer from the transform alone: the server's NavMeshAgent and the clients' NetworkTransform both
    /// move the root, so no animation state is synced. Turning toward the player is ShooterDroid's
    /// job (it rotates the root on the server); this only animates the legs.
    ///
    /// Controller parameters: Speed (0 = idle, 1 = walk) and WalkRate (the blend state's speed
    /// multiplier, so a droid faster than the walk clip doesn't slide its feet).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class DroidAnimator : MonoBehaviour
    {
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int WalkRateParam = Animator.StringToHash("WalkRate");

        [Tooltip("Ground speed (m/s) at which the walk clip's feet don't slide. Blend tree Speed 1 = this.")]
        [SerializeField] private float walkClipSpeed = 1.5f;
        [Tooltip("Above walkClipSpeed the walk cycle plays faster, up to this rate.")]
        [SerializeField] private float maxWalkRate = 2.2f;
        [Tooltip("Seconds to smooth the measured speed: the idle/walk cross-fade, and it hides NetworkTransform jitter.")]
        [SerializeField] private float speedSmoothTime = 0.15f;
        [Tooltip("A jump farther than this in one frame is a warp (respawn), not walking.")]
        [SerializeField] private float teleportDistance = 2f;

        private Animator animator;
        private Vector3 lastPosition;
        private float speed;
        private float speedVelocity;

        private void Awake()
        {
            animator = GetComponent<Animator>();
        }

        private void OnEnable()
        {
            ResetSpeed();
        }

        private void Update()
        {
            // The ragdoll disables the Animator; start from rest when it stands back up.
            if (!animator.enabled)
            {
                ResetSpeed();
                return;
            }

            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            float measured = delta.sqrMagnitude > teleportDistance * teleportDistance ? 0f : delta.magnitude / dt;
            speed = Mathf.SmoothDamp(speed, measured, ref speedVelocity, speedSmoothTime);

            float normalized = speed / Mathf.Max(0.01f, walkClipSpeed);
            animator.SetFloat(SpeedParam, Mathf.Clamp01(normalized));
            animator.SetFloat(WalkRateParam, Mathf.Clamp(normalized, 1f, maxWalkRate));
        }

        private void ResetSpeed()
        {
            lastPosition = transform.position;
            speed = 0f;
            speedVelocity = 0f;
        }
    }
}
