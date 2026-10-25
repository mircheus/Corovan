using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using Akila.FPSFramework;

namespace FPSGame.Player
{
    /// <summary>
    /// Caches the Attack input action from the PlayerInput component (jump comes from FPSF's
    /// CharacterInput, which owns the movement keys) and exposes per-frame query properties for
    /// GrapplingHookController.
    /// </summary>
    public class GrapplingHookInput : NetworkBehaviour
    {
        private InputAction fireAction;
        private CharacterInput fpsfInput;
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
            fpsfInput = GetComponent<CharacterInput>();
            initialized = true;
        }

        private void Update()
        {
            FireTriggered = false;
            JumpTriggered = false;

            if (!isOwned || !initialized)
                return;

            FireTriggered = fireAction != null && fireAction.WasPressedThisFrame();
            JumpTriggered = fpsfInput != null && fpsfInput.jumpInput;
        }
    }
}
