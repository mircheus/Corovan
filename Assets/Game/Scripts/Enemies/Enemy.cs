using Mirror;
using UnityEngine;
using FPSGame.Player;
using FPSGame.Vehicle;
using Unity.VisualScripting;

namespace FPSGame.Environment
{
    public class Enemy : NetworkBehaviour
    {
        [Header("Combat")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float fireRate = 1f;
        [SerializeField] private int projectileDamage = 10;
        [SerializeField] private float projectileSpeed = 20f;
        [SerializeField] private float rotationSpeed = 5f;
        [SerializeField] private bool keepAttackAfterTriggerExit;
        [SerializeField] private Collider detectionCollider;
        [Tooltip("Child object (the gun/turret) that rotates to face the target. Falls back to this transform if unset.")]
        [SerializeField] private Transform rotatingPart;

        [Header("Health")]
        [SerializeField] private int maxHealth = 100;
        
        [Header("Materials")]
        [SerializeField] private Material attacking;
        [SerializeField] private Material neutral;
        
        [Header("Animation")]
        [SerializeField] private Animator animator;

        [Header("Shooting FX")]
        [SerializeField] private ParticleSystem shotEffect;

        [Header("Death FX")]
        [SerializeField] private GameObject deathEffect;
        [SerializeField] private float deathEffectDuration = 3f;

        [Header("Death")]
        [SerializeField] private GameObject objectToHideOnDeath;
        [SerializeField] private Collider bodyCollider;

        [SyncVar(hook = nameof(OnAliveChanged))]
        private bool isAlive;

        [SyncVar]
        private int health;

        private float fireCooldown;
        private PlayerHealth currentPlayerTarget;
        private CarController currentCarTarget;
        private Renderer detectionRenderer;
        private float initialRotX;
        private float initialRotZ;
        
        private int _shot = Animator.StringToHash("Shot");

        public bool IsAlive => isAlive;
        public int Health => health;
        public int MaxHealth => maxHealth;

        private void Awake()
        {
            detectionCollider.isTrigger = true;
            // detectionRenderer = GetComponent<Renderer>();
            // detectionRenderer.material = neutral;
            if (rotatingPart == null)
                rotatingPart = transform;
            initialRotX = rotatingPart.eulerAngles.x;
            initialRotZ = rotatingPart.eulerAngles.z;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            health = maxHealth;
            isAlive = true;
        }

        private void Update()
        {
            if (!isServer || !isAlive)
                return;

            // Validate and clear stale targets.
            if (currentPlayerTarget != null && currentPlayerTarget.IsDead)
            {
                ClearTarget();
                return;
            }
            if (currentCarTarget != null && (!currentCarTarget.HasDriver || !currentCarTarget.VehicleIsAlive))
            {
                ClearTarget();
                return;
            }

            if (currentPlayerTarget == null && currentCarTarget == null)
                return;

            RotateTowardsTarget();

            fireCooldown -= Time.deltaTime;
            if (fireCooldown > 0f)
                return;

            FireAt(GetTargetPosition());
            fireCooldown = 1f / fireRate;
        }

        private Vector3 GetTargetPosition()
        {
            if (currentPlayerTarget != null)
                return currentPlayerTarget.transform.position + Vector3.up;
            return currentCarTarget.transform.position + Vector3.up;
        }

        private void ClearTarget()
        {
            currentPlayerTarget = null;
            currentCarTarget = null;
            // detectionRenderer.material = neutral;
        }

        private void RotateTowardsTarget()
        {
            Vector3 dir = GetTargetPosition() - rotatingPart.position;
            if (dir.sqrMagnitude < 0.001f)
                return;

            float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float newY = Mathf.LerpAngle(rotatingPart.eulerAngles.y, targetAngle, rotationSpeed * Time.deltaTime);
            rotatingPart.rotation = Quaternion.Euler(initialRotX, newY, initialRotZ);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isServer || !isAlive || currentPlayerTarget != null || currentCarTarget != null)
                return;

            var ph = other.GetComponentInParent<PlayerHealth>();
            if (ph != null && !ph.IsDead)
            {
                currentPlayerTarget = ph;
                // detectionRenderer.material = attacking;
                Debug.Log($"[Enemy] {name}: player detected — {ph.name}");
                return;
            }

            var car = other.GetComponentInParent<CarController>();
            if (car != null && car.HasDriver && car.VehicleIsAlive)
            {
                currentCarTarget = car;
                // detectionRenderer.material = attacking;
                Debug.Log($"[Enemy] {name}: vehicle detected — {car.name}");
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!isServer)
                return;

            if (keepAttackAfterTriggerExit)
            {
                return;
            }

            var ph = other.GetComponentInParent<PlayerHealth>();
            if (ph != null && ph == currentPlayerTarget)
            {
                ClearTarget();
                Debug.Log($"[Enemy] {name}: player left range — {ph.name}");
                return;
            }

            var car = other.GetComponentInParent<CarController>();
            if (car != null && car == currentCarTarget)
            {
                ClearTarget();
                Debug.Log($"[Enemy] {name}: vehicle left range — {car.name}");
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
                projectile.Initialize(direction, projectileDamage, projectileSpeed);

                foreach (var col in GetComponentsInChildren<Collider>())
                    projectile.IgnoreCollider(col);
            }

            NetworkServer.Spawn(obj);
            RpcPlayShotFx();
        }

        [ClientRpc]
        private void RpcPlayShotFx()
        {
            // FireAt runs server-only, so shot visuals must be relayed to every
            // client via RPC.
            if (shotEffect != null)
                shotEffect.Play();
            if (animator != null)
                animator.Play(_shot);
        }

        [Server]
        public void TakeDamage(int damage)
        {
            if (!isAlive || damage <= 0)
                return;

            health = Mathf.Max(0, health - damage);
            if (health <= 0)
                isAlive = false;
        }

        private void OnAliveChanged(bool oldValue, bool newValue)
        {
            // Hook fires on all clients. Apply death visuals only on the alive ->
            // dead transition, so it doesn't replay for late-joining clients.
            if (oldValue && !newValue)
                HandleDeath();
        }

        private void HandleDeath()
        {
            PlayDeathEffect();

            // Hide the turret body and disable its solid collider so the corpse
            // doesn't block the player from walking through.
            if (objectToHideOnDeath != null)
                objectToHideOnDeath.SetActive(false);
            if (bodyCollider != null)
                bodyCollider.enabled = false;
        }

        private void PlayDeathEffect()
        {
            if (deathEffect == null)
                return;

            Debug.Log($"Play Death Effect");
            deathEffect.SetActive(true);
            if (deathEffectDuration > 0f)
            {
                CancelInvoke(nameof(HideDeathEffect));
                Invoke(nameof(HideDeathEffect), deathEffectDuration);
            }
        }

        private void HideDeathEffect()
        {
            if (deathEffect != null)
                deathEffect.SetActive(false);
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