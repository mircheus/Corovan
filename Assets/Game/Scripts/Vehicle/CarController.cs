using UnityEngine;

namespace FPSGame.Vehicle
{
    /// <summary>
    /// Placeholder for arcade car controller.
    /// Will be implemented in Phase 4.
    /// </summary>
    public class CarController : MonoBehaviour
    {
        [Header("Vehicle Settings")]
        [SerializeField] private float maxSpeed = 30f;
        [SerializeField] private float acceleration = 10f;
        [SerializeField] private float brakeForce = 15f;
        [SerializeField] private float turnSpeed = 100f;

        [Header("Wheel References")]
        [SerializeField] private Transform[] wheelMeshes;
        [SerializeField] private WheelCollider[] wheelColliders;

        [Header("Driver")]
        [SerializeField] private Transform driverSeat;
        [SerializeField] private bool hasDriver;

        private Rigidbody rb;
        private float currentSpeed;
        private float steerInput;
        private float throttleInput;

        public bool HasDriver => hasDriver;
        public Transform DriverSeat => driverSeat;
        public float CurrentSpeed => currentSpeed;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (!hasDriver)
                return;

            // Placeholder - will implement arcade physics
        }

        public void SetInput(float throttle, float steer)
        {
            throttleInput = throttle;
            steerInput = steer;
        }

        public void EnterVehicle(GameObject driver)
        {
            hasDriver = true;
            // Placeholder - will disable driver character controller and parent to seat
            Debug.Log($"{driver.name} entered vehicle");
        }

        public void ExitVehicle()
        {
            hasDriver = false;
            // Placeholder - will re-enable driver and position them outside
            Debug.Log("Driver exited vehicle");
        }
    }
}
