using UnityEngine;
using Mirror;
using FPSGame.Vehicle;

namespace FPSGame.Environment
{
    // Server-side trigger that stops the autopilot of any CarController that enters it.
    // Place on a GameObject with a trigger Collider. Has no effect on driven cars
    // (driver retains control); only autopiloted cars are halted.
    [RequireComponent(typeof(Collider))]
    public class CarStopTrigger : NetworkBehaviour
    {
        [SerializeField] private bool onlyWhileAutopilot = true;

        private void OnTriggerEnter(Collider other)
        {
            Debug.Log($"OnTriggerEnter {other.name}");
            if (!isServer) return;

            CarController car = other.GetComponent<CarController>();
            if (car == null) return;

            if (onlyWhileAutopilot && car.HasDriver) return;
            Debug.Log($"Point1");
            car.ServerHalt();
        }
    }
}
