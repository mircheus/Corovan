using Mirror;
using UnityEngine;
using FPSGame.Tools;

namespace FPSGame.Vehicle
{
    // Attach to a child GameObject of the car. The GameObject must have its own
    // Trigger collider; auto-attaches a Safe that enters the trigger to the anchor.
    public class SafeMount : MonoBehaviour
    {
        [SerializeField] private Transform anchor;

        private CarController car;
        private Safe attachedSafe;

        public Safe AttachedSafe => attachedSafe;
        public Transform Anchor => anchor != null ? anchor : transform;

        private void Awake()
        {
            car = GetComponentInParent<CarController>();

            if (car == null)
                Debug.LogError("[SafeMount] No CarController found in parent hierarchy.", this);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkServer.active || car == null) return;
            if (attachedSafe != null) return;

            var safe = other.GetComponentInParent<Safe>();
            if (safe == null || safe.IsAttached) return;

            attachedSafe = safe;
            safe.ServerAttach(car.netIdentity.netId, Anchor);
        }

        public void ServerDetach()
        {
            if (!NetworkServer.active) return;
            if (attachedSafe == null) return;
            attachedSafe.ServerDetach();
            attachedSafe = null;
        }
    }
}
