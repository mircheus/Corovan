using UnityEngine;
using FPSGame.Environment;

namespace FPSGame.Vehicle
{
    /// <summary>
    /// Car feel C2: turns only the visual part of the car (<see cref="visualRoot"/>) — yaw toward where the car
    /// really goes over the road, pitch and roll from acceleration on a damped spring, like a body on suspension.
    /// The logical car (Rigidbody, colliders, movement) never rotates: everything stays strictly in XZ.
    ///
    /// Runs on every peer and syncs nothing. The driver's client reads the velocity model
    /// (<see cref="CarController.OwnerLocalVelocity"/>); every other peer derives velocity and acceleration from
    /// the interpolated position (smoothed derivative, then a more smoothed derivative of that).
    ///
    /// Also keeps the gun model pointing where the bullets go (C4): the visual yaw is cancelled on it unless
    /// <see cref="CarMachineGun.GunFollowsBodyYaw"/> is on.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the NetworkTransform has moved remote copies this frame
    public class CarBodyVisual : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Child that holds every mesh of the car (body, wheels, gun, driver stand-in). Only it is rotated.")]
        [SerializeField] private Transform visualRoot;
        [Tooltip("Rotation pivot in the car's local space (roughly the body's centre). The visual root is rotated around it.")]
        [SerializeField] private Vector3 pivot = new Vector3(0f, 0.8f, 0.3f);
        [Tooltip("Gun model under the visual root. Its visual yaw is cancelled so the barrel points where the shots go. Optional.")]
        [SerializeField] private Transform gunModel;

        [Header("Switch (A/B)")]
        [SerializeField] private bool effectEnabled = true;

        [Header("Yaw: nose toward the travel direction over the road")]
        [SerializeField] private float yawExaggeration = 2.5f;
        [SerializeField] private float maxVisualYaw = 15f;
        [Tooltip("Exponential follow rate (1/s). Higher = snappier, never overshoots.")]
        [SerializeField] private float yawSharpness = 8f;
        [Tooltip("Floor for the forward speed over the road in the yaw formula (m/s). Keeps the nose from flipping " +
                 "round when the car drops behind faster than the convoy moves (backward dash), or with no road at all.")]
        [SerializeField, Min(0.1f)] private float minGroundForwardSpeed = 3f;

        [Header("Pitch / roll from acceleration (suspension spring)")]
        [Tooltip("Nose-up degrees at full forward acceleration (a.z = aRef). Braking gives the opposite sign.")]
        [SerializeField] private float pitchAtFullAccel = 3f;
        [Tooltip("Degrees of roll at full sideways acceleration. Positive = the body leans away from the acceleration (outward), like a real car.")]
        [SerializeField] private float rollAtFullAccel = 4f;
        [SerializeField, Min(0.1f)] private float tiltSpringFrequency = 2.5f;
        [Tooltip("< 1 overshoots: on a hard stop the nose dips and comes back.")]
        [SerializeField, Range(0.05f, 2f)] private float tiltDampingRatio = 0.5f;
        [Tooltip("Pitch kick on any dash, roughly the degrees it adds (fed into the spring's velocity). Positive = nose up.")]
        [SerializeField] private float dashPitchImpulse = 2f;
        [Tooltip("Extra roll kick on a sideways dash, roughly in degrees; positive leans away from the dash.")]
        [SerializeField] private float dashRollImpulse = 4f;

        [Header("Remote copies (velocity from the interpolated position)")]
        [Tooltip("Time constant (s) of the velocity smoothing.")]
        [SerializeField, Min(0.001f)] private float remoteVelocitySmoothing = 0.08f;
        [Tooltip("Time constant (s) of the acceleration smoothing. Larger than the velocity one.")]
        [SerializeField, Min(0.001f)] private float remoteAccelerationSmoothing = 0.2f;
        [Tooltip("A position jump faster than this (m/s) is a teleport, not motion: the estimate restarts.")]
        [SerializeField] private float teleportSpeed = 120f;

