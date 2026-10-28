using UnityEngine;
using Mirror;
using Akila.FPSFramework;
using FPSGame.Vehicle;
using FPSGame.Environment;

namespace FPSGame.Player
{
    // Our movement drives the FPS Framework: it reads FPSF's CharacterInput and exposes itself as the
    // ICharacterController that FPSF's camera, weapon motion and firearms query for speeds.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour, ICharacterController
    {
        [Header("Movement Settings")]
        [SerializeField] private float walkSpeed = 5.0f;
        [SerializeField] private float sprintSpeed = 8.5f;
        [Tooltip("Double-tap sprint (FPSF tactical sprint).")]
        [SerializeField] private float tacticalSprintSpeed = 10.5f;
        [SerializeField] private float crouchSpeed = 2.5f;
        [SerializeField] private float airControlMultiplier = 0.3f;

        [Header("Jump Settings")]
        [SerializeField] private float jumpForce = 7.0f;
        [SerializeField] private float carJumpForce = 10f;
        [SerializeField] private float gravity = -20.0f;
        [SerializeField] private float fallTransitionTime = 0.15f;

        [Header("Ground Detection")]
        [SerializeField] private float groundCheckRadius = 0.3f;
        [SerializeField] private float groundCheckOffset = 0.1f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Slope Settings")]
        [SerializeField] private float maxSlopeAngle = 45.0f;
        [SerializeField] private float stepHeight = 0.35f;

        [Header("Crouch Settings")]
        [SerializeField] private float standingHeight = 2.0f;
        [SerializeField] private float crouchHeight = 1.0f;
        [SerializeField] private float crouchTransitionSpeed = 10.0f;

        private CharacterController characterController;
        private CharacterInput fpsfInput;
        private PlayerCamera playerCamera;
        private PlayerCarry playerCarry;
        private Vector3 velocity;
        private Vector3 externalVelocity;
        private Vector2 moveInput;
        private bool isGrounded;
        private bool isOnCar;
        private bool isSprinting;
        private bool isTacticalSprinting;
        private bool isCrouching;

        // Speeds currently in force. FPSF firearms override them while held (aim / fire walk speed)
        // through ICharacterController.SetSpeed and restore them with ResetSpeed.
        private float activeWalkSpeed;
        private float activeSprintSpeed;
        private float activeTacticalSprintSpeed;
        private bool jumpRequested;
        private bool isDowned;
        private float targetHeight;
        private Vector3 groundNormal = Vector3.up;
        private float airTime;
        private float carrySpeedMultiplier = 1f;

        // Moving-platform support: the car the player currently stands on and its
        // last sampled position, used to carry the player along as the car drives.
        private Transform carPlatform;
        private Vector3 carPlatformLastPos;
        private static readonly Collider[] groundOverlapBuffer = new Collider[8];
        private int groundOverlapCount;

        // The MotionPlatform the player currently stands on (null when off any convoy platform).
        // Resolved for free inside CheckGround's existing feet sweep — no extra physics query.
        private MotionPlatform currentMotionPlatform;

        public bool IsGrounded => isGrounded;
        public bool IsDowned => isDowned;
        public bool OnMotionPlatform => currentMotionPlatform != null;
        public bool IsSprinting => isSprinting && !isCrouching && moveInput.y > 0;
        public bool IsTacticalSprinting => isTacticalSprinting && IsSprinting;
        public bool IsCrouching => isCrouching;
        public Vector3 Velocity => velocity;
        public bool InVehicle { get; private set; }
        public bool IsGrappling { get; set; }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            characterController.stepOffset = stepHeight;
            targetHeight = standingHeight;

            fpsfInput = GetComponent<CharacterInput>();
            playerCamera = GetComponentInChildren<PlayerCamera>();
            playerCarry = GetComponent<PlayerCarry>();
            ResetSpeed();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            // Enable CharacterController for local player
            characterController.enabled = true;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Disable CharacterController for remote players (NetworkTransform handles sync)
            if (!isOwned)
            {
                characterController.enabled = false;
            }
        }

