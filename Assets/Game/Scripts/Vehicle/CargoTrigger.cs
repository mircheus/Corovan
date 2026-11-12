using Mirror;
using UnityEngine;
using FPSGame.Environment;
using FPSGame.Networking;

namespace FPSGame.Vehicle
{
    // Attach to a child GameObject of the car with a Trigger collider.
    // The child must NOT have its own Rigidbody — it will share the car's Rigidbody.
    [RequireComponent(typeof(Collider))]
    public class CargoTrigger : MonoBehaviour
    {
        public enum CargoMode
        {
            Deposit,  // loot is destroyed and its value added to the score (original behaviour)
            Stow      // loot is attached to the car (kinematic, follows it) and its value added to the score
        }

        [Tooltip("Deposit = destroy the loot and score it. Stow = keep it in the bed, pinned to the car (and score it).")]
        [SerializeField] private CargoMode mode = CargoMode.Deposit;

        [Tooltip("Stow mode: where stowed loot is placed. Empty = this transform.")]
        [SerializeField] private Transform stowPoint;

        [Tooltip("Stow mode: offset between consecutive stowed items along the stow point's local Z.")]
        [SerializeField] private float stowSpacing = 0.6f;

        private CarController car;
        private int stowedCount;

        public CargoMode Mode => mode;

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
            if (carryable == null || carryable.IsCarried || carryable.IsStowed)
                return;

            if (mode == CargoMode.Deposit)
            {
                car.ServerDepositLoot(carryable);
                return;
            }

            Transform point = stowPoint != null ? stowPoint : transform;
            Vector3 position = point.position + point.forward * (stowSpacing * stowedCount);
            carryable.ServerStow(car.netIdentity, position, point.rotation);
            stowedCount++;
            Debug.Log($"[CargoTrigger] {carryable.name} stowed in {car.name}.");

            if (ScoreManager.Instance != null)
                ScoreManager.Instance.AddScore(carryable.Value);
        }
    }
}