        private CarController car;
        private CarMachineGun gun;
        private Vector3 visualRootBasePosition;
        private Quaternion visualRootBaseRotation = Quaternion.identity;
        private Quaternion gunBaseRotation = Quaternion.identity;

        private float yaw, pitch, roll;          // degrees, pitch positive = nose up, roll positive = left side down
        private float pitchVelocity, rollVelocity;

        private Vector3 lastPosition;
        private bool hasLastPosition;
        private Vector3 remoteVelocity;          // local frame, smoothed
        private Vector3 remoteVelocityPrev;
        private Vector3 remoteAcceleration;      // local frame, smoothed more

        public bool EffectEnabled
        {
            get => effectEnabled;
            set
            {
                if (effectEnabled == value)
                    return;
                effectEnabled = value;
                if (!value)
                    ResetPose();
            }
        }

        /// <summary>Visual angles in degrees (0 while the effect is off). Pitch: nose up = positive; roll: positive = left side down.</summary>
        public float Yaw => yaw;
        public float Pitch => pitch;
        public float Roll => roll;

        /// <summary>Speed relative to the convoy in the car's local XZ frame: the model on the driver's client,
        /// the smoothed estimate elsewhere. Computed even while the visual effect is off.</summary>
        public Vector3 Velocity => car != null && car.IsDrivenLocally ? car.OwnerLocalVelocity : remoteVelocity;

        /// <summary>Acceleration in the car's local XZ frame: the model's (unsmoothed) on the driver's client,
        /// the smoothed estimate elsewhere.</summary>
        public Vector3 Acceleration => car != null && car.IsDrivenLocally ? car.OwnerLocalAcceleration : remoteAcceleration;

        /// <summary>Reference acceleration per axis: full speed / accelTime (x = sideways, z = forward).</summary>
        public Vector3 ReferenceAcceleration
        {
            get
            {
                if (car == null)
                    return Vector3.one;
                float t = Mathf.Max(0.01f, car.AccelTime);
                return new Vector3(Mathf.Max(0.01f, car.MaxStrafeSpeed) / t, 1f, Mathf.Max(0.01f, car.MaxForwardSpeed) / t);
            }
        }

        private void Awake()
        {
            car = GetComponent<CarController>();
            gun = GetComponent<CarMachineGun>();
            if (visualRoot != null)
            {
                visualRootBasePosition = visualRoot.localPosition;
                visualRootBaseRotation = visualRoot.localRotation;
            }
            if (gunModel != null)
                gunBaseRotation = gunModel.localRotation;
        }

        private void OnEnable()
        {
            hasLastPosition = false;
            if (car != null)
                car.OnDashStarted += HandleDash;
        }

