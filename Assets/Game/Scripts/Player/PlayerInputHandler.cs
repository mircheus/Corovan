using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Mirror;
using FPSGame.Vehicle;
using FPSGame.Environment;

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

        private InteractableObject currentInteractTarget;
        private float interactHoldTimer;
        private bool isInteractHeld;

        public bool IsInVehicle => currentVehicle != null;

        // Subscribe to these in HUD scripts for progress bar / cancel feedback.
        public event System.Action<float> OnInteractProgress;  // 0–1
        public event System.Action OnInteractCancelled;

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

        // Number row keys used for direct weapon slot selection (1 = slot 0, 2 = slot 1 …)
        private static readonly Key[] slotKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
            Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
        };

        private void Update()
        {
            if (!isOwned || !isInitialized)
                return;

            if (aimAction != null)
                playerShooting?.SetAiming(aimAction.IsPressed());

            if (attackAction != null)
                playerShooting?.SetFiring(attackAction.IsPressed());

            if (currentVehicle == null && sprintAction != null)
                playerController?.SetSprinting(sprintAction.IsPressed());

            // Advance hold-interaction timer.
            if (isInteractHeld)
            {
                bool outOfRange = currentInteractTarget == null ||
                    Vector3.Distance(transform.position, currentInteractTarget.transform.position)
                        > currentInteractTarget.InteractRange;

                if (outOfRange)
                {
                    CancelInteraction();
                }
                else
                {
                    interactHoldTimer += Time.deltaTime;
                    OnInteractProgress?.Invoke(interactHoldTimer / currentInteractTarget.HoldDuration);

                    if (interactHoldTimer >= currentInteractTarget.HoldDuration)
                        CompleteInteraction();
                }
            }

            // Direct slot selection via number keys — handled here rather than via
            // input actions so a variable number of slots is naturally supported.
            if (currentVehicle == null && playerShooting != null)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    int count = Mathf.Min(playerShooting.WeaponCount, slotKeys.Length);
                    for (int i = 0; i < count; i++)
                    {
                        if (kb[slotKeys[i]].wasPressedThisFrame)
                        {
                            playerShooting.SelectWeaponSlot(i);
                            break;
                        }
                    }
                }
            }
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

        public void OnPrevious(InputValue value)
        {
            if (!isOwned || currentVehicle != null)
                return;
            if (value.isPressed)
                playerShooting?.SelectPreviousWeapon();
        }

        public void OnNext(InputValue value)
        {
            if (!isOwned || currentVehicle != null)
                return;
            if (value.isPressed)
                playerShooting?.SelectNextWeapon();
        }

        public void OnInteract(InputValue value)
        {
            if (!isOwned)
                return;

            if (value.isPressed)
            {
                // Vehicle exit takes priority.
                if (currentVehicle != null)
                {
                    CmdExitVehicle(currentVehicle.netIdentity);
                    ExitVehicle();
                    return;
                }

                // Vehicle enter next.
                CarController car = FindNearbyCar();
                if (car != null && !car.HasDriver)
                {
                    CmdEnterVehicle(car.netIdentity);
                    EnterVehicle(car);
                    return;
                }

                // Start hold for interactive objects.
                InteractableObject target = FindNearbyInteractable();
                if (target != null)
                {
                    currentInteractTarget = target;
                    interactHoldTimer = 0f;
                    isInteractHeld = true;
                    OnInteractProgress?.Invoke(0f);
                }
            }
            else
            {
                // Key released before hold completed — cancel.
                if (isInteractHeld)
                    CancelInteraction();
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
            if (isInteractHeld)
                CancelInteraction();

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

        private InteractableObject FindNearbyInteractable()
        {
            // Broad-phase: grab everything in a generous radius, then filter
            // by each object's own InteractRange for authoritative distance check.
            Collider[] hits = Physics.OverlapSphere(transform.position, 10f);
            InteractableObject closest = null;
            float closestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                var interactable = hit.GetComponentInParent<InteractableObject>();
                if (interactable == null)
                    continue;

                float dist = Vector3.Distance(transform.position, interactable.transform.position);
                if (dist <= interactable.InteractRange && dist < closestDist)
                {
                    closest = interactable;
                    closestDist = dist;
                }
            }

            return closest;
        }

        private void CancelInteraction()
        {
            isInteractHeld = false;
            interactHoldTimer = 0f;
            currentInteractTarget = null;
            OnInteractCancelled?.Invoke();
        }

        private void CompleteInteraction()
        {
            isInteractHeld = false;
            interactHoldTimer = 0f;
            var target = currentInteractTarget;
            currentInteractTarget = null;
            CmdInteract(target.netIdentity);
        }

        [Command]
        private void CmdInteract(NetworkIdentity targetIdentity)
        {
            if (targetIdentity == null)
                return;

            var interactable = targetIdentity.GetComponent<InteractableObject>();
            if (interactable == null)
                return;

            // Server-side range re-validation (+1 m tolerance for network latency).
            float dist = Vector3.Distance(transform.position, interactable.transform.position);
            if (dist > interactable.InteractRange + 1f)
                return;

            interactable.ServerInteract(gameObject);
        }
    }
}
