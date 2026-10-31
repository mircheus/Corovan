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

        private const int MaxCastHits = 16;
        private static readonly RaycastHit[] castBuffer = new RaycastHit[MaxCastHits];
        private static readonly Collider[] overlapBuffer = new Collider[MaxCastHits];

        private Vector3 direction;
        private float spawnTime;
        private int damage;
        private bool hasHit;
        private bool checkedSpawnOverlap;
        private float radius;
        private Rigidbody rb;
        private Collider ownCollider;
        // Colliders the shot flies through (its shooter). Replaces Physics.IgnoreCollision: the
        // projectile moves by sweep, not by physics contacts.
        private readonly System.Collections.Generic.HashSet<Collider> ignored =
            new System.Collections.Generic.HashSet<Collider>();

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            ownCollider = GetComponent<Collider>();
            // Hits are found by a sweep in FixedUpdate. A dynamic body could be deflected by a
            // CharacterController contact that raises no OnCollisionEnter (the shot then flew on
            // sideways without damaging anyone); a kinematic trigger body can't be pushed.
            rb.isKinematic = true;
            rb.useGravity = false;
            if (ownCollider != null)
            {
                ownCollider.isTrigger = true;
                radius = ownCollider is SphereCollider sphere
                    ? sphere.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z)
                    : ownCollider.bounds.extents.magnitude;
            }
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
            if (col != null)
                ignored.Add(col);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            spawnTime = Time.time;
            Invoke(nameof(DestroyProjectile), lifetime);
        }

        private void FixedUpdate()
        {
            if (!isServer || hasHit)
                return;

            Vector3 origin = rb.position;

            // A sweep skips colliders it starts inside (muzzle against a wall), so check once.
            if (!checkedSpawnOverlap)
            {
                checkedSpawnOverlap = true;
                int n = Physics.OverlapSphereNonAlloc(origin, radius, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (IsValidTarget(overlapBuffer[i]))
                    {
                        Hit(overlapBuffer[i], overlapBuffer[i].ClosestPoint(origin), -direction);
                        return;
                    }
                }
            }

            float step = speed * Time.fixedDeltaTime;
            int count = Physics.SphereCastNonAlloc(origin, radius, direction, castBuffer, step, ~0, QueryTriggerInteraction.Ignore);
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                if (!IsValidTarget(castBuffer[i].collider))
                    continue;
                if (best < 0 || castBuffer[i].distance < castBuffer[best].distance)
                    best = i;
            }

            if (best >= 0)
            {
                RaycastHit hit = castBuffer[best];
                rb.position = origin + direction * hit.distance;
                // A target that moved into the sphere since the last step (a car driving at the shot)
                // is already overlapping: the cast then reports distance 0 and point (0,0,0).
                if (hit.distance <= 0f)
                    Hit(hit.collider, hit.collider.ClosestPoint(origin), -direction);
                else
                    Hit(hit.collider, hit.point, hit.normal);
                return;
            }

            rb.MovePosition(origin + direction * step);
        }

        // Shots pass through the shooter's colliders and through each other.
        private bool IsValidTarget(Collider col)
        {
            return col != null && col != ownCollider && !col.isTrigger && !ignored.Contains(col)
                   && col.GetComponentInParent<EnemyProjectile>() == null;
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
            // The host already played it in Hit().
            if (fx != null && !isServer)
                fx.PlayHit(point, normal);
        }

        private void Hit(Collider col, Vector3 point, Vector3 normal)
        {
            hasHit = true;
            if (fx != null)
            {
                // On the host the RPC is queued and handled next update, but NetworkServer.Destroy below
                // removes the object from the host client at once, so the RPC is dropped. Play it here.
                if (isClient)
                    fx.PlayHit(point, normal);
                RpcHit(point, normal);
            }
            if (logHits)
                Debug.Log($"[EnemyProjectile] {name} hit '{col.name}' " +
                          $"(layer {LayerMask.LayerToName(col.gameObject.layer)}, " +
                          $"root '{col.transform.root.name}') {Time.time - spawnTime:F3}s after spawn", col);

            var playerHealth = col.GetComponentInParent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage);
            }
            else
            {
                var car = col.GetComponentInParent<CarController>();
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
