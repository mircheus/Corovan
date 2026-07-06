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
        [Tooltip("Initial horizontal speed (world units per second).")]
        [SerializeField] private float moveSpeed = 8f;
        [Tooltip("Horizontal launch direction in world space. Y is ignored; the vector is normalized. Default is straight down -Z.")]
        [SerializeField] private Vector3 launchDirection = new Vector3(0f, 0f, -1f);
        [SerializeField] private float despawnDistance = 60f;

        [Header("Bounce Physics")]
        [Tooltip("Downward acceleration applied every physics step (units/s²). Negative pulls the barrel toward the ground.")]
        [SerializeField] private float gravity = -20f;
        [Tooltip("Fraction of vertical speed kept after each ground bounce. 0 = no bounce, 1 = perfectly elastic.")]
        [Range(0f, 1f)]
        [SerializeField] private float bounciness = 0.6f;

        [Header("Bounce Acceleration")]
        [Tooltip("When enabled, the barrel speeds up horizontally on every real bounce.")]
        [SerializeField] private bool accelerateOnBounce = false;
        [Tooltip("Horizontal speed is multiplied by this on each bounce (1 = no change, 1.15 = +15% per bounce).")]
        [SerializeField] private float bounceSpeedMultiplier = 1.15f;
        [Tooltip("Upper limit for the accelerated horizontal speed. 0 = uncapped.")]
        [SerializeField] private float maxMoveSpeed = 0f;
        [Tooltip("Upward speed given to the barrel on activation for its first hop.")]
        [SerializeField] private float initialBounceVelocity = 6f;
        [Tooltip("Once a post-bounce vertical speed drops below this, the barrel stops hopping and slides flat along the ground.")]
        [SerializeField] private float minBounceVelocity = 1.5f;
        [Tooltip("Distance from the barrel pivot down to the point that should rest on the ground surface.")]
        [SerializeField] private float groundOffset = 0.5f;
        [Tooltip("How far below the pivot to look for ground each step. Keep larger than one step's fall distance.")]
        [SerializeField] private float groundCheckDistance = 5f;
        [Tooltip("Layers treated as ground for bounce detection. Set this to your ground/road layer(s).")]
        [SerializeField] private LayerMask groundMask = ~0;

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
        private Vector3 velocity;
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

            Vector3 horizontal = new Vector3(launchDirection.x, 0f, launchDirection.z);
            horizontal = horizontal.sqrMagnitude > 0.0001f ? horizontal.normalized : Vector3.back;
            horizontal *= moveSpeed;
            velocity = new Vector3(horizontal.x, initialBounceVelocity, horizontal.z);

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

            float dt = Time.fixedDeltaTime;
            velocity.y += gravity * dt;

            Vector3 current = rb.position;
            Vector3 next = current + velocity * dt;

            // Only test for the ground while descending, then reflect the vertical
            // velocity with damping so successive hops lose height and eventually settle.
            if (velocity.y < 0f &&
                Physics.Raycast(current, Vector3.down, out RaycastHit hit,
                    groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore))
            {
                float groundY = hit.point.y + groundOffset;
                if (next.y <= groundY)
                {
                    next.y = groundY;
                    float bounceUp = -velocity.y * bounciness;
                    if (bounceUp < minBounceVelocity)
                    {
                        // Settled: glue to the ground and roll flat. No acceleration here,
                        // otherwise the per-frame re-snap would speed the barrel up endlessly.
                        velocity.y = 0f;
                    }
                    else
                    {
                        velocity.y = bounceUp;
                        if (accelerateOnBounce)
                            ApplyBounceAcceleration();
                    }
                }
            }

            rb.MovePosition(next);

            Vector3 travelled = next - spawnPosition;
            travelled.y = 0f;
            if (travelled.magnitude >= despawnDistance)
                ServerDeactivate();
        }

        private void ApplyBounceAcceleration()
        {
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z) * bounceSpeedMultiplier;
            if (maxMoveSpeed > 0f && horizontal.magnitude > maxMoveSpeed)
                horizontal = horizontal.normalized * maxMoveSpeed;
            velocity.x = horizontal.x;
            velocity.z = horizontal.z;
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