        private void OnDisable()
        {
            if (car != null)
                car.OnDashStarted -= HandleDash;
            ResetPose();
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            UpdateRemoteEstimate(dt);

            if (!effectEnabled || visualRoot == null)
                return;

            Vector3 v = Velocity;
            Vector3 a = Acceleration;
            Vector3 aRef = ReferenceAcceleration;

            // Yaw: heading of the car's velocity over the road (the convoy itself moves along +Z at its speed).
            float convoySpeed = RoadScroll.Instance != null ? RoadScroll.Instance.Speed : 0f;
            float groundForward = Mathf.Max(convoySpeed + v.z, minGroundForwardSpeed);
            float targetYaw = Mathf.Atan2(v.x, groundForward) * Mathf.Rad2Deg * yawExaggeration;
            targetYaw = Mathf.Clamp(targetYaw, -maxVisualYaw, maxVisualYaw);
            yaw = Mathf.Lerp(yaw, targetYaw, 1f - Mathf.Exp(-yawSharpness * dt));

            float targetPitch = pitchAtFullAccel * Mathf.Clamp(a.z / aRef.z, -1f, 1f);
            float targetRoll = rollAtFullAccel * Mathf.Clamp(a.x / aRef.x, -1f, 1f);

            // Damped spring, substepped so a long frame can't blow it up.
            float omega = 2f * Mathf.PI * tiltSpringFrequency;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / (1f / 120f)), 1, 8);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                StepSpring(ref pitch, ref pitchVelocity, targetPitch, omega, h);
                StepSpring(ref roll, ref rollVelocity, targetRoll, omega, h);
            }

            ApplyPose();
        }

        private void StepSpring(ref float x, ref float velocity, float target, float omega, float h)
        {
            float accel = omega * omega * (target - x) - 2f * tiltDampingRatio * omega * velocity;
            velocity += accel * h;
            x += velocity * h;
        }

        private Quaternion TiltRotation => Quaternion.AngleAxis(-pitch, Vector3.right) * Quaternion.AngleAxis(roll, Vector3.forward);

        private void ApplyPose()
        {
            Quaternion tilt = TiltRotation;
            Quaternion rot = Quaternion.AngleAxis(yaw, Vector3.up) * tilt;
            visualRoot.localRotation = rot * visualRootBaseRotation;
            visualRoot.localPosition = visualRootBasePosition + pivot - rot * pivot;

            if (gunModel != null)
            {
                bool followYaw = gun != null && gun.GunFollowsBodyYaw;
                if (followYaw || gunModel.parent != visualRoot)
                {
                    gunModel.localRotation = gunBaseRotation;
                }
                else
                {
                    // Wanted car-space rotation = tilt only (no yaw) * base; expressed under the rotated root.
                    Quaternion rootRot = visualRoot.localRotation;
                    gunModel.localRotation = Quaternion.Inverse(rootRot) * tilt * visualRootBaseRotation * gunBaseRotation;
                }
            }
        }

        private void ResetPose()
        {
            yaw = pitch = roll = 0f;
            pitchVelocity = rollVelocity = 0f;
            if (visualRoot != null)
            {
                visualRoot.localRotation = visualRootBaseRotation;
                visualRoot.localPosition = visualRootBasePosition;
            }
            if (gunModel != null)
                gunModel.localRotation = gunBaseRotation;
        }

        // Owner only (the dash runs on the driver's client). Other peers see the dash through the estimated
        // acceleration spike instead.
        private void HandleDash()
        {
            if (!effectEnabled || car == null)
                return;

            float omega = 2f * Mathf.PI * tiltSpringFrequency;
            pitchVelocity += dashPitchImpulse * omega;

            Vector3 dir = car.LastDashLocalDirection;
            if (Mathf.Abs(dir.x) > 0.5f)
                rollVelocity += dashRollImpulse * omega * Mathf.Sign(dir.x);
        }

        // Every peer, every frame, so the estimate is warm when control changes hands. On the driver's client
        // it is not used (the model is), but cheap.
        private void UpdateRemoteEstimate(float dt)
        {
            Vector3 position = transform.position;
            if (!hasLastPosition)
            {
                lastPosition = position;
                hasLastPosition = true;
                remoteVelocity = remoteVelocityPrev = remoteAcceleration = Vector3.zero;
                return;
            }

            Vector3 worldVelocity = (position - lastPosition) / dt;
            lastPosition = position;
            worldVelocity.y = 0f;

            if (worldVelocity.magnitude > teleportSpeed)
            {
                remoteVelocity = remoteVelocityPrev = remoteAcceleration = Vector3.zero;
                return;
            }

            Vector3 rawLocal = transform.InverseTransformDirection(worldVelocity);
            rawLocal.y = 0f;

            remoteVelocity = Vector3.Lerp(remoteVelocity, rawLocal, 1f - Mathf.Exp(-dt / remoteVelocitySmoothing));
            Vector3 rawAccel = (remoteVelocity - remoteVelocityPrev) / dt;
            remoteVelocityPrev = remoteVelocity;
            remoteAcceleration = Vector3.Lerp(remoteAcceleration, rawAccel, 1f - Mathf.Exp(-dt / remoteAccelerationSmoothing));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.TransformPoint(pivot), 0.15f);
        }
    }
}
