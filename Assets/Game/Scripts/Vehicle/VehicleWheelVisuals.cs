using UnityEngine;

namespace FPSGame.Vehicle
{
    /// <summary>
    /// Cosmetic wheel spin and steering wheel turn for any vehicle. Runs on every peer and syncs
    /// nothing. Wheels spin at a constant speed. The steering wheel follows the vehicle's sideways
    /// movement, measured from this transform (NetworkTransform already moves remote copies), so it
    /// turns the same for the driver and for observers.
    /// </summary>
    public class VehicleWheelVisuals : MonoBehaviour
    {
        [Header("Wheels")]
        [Tooltip("Wheel meshes to spin. Each spins around Spin Axis in its own local space, starting from its rotation in the prefab.")]
        [SerializeField] private Transform[] wheels;
        [Tooltip("Constant wheel spin speed, degrees per second. Negative spins the other way.")]
        [SerializeField] private float spinSpeed = 720f;
        [Tooltip("Local axle axis of the wheel meshes (usually X).")]
        [SerializeField] private Vector3 spinAxis = Vector3.right;

        [Header("Steering Wheel")]
        [Tooltip("Steering wheel in the cabin. Optional.")]
        [SerializeField] private Transform steeringWheel;
        [Tooltip("Local axis the steering wheel turns around (its column axis).")]
        [SerializeField] private Vector3 steeringAxis = Vector3.forward;
        [Tooltip("Steering wheel angle at full turn, degrees. Kept small: the car strafes, it doesn't really turn.")]
        [SerializeField] private float maxSteeringAngle = 30f;
        [Tooltip("Sideways speed (m/s) that gives the full steering angle. Usually the car's strafe speed.")]
        [SerializeField] private float fullSteerSideSpeed = 10f;
        [Tooltip("Flip if the steering wheel turns the wrong way.")]
        [SerializeField] private bool invertSteering;
        [Tooltip("How fast the steering wheel follows the sideways speed.")]
        [SerializeField] private float steeringSmoothing = 6f;

        private Quaternion[] wheelBaseRotations;
        private Quaternion steeringBaseRotation;
        private Vector3 lastPosition;
        private float spinAngle;
        private float steerAngle;

        private void Awake()
        {
            wheelBaseRotations = new Quaternion[wheels != null ? wheels.Length : 0];
            for (int i = 0; i < wheelBaseRotations.Length; i++)
                if (wheels[i] != null)
                    wheelBaseRotations[i] = wheels[i].localRotation;

            if (steeringWheel != null)
                steeringBaseRotation = steeringWheel.localRotation;
        }

        private void OnEnable()
        {
            lastPosition = transform.position;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            UpdateWheels(dt);
            UpdateSteeringWheel(dt);
        }

        private void UpdateWheels(float dt)
        {
            if (wheelBaseRotations.Length == 0)
                return;

            spinAngle = Mathf.Repeat(spinAngle + spinSpeed * dt, 360f);
            Quaternion spin = Quaternion.AngleAxis(spinAngle, spinAxis);

            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i] != null)
                    wheels[i].localRotation = wheelBaseRotations[i] * spin;
        }

        private void UpdateSteeringWheel(float dt)
        {
            Vector3 velocity = (transform.position - lastPosition) / dt;
            lastPosition = transform.position;

            if (steeringWheel == null || fullSteerSideSpeed <= 0f)
                return;

            float steer = Mathf.Clamp(Vector3.Dot(velocity, transform.right) / fullSteerSideSpeed, -1f, 1f);
            if (invertSteering)
                steer = -steer;

            // Negative: seen from the driver's seat, a positive turn around the column (+Z) is
            // counter-clockwise, i.e. a left turn.
            float target = -steer * maxSteeringAngle;
            // The smoothing also hides frames where the physics step didn't move the car.
            steerAngle = Mathf.Lerp(steerAngle, target, 1f - Mathf.Exp(-steeringSmoothing * dt));
            steeringWheel.localRotation = steeringBaseRotation * Quaternion.AngleAxis(steerAngle, steeringAxis);
        }
    }
}