        private void Update()
        {
            if (!isOwned || InVehicle || IsGrappling)
                return;

            ReadFPSFInput();
            CheckGround();
            CheckOnCar();
            ApplyPlatformMovement();
            HandleCrouch();
            HandleMovement();
            ApplyGravity();
        }

        // FPSF's CharacterInput owns the movement keys; route them through our setters so the
        // downed / carry rules stay in one place.
        private void ReadFPSFInput()
        {
            if (fpsfInput == null)
                return;

            SetMoveInput(fpsfInput.moveInput);
            SetSprinting(fpsfInput.sprintInput || fpsfInput.tacticalSprintInput);
            if (!isDowned)
                isTacticalSprinting = fpsfInput.tacticalSprintInput;

            // No jumping while carrying loot.
            if (fpsfInput.jumpInput && (playerCarry == null || !playerCarry.IsCarrying))
                Jump();

            // FPSF toggles crouchInput itself; we may refuse to stand (low ceiling, downed), so the
            // resolved state is written back for FPSF's weapon motion to read.
            if (fpsfInput.crouchInput != isCrouching)
                ToggleCrouch();
            fpsfInput.crouchInput = isCrouching;
        }

        private void CheckGround()
        {
            // Place the sphere just above the CC bottom so it doesn't start overlapping the ground surface.
            // Using (height/2 - radius - offset) moves the origin upward from the bottom, keeping the
            // sphere's lowest point at offset-height above the ground when standing.
            // Vector3 sphereOrigin = transform.position + Vector3.down * (groundCheckOffset);
            // isGrounded = Physics.SphereCast(
            //     sphereOrigin,
            //     groundCheckRadius,
            //     Vector3.down,
            //     out RaycastHit hit,
            //     groundCheckOffset * 2 + 0.1f,
            //     groundMask
            // );
            
            Vector3 sphereOrigin = transform.position + Vector3.down * (groundCheckOffset);
            int count = Physics.OverlapSphereNonAlloc(
                sphereOrigin, groundCheckRadius, groundOverlapBuffer, groundMask, QueryTriggerInteraction.Ignore);
            isGrounded = count > 0;
            groundOverlapCount = count;

            // Same sweep also tells us whether the ground we're on is a MotionPlatform,
            // so the ride only affects the player while actually standing on the convoy.
            MotionPlatform platform = null;
            for (int i = 0; i < count; i++)
            {
                platform = groundOverlapBuffer[i].GetComponentInParent<MotionPlatform>();
                if (platform != null)
                    break;
            }
            currentMotionPlatform = platform;

            // groundNormal = isGrounded ? hit.normal : Vector3.up;
        }

        // Reuses CheckGround's feet sweep: a car is found by its CarController, not by layer, so a
        // car prefab whose colliders sit on Default still carries the player standing on it.
        private void CheckOnCar()
        {
            // Resolve the actual car root so platform tracking uses a stable transform
            // even if several of the car's child colliders fall inside the sphere.
            Transform newPlatform = null;
            for (int i = 0; i < groundOverlapCount; i++)
            {
                CarController car = groundOverlapBuffer[i].GetComponentInParent<CarController>();
                if (car != null)
                {
                    newPlatform = car.transform;
                    break;
                }
            }

            isOnCar = newPlatform != null;

            // Stepping onto a (different) car: reset the sampled position so the first
            // frame doesn't apply one huge accumulated delta.
            if (newPlatform != carPlatform)
            {
                carPlatform = newPlatform;
                if (carPlatform != null)
                    carPlatformLastPos = carPlatform.position;
            }
        }

        // Carries the player along with the car they stand on: applies the car's
        // per-frame position change directly to the CharacterController.
        private void ApplyPlatformMovement()
        {
            if (carPlatform == null)
                return;

            Vector3 delta = carPlatform.position - carPlatformLastPos;
            carPlatformLastPos = carPlatform.position;

            if (delta.sqrMagnitude > 0.0000001f)
                characterController.Move(delta);
        }

