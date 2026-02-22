using Mirror;
using UnityEngine;
using FPSGame.Environment;

namespace FPSGame.Player
{
    public class PlayerCarry : NetworkBehaviour
    {
        [SerializeField] private float carryDistance = 2f;
        [SerializeField] private float carryLerpSpeed = 15f;

        private PlayerCamera playerCamera;
        private PlayerShooting playerShooting;
        private CarryableObject carriedObject;

        public bool IsCarrying => carriedObject != null;

        private void Awake()
        {
            playerCamera = GetComponentInChildren<PlayerCamera>();
            playerShooting = GetComponent<PlayerShooting>();
        }

        private void FixedUpdate()
        {
            if (!isOwned || carriedObject == null)
                return;

            Ray aimRay = playerCamera.GetAimRay();
            Vector3 targetPos = aimRay.origin + aimRay.direction * carryDistance;
            carriedObject.MoveToPosition(targetPos, carryLerpSpeed);
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
            playerShooting?.SetCarryingMode(true);
            CmdPickup(target.netIdentity);
        }

        // Called from PlayerInputHandler when LMB is pressed while carrying.
        public void TryDrop()
        {
            if (!isOwned || carriedObject == null)
                return;

            CmdDrop(carriedObject.netIdentity);
            carriedObject = null;
            playerShooting?.SetCarryingMode(false);
        }

        // Called from PlayerInputHandler when RMB is pressed while carrying.
        public void TryThrow()
        {
            if (!isOwned || carriedObject == null)
                return;

            Ray aimRay = playerCamera.GetAimRay();
            Vector3 impulse = aimRay.direction * carriedObject.ThrowForce;
            CmdThrow(carriedObject.netIdentity, impulse);
            carriedObject = null;
            playerShooting?.SetCarryingMode(false);
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
