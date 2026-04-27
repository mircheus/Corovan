using UnityEngine;
using Mirror;
using FPSGame.Player;
using FPSGame.Vehicle;

namespace FPSGame.Environment
{
    [RequireComponent(typeof(Collider))]
    public class KillVolume : MonoBehaviour
    {
        [SerializeField] private bool killCars = false;

        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkServer.active) return;

            var health = other.GetComponentInParent<PlayerHealth>();
            if (health != null)
            {
                health.SetHealth(0);
                return;
            }

            if (killCars)
            {
                var car = other.GetComponentInParent<CarController>();
                if (car != null)
                    car.TakeDamage(car.MaxVehicleHealth);
            }
        }
    }
}
