using Mirror;
using UnityEngine;
using FPSGame.Player;
using FPSGame.Vehicle;

namespace FPSGame.Environment
{
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyProjectile : NetworkBehaviour
    {
        [SerializeField] private float speed = 20f;
        [SerializeField] private float lifetime = 5f;

        private Vector3 direction;
        private float damage;
        private bool hasHit;
        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        // Called by Enemy before NetworkServer.Spawn.
        public void Initialize(Vector3 dir, float dmg)
        {
            direction = dir;
            damage = dmg;
        }

        // Called by Enemy to prevent the projectile from hitting the enemy's own colliders.
        public void IgnoreCollider(Collider col)
        {
            var myCol = GetComponent<Collider>();
            if (col != null && myCol != null)
                Physics.IgnoreCollision(myCol, col);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            rb.linearVelocity = direction * speed;
            Invoke(nameof(DestroyProjectile), lifetime);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!isServer || hasHit)
                return;

            hasHit = true;
            var playerHealth = collision.collider.GetComponentInParent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage);
            }
            else
            {
                var car = collision.collider.GetComponentInParent<CarController>();
                if (car != null)
                    car.TakeDamage(Mathf.FloorToInt(damage));
            }

            DestroyProjectile();
        }

        private void DestroyProjectile()
        {
            CancelInvoke(nameof(DestroyProjectile));
            NetworkServer.Destroy(gameObject);
        }
    }
}
