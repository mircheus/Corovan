using Mirror;
using UnityEngine;
using FPSGame.Environment;

namespace FPSGame.Vehicle
{
    // Attach to a child GameObject of the car with a Trigger collider.
    // The child must NOT have its own Rigidbody — it will share the car's Rigidbody.
    [RequireComponent(typeof(Collider))]
    public class CargoTrigger : MonoBehaviour
    {
        private CarController car;

        private void Awake()
        {
            car = GetComponentInParent<CarController>();

            var col = GetComponent<Collider>();
            col.isTrigger = true;

            if (car == null)
                Debug.LogError("[CargoTrigger] No CarController found in parent hierarchy.", this);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkServer.active || car == null)
                return;

            var carryable = other.GetComponentInParent<CarryableObject>();
            if (carryable == null || carryable.IsCarried)
                return;

            car.ServerDepositLoot(carryable);
        }
    }
}
