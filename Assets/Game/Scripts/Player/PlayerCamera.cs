using UnityEngine;
using Mirror;
using Akila.FPSFramework;
using FPSGame.Networking;

namespace FPSGame.Player
{
    // Owns pitch/yaw and the cursor. Look comes from FPSF's CharacterInput (which also carries FPSF
    // weapon recoil); FOV, ADS, sprint FOV kick, head bob and lean belong to FPSF's CameraManager.
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

        [Header("Death")]
        [SerializeField] private float deadCameraHeight = -0.6f;
        [SerializeField] private float deadCameraDropSpeed = 3f;

        private float pitch;
        private float yaw;
        private Vector3 originalCameraPosition;

        // Additive shake offsets set each frame by an external source (e.g. CameraMotionShake).
        // Position is on the camera child; rotation feeds pitch (x) and roll (z) of the pivot.
        private Vector3 shakePositionOffset;
        private Vector3 shakeRotationOffset;
        private bool isInitialized;
        private bool isDead;

        private CharacterInput fpsfInput;
        private CameraManager cameraManager;

        // FOV punch: fades from punchAmount to 0 over punchDuration; CameraManager smooths the attack.
        private float punchAmount;
        private float punchDuration;
        private float punchTimer;

        // Additive FOV degrees from an outside source (DriverFovController), summed with the punch.
        private float externalFovOffset;

        // World-space rotation applied on top of the look (DriverCameraFollow: a share of the car body's turn).
        // Identity = plain mouse look.
        private Quaternion baseRotationOffset = Quaternion.identity;

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
            if (fpsfInput != null)
                cameraManager = fpsfInput.GetComponentInChildren<CameraManager>(true);

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
            {
                health.OnDeath += OnPlayerDied;
                health.OnRespawn += OnPlayerRespawned;
            }

            LockCursor();
            isInitialized = true;
        }

        private void OnPlayerDied()
        {
            isDead = true;
            UnlockCursor();
        }

        // Dead -> Alive. The death drop stops; ApplyCameraOffsets puts the camera back at eye height.
        private void OnPlayerRespawned()
        {
            isDead = false;
            if (GameStateManager.Instance == null || !GameStateManager.Instance.IsMatchOver)
                LockCursor();
        }

        /// <summary>Owner: face a world direction (yaw, pitch in degrees) — used when respawning.</summary>
        public void SetLook(float newYaw, float newPitch)
        {
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
            transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            if (playerBody != null)
                playerBody.rotation = Quaternion.Euler(0f, yaw, 0f);
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

            UpdateFOVPunch();

            if (isDead)
            {
                HandleDeathDrop();
                return;
            }

            HandleLook();
            ApplyCameraOffsets();
        }

        // Short FOV widening (e.g. car dash). A new punch restarts the fade.
        public void PunchFOV(float amount, float duration)
        {
            if (!isOwned || duration <= 0f)
                return;
            punchAmount = amount;
            punchDuration = duration;
            punchTimer = 0f;
        }

        /// <summary>Owner: extra FOV degrees on top of everything else, set every frame by its owner
        /// (0 = none). Summed with the dash punch, never replaces it.</summary>
        public void SetExternalFovOffset(float degrees)
        {
            externalFovOffset = degrees;
        }

        /// <summary>Owner: world-space rotation laid over the look, refreshed every frame by its owner
        /// (identity = none). Mouse look keeps working underneath it.</summary>
        public void SetBaseRotationOffset(Quaternion worldOffset)
        {
            baseRotationOffset = worldOffset;
        }

        private void UpdateFOVPunch()
        {
            if (cameraManager == null)
                return;

            float punch = 0f;
            if (punchDuration > 0f)
            {
                punchTimer += Time.deltaTime;
                float t = Mathf.Clamp01(punchTimer / punchDuration);
                float fade = 1f - t;
                punch = punchAmount * fade * fade;

                if (t >= 1f)
                    punchDuration = 0f;
            }

            cameraManager.fieldOfViewOffset = punch + externalFovOffset;
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
            if (fpsfInput == null)
                return;

            // Already in degrees: sensitivity, FOV scaling and FPSF recoil are applied upstream.
            float mouseX = fpsfInput.lookInput.x;
            float mouseY = fpsfInput.lookInput.y * (invertY ? 1 : -1);

            yaw += mouseX;
            pitch += mouseY;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            Vector3 finalRotation = new Vector3(pitch, 0, 0) + shakeRotationOffset;
            Quaternion look = Quaternion.Euler(finalRotation.x, 0, finalRotation.z);

            Quaternion bodyRotation = Quaternion.Euler(0, yaw, 0);
            if (playerBody != null)
            {
                playerBody.rotation = bodyRotation;
            }

            // The offset is in world space; the pivot hangs under the yawed body, so bring it into the body's frame.
            transform.localRotation = Quaternion.Inverse(bodyRotation) * baseRotationOffset * bodyRotation * look;
        }

        // Motion shake rides on the camera's local position; head bob is CameraManager's.
        private void ApplyCameraOffsets()
        {
            if (playerCamera == null)
                return;

            playerCamera.transform.localPosition = originalCameraPosition + shakePositionOffset;
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
