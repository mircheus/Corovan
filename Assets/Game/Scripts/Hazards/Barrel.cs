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
        [SerializeField] private float carDamage = 25f;
        [SerializeField] private float playerDamage = 15f;
        [SerializeField] private bool damagePlayersOnFoot = true;

        [SyncVar(hook = nameof(OnActiveStateChanged))]
        private bool isActive;

        private Rigidbody rb;
        private Renderer barrelRenderer;
        private CapsuleCollider barrelCollider;

        private Vector3 spawnPosition;
        private float activeTime;

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
            isActive = true;
        }

        [Server]
        public void ServerDeactivate()
        {
            if (!isActive) return;
            isActive = false;
            OnReturnToPool?.Invoke(this);
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
                car.TakeDamage(Mathf.FloorToInt(carDamage));
                ServerDeactivate();
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

        private void OnActiveStateChanged(bool old, bool newVal) => ApplyActiveState(newVal);

        private void ApplyActiveState(bool active)
        {
            if (barrelRenderer != null) barrelRenderer.enabled = active;
            if (barrelCollider != null) barrelCollider.enabled = active;
        }
    }
}
