using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSGame.Player
{
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerInputHandler : MonoBehaviour
    {
        private PlayerController playerController;
        private PlayerCamera playerCamera;
        private PlayerShooting playerShooting;
        private PlayerInput playerInput;
        private InputAction aimAction;
        private InputAction attackAction;
        private InputAction sprintAction;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            playerCamera = GetComponentInChildren<PlayerCamera>();
            playerShooting = GetComponent<PlayerShooting>();
            playerInput = GetComponent<PlayerInput>();

            aimAction = playerInput.actions["Aim"];
            attackAction = playerInput.actions["Attack"];
            sprintAction = playerInput.actions["Sprint"];
        }

        private void Update()
        {
            // Read hold-type inputs every frame
            playerShooting?.SetAiming(aimAction.IsPressed());
            playerShooting?.SetFiring(attackAction.IsPressed());
            playerController?.SetSprinting(sprintAction.IsPressed());
        }

        public void OnMove(InputValue value)
        {
            playerController?.SetMoveInput(value.Get<Vector2>());
        }

        public void OnLook(InputValue value)
        {
            playerCamera?.SetLookInput(value.Get<Vector2>());
        }

        public void OnJump(InputValue value)
        {
            if (value.isPressed)
            {
                playerController?.Jump();
            }
        }

        public void OnCrouch(InputValue value)
        {
            if (value.isPressed)
            {
                playerController?.ToggleCrouch();
            }
        }

        public void OnReload(InputValue value)
        {
            if (value.isPressed)
            {
                playerShooting?.StartReload();
            }
        }
    }
}
