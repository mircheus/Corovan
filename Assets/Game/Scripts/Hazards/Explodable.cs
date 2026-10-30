using UnityEngine;
using UnityEngine.Events;
using Mirror;
using FPSGame.Combat;
using FPSGame.Vehicle;
using FPSGame.Player;

namespace FPSGame.Hazards
{
    /// <summary>
    /// Stationary explosive object: detonates once the damage taken adds up to
    /// <see cref="explosionDamageThreshold"/>, or on contact with a car / player.
    /// The destruction + explosion half of <see cref="Barrel"/>, without any movement.
    /// </summary>
    public class Explodable : NetworkBehaviour, IDamageReceiver
    {
        // No health bar: it detonates once the hits add up to the threshold.
        int IDamageReceiver.CurrentHealth => Mathf.Max(0, explosionDamageThreshold - accumulatedDamage);
        int IDamageReceiver.MaxHealth => explosionDamageThreshold;
        bool IDamageReceiver.IsDead => !isIntact;

        [Header("Contact Damage")]
        [Tooltip("Explode when a car enters the trigger, dealing carDamage to it.")]
        [SerializeField] private bool explodeOnCarContact = true;
        [SerializeField] private int carDamage = 25;
        [Tooltip("Explode when a player on foot enters the trigger, dealing playerDamage to them.")]
        [SerializeField] private bool explodeOnPlayerContact = true;
        [SerializeField] private int playerDamage = 15;

        [Header("Health")]
        [Tooltip("Once the total damage taken reaches this threshold, the object explodes.")]
        [SerializeField] private int explosionDamageThreshold = 5;
        [Tooltip("Non-trigger collider used purely as a raycast hitbox so weapons can hit the object. Keep it on a layer that physically collides with nothing.")]
        [SerializeField] private Collider hitboxCollider;

        [Header("Visuals")]
        [Tooltip("Body mesh — hidden on explosion, restored by ServerReset. Must NOT contain the explosion effect as a child.")]
        [SerializeField] private GameObject meshObject;

        [Header("FX")]
        [SerializeField] private GameObject explosionEffect;
        [SerializeField] private float explosionDuration = 2f;

        [Header("Events")]
        [Tooltip("Invoked on the server when the object explodes.")]
        [SerializeField] private UnityEvent onExploded;

        [SyncVar(hook = nameof(OnIntactChanged))]
        private bool isIntact = true;

        private Renderer ownRenderer;
        private Collider ownCollider;
        private int accumulatedDamage;

        public bool IsIntact => isIntact;

        private void Awake()
        {
            ownRenderer = GetComponent<Renderer>();
            ownCollider = GetComponent<Collider>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyIntactState(isIntact);
        }

        /// <summary>
        /// Server-authoritative damage entry point. Accumulates incoming damage and
        /// detonates once the total reaches <see cref="explosionDamageThreshold"/>.
        /// A single hit at or above the threshold triggers it immediately.
        /// </summary>
        [Server]
        public void TakeDamage(int damage)
        {
            if (!isIntact || damage <= 0) return;

            accumulatedDamage += damage;
            if (accumulatedDamage >= explosionDamageThreshold)
                ServerExplode();
        }

        [Server]
        public void ServerExplode()
        {
            if (!isIntact) return;
            isIntact = false;
            RpcPlayExplosion();
            onExploded?.Invoke();
        }

        /// <summary>Restores the object to its intact state (for reuse / respawn).</summary>
        [Server]
        public void ServerReset()
        {
            accumulatedDamage = 0;
            isIntact = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isServer || !isIntact) return;

            if (explodeOnCarContact)
            {
                var car = other.GetComponent<CarController>();
                if (car != null)
                {
                    car.TakeDamage(carDamage);
                    ServerExplode();
                    return;
                }
            }

            if (explodeOnPlayerContact)
            {
                var health = other.GetComponent<PlayerHealth>();
                if (health != null)
                {
                    health.TakeDamage(playerDamage);
                    ServerExplode();
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

        private void OnIntactChanged(bool old, bool newVal) => ApplyIntactState(newVal);

        private void ApplyIntactState(bool intact)
        {
            if (ownRenderer != null) ownRenderer.enabled = intact;
            if (ownCollider != null) ownCollider.enabled = intact;
            if (hitboxCollider != null) hitboxCollider.enabled = intact;
            if (meshObject != null) meshObject.SetActive(intact);
        }
    }
}
