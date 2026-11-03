using UnityEngine;
using FPSGame.Vehicle;

namespace FPSGame.Hazards
{
    /// <summary>
    /// A flying trigger that shoves cars: a car overlapping it is carried along at the trigger's own velocity,
    /// as if it had crashed into a wall that keeps moving. Runs on every peer, but only the peer that drives
    /// the touched car acts (<see cref="CarController.ApplyPush"/> ignores everyone else) — the car is moved
    /// by its driver's client, so the push has to happen there, against the obstacle the driver actually sees.
    /// Put it next to the <see cref="CarEjectionTrigger"/> that flies the object, with its Eject Driver off.
    /// </summary>
    [RequireComponent(typeof(CarEjectionTrigger))]
    public class CarPushTrigger : MonoBehaviour
    {
        [Tooltip("Seconds a car must stay inside before it is shoved. The server decides on ejection from its own, slightly " +
                 "late copy of the car, so without this delay the push could carry the car out before the server saw the " +
                 "touch. Set it above the round-trip time; a driver who is thrown out is no longer pushed at all.")]
        [SerializeField, Min(0f)] private float pushDelay = 0.3f;

        private CarEjectionTrigger source;

        private void Awake()
        {
            source = GetComponent<CarEjectionTrigger>();
        }

        private void OnTriggerStay(Collider other)
        {
            Vector3 velocity = source.Velocity;
            if (velocity.sqrMagnitude < 0.0001f)
                return;

            CarController car = other.GetComponentInParent<CarController>();
            if (car != null)
                car.ApplyPush(velocity, pushDelay);
        }
    }
}
