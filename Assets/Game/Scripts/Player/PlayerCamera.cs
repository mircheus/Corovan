using UnityEngine;
using Mirror;
using Akila.FPSFramework;

namespace FPSGame.Player
{
    // Owns pitch/yaw and the cursor. Look comes from FPSF's CharacterInput (which also carries FPSF
    // weapon recoil); FOV, sprint FOV kick, head bob and lean belong to FPSF's CameraManager.
    public class PlayerCamera : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform playerBody;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener audioListener;

        [Header("Sensitivity")]
        [SerializeField] private float sensitivityX = 2.0f;
        [SerializeField] private float sensitivityY = 2.0f;
        [SerializeField] private bool invertY = false;

        [Header("Pitch Limits")]
        [SerializeField] private float minPitch = -85.0f;
        [SerializeField] private float maxPitch = 85.0f;

        [Header("FOV Settings")]
        [Tooltip("Resting FOV. Handed to FPSF's CameraManager, which owns the camera FOV.")]
        [SerializeField] private float baseFOV = 90.0f;
        [SerializeField] private float adsFOV = 55.0f;
        [SerializeField] private float fovTransitionSpeed = 10.0f;
        [Tooltip("How long after releasing ADS we keep easing the FOV back before handing it to FPSF.")]
        [SerializeField] private float adsReleaseBlendTime = 0.5f;

        [Header("Recoil")]
        [SerializeField] private float recoilReturnSpeed = 15.0f;
        [SerializeField] private float recoilSnapiness = 6.0f;

        [Header("Death")]
        [SerializeField] private float deadCameraHeight = -0.6f;
        [SerializeField] private float deadCameraDropSpeed = 3f;

        private float pitch;
        private float yaw;
        private Vector2 lookInput;
        private float adsReleaseTimer;
        private Vector3 originalCameraPosition;
        private Vector3 currentRecoil;
        private Vector3 targetRecoil;

        // Additive shake offsets set each frame by an external source (e.g. CameraMotionShake).
        // Position is on the camera child; rotation feeds pitch (x) and roll (z) of the pivot.
        private Vector3 shakePositionOffset;
        private Vector3 shakeRotationOffset;
        private bool isAiming;
        private bool isInitialized;
        private bool isDead;

        private CharacterInput fpsfInput;
        private CameraManager cameraManager;

        public bool IsAiming => isAiming;
        public Camera Camera => playerCamera;
        public bool IsPitchClamped => pitch <= minPitch + 1f || pitch >= maxPitch - 1f;

        // FPSF scales look by FOV: delta * xMultiplier * fixedDeltaTime * (sens * fov / 179) / 50.
        // This is the FPSF sensitivity that reproduces our sensitivityX at baseFOV; below it (ADS)
        // the look slows down proportionally to the FOV.
        public float FPSFSensitivity =>
            sensitivityX * 179f * 50f / (FPSFrameworkUtility.xSensitivityMultiplier * Time.fixedDeltaTime * baseFOV);

        private void Awake()
        {
            if (playerCamera == null)
                playerCamera = GetComponentInChildren<Camera>();

            if (audioListener == null && playerCamera != null)
                audioListener = playerCamera.GetComponent<AudioListener>();

            if (playerBody == null)
                playerBody = transform.parent;

            fpsfInput = GetComponentInParent<CharacterInput>();
            cameraManager = GetComponentInChildren<CameraManager>();

            if (playerCamera != null)
            {
                originalCameraPosition = playerCamera.transform.localPosition;
                // CameraManager reads its default FOV from the camera in Start.
                playerCamera.fieldOfView = baseFOV;
            }

            ApplyFPSFSensitivityRatio();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            if (playerCamera != null)
            {
                playerCamera.enabled = true;
                playerCamera.tag = "MainCamera";
            }

            if (audioListener != null)
                audioListener.enabled = true;

            var health = GetComponentInParent<FPSGame.Player.PlayerHealth>();
            if (health != null)
                health.OnDeath += OnPlayerDied;

            LockCursor();
            isInitialized = true;
        }

        private void OnPlayerDied()
        {
            isDead = true;
            UnlockCursor();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Disable camera and audio for remote players
            if (!isOwned)
            {
                if (playerCamera != null)
                {
                    playerCamera.enabled = false;
                }

                if (audioListener != null)
                {
                    audioListener.enabled = false;
                }
            }
        }

