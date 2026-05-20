using System;
using UnityEngine;
using Mirror;
using FPSGame.Vehicle;
using FPSGame.Player;

namespace FPSGame.Hazards
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class Barrel : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 8f;
        [SerializeField] private float despawnDistance = 60f;

        [Header("Bounce")]
        [SerializeField] private float bounceAmplitude = 0.8f;
        [SerializeField] private float bounceFrequency = 0.3f;
        // One cycle of the bounce shape. X axis: 0–1 (normalized period). Y axis: height multiplier (scaled by bounceAmplitude).
        [SerializeField] private AnimationCurve bounceCurve = new AnimationCurve(
            new Keyframe(0f,    0f),
            new Keyframe(0.25f, 1f),
            new Keyframe(0.5f,  0f),
            new Keyframe(0.75f, -1f),
            new Keyframe(1f,    0f));

        [Header("Damage")]
        [SerializeField] private int carDamage = 25;
        [SerializeField] private int playerDamage = 15;
        [SerializeField] private bool damagePlayersOnFoot = true;

        [Header("Health")]
        [Tooltip("Once the total damage taken reaches this threshold, the barrel explodes.")]
        [SerializeField] private int explosionDamageThreshold = 5;
        [Tooltip("Non-trigger collider used purely as a raycast hitbox so weapons can hit the barrel. Keep it on a layer that physically collides with nothing.")]
        [SerializeField] private Collider hitboxCollider;

        [Header("Visuals")]
        [Tooltip("Barrel body mesh — hidden when the barrel deactivates (explosion or despawn), restored on pool reuse. Must NOT contain the explosion effect as a child.")]
        [SerializeField] private GameObject meshObject;

        [Header("FX")]
        [SerializeField] private GameObject explosionEffect;
        [SerializeField] private float explosionDuration = 2f;

        [SyncVar(hook = nameof(OnActiveStateChanged))]
        private bool isActive;

        private Rigidbody rb;
        private Renderer barrelRenderer;
        private CapsuleCollider barrelCollider;

        private Vector3 spawnPosition;
        private float activeTime;
        private int accumulatedDamage;

        // Assigned by BarrelSpawner; fires on server when barrel should return to pool
        public Action<Barrel> OnReturnToPool;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            barrelRenderer = GetComponent<Renderer>();
            barrelCollider = GetComponent<CapsuleCollider>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyActiveState(isActive);
        }

        [Server]
        public void ServerActivate(Vector3 position)
        {
            spawnPosition = position;
            rb.position = position;
            activeTime = 0f;
            accumulatedDamage = 0;
            isActive = true;
        }

        [Server]
        public void ServerDeactivate()
        {
            if (!isActive) return;
            isActive = false;
            OnReturnToPool?.Invoke(this);
        }

        /// <summary>
        /// Server-authoritative damage entry point. Accumulates incoming damage and
        /// detonates the barrel once the total reaches <see cref="explosionDamageThreshold"/>.
        /// A single hit at or above the threshold triggers it immediately.
        /// </summary>
        [Server]
        public void TakeDamage(int damage)
        {
            if (!isActive || damage <= 0) return;

            accumulatedDamage += damage;
            if (accumulatedDamage >= explosionDamageThreshold)
                ServerExplode();
        }

        [Server]
        private void ServerExplode()
        {
            if (!isActive) return;
            RpcPlayExplosion();
            ServerDeactivate();
        }

        private void FixedUpdate()
        {
            if (!isServer || !isActive) return;

            activeTime += Time.fixedDeltaTime;

            float zOffset = moveSpeed * activeTime;
            float yOffset = bounceCurve.Evaluate((activeTime * bounceFrequency) % 1f) * bounceAmplitude;

            rb.MovePosition(spawnPosition + new Vector3(0f, yOffset, -zOffset));

            if (zOffset >= despawnDistance)
                ServerDeactivate();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isServer || !isActive) return;

            var car = other.GetComponent<CarController>();
            if (car != null)
            {
                car.TakeDamage(carDamage);
                ServerExplode();
                return;
            }

            if (damagePlayersOnFoot)
            {
                var health = other.GetComponent<PlayerHealth>();
                if (health != null)
                {
                    health.TakeDamage(playerDamage);
                    ServerDeactivate();
                }
            }
        }

        [ClientRpc]
        private void RpcPlayExplosion()
        {
            if (explosionEffect == null) return;
            explosionEffect.SetActive(true);
            CancelInvoke(nameof(HideExplosion));
            Invoke(nameof(HideExplosion), explosionDuration);
        }

        private void HideExplosion()
        {
            if (explosionEffect != null)
                explosionEffect.SetActive(false);
        }

        private void OnActiveStateChanged(bool old, bool newVal) => ApplyActiveState(newVal);

        private void ApplyActiveState(bool active)
        {
            if (barrelRenderer != null) barrelRenderer.enabled = active;
            if (barrelCollider != null) barrelCollider.enabled = active;
            if (hitboxCollider != null) hitboxCollider.enabled = active;
            if (meshObject != null) meshObject.SetActive(active);
        }
    }
}
