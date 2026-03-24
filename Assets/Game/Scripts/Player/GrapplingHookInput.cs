using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSGame.Player
{
    /// <summary>
    /// Caches the Attack and Jump input actions from the PlayerInput component and
    /// exposes per-frame query properties for GrapplingHookController.
    /// Reads actions directly (same pattern as PlayerInputHandler) so no modifications
    /// to PlayerInputHandler are required.
    /// </summary>
    public class GrapplingHookInput : NetworkBehaviour
    {
        private InputAction fireAction;
        private InputAction jumpAction;
        private bool initialized;

        /// <summary>True for one frame when the fire (Attack) action is triggered.</summary>
        public bool FireTriggered { get; private set; }

        /// <summary>True for one frame when the jump action is triggered.</summary>
        public bool JumpTriggered { get; private set; }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            var playerInput = GetComponent<PlayerInput>();
            if (playerInput == null)
            {
                Debug.LogError("[GrapplingHookInput] No PlayerInput found on Player.", this);
                return;
            }

            fireAction = playerInput.actions["Attack"];
            jumpAction = playerInput.actions["Jump"];
            initialized = true;
        }

        private void Update()
        {
            FireTriggered = false;
            JumpTriggered = false;

            if (!isOwned || !initialized)
                return;

            FireTriggered = fireAction != null && fireAction.WasPressedThisFrame();
            JumpTriggered = jumpAction != null && jumpAction.WasPressedThisFrame();
        }
    }
}
