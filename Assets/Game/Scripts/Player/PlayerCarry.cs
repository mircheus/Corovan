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
        private PlayerLoadout loadout;
        private PlayerController playerController;
        private CarryableObject carriedObject;

        public bool IsCarrying => carriedObject != null;
        public bool AllowDrop => allowDrop;
        public bool AllowThrow => allowThrow;

        // Fires on the owning client when carrying starts (true) / ends (false). Drives the HUD hint.
        public event System.Action<bool> OnCarryingChanged;

        private void Awake()
        {
            playerCamera = GetComponentInChildren<PlayerCamera>();
            loadout = GetComponent<PlayerLoadout>();
            playerController = GetComponent<PlayerController>();
        }

        // Picks up whatever is nearest. Kept for callers that don't do their own target selection.
        public void TryPickup()
        {
            if (!isOwned || carriedObject != null)
                return;

            TryPickupTarget(FindNearbyCarryable());
        }

        // Picks up a specific object. PlayerInputHandler resolves and view-checks the target itself,
        // so the "[E] Взять" prompt and the actual pickup agree on what's in reach.
        public void TryPickupTarget(CarryableObject target)
        {
            if (!isOwned || carriedObject != null || target == null || !target.CanBePickedUp)
                return;

            carriedObject = target;
            target.SetCarriedLocally(true);
            AttachToCarryPoint();
            loadout?.SetCarryingMode(true);
            playerController?.SetCarrySpeedMultiplier(target.MovementSpeedMultiplier);
            CmdPickup(target.netIdentity);
            OnCarryingChanged?.Invoke(true);
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
            loadout?.SetCarryingMode(false);
            playerController?.SetCarrySpeedMultiplier(1f);
            OnCarryingChanged?.Invoke(false);
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
            loadout?.SetCarryingMode(false);
            playerController?.SetCarrySpeedMultiplier(1f);
            OnCarryingChanged?.Invoke(false);
        }

        // A client-only carrier owns the object only after CmdPickup's round trip. Until then its
        // NetworkTransform is still a non-owner and keeps applying the server's snapshots, which
        // knock the object off the carry point — so re-pin it every frame (after NT's Update).
        private void LateUpdate()
        {
            if (!isOwned || carriedObject == null)
                return;

            carriedObject.transform.localPosition = Vector3.zero;
            carriedObject.transform.localRotation = Quaternion.identity;
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
                if (carryable == null || !carryable.CanBePickedUp)
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
            if (obj == null || !obj.CanBePickedUp)
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