        private void HandleMovement()
        {
            float currentSpeed = GetCurrentSpeed();

            Vector3 moveDirection = transform.right * moveInput.x + transform.forward * moveInput.y;

            if (isGrounded)
            {
                float slopeAngle = Vector3.Angle(groundNormal, Vector3.up);
                if (slopeAngle > 0 && slopeAngle <= maxSlopeAngle)
                {
                    moveDirection = Vector3.ProjectOnPlane(moveDirection, groundNormal).normalized;
                }
                else if (slopeAngle > maxSlopeAngle)
                {
                    Vector3 slopeDirection = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
                    moveDirection += slopeDirection;
                }
            }

            Vector3 move = moveDirection * currentSpeed;

            if (!isGrounded)
            {
                move *= airControlMultiplier;
                move += new Vector3(velocity.x * (1 - airControlMultiplier), 0, velocity.z * (1 - airControlMultiplier));
            }

            velocity.x = move.x;
            velocity.z = move.z;

            if (jumpRequested && isGrounded)
            {
                velocity.y = isOnCar ? carJumpForce : jumpForce;
                jumpRequested = false;
            }

            characterController.Move((velocity + externalVelocity) * Time.deltaTime);
            externalVelocity = Vector3.zero;
        }

        public void AddExternalVelocity(Vector3 worldVelocity)
        {
            if (!isOwned || InVehicle)
                return;
            externalVelocity += worldVelocity;
        }

        // Owner-side teleport. Disabling the CharacterController lets us hard-set the transform
        // without the controller fighting the move; velocities are cleared so the player doesn't
        // keep the momentum that threw them off. Replicates to others via NetworkTransform.
        public void TeleportTo(Vector3 worldPosition)
        {
            if (!isOwned)
                return;

            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.position = worldPosition;
            velocity = Vector3.zero;
            externalVelocity = Vector3.zero;
            characterController.enabled = wasEnabled;
        }

        private float GetCurrentSpeed()
        {
            float baseSpeed;
            if (isCrouching)
                baseSpeed = crouchSpeed;
            else if (IsTacticalSprinting)
                baseSpeed = activeTacticalSprintSpeed;
            else if (IsSprinting)
                baseSpeed = activeSprintSpeed;
            else
                baseSpeed = activeWalkSpeed;

            return baseSpeed * carrySpeedMultiplier;
        }

        #region ICharacterController (FPS Framework)

        public void SetSpeed(float walk, float run, float tacticalSprint)
        {
            activeWalkSpeed = walk;
            activeSprintSpeed = run;
            activeTacticalSprintSpeed = tacticalSprint;
        }

        public void ResetSpeed()
        {
            activeWalkSpeed = walkSpeed;
            activeSprintSpeed = sprintSpeed;
            activeTacticalSprintSpeed = tacticalSprintSpeed;
        }

        float ICharacterController.sensitivity => playerCamera != null ? playerCamera.FPSFSensitivity : 1f;
        float ICharacterController.walkSpeed => activeWalkSpeed;
        float ICharacterController.sprintSpeed => activeSprintSpeed;
        // FPSF divides by this for its head bob; never let it reach zero.
        float ICharacterController.tacticalSprintSpeed => Mathf.Max(activeTacticalSprintSpeed, 0.01f);
        float ICharacterController.tacticalSprintAmount => IsTacticalSprinting ? 1f : 0f;
        bool ICharacterController.MaxedCameraRotation() => playerCamera != null && playerCamera.IsPitchClamped;

        #endregion

        // Set by PlayerCarry: 1 = normal speed, <1 = slowed while carrying loot.
        public void SetCarrySpeedMultiplier(float multiplier)
        {
            if (!isOwned)
                return;
            carrySpeedMultiplier = Mathf.Clamp(multiplier, 0.1f, 1f);
        }

