using Mirror;
using UnityEngine;
using FPSGame.Player;
using FPSGame.Vehicle;

namespace FPSGame.Environment
{
    public class EnemyTurret : NetworkBehaviour
    {
        [Header("Combat")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float fireRate = 1f;
        [SerializeField] private int projectileDamage = 10;
        [SerializeField] private float projectileSpeed = 20f;
        [SerializeField] private float rotationSpeed = 5f;
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
        private float initialRotX;
        private float initialRotZ;

        private int _shot = Animator.StringToHash("Shot");

        public bool IsAlive => isAlive;
        public int Health => health;
        public int MaxHealth => maxHealth;

        // Target ownership is exposed to EnemyTurretTrigger, which owns all detection and
        // pushes acquire/clear here. The turret itself only tracks, aims and fires.
        public bool HasTarget => currentPlayerTarget != null || currentCarTarget != null;
        public PlayerHealth CurrentPlayerTarget => currentPlayerTarget;
        public CarController CurrentCarTarget => currentCarTarget;

        private void Awake()
        {
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

            // Detection (enter/exit/re-sweep) lives in EnemyTurretTrigger. Here we only drop a
            // target that stopped being valid without ever leaving the volume — e.g. a player who
            // goes down inside it (no OnTriggerExit fires). The trigger's periodic sweep re-acquires
            // them once revived.
            if (currentPlayerTarget != null)
            {
                if (!currentPlayerTarget.IsTargetable)
                {
                    ServerClearTarget();
                    return;
                }
            }
            else if (currentCarTarget != null)
            {
                if (!currentCarTarget.HasDriver || !currentCarTarget.VehicleIsAlive)
                {
                    ServerClearTarget();
                    return;
                }
            }
            else
            {
                return;
            }

            RotateTowardsTarget();

            fireCooldown -= Time.deltaTime;
            if (fireCooldown > 0f)
                return;

            FireAt(GetTargetPosition());
            fireCooldown = 1f / fireRate;
        }

        [Server]
        public void ServerAcquireTarget(PlayerHealth player)
        {
            if (player == null)
                return;
            currentPlayerTarget = player;
            currentCarTarget = null;
            // detectionRenderer.material = attacking;
        }

        [Server]
        public void ServerAcquireTarget(CarController car)
        {
            if (car == null)
                return;
            currentCarTarget = car;
            currentPlayerTarget = null;
            // detectionRenderer.material = attacking;
        }

        [Server]
        public void ServerClearTarget()
        {
            currentPlayerTarget = null;
            currentCarTarget = null;
            // detectionRenderer.material = neutral;
        }

        private Vector3 GetTargetPosition()
        {
            if (currentPlayerTarget != null)
                return currentPlayerTarget.transform.position + Vector3.up;
            return currentCarTarget.transform.position + Vector3.up;
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
    }
}
