using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Mirror;
using FPSGame.Vehicle;
using FPSGame.Environment;
using FPSGame.Networking;

namespace FPSGame.Player
{
    public class PlayerInputHandler : NetworkBehaviour
    {
        [SerializeField] private float vehicleSearchRadius = 5f;
        [SerializeField] private LayerMask vehicleLayerMask = ~0;
        [Tooltip("How often (seconds) to re-scan for a nearby interactable to drive the \"[E] …\" prompt.")]
        [SerializeField] private float promptScanInterval = 0.1f;
        [Tooltip("Half-angle (deg) of the cone in front of the camera a world object must sit in to be " +
                 "interactable. Wider = more forgiving. Car entry/exit and reviving ignore this.")]
        [Range(5f, 90f)]
        [SerializeField] private float lookAngleThreshold = 40f;

        private PlayerController playerController;
        private PlayerCamera playerCamera;
        private PlayerShooting playerShooting;
        private PlayerCarry playerCarry;
        private PlayerKeycardHand playerKeycardHand;
        private PlayerKeyring playerKeyring;
        private PlayerHealth playerHealth;
        private PlayerInput playerInput;
        private InputAction aimAction;
        private InputAction attackAction;
        private InputAction sprintAction;
        private InputAction interactAction;
        private bool isInitialized;
        // After a drop/throw the loot leaves the hands the same frame; block the button's
        // normal fire/aim action until it is released so the same press doesn't also shoot/ADS.
        private bool fireBlockedAfterCarry;
        private bool aimBlockedAfterCarry;

        private CarController currentVehicle;

        private InteractableObject currentInteractTarget;
        private float interactHoldTimer;
        private bool isInteractHeld;

        // Reviving a downed teammate runs its own hold, separate from InteractableObject's,
        // but reports through the same progress events so the HUD bar works unchanged.
        private PlayerRevive currentReviveTarget;
        private float reviveHoldTimer;
        private bool isReviveHeld;

        public bool IsInVehicle => currentVehicle != null;
        private bool IsDowned => playerHealth != null && playerHealth.IsDowned;

        // Throttled scan for whatever E would act on right now, so the HUD can show a prompt
        // before the player even presses the key.
        private float promptScanTimer;
        private InteractPrompt currentPrompt = InteractPrompt.None;

        // Subscribe to these in HUD scripts for progress bar / cancel feedback.
        public event System.Action<float> OnInteractProgress;  // 0–1
        public event System.Action OnInteractCancelled;
        public event System.Action OnInteractFinished;
        // Fires with whatever E would do right now (an action verb, or a "requirement not met"
        // warning), or InteractPrompt.None when nothing is in reach. Drives the HUD prompt line.
        public event System.Action<InteractPrompt> OnInteractPromptChanged;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            playerCamera = GetComponentInChildren<PlayerCamera>();
            playerShooting = GetComponent<PlayerShooting>();
            playerCarry = GetComponent<PlayerCarry>();
            playerKeycardHand = GetComponent<PlayerKeycardHand>();
            playerKeyring = GetComponent<PlayerKeyring>();
            playerHealth = GetComponent<PlayerHealth>();
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

            if (playerHealth != null)
            {
                playerHealth.OnDeath += OnPlayerDied;
                playerHealth.OnDowned += OnPlayerDowned;
                playerHealth.OnRevived += OnPlayerRevived;
            }

            if (GameStateManager.Instance != null)
                GameStateManager.Instance.OnMatchEnded += OnMatchEnded;

            isInitialized = true;

            // Spawned into a match that is already decided — late join, or a respawn after the wipe.
            if (GameStateManager.Instance != null && GameStateManager.Instance.IsMatchOver)
                OnMatchEnded(GameStateManager.Instance.Result);
        }

        private void OnDestroy()
        {
            if (playerHealth != null)
            {
                playerHealth.OnDeath -= OnPlayerDied;
                playerHealth.OnDowned -= OnPlayerDowned;
                playerHealth.OnRevived -= OnPlayerRevived;
            }

            if (GameStateManager.Instance != null)
                GameStateManager.Instance.OnMatchEnded -= OnMatchEnded;
        }

