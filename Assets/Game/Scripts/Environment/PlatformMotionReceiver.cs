using UnityEngine;

namespace FPSGame.Environment
{
    // Pure cosmetic consumer of the MotionDirector signal: banks and jolts a visual
    // platform mesh so it reads as "we're driving and it shakes". Local-only, not networked
    // — every client tilts its own copy from the shared signal.
    //
    // Put this on the visual root you want to lean (the platform mesh). Keep colliders/gameplay
    // geometry on a separate, un-tilted object if you don't want the physics floor to rotate.
    public class PlatformMotionReceiver : MonoBehaviour
    {
        [Header("Tilt response (degrees per signal unit)")]
        [Tooltip("Roll (bank) into the swerve from the lateral signal.")]
        [SerializeField] private float rollPerUnit = 6f;
        [Tooltip("Pitch (nose up) from the vertical bump signal.")]
        [SerializeField] private float pitchPerUnit = 4f;

        [Header("Vertical bounce")]
        [Tooltip("Metres of upward hop per vertical signal unit.")]
        [SerializeField] private float bouncePerUnit = 0.1f;

        [Header("Smoothing")]
        [SerializeField] private float smoothSpeed = 12f;

        private Quaternion baseRotation;
        private Vector3 basePosition;
        private float roll;
        private float pitch;
        private float bounce;

        private void Awake()
        {
            baseRotation = transform.localRotation;
            basePosition = transform.localPosition;
        }

        private void Update()
        {
            MotionDirector director = MotionDirector.Instance;
            if (director == null)
                return;

            float targetRoll = -director.LateralSignal * rollPerUnit;
            float targetPitch = -director.VerticalSignal * pitchPerUnit;
            float targetBounce = director.VerticalSignal * bouncePerUnit;

            float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
            roll = Mathf.Lerp(roll, targetRoll, t);
            pitch = Mathf.Lerp(pitch, targetPitch, t);
            bounce = Mathf.Lerp(bounce, targetBounce, t);

            transform.localRotation = baseRotation * Quaternion.Euler(pitch, 0f, roll);
            transform.localPosition = basePosition + Vector3.up * bounce;
        }
    }
}
