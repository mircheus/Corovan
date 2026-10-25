using Mirror;
using UnityEngine;
using FPSGame.Enemies;
using FPSGame.Player;
using FPSGame.Vehicle;

namespace FPSGame.Environment
{
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyProjectile : NetworkBehaviour
    {
        [Tooltip("Fallback speed. The firing enemy normally overrides this via Initialize().")]
        [SerializeField] private float speed = 20f;
        [SerializeField] private float lifetime = 5f;
        [Tooltip("Log what each projectile hits (debugging).")]
        [SerializeField] private bool logHits;
        [Tooltip("Muzzle flash / projectile body / impact. Empty = the projectile has no visuals of its own.")]
        [SerializeField] private ProjectileFxSet fx;

        public ProjectileFxSet Fx => fx;

        private Vector3 direction;
        private float spawnTime;
        private int damage;
        private bool hasHit;
        private Rigidbody rb;
        private Collider ownCollider;

        // Live server projectiles. Shots never collide with each other: a burst would otherwise
        // shoot down its own leading round.
        private static readonly System.Collections.Generic.List<EnemyProjectile> serverLive =
            new System.Collections.Generic.List<EnemyProjectile>();

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            ownCollider = GetComponent<Collider>();
            rb.useGravity = false;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        // Called by Enemy before NetworkServer.Spawn.
        public void Initialize(Vector3 dir, int dmg, float projectileSpeed)
        {
            direction = dir;
            damage = dmg;
            speed = projectileSpeed;
        }

        // Called by Enemy to prevent the projectile from hitting the enemy's own colliders.
        public void IgnoreCollider(Collider col)
        {
            if (col != null && ownCollider != null)
                Physics.IgnoreCollision(ownCollider, col);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            serverLive.RemoveAll(p => p == null); // a scene change can destroy one without OnStopServer
            foreach (EnemyProjectile other in serverLive)
                IgnoreCollider(other.ownCollider);
            serverLive.Add(this);

            rb.linearVelocity = direction * speed;
            spawnTime = Time.time;
            Invoke(nameof(DestroyProjectile), lifetime);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            serverLive.Remove(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (fx != null)
                fx.AttachProjectile(transform);
        }

        [ClientRpc]
        private void RpcHit(Vector3 point, Vector3 normal)
        {
            if (fx != null)
                fx.PlayHit(point, normal);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!isServer || hasHit)
                return;

            hasHit = true;
            if (fx != null && collision.contactCount > 0)
            {
                ContactPoint contact = collision.GetContact(0);
                RpcHit(contact.point, contact.normal);
            }
            if (logHits)
                Debug.Log($"[EnemyProjectile] {name} hit '{collision.collider.name}' " +
                          $"(layer {LayerMask.LayerToName(collision.collider.gameObject.layer)}, " +
                          $"root '{collision.collider.transform.root.name}') {Time.time - spawnTime:F3}s after spawn", collision.collider);

            var playerHealth = collision.collider.GetComponentInParent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage);
            }
            else
            {
                var car = collision.collider.GetComponentInParent<CarController>();
                if (car != null)
                    car.TakeDamage(damage);
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