        private void ApplyGravity()
        {
            if (isGrounded && velocity.y < 0)
            {
                velocity.y = -2f;
                airTime = 0f;
            }
            else
            {
                airTime += Time.deltaTime;

                // Jumping up: full gravity immediately so the arc stays snappy.
                // Falling naturally (stepped off ledge): ramp gravity up over fallTransitionTime
                // so the transition from walking to falling feels smooth instead of a sudden snap.
                float gravityScale = velocity.y > 0f ? 1f : Mathf.Clamp01(airTime / fallTransitionTime);
                velocity.y += gravity * gravityScale * Time.deltaTime;
            }
        }

        private void HandleCrouch()
        {
            targetHeight = isCrouching ? crouchHeight : standingHeight;

            if (Mathf.Abs(characterController.height - targetHeight) > 0.01f)
            {
                float previousHeight = characterController.height;
                characterController.height = Mathf.Lerp(characterController.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);

                float heightDifference = characterController.height - previousHeight;
                transform.position += Vector3.up * (heightDifference / 2);
            }
        }

        public void SetMoveInput(Vector2 input)
        {
            if (!isOwned || isDowned)
                return;
            moveInput = input;
        }

        public void Jump()
        {
            if (!isOwned || isDowned)
                return;
            if (isGrounded)
            {
                jumpRequested = true;
            }
        }

        public void SetSprinting(bool sprinting)
        {
            if (!isOwned || isDowned)
                return;
            isSprinting = sprinting;
        }

        public void ToggleCrouch()
        {
            if (!isOwned || isDowned)
                return;
            if (!isCrouching)
            {
                isCrouching = true;
            }
            else if (CanStand())
            {
                isCrouching = false;
            }
        }

        private bool CanStand()
        {
            float checkDistance = standingHeight - crouchHeight;
            return !Physics.Raycast(
                transform.position,
                Vector3.up,
                checkDistance + 0.1f,
                groundMask,
                QueryTriggerInteraction.Ignore
            );
        }

        // A downed player is pinned where they fell but keeps their CharacterController: gravity
        // still settles them onto the ground and the convoy platform still carries them along.
        // Only the input is taken away — the collapsed pose is the crouch height.
        public void SetDownedMode(bool downed)
        {
            if (!isOwned)
                return;

            isDowned = downed;

            if (downed)
            {
                moveInput = Vector2.zero;
                velocity.x = 0f;
                velocity.z = 0f;
                isSprinting = false;
                isTacticalSprinting = false;
                jumpRequested = false;
                isCrouching = true;
            }
            else
            {
                isCrouching = !CanStand();
            }
            SyncFPSFCrouch();
        }

        // Our forced crouch changes must reach FPSF's crouch flag too, or ReadFPSFInput would
        // take the stale flag for a C press next frame and undo them (e.g. stay crouched after a revive).
        private void SyncFPSFCrouch()
        {
            if (fpsfInput != null)
                fpsfInput.crouchInput = isCrouching;
        }

        public void SetVehicleMode(bool inVehicle)
        {
            InVehicle = inVehicle;
            characterController.enabled = !inVehicle;
            if (inVehicle)
            {
                moveInput = Vector2.zero;
                velocity = Vector3.zero;
                isSprinting = false;
                isTacticalSprinting = false;
                isCrouching = false;
                SyncFPSFCrouch();
            }
        }

        private void OnDrawGizmosSelected()
        {
            CharacterController cc = characterController != null ? characterController : GetComponent<CharacterController>();
            if (cc == null)
                return;

            // Ground SphereCast
            // float castDistance = groundCheckOffset * 2 + 0.1f;
            Vector3 sphereOrigin = transform.position + Vector3.down * (groundCheckOffset);
            // Vector3 castEnd = sphereOrigin + Vector3.down * castDistance;

            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(sphereOrigin, groundCheckRadius);
            // Gizmos.color = Color.yellow;
            // Gizmos.DrawWireSphere(castEnd, groundCheckRadius);
            // Gizmos.color = Color.white;
            // Gizmos.DrawLine(sphereOrigin + Vector3.down * groundCheckRadius, castEnd + Vector3.down * groundCheckRadius);

            // CanStand raycast
            float standCheckDistance = standingHeight - crouchHeight + 0.1f;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, Vector3.up * standCheckDistance);
        }
    }
}
