using UnityEngine;

namespace FPSGame.Player
{
    public class PlayerCamera : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform playerBody;
        [SerializeField] private Camera playerCamera;

        [Header("Sensitivity")]
        [SerializeField] private float sensitivityX = 2.0f;
        [SerializeField] private float sensitivityY = 2.0f;
        [SerializeField] private bool invertY = false;

        [Header("Pitch Limits")]
        [SerializeField] private float minPitch = -85.0f;
        [SerializeField] private float maxPitch = 85.0f;

        [Header("FOV Settings")]
        [SerializeField] private float baseFOV = 90.0f;
        [SerializeField] private float sprintFOV = 100.0f;
        [SerializeField] private float adsFOV = 55.0f;
        [SerializeField] private float fovTransitionSpeed = 10.0f;

        [Header("Head Bob")]
        [SerializeField] private bool enableHeadBob = true;
        [SerializeField] private float bobFrequency = 1.8f;
        [SerializeField] private float bobAmplitude = 0.06f;
        [SerializeField] private float sprintBobMultiplier = 1.4f;

        [Header("Recoil")]
        [SerializeField] private float recoilReturnSpeed = 15.0f;
        [SerializeField] private float recoilSnapiness = 6.0f;

        private float pitch;
        private float yaw;
        private Vector2 lookInput;
        private float targetFOV;
        private float currentFOV;
        private float bobTimer;
        private Vector3 originalCameraPosition;
        private Vector3 currentRecoil;
        private Vector3 targetRecoil;
        private bool isAiming;

        private PlayerController playerController;

        public bool IsAiming => isAiming;

        private void Awake()
        {
            if (playerCamera == null)
                playerCamera = GetComponentInChildren<Camera>();

            if (playerBody == null)
                playerBody = transform;

            playerController = GetComponentInParent<PlayerController>();

            originalCameraPosition = playerCamera.transform.localPosition;
            targetFOV = baseFOV;
            currentFOV = baseFOV;

            LockCursor();
        }

        private void Start()
        {
            yaw = playerBody.eulerAngles.y;
        }

        private void Update()
        {
            HandleLook();
            HandleFOV();
            HandleHeadBob();
            HandleRecoil();
        }

        private void HandleLook()
        {
            float mouseX = lookInput.x * sensitivityX;
            float mouseY = lookInput.y * sensitivityY * (invertY ? 1 : -1);

            yaw += mouseX;
            pitch += mouseY;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            Vector3 finalRotation = new Vector3(pitch, 0, 0) + currentRecoil;
            transform.localRotation = Quaternion.Euler(finalRotation.x, 0, finalRotation.z);
            playerBody.rotation = Quaternion.Euler(0, yaw + currentRecoil.y, 0);
        }

        private void HandleFOV()
        {
            if (isAiming)
            {
                targetFOV = adsFOV;
            }
            else if (playerController != null && playerController.IsSprinting)
            {
                targetFOV = sprintFOV;
            }
            else
            {
                targetFOV = baseFOV;
            }

            currentFOV = Mathf.Lerp(currentFOV, targetFOV, fovTransitionSpeed * Time.deltaTime);
            playerCamera.fieldOfView = currentFOV;
        }

        private void HandleHeadBob()
        {
            if (!enableHeadBob || playerController == null)
                return;

            if (playerController.IsGrounded && playerController.Velocity.sqrMagnitude > 0.1f)
            {
                float bobMultiplier = playerController.IsSprinting ? sprintBobMultiplier : 1.0f;
                bobTimer += Time.deltaTime * bobFrequency * bobMultiplier;

                float bobOffsetY = Mathf.Sin(bobTimer * Mathf.PI * 2) * bobAmplitude;
                float bobOffsetX = Mathf.Cos(bobTimer * Mathf.PI) * bobAmplitude * 0.5f;

                Vector3 bobOffset = new Vector3(bobOffsetX, bobOffsetY, 0);
                playerCamera.transform.localPosition = originalCameraPosition + bobOffset;
            }
            else
            {
                bobTimer = 0;
                playerCamera.transform.localPosition = Vector3.Lerp(
                    playerCamera.transform.localPosition,
                    originalCameraPosition,
                    Time.deltaTime * 10f
                );
            }
        }

        private void HandleRecoil()
        {
            targetRecoil = Vector3.Lerp(targetRecoil, Vector3.zero, recoilReturnSpeed * Time.deltaTime);
            currentRecoil = Vector3.Slerp(currentRecoil, targetRecoil, recoilSnapiness * Time.deltaTime);
        }

        public void AddRecoil(Vector2 recoilAmount)
        {
            targetRecoil += new Vector3(-recoilAmount.y, recoilAmount.x, 0);
        }

        public void SetLookInput(Vector2 input)
        {
            lookInput = input;
        }

        public void SetAiming(bool aiming)
        {
            isAiming = aiming;
        }

        public void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public Ray GetAimRay()
        {
            return new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        }

        public void SetSensitivity(float x, float y)
        {
            sensitivityX = x;
            sensitivityY = y;
        }
    }
}