        // Win or lose, the match is over for everyone at once: stop routing input and hand the
        // mouse back so the end screen is clickable. The world keeps running around them.
        private void OnMatchEnded(MatchResult result)
        {
            AbandonHolds();
            ClearInteractPrompt();

            isInitialized = false;
            if (playerInput != null) playerInput.enabled = false;

            if (currentVehicle != null)
            {
                CmdExitVehicle(currentVehicle.netIdentity);
                ExitVehicle();
            }

            playerShooting?.SetFiring(false);
            playerShooting?.SetAiming(false);
            playerController?.SetSprinting(false);
            playerController?.SetMoveInput(Vector2.zero);

            playerCamera?.UnlockCursor();
        }

        private void OnPlayerDied()
        {
            // An instant kill skips the downed state entirely, so a hold may still be in flight.
            AbandonHolds();
            ClearInteractPrompt();

            isInitialized = false;
            if (playerInput != null) playerInput.enabled = false;
            playerShooting?.SetFiring(false);
            playerShooting?.SetAiming(false);
            playerController?.SetSprinting(false);
            playerKeycardHand?.Unequip();
            if (currentVehicle != null)
            {
                CmdExitVehicle(currentVehicle.netIdentity);
                ExitVehicle();
            }
        }

        // Unlike death, the downed player keeps their PlayerInput: they still get to look around
        // while waiting for a teammate. Everything else is taken away here.
        private void OnPlayerDowned()
        {
            AbandonHolds();

            if (currentVehicle != null)
            {
                CmdExitVehicle(currentVehicle.netIdentity);
                ExitVehicle();
            }

            playerCarry?.ForceDrop();
            playerKeycardHand?.Unequip();

            playerShooting?.SetFiring(false);
            playerShooting?.SetAiming(false);
            playerShooting?.SetDowned(true);

            playerController?.SetSprinting(false);
            playerController?.SetDownedMode(true);

            fireBlockedAfterCarry = false;
            aimBlockedAfterCarry = false;
        }

        private void OnPlayerRevived()
        {
            playerShooting?.SetDowned(false);
            playerController?.SetDownedMode(false);
        }

        // Drops whatever hold was in flight — revive or interactable.
        private void AbandonHolds()
        {
            if (isReviveHeld)
                CancelRevive();
            if (isInteractHeld)
                CancelInteraction();
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

            // Runs even while downed so the prompt clears itself when the player goes down.
            UpdateInteractPrompt();

            // Down on the floor: PlayerCamera still runs the look, nothing else is available.
            if (IsDowned)
                return;

            if (currentVehicle != null && currentVehicle.DriverSeat != null)
                transform.position = currentVehicle.DriverSeat.position;

            bool isCarrying = playerCarry != null && playerCarry.IsCarrying;
            bool cardInHand = playerKeycardHand != null && playerKeycardHand.IsEquipped;

            if (isCarrying || cardInHand)
            {
                // Hands occupied (loot or keycard) — firing/aiming is fully suppressed
                // (PlayerShooting.isCarrying). LMB throws; for loot RMB also puts it down.
                if (attackAction != null && attackAction.triggered)
                {
                    fireBlockedAfterCarry = true;
                    if (isCarrying)
                        playerCarry.TryThrow();
                    else
                        playerKeycardHand.TryThrow();
                }
                if (aimAction != null && aimAction.triggered)
                {
                    aimBlockedAfterCarry = true;
                    // Keycard has no RMB action; it's put away via "0" or a weapon key.
                    if (isCarrying)
                        playerCarry.TryDrop();
                }
            }
            else
            {
                if (aimAction != null)
                {
                    // Clear the block only once RMB is fully released after a drop.
                    if (aimBlockedAfterCarry && !aimAction.IsPressed())
                        aimBlockedAfterCarry = false;

                    playerShooting?.SetAiming(!aimBlockedAfterCarry && aimAction.IsPressed());
                }

                if (attackAction != null)
                {
                    // Clear the block only once LMB is fully released after a throw.
                    if (fireBlockedAfterCarry && !attackAction.IsPressed())
                        fireBlockedAfterCarry = false;

                    playerShooting?.SetFiring(!fireBlockedAfterCarry && attackAction.IsPressed());
                }
            }

            if (currentVehicle == null && sprintAction != null)
                playerController?.SetSprinting(sprintAction.IsPressed());

            if (currentVehicle != null)
            {
                var kb = Keyboard.current;
                if (kb != null && kb[Key.G].wasPressedThisFrame)
                    CmdDetachSafe(currentVehicle.netIdentity);
            }

            // Advance the revive hold. The server re-checks all of this before it lets the
            // teammate back up; this side only drives the timer and the progress bar.
            if (isReviveHeld)
            {
                bool reviveLost = currentReviveTarget == null ||
                    currentReviveTarget.Health == null ||
                    !currentReviveTarget.Health.IsDowned ||
                    Vector3.Distance(transform.position, currentReviveTarget.transform.position)
                        > currentReviveTarget.ReviveRange;

                if (reviveLost)
                {
                    CancelRevive();
                }
                else
                {
                    reviveHoldTimer += Time.deltaTime;
                    OnInteractProgress?.Invoke(reviveHoldTimer / currentReviveTarget.ReviveHoldDuration);

                    if (reviveHoldTimer >= currentReviveTarget.ReviveHoldDuration)
                        CompleteRevive();
                }
            }

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
                else if (currentInteractTarget.HoldToActivate)
                {
                    // Hold-to-activate mode: nothing to advance — active state is server-driven
                    // and stays on until release or range loss. Range loss handled above.
                }
                else
                {
                    interactHoldTimer += Time.deltaTime;
                    OnInteractProgress?.Invoke(interactHoldTimer / currentInteractTarget.HoldDuration);

                    if (interactHoldTimer >= currentInteractTarget.HoldDuration)
                        CompleteInteraction();
                }
            }