        private void Start()
        {
            if (playerBody != null)
            {
                yaw = playerBody.eulerAngles.y;
            }
        }

        private void Update()
        {
            if (!isOwned || !isInitialized)
                return;

            if (isDead)
            {
                HandleDeathDrop();
                return;
            }

            HandleLook();
            HandleFOV();
            ApplyCameraOffsets();
            HandleRecoil();
        }

        private void HandleDeathDrop()
        {
            if (playerCamera == null)
                return;

            Vector3 pos = playerCamera.transform.localPosition;
            float targetY = originalCameraPosition.y + deadCameraHeight;
            pos.y = Mathf.MoveTowards(pos.y, targetY, deadCameraDropSpeed * Time.deltaTime);
            playerCamera.transform.localPosition = pos;
        }

        private void HandleLook()
        {
            float mouseX;
            float mouseY;
            if (fpsfInput != null)
            {
                // Already in degrees: sensitivity, FOV scaling and FPSF recoil are applied upstream.
                mouseX = fpsfInput.lookInput.x;
                mouseY = fpsfInput.lookInput.y * (invertY ? 1 : -1);
            }
            else
            {
                mouseX = lookInput.x * sensitivityX;
                mouseY = lookInput.y * sensitivityY * (invertY ? 1 : -1);
            }

            yaw += mouseX;
            pitch += mouseY;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            Vector3 finalRotation = new Vector3(pitch, 0, 0) + currentRecoil + shakeRotationOffset;
            transform.localRotation = Quaternion.Euler(finalRotation.x, 0, finalRotation.z);

            if (playerBody != null)
            {
                playerBody.rotation = Quaternion.Euler(0, yaw + currentRecoil.y, 0);
            }
        }

        // ADS of our own weapons (PlayerShooting). CameraManager owns the FOV, so we only steer it
        // while aiming and for a short blend after release — FPSF weapons drive it themselves.
        private void HandleFOV()
        {
            if (cameraManager == null)
                return;

            float t = fovTransitionSpeed * Time.deltaTime;
            if (isAiming)
            {
                cameraManager.SetFieldOfView(adsFOV, cameraManager.overlayFieldOfView, t, 0f);
                adsReleaseTimer = adsReleaseBlendTime;
            }
            else if (adsReleaseTimer > 0f)
            {
                adsReleaseTimer -= Time.deltaTime;
                cameraManager.ResetFieldOfView(t);
            }
        }

        // Motion shake rides on the camera's local position; head bob is CameraManager's.
        private void ApplyCameraOffsets()
        {
            if (playerCamera == null)
                return;

            playerCamera.transform.localPosition = originalCameraPosition + shakePositionOffset;
        }

        private void HandleRecoil()
        {
            targetRecoil = Vector3.Lerp(targetRecoil, Vector3.zero, recoilReturnSpeed * Time.deltaTime);
            currentRecoil = Vector3.Slerp(currentRecoil, targetRecoil, recoilSnapiness * Time.deltaTime);
        }

        public void AddRecoil(Vector2 recoilAmount)
        {
            if (!isOwned)
                return;
            targetRecoil += new Vector3(-recoilAmount.y, recoilAmount.x, 0);
        }

        // Additive camera shake, refreshed every frame by an external driver. Position offset is
        // applied to the camera child; rotation offset feeds pitch (x) and roll (z).
        public void SetShakeOffsets(Vector3 positionOffset, Vector3 rotationOffset)
        {
            if (!isOwned)
                return;
            shakePositionOffset = positionOffset;
            shakeRotationOffset = rotationOffset;
        }

        public void SetLookInput(Vector2 input)
        {
            if (!isOwned)
                return;
            lookInput = input;
        }

        public void SetAiming(bool aiming)
        {
            if (!isOwned)
                return;
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
            if (playerCamera != null)
            {
                return new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            }
            return new Ray(transform.position, transform.forward);
        }

        public void SetSensitivity(float x, float y)
        {
            sensitivityX = x;
            sensitivityY = y;
            ApplyFPSFSensitivityRatio();
        }

        // FPSF has one sensitivity value; the Y/X ratio goes through its global Y multiplier.
        // Only the local player touches it, so the static is safe.
        private void ApplyFPSFSensitivityRatio()
        {
            if (sensitivityX > 0f)
                FPSFrameworkUtility.ySensitivityMultiplier =
                    FPSFrameworkUtility.xSensitivityMultiplier * sensitivityY / sensitivityX;
        }
    }
}
