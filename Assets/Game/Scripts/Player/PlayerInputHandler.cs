using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

namespace FPSGame.Player
{
    public class PlayerInputHandler : NetworkBehaviour
    {
        private PlayerController playerController;
        private PlayerCamera playerCamera;
        private PlayerShooting playerShooting;
        private PlayerInput playerInput;
        private InputAction aimAction;
        private InputAction attackAction;
        private InputAction sprintAction;
        private bool isInitialized;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            playerCamera = GetComponentInChildren<PlayerCamera>();
            playerShooting = GetComponent<PlayerShooting>();
            playerInput = GetComponent<PlayerInput>();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            // Enable PlayerInput only for local player
            if (playerInput != null)
            {
                playerInput.enabled = true;

                aimAction = playerInput.actions["Aim"];
                attackAction = playerInput.actions["Attack"];
                sprintAction = playerInput.actions["Sprint"];
            }

            isInitialized = true;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Disable PlayerInput for remote players
            if (!isOwned && playerInput != null)
            {
                playerInput.enabled = false;
            }
        }

        private void Update()
        {
            if (!isOwned || !isInitialized)
                return;

            // Read hold-type inputs every frame
            if (aimAction != null)
                playerShooting?.SetAiming(aimAction.IsPressed());

            if (attackAction != null)
                playerShooting?.SetFiring(attackAction.IsPressed());

            if (sprintAction != null)
                playerController?.SetSprinting(sprintAction.IsPressed());
        }

        public void OnMove(InputValue value)
        {
            if (!isOwned)
                return;
            playerController?.SetMoveInput(value.Get<Vector2>());
        }

        public void OnLook(InputValue value)
        {
            if (!isOwned)
                return;
            playerCamera?.SetLookInput(value.Get<Vector2>());
        }

        public void OnJump(InputValue value)
        {
            if (!isOwned)
                return;
            if (value.isPressed)
            {
                playerController?.Jump();
            }
        }

        public void OnCrouch(InputValue value)
        {
            if (!isOwned)
                return;
            if (value.isPressed)
            {
                playerController?.ToggleCrouch();
            }
        }

        public void OnReload(InputValue value)
        {
            if (!isOwned)
                return;
            if (value.isPressed)
            {
                playerShooting?.StartReload();
            }
        }
    }
}