            // Number keys — on foot and not carrying loot. "0" pulls the keycard
            // into / out of hand; 1-9 select weapon slots (and put any held card away).
            if (currentVehicle == null && !isCarrying && playerShooting != null)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb[Key.Digit0].wasPressedThisFrame && playerKeycardHand != null)
                    {
                        playerKeycardHand.ToggleEquip();
                    }
                    else
                    {
                        int count = Mathf.Min(playerShooting.WeaponCount, slotKeys.Length);
                        for (int i = 0; i < count; i++)
                        {
                            if (kb[slotKeys[i]].wasPressedThisFrame)
                            {
                                // Selecting a weapon puts the keycard away.
                                playerKeycardHand?.Unequip();
                                playerShooting.SelectWeaponSlot(i);
                                break;
                            }
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
            // No jumping while carrying loot.
            if (playerCarry != null && playerCarry.IsCarrying)
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
            if (!isOwned || IsDowned)
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

                // Can't pick up or interact while already carrying loot or holding a card.
                if (playerCarry != null && playerCarry.IsCarrying)
                    return;
                if (playerKeycardHand != null && playerKeycardHand.IsEquipped)
                    return;

                // Picking a teammate up outranks everything else on foot — a body dropped next to
                // the car or a crate must not be shadowed by boarding or looting.
                PlayerRevive downedTeammate = FindNearbyDownedPlayer();
                if (downedTeammate != null)
                {
                    currentReviveTarget = downedTeammate;
                    reviveHoldTimer = 0f;
                    isReviveHeld = true;
                    CmdBeginRevive(downedTeammate.netIdentity);
                    OnInteractProgress?.Invoke(0f);
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

                // Try to pick up a carryable object — must be roughly looked at, like other world objects.
                if (playerCarry != null)
                {
                    CarryableObject carryTarget = playerCarry.FindNearbyCarryable();
                    if (carryTarget != null && IsInView(InteractionCenter(carryTarget)))
                    {
                        playerCarry.TryPickupTarget(carryTarget);
                        if (playerCarry.IsCarrying)
                            return;
                    }
                }

                // Toggle a nearby breach-console key-card slot (insert / pull out).
                BreachConsoleSlot slot = FindNearbyConsoleSlot();
                if (slot != null)
                {
                    CmdToggleBreachConsole(slot.netIdentity);
                    return;
                }

                // Start hold for interactive objects.
                InteractableObject target = FindNearbyInteractable();
                if (target != null)
                {
                    currentInteractTarget = target;
                    interactHoldTimer = 0f;
                    isInteractHeld = true;

                    if (target.HoldToActivate)
                        CmdSetInteractActive(target.netIdentity, true);
                    else
                        OnInteractProgress?.Invoke(0f);
                }
            }
            else
            {
                // Key released before hold completed — cancel.
                if (isReviveHeld)
                    CancelRevive();
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

            // Put any held keycard away before driving.
            playerKeycardHand?.Unequip();

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

        // Throttled: re-scans for the best interaction target and pushes its verb to the HUD only
        // when it changes. Mirrors the priority order in OnInteract so the prompt never promises
        // something a press wouldn't actually do.
        private void UpdateInteractPrompt()
        {
            promptScanTimer -= Time.deltaTime;
            if (promptScanTimer > 0f)
                return;
            promptScanTimer = promptScanInterval;

            InteractPrompt prompt = ComputeInteractPrompt();
            if (prompt == currentPrompt)
                return;

            currentPrompt = prompt;
            OnInteractPromptChanged?.Invoke(prompt);
        }

        private InteractPrompt ComputeInteractPrompt()
        {
            // Downed, or a hold already in flight (the progress bar owns the screen then).
            if (IsDowned || isInteractHeld || isReviveHeld)
                return InteractPrompt.None;

            // In a vehicle E only exits.
            if (currentVehicle != null)
                return InteractPrompt.Action("Выйти из машины");

            // Hands full — E does nothing until the loot/keycard is put down.
            bool isCarrying = playerCarry != null && playerCarry.IsCarrying;
            bool cardInHand = playerKeycardHand != null && playerKeycardHand.IsEquipped;
            if (isCarrying || cardInHand)
                return InteractPrompt.None;

            if (FindNearbyDownedPlayer() != null)
                return InteractPrompt.Action("Поднять напарника");

            CarController car = FindNearbyCar();
            if (car != null && !car.HasDriver)
                return InteractPrompt.Action("Сесть в машину");

            if (playerCarry != null)
            {
                CarryableObject carry = playerCarry.FindNearbyCarryable();
                if (carry != null && IsInView(InteractionCenter(carry)))
                    return InteractPrompt.Action("Взять");
            }

            BreachConsoleSlot slot = FindNearbyConsoleSlot();
            if (slot != null)
            {
                // Missing the required key → tell the player which one, instead of a dead "[E]".
                // (The current operator still holds the key, so pulling their own card out isn't blocked.)
                if (playerKeyring != null && !playerKeyring.HasKey(slot.RequiredKey))
                    return InteractPrompt.Warn($"Требуется {KeycardNames.DisplayName(slot.RequiredKey)}");

                return InteractPrompt.Action("Использовать консоль");
            }

            InteractableObject interactable = FindNearbyInteractable();
            if (interactable != null)
                return InteractPrompt.Action(interactable.PromptText);

            return InteractPrompt.None;
        }

        private void ClearInteractPrompt()
        {
            if (!currentPrompt.HasValue)
                return;

            currentPrompt = InteractPrompt.None;
            OnInteractPromptChanged?.Invoke(InteractPrompt.None);
        }

        // A world object counts as "looked at" when it sits within lookAngleThreshold degrees of the
        // camera's forward. This is a forgiving cone, not a pixel-precise ray — no pixel-hunting — and
        // it aims at the collider's bounds centre, not the pivot, so tall crates don't fall out of it.
        private bool IsInView(Vector3 targetCenter)
        {
            if (playerCamera == null)
                return true;

            Ray aim = playerCamera.GetAimRay();
            Vector3 toTarget = targetCenter - aim.origin;
            if (toTarget.sqrMagnitude < 0.0001f)
                return true;

            float dot = Vector3.Dot(aim.direction, toTarget.normalized);
            return dot >= Mathf.Cos(lookAngleThreshold * Mathf.Deg2Rad);
        }

        // Bounds centre of the object's collider if it has one, else its pivot. Keeps the view check
        // stable for objects whose pivot sits at the floor.
        private static Vector3 InteractionCenter(Component c)
        {
            var col = c.GetComponentInChildren<Collider>();
            return col != null ? col.bounds.center : c.transform.position;
        }

        private InteractableObject FindNearbyInteractable()
        {
            // Broad-phase: grab everything in a generous radius, then filter
            // by each object's own InteractRange for authoritative distance check.
            Collider[] hits = new Collider[10];
            var size = Physics.OverlapSphereNonAlloc(transform.position, 10f, hits);
            InteractableObject closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < size; i++)
            {
                var interactable = hits[i].GetComponentInParent<InteractableObject>();
                if (interactable == null)
                    continue;

                float dist = Vector3.Distance(transform.position, interactable.transform.position);
                if (dist <= interactable.InteractRange && dist < closestDist && IsInView(hits[i].bounds.center))
                {
                    closest = interactable;
                    closestDist = dist;
                }
            }

            return closest;
        }

        // Revive targets are found by walking the spawned players, NOT by a physics overlap: a
        // downed teammate is a remote player on the reviver's client, and remote players have their
        // CharacterController — the only body collider on the prefab — disabled, so OverlapSphere
        // never sees them.
        private PlayerRevive FindNearbyDownedPlayer()
        {
            PlayerRevive closest = null;
            float closestDist = float.MaxValue;

            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if (identity == null)
                    continue;

                var candidate = identity.GetComponent<PlayerRevive>();
                if (candidate == null || candidate.gameObject == gameObject)
                    continue;

                bool downed = candidate.Health != null && candidate.Health.IsDowned;
                if (!downed)
                    continue;

                float dist = Vector3.Distance(transform.position, candidate.transform.position);
                if (dist <= candidate.ReviveRange && dist < closestDist)
                {
                    closest = candidate;
                    closestDist = dist;
                }
            }

            return closest;
        }

        private void CancelRevive()
        {
            if (currentReviveTarget != null)
                CmdCancelRevive(currentReviveTarget.netIdentity);

            isReviveHeld = false;
            reviveHoldTimer = 0f;
            currentReviveTarget = null;
            OnInteractCancelled?.Invoke();
        }

        private void CompleteRevive()
        {
            isReviveHeld = false;
            reviveHoldTimer = 0f;
            var target = currentReviveTarget;
            currentReviveTarget = null;
            CmdCompleteRevive(target.netIdentity);
            OnInteractFinished?.Invoke();
        }

        [Command]
        private void CmdBeginRevive(NetworkIdentity targetIdentity)
        {
            if (targetIdentity == null)
                return;

            var revive = targetIdentity.GetComponent<PlayerRevive>();
            revive?.ServerBeginRevive(gameObject);
        }

        [Command]
        private void CmdCancelRevive(NetworkIdentity targetIdentity)
        {
            if (targetIdentity == null)
                return;

            var revive = targetIdentity.GetComponent<PlayerRevive>();
            revive?.ServerCancelRevive(gameObject);
        }

        [Command]
        private void CmdCompleteRevive(NetworkIdentity targetIdentity)
        {
            if (targetIdentity == null)
                return;

            var revive = targetIdentity.GetComponent<PlayerRevive>();
            revive?.ServerCompleteRevive(gameObject);
        }

        private BreachConsoleSlot FindNearbyConsoleSlot()
        {
            // Broad-phase overlap, then filter by each slot's own InteractRange.
            Collider[] hits = new Collider[10];
            var size = Physics.OverlapSphereNonAlloc(transform.position, 10f, hits);
            BreachConsoleSlot closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < size; i++)
            {
                var candidate = hits[i].GetComponentInParent<BreachConsoleSlot>();
                if (candidate == null)
                    continue;

                float dist = Vector3.Distance(transform.position, candidate.transform.position);
                if (dist <= candidate.InteractRange && dist < closestDist && IsInView(hits[i].bounds.center))
                {
                    closest = candidate;
                    closestDist = dist;
                }
            }

            return closest;
        }

        [Command]
        private void CmdToggleBreachConsole(NetworkIdentity slotIdentity)
        {
            if (slotIdentity == null)
                return;

            var slot = slotIdentity.GetComponent<BreachConsoleSlot>();
            if (slot == null)
                return;

            // Server-side range re-validation (+1 m tolerance for network latency).
            float dist = Vector3.Distance(transform.position, slot.transform.position);
            if (dist > slot.InteractRange + 1f)
                return;

            slot.ServerToggle(gameObject);
        }

        private void CancelInteraction()
        {
            if (currentInteractTarget != null && currentInteractTarget.HoldToActivate)
                CmdSetInteractActive(currentInteractTarget.netIdentity, false);

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
            OnInteractFinished?.Invoke();
        }

        [Command]
        private void CmdDetachSafe(NetworkIdentity carNetId)
        {
            if (carNetId == null) return;
            var car = carNetId.GetComponent<CarController>();
            if (car == null) return;
            if (car.netIdentity.connectionToClient != connectionToClient) return;
            car.ServerDetachSafe();
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

        [Command]
        private void CmdSetInteractActive(NetworkIdentity targetIdentity, bool active)
        {
            if (targetIdentity == null)
                return;

            var interactable = targetIdentity.GetComponent<InteractableObject>();
            if (interactable == null)
                return;

            if (active)
            {
                // Range re-validation only matters when starting activation.
                float dist = Vector3.Distance(transform.position, interactable.transform.position);
                if (dist > interactable.InteractRange + 1f)
                    return;
            }

            interactable.ServerSetActive(active, gameObject);
        }
    }
}
