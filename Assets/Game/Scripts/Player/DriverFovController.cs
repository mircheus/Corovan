using UnityEngine;

namespace FPSGame.Player
{
    /// <summary>
    /// Car feel C5: additive FOV offset for the local driver, on top of whatever FOV the camera has (FPSF base,
    /// ADS, FOV kick stay intact — it goes through <see cref="PlayerCamera.SetExternalFovOffset"/> into
    /// CameraManager.fieldOfViewOffset). Parts: relative forward speed (wider ahead of the convoy, narrower
    /// behind it), forward acceleration (a kick on W), and a dash kick with a fast attack and a smooth release.
    /// While it is on, it replaces the old dash FOV punch (<see cref="HandlesDash"/>).
    /// </summary>
    public class DriverFovController : DriverFeelEffect
    {
        [Header("Speed part (v.z / max forward speed)")]
        [Tooltip("Degrees at full forward speed relative to the convoy.")]
        [SerializeField] private float fovForwardMax = 6f;
        [Tooltip("Degrees taken away at full backward speed (falling behind).")]
        [SerializeField] private float fovBackMax = 3f;

        [Header("Acceleration part (a.z / aRef)")]
        [SerializeField] private float fovAccelKick = 3f;
        [Tooltip("Time constant (s) of the acceleration smoothing.")]
        [SerializeField, Min(0.001f)] private float accelSmoothing = 0.15f;

        [Header("Dash kick")]
        [SerializeField] private float dashFovKick = 10f;
        [SerializeField, Min(0.001f)] private float dashFovAttack = 0.08f;
        [SerializeField, Min(0.001f)] private float dashFovRelease = 0.4f;

        [Header("Total")]
        [SerializeField] private float fovOffsetMax = 15f;
        [Tooltip("Exponential follow rate (1/s) of the final offset. CameraManager smooths the camera FOV a bit more on top.")]
        [SerializeField] private float fovSharpness = 20f;

        private float smoothedAccel;
        private float continuousOffset;
        private float dashTime = float.PositiveInfinity;

        /// <summary>Current offset in degrees (already weighted by the enter/exit fade).</summary>
        public float CurrentOffset { get; private set; }

        /// <summary>True while this controller owns the dash FOV — the caller then skips the old PlayerCamera.PunchFOV.</summary>
        public bool HandlesDash => effectEnabled && isActiveAndEnabled;

        protected override void Tick(float dt)
        {
            float target = 0f;
            if (Body != null && Car != null)
            {
                Vector3 v = Body.Velocity;
                Vector3 aRef = Body.ReferenceAcceleration;

                float forward = Mathf.Clamp(v.z / Mathf.Max(0.01f, Car.MaxForwardSpeed), -1f, 1f);
                target += forward >= 0f ? forward * fovForwardMax : forward * fovBackMax;

                float a = Mathf.Clamp(Body.Acceleration.z / aRef.z, -1f, 1f);
                smoothedAccel = Mathf.Lerp(smoothedAccel, a, 1f - Mathf.Exp(-dt / accelSmoothing));
                target += Mathf.Clamp01(smoothedAccel) * fovAccelKick;
            }
            else
            {
                smoothedAccel = 0f;
            }

            target = Mathf.Clamp(target * Weight, -fovOffsetMax, fovOffsetMax);
            continuousOffset = Mathf.Lerp(continuousOffset, target, 1f - Mathf.Exp(-fovSharpness * dt));

            // The dash kick goes on after the smoothing, or the final lerp would eat its fast attack.
            dashTime += dt;
            float total = Mathf.Clamp(continuousOffset + DashKick() * Weight, -fovOffsetMax, fovOffsetMax);
            CurrentOffset = total;

            if (playerCamera != null)
                playerCamera.SetExternalFovOffset(total);
        }

        private float DashKick()
        {
            if (dashTime < dashFovAttack)
                return dashFovKick * (dashTime / dashFovAttack);
            float t = (dashTime - dashFovAttack) / dashFovRelease;
            if (t >= 1f)
                return 0f;
            float fade = 1f - t;
            return dashFovKick * fade * fade;
        }

        protected override void OnDash(Vector3 localDirection)
        {
            dashTime = 0f;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            CurrentOffset = continuousOffset = smoothedAccel = 0f;
            dashTime = float.PositiveInfinity;
            if (playerCamera != null)
                playerCamera.SetExternalFovOffset(0f);
        }
    }
}
