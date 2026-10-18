using UnityEngine;
using Akila.FPSFramework;
using FPSGame.Combat;
using FPSGame.Weapons;

namespace FPSGame.Player
{
    /// <summary>
    /// A melee loadout slot (the axe) living in the FPS Framework Inventory. LMB swings: a procedural
    /// chop of the model, and at the swing's peak a SphereCast from the camera — the same wide,
    /// forgiving sweep PlayerShooting used. Hits go through PlayerHitReporter like FPSF weapon hits
    /// (client-authoritative, friendly-fire rules on the server).
    /// </summary>
    public class MeleeItem : Item
    {
        [Tooltip("Damage, Range, MeleeRadius and FireRate (swings per second).")]
        [SerializeField] private WeaponData data;
        [Tooltip("Pivot rotated for the swing (usually the model's root).")]
        [SerializeField] private Transform swingPivot;
        [SerializeField] private Vector3 swingEuler = new Vector3(60f, -25f, 0f);
        [Tooltip("Fraction of the swing at which the hit is tested.")]
        [Range(0f, 1f)] [SerializeField] private float hitMoment = 0.35f;
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Velocity (m/s) a killing swing gives a ragdoll.")]
        [SerializeField] private float hitPushSpeed = 5f;

        private PlayerCamera playerCamera;
        private PlayerHealth ownHealth;
        private Quaternion restRotation;
        private float swingTime = -1f; // seconds into the current swing, -1 = idle
        private bool hitDone;

        private float SwingDuration => data != null && data.FireRate > 0f ? 1f / data.FireRate : 0.6f;

        private void Awake()
        {
            Setup(name, null);
            playerCamera = GetComponentInParent<PlayerCamera>();
            ownHealth = GetComponentInParent<PlayerHealth>();
            if (swingPivot != null)
                restRotation = swingPivot.localRotation;
        }

        private void OnDisable()
        {
            swingTime = -1f;
            if (swingPivot != null)
                swingPivot.localRotation = restRotation;
        }

        private void Update()
        {
            if (swingTime < 0f)
            {
                if (itemInput != null && itemInput.triggredFire)
                {
                    swingTime = 0f;
                    hitDone = false;
                }
                return;
            }

            swingTime += Time.deltaTime;
            float t = swingTime / SwingDuration;

            if (!hitDone && t >= hitMoment)
            {
                hitDone = true;
                TryHit();
            }

            if (swingPivot != null)
            {
                // Fast chop down, slower recovery.
                float k = t < hitMoment ? t / hitMoment : 1f - (t - hitMoment) / (1f - hitMoment);
                swingPivot.localRotation = restRotation * Quaternion.Euler(swingEuler * Mathf.Clamp01(k));
            }

            if (t >= 1f)
                swingTime = -1f;
        }

        private void TryHit()
        {
            if (data == null || playerCamera == null || PlayerHitReporter.Local == null)
                return;

            Ray ray = playerCamera.GetAimRay();
            if (!Physics.SphereCast(ray, data.MeleeRadius, out RaycastHit hit, data.Range, hitMask,
                    QueryTriggerInteraction.Ignore))
                return;

            IDamageReceiver receiver = DamageRouter.FindReceiver(hit.collider);
            if (receiver == null || ReferenceEquals(receiver, ownHealth))
                return;

            PlayerHitReporter.Local.ReportHit(receiver, data.Damage, ray.direction * hitPushSpeed);
        }
    }
}
