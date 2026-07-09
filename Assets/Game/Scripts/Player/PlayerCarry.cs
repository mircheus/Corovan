using Mirror;
using UnityEngine;
using FPSGame.Environment;

namespace FPSGame.Player
{
    public class PlayerCarry : NetworkBehaviour
    {
        [SerializeField] private Transform carryPoint;

        [Header("Actions")]
        [Tooltip("If enabled, LMB throws the carried loot.")]
        [SerializeField] private bool allowThrow = false;
        [Tooltip("If enabled, RMB puts the carried loot down.")]
        [SerializeField] private bool allowDrop = true;

        private PlayerCamera playerCamera;
        private PlayerShooting playerShooting;
        private PlayerController playerController;
        private CarryableObject carriedObject;

        public bool IsCarrying => carriedObject != null;

        private void Awake()
        {
            playerCamera = GetComponentInChildren<PlayerCamera>();
            playerShooting = GetComponent<PlayerShooting>();
            playerController = GetComponent<PlayerController>();
        }

        // Called from PlayerInputHandler when E is pressed.
        public void TryPickup()
        {
            if (!isOwned || carriedObject != null)
                return;

            CarryableObject target = FindNearbyCarryable();
            if (target == null)
                return;

            carriedObject = target;
            target.SetCarriedLocally(true);
            AttachToCarryPoint();
            playerShooting?.SetCarryingMode(true);
            playerController?.SetCarrySpeedMultiplier(target.MovementSpeedMultiplier);
            CmdPickup(target.netIdentity);
        }

        // Called from PlayerInputHandler when RMB is pressed while carrying.
        public void TryDrop()
        {
            if (!allowDrop)
                return;

            ForceDrop();
        }

        // Drop the carried object regardless of the allowDrop toggle. Used by fall recovery so
        // the ride can knock loot out of the player's hands even when manual drop is disabled.
        public void ForceDrop()
        {
            if (!isOwned || carriedObject == null)
                return;

            DetachFromCarryPoint();
            carriedObject.SetCarriedLocally(false);
            CmdDrop(carriedObject.netIdentity);
            carriedObject = null;
            playerShooting?.SetCarryingMode(false);
            playerController?.SetCarrySpeedMultiplier(1f);
        }

        // Called from PlayerInputHandler when LMB is pressed while carrying.
        public void TryThrow()
        {
            if (!isOwned || carriedObject == null || !allowThrow)
                return;

            Ray aimRay = playerCamera.GetAimRay();
            Vector3 impulse = aimRay.direction * carriedObject.ThrowForce;
            DetachFromCarryPoint();
            carriedObject.SetCarriedLocally(false);
            CmdThrow(carriedObject.netIdentity, impulse);
            carriedObject = null;
            playerShooting?.SetCarryingMode(false);
            playerController?.SetCarrySpeedMultiplier(1f);
        }

        private void AttachToCarryPoint()
        {
            Transform parent = carryPoint != null ? carryPoint : transform;
            carriedObject.transform.SetParent(parent);
            carriedObject.transform.localPosition = Vector3.zero;
            carriedObject.transform.localRotation = Quaternion.identity;
        }

        private void DetachFromCarryPoint()
        {
            if (carriedObject != null)
                carriedObject.transform.SetParent(null);
        }

        public CarryableObject FindNearbyCarryable()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, 10f);
            CarryableObject closest = null;
            float closestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                var carryable = hit.GetComponentInParent<CarryableObject>();
                if (carryable == null || carryable.IsCarried)
                    continue;

                float dist = Vector3.Distance(transform.position, carryable.transform.position);
                if (dist <= carryable.PickupRange && dist < closestDist)
                {
                    closest = carryable;
                    closestDist = dist;
                }
            }

            return closest;
        }

        [Command]
        private void CmdPickup(NetworkIdentity targetId)
        {
            if (targetId == null)
                return;

            var obj = targetId.GetComponent<CarryableObject>();
            if (obj == null || obj.IsCarried)
                return;

            float dist = Vector3.Distance(transform.position, obj.transform.position);
            if (dist > obj.PickupRange + 1f)
                return;

            obj.ServerPickup(connectionToClient);
        }

        [Command]
        private void CmdDrop(NetworkIdentity targetId)
        {
            if (targetId == null)
                return;

            var obj = targetId.GetComponent<CarryableObject>();
            if (obj == null || !obj.IsCarried)
                return;

            if (obj.netIdentity.connectionToClient != connectionToClient)
                return;

            obj.ServerDrop();
        }

        [Command]
        private void CmdThrow(NetworkIdentity targetId, Vector3 impulse)
        {
            if (targetId == null)
                return;

            var obj = targetId.GetComponent<CarryableObject>();
            if (obj == null || !obj.IsCarried)
                return;

            if (obj.netIdentity.connectionToClient != connectionToClient)
                return;

            obj.ServerThrow(impulse);
        }
    }
}
