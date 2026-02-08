using UnityEngine;
using Mirror;

namespace FPSGame.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float walkSpeed = 5.0f;
        [SerializeField] private float sprintSpeed = 8.5f;
        [SerializeField] private float crouchSpeed = 2.5f;
        [SerializeField] private float airControlMultiplier = 0.3f;

        [Header("Jump Settings")]
        [SerializeField] private float jumpForce = 7.0f;
        [SerializeField] private float gravity = -20.0f;

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
        private Vector3 velocity;
        private Vector2 moveInput;
        private bool isGrounded;
        private bool isSprinting;
        private bool isCrouching;
        private bool jumpRequested;
        private float targetHeight;
        private Vector3 groundNormal = Vector3.up;

        public bool IsGrounded => isGrounded;
        public bool IsSprinting => isSprinting && !isCrouching && moveInput.y > 0;
        public bool IsCrouching => isCrouching;
        public Vector3 Velocity => velocity;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            characterController.stepOffset = stepHeight;
            targetHeight = standingHeight;
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
            // Only process movement for local player
            if (!isOwned)
                return;

            CheckGround();
            HandleCrouch();
            HandleMovement();
            ApplyGravity();
        }

        private void CheckGround()
        {
            Vector3 spherePosition = transform.position + Vector3.down * (characterController.height / 2 - groundCheckRadius + groundCheckOffset);
            isGrounded = Physics.SphereCast(
                transform.position,
                groundCheckRadius,
                Vector3.down,
                out RaycastHit hit,
                characterController.height / 2 - groundCheckRadius + groundCheckOffset + 0.1f,
                groundMask,
                QueryTriggerInteraction.Ignore
            );

            if (isGrounded)
            {
                groundNormal = hit.normal;
            }
            else
            {
                groundNormal = Vector3.up;
            }
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
                velocity.y = jumpForce;
                jumpRequested = false;
            }

            characterController.Move(velocity * Time.deltaTime);
        }

        private float GetCurrentSpeed()
        {
            if (isCrouching)
                return crouchSpeed;
            if (IsSprinting)
                return sprintSpeed;
            return walkSpeed;
        }

        private void ApplyGravity()
        {
            if (isGrounded && velocity.y < 0)
            {
                velocity.y = -2f;
            }
            else
            {
                velocity.y += gravity * Time.deltaTime;
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
            if (!isOwned)
                return;
            moveInput = input;
        }

        public void Jump()
        {
            if (!isOwned)
                return;
            if (isGrounded)
            {
                jumpRequested = true;
            }
        }

        public void SetSprinting(bool sprinting)
        {
            if (!isOwned)
                return;
            isSprinting = sprinting;
        }

        public void ToggleCrouch()
        {
            if (!isOwned)
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
    }
}
