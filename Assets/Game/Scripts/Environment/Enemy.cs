using Mirror;
using UnityEngine;
using FPSGame.Player;

namespace FPSGame.Environment
{
    [RequireComponent(typeof(SphereCollider))]
    public class Enemy : NetworkBehaviour
    {
        [Header("Combat")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float fireRate = 1f;
        [SerializeField] private float projectileDamage = 10f;

        [Header("Health")]
        [SerializeField] private float maxHealth = 100f;
        
        [Header("Materials")]
        [SerializeField] private Material attacking;
        [SerializeField] private Material neutral;

        [SyncVar(hook = nameof(OnAliveChanged))]
        private bool isAlive;

        [SyncVar]
        private float health;

        private float fireCooldown;
        private PlayerHealth currentTarget;
        private SphereCollider detectionCollider;
        private Renderer detectionRenderer;

        public bool IsAlive => isAlive;
        public float Health => health;
        public float MaxHealth => maxHealth;

        private void Awake()
        {
            detectionCollider = GetComponent<SphereCollider>();
            detectionCollider.isTrigger = true;
            detectionRenderer = GetComponent<Renderer>();
            detectionRenderer.material = neutral;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            health = maxHealth;
            isAlive = true;
        }

        private void Update()
        {
            if (!isServer || !isAlive || currentTarget == null)
                return;

            // Clear stale target if player died while inside the trigger.
            if (currentTarget.IsDead)
            {
                currentTarget = null;
                return;
            }

            fireCooldown -= Time.deltaTime;
            if (fireCooldown > 0f)
                return;

            FireAt(currentTarget.transform.position + Vector3.up * 1f);
            fireCooldown = 1f / fireRate;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isServer || !isAlive || currentTarget != null)
                return;

            var ph = other.GetComponentInParent<PlayerHealth>();
            if (ph == null || ph.IsDead)
                return;

            currentTarget = ph;
            detectionRenderer.material = attacking;
            Debug.Log($"[Enemy] {name}: player detected — {ph.name}");
        }

        private void OnTriggerExit(Collider other)
        {
            if (!isServer)
                return;

            var ph = other.GetComponentInParent<PlayerHealth>();
            if (ph != null && ph == currentTarget)
            {
                currentTarget = null;
                Debug.Log($"[Enemy] {name}: player left range — {ph.name}");
            }
        }

        private void FireAt(Vector3 targetPos)
        {
            if (projectilePrefab == null)
            {
                Debug.LogError($"[Enemy] {name}: projectilePrefab is not assigned!");
                return;
            }

            if (firePoint == null)
            {
                Debug.LogError($"[Enemy] {name}: firePoint is not assigned!");
                return;
            }

            Vector3 direction = (targetPos - firePoint.position).normalized;
            var obj = Instantiate(projectilePrefab, firePoint.position, Quaternion.LookRotation(direction));

            var projectile = obj.GetComponent<EnemyProjectile>();
            if (projectile != null)
            {
                projectile.Initialize(direction, projectileDamage);

                foreach (var col in GetComponentsInChildren<Collider>())
                    projectile.IgnoreCollider(col);
            }

            NetworkServer.Spawn(obj);
        }

        [Server]
        public void TakeDamage(float damage)
        {
            if (!isAlive || damage <= 0f)
                return;

            health = Mathf.Max(0f, health - damage);
            if (health <= 0f)
                isAlive = false;
        }

        private void OnAliveChanged(bool oldValue, bool newValue)
        {
            // Hook fires on all clients — extend here for death visuals/effects.
        }

        private void OnDrawGizmosSelected()
        {
            var col = GetComponent<SphereCollider>();
            if (col == null) return;

            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
            Gizmos.DrawSphere(transform.position, col.radius);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 1f);
            Gizmos.DrawWireSphere(transform.position, col.radius);
        }
    }
}
