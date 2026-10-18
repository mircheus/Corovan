using Mirror;
using UnityEngine;

namespace FPSGame.Player
{
    /// <summary>
    /// Self-contained grappling hook. Add to the Player prefab alongside the other
    /// player components; it is active while the loadout item with grappleItemId
    /// (a plain ToolItem under the Inventory, no tool Behaviour) is in hand.
    /// </summary>
    public class GrapplingHookController : NetworkBehaviour
    {
        private enum GrappleState { Idle, Grappling }

        [Header("References")]
        [SerializeField] private PlayerLoadout loadout;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private PlayerCamera playerCamera;
        [SerializeField] private GrapplingHookInput grapplingInput;

        [Header("Settings")]
        /// <summary>LoadoutItem id of the grappling hook; the hook aims and fires only while it is in hand.</summary>
        [SerializeField] private string grappleItemId = "grapple";

        /// <summary>Maximum range for a valid grapple target.</summary>
        [SerializeField] private float maxRange = 30f;

        /// <summary>Movement speed while grappling toward the target.</summary>
        [SerializeField] private float grappleSpeed = 20f;

        /// <summary>Distance at which arrival is considered complete.</summary>
        [SerializeField] private float arrivalThreshold = 0.5f;

        private GrappleState state = GrappleState.Idle;
        private Vector3 targetPoint;
        private bool hasValidTarget;

        private CharacterController characterController;
        private GameObject aimPlaceholder;
        private Renderer placeholderRenderer;

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();

            if (loadout == null)
                loadout = GetComponent<PlayerLoadout>();
            if (playerController == null)
                playerController = GetComponent<PlayerController>();
            if (playerCamera == null)
                playerCamera = GetComponentInChildren<PlayerCamera>();
            if (grapplingInput == null)
                grapplingInput = GetComponent<GrapplingHookInput>();

            CreateAimPlaceholder();
        }

        private void Update()
        {
            if (!isOwned)
                return;

            switch (state)
            {
                case GrappleState.Idle:
                    UpdateAiming();
                    CheckFire();
                    break;

                case GrappleState.Grappling:
                    UpdateGrapple();
                    break;
            }
        }

        // ── Idle / aiming ──────────────────────────────────────────────────

        private void UpdateAiming()
        {
            bool isGrappleSlotActive = loadout != null && loadout.IsEquipped(grappleItemId);

            if (!isGrappleSlotActive)
            {
                hasValidTarget = false;
                SetPlaceholderVisible(false);
                return;
            }

            Ray ray = playerCamera.GetAimRay();
            if (Physics.Raycast(ray, out RaycastHit hit, maxRange))
            {
                hasValidTarget = true;
                targetPoint = hit.point;
                aimPlaceholder.transform.position = targetPoint;
                SetPlaceholderVisible(true);
            }
            else
            {
                hasValidTarget = false;
                SetPlaceholderVisible(false);
            }
        }

        private void CheckFire()
        {
            if (!hasValidTarget)
                return;

            if (grapplingInput != null && grapplingInput.FireTriggered)
                BeginGrapple();
        }

        // ── Grapple movement ───────────────────────────────────────────────

        private void BeginGrapple()
        {
            state = GrappleState.Grappling;
            SetPlaceholderVisible(false);
            playerController.IsGrappling = true;
        }

        private void UpdateGrapple()
        {
            // Jump cancels.
            if (grapplingInput != null && grapplingInput.JumpTriggered)
            {
                EndGrapple();
                return;
            }

            Vector3 toTarget = targetPoint - transform.position;

            // Arrived.
            if (toTarget.magnitude <= arrivalThreshold)
            {
                EndGrapple();
                return;
            }

            characterController.Move(toTarget.normalized * grappleSpeed * Time.deltaTime);
        }

        private void EndGrapple()
        {
            state = GrappleState.Idle;
            playerController.IsGrappling = false;
        }

        // ── Aim placeholder ────────────────────────────────────────────────

        private void CreateAimPlaceholder()
        {
            aimPlaceholder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            aimPlaceholder.name = "GrappleAimPlaceholder";
            aimPlaceholder.transform.localScale = Vector3.one * 0.2f;

            // Destroy the collider so the sphere doesn't interfere with physics.
            Destroy(aimPlaceholder.GetComponent<Collider>());

            // Create a transparent green material at runtime.
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0f, 1f, 0f, 0.4f);
            mat.SetFloat("_Mode", 3);                        // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;

            placeholderRenderer = aimPlaceholder.GetComponent<Renderer>();
            placeholderRenderer.material = mat;
            placeholderRenderer.enabled = false;
        }

        private void SetPlaceholderVisible(bool visible)
        {
            if (placeholderRenderer != null)
                placeholderRenderer.enabled = visible;
        }

        private void OnDestroy()
        {
            if (aimPlaceholder != null)
                Destroy(aimPlaceholder);
        }
    }
}
