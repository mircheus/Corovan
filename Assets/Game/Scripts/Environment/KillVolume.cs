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
                // No being revived at the bottom of a kill volume.
                health.ServerKillInstant();
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
