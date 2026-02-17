using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;
using FPSGame.Vehicle;

namespace FPSGame.Player
{
    public class PlayerInputHandler : NetworkBehaviour
    {
        [SerializeField] private float vehicleSearchRadius = 5f;
        [SerializeField] private LayerMask vehicleLayerMask = ~0;

        private PlayerController playerController;
        private PlayerCamera playerCamera;
        private PlayerShooting playerShooting;
        private PlayerInput playerInput;
        private InputAction aimAction;
        private InputAction attackAction;
        private InputAction sprintAction;
        private InputAction interactAction;
        private bool isInitialized;

        private CarController currentVehicle;

        public bool IsInVehicle => currentVehicle != null;

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

            if (playerInput != null)
            {
                playerInput.enabled = true;

                aimAction = playerInput.actions["Aim"];
                attackAction = playerInput.actions["Attack"];
                sprintAction = playerInput.actions["Sprint"];
                interactAction = playerInput.actions["Interact"];
            }

            isInitialized = true;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (!isOwned && playerInput != null)
            {
                playerInput.enabled = false;
            }
        }

        private void Update()
        {
            if (!isOwned || !isInitialized)
                return;

            if (currentVehicle != null)
            {
                // In vehicle: skip shooting/sprint inputs
                return;
            }

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

            Vector2 input = value.Get<Vector2>();

            if (currentVehicle != null)
            {
                currentVehicle.SetMoveInput(input);
                return;
            }

            playerController?.SetMoveInput(input);
        }

        public void OnLook(InputValue value)
        {
            if (!isOwned)
                return;
            playerCamera?.SetLookInput(value.Get<Vector2>());
        }

        public void OnJump(InputValue value)
        {
            if (!isOwned || currentVehicle != null)
                return;
            if (value.isPressed)
            {
                playerController?.Jump();
            }
        }

        public void OnCrouch(InputValue value)
        {
            if (!isOwned || currentVehicle != null)
                return;
            if (value.isPressed)
            {
                playerController?.ToggleCrouch();
            }
        }

        public void OnReload(InputValue value)
        {
            if (!isOwned || currentVehicle != null)
                return;
            if (value.isPressed)
            {
                playerShooting?.StartReload();
            }
        }

        public void OnInteract(InputValue value)
        {
            if (!isOwned || !value.isPressed)
                return;

            if (currentVehicle != null)
            {
                CmdExitVehicle(currentVehicle.netIdentity);
                ExitVehicle();
            }
            else
            {
                CarController car = FindNearbyCar();
                if (car != null && !car.HasDriver)
                {
                    CmdEnterVehicle(car.netIdentity);
                    EnterVehicle(car);
                }
            }
        }

        private CarController FindNearbyCar()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, vehicleSearchRadius, vehicleLayerMask);
            CarController closest = null;
            float closestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                var car = hit.GetComponentInParent<CarController>();
                if (car == null)
                {
                    continue;
                }

                float dist = Vector3.Distance(transform.position, car.transform.position);
                if (dist < car.InteractDistance && dist < closestDist)
                {
                    closest = car;
                    closestDist = dist;
                }
            }

            return closest;
        }

        [Command]
        private void CmdEnterVehicle(NetworkIdentity carNetId)
        {
            if (carNetId == null)
                return;

            var car = carNetId.GetComponent<CarController>();
            if (car == null || car.HasDriver)
                return;

            float dist = Vector3.Distance(transform.position, car.transform.position);
            if (dist > car.InteractDistance + 1f)
                return;

            car.ServerEnter(connectionToClient, netIdentity);
        }

        [Command]
        private void CmdExitVehicle(NetworkIdentity carNetId)
        {
            if (carNetId == null)
                return;

            var car = carNetId.GetComponent<CarController>();
            if (car == null || !car.HasDriver)
                return;

            // Validate that the requesting player actually has authority over this car
            if (car.netIdentity.connectionToClient != connectionToClient)
                return;

            car.ServerExit();
        }

        private void EnterVehicle(CarController car)
        {
            currentVehicle = car;
            playerController?.SetVehicleMode(true);

            // Clear shooting states
            playerShooting?.SetAiming(false);
            playerShooting?.SetFiring(false);
            playerController?.SetSprinting(false);
        }

        private void ExitVehicle()
        {
            if (currentVehicle != null)
            {
                currentVehicle.SetMoveInput(Vector2.zero);
            }
            currentVehicle = null;
            playerController?.SetVehicleMode(false);
        }
    }
}
