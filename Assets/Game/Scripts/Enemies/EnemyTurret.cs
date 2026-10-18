using Mirror;
using FPSGame.Combat;
using UnityEngine;
using FPSGame.Player;
using FPSGame.Vehicle;

namespace FPSGame.Environment
{
    /// <summary>
    /// What a turret is allowed to shoot at. A mask rather than two bools so the inspector shows a
    /// single checkbox dropdown and the "targets nothing" case is representable (and warned about).
    /// </summary>
    [System.Flags]
    public enum TurretTargets
    {
        None = 0,
        Players = 1 << 0,
        Vehicles = 1 << 1
    }

    public class EnemyTurret : NetworkBehaviour, IDamageReceiver
    {
        int IDamageReceiver.CurrentHealth => health;
        bool IDamageReceiver.IsDead => !isAlive;

        [Header("Targeting")]
        [Tooltip("Which kinds of target this turret will engage. Detection is shared by EnemyTurretTrigger, " +
                 "so one sensor can feed a mixed battery of anti-personnel and anti-vehicle turrets.")]
        [SerializeField] private TurretTargets targetKinds = TurretTargets.Players | TurretTargets.Vehicles;

        [Tooltip("After the driver leaves the seat: ShootPlayer drops the car (the driver on foot becomes " +
                 "a normal target), ShootCar keeps hammering the abandoned vehicle.")]
        [SerializeField] private DriverExitBehavior onDriverExit = DriverExitBehavior.ShootPlayer;

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

        /// <summary>True if any bit of <paramref name="kind"/> is engageable by this turret.</summary>
        public bool Targets(TurretTargets kind) => (targetKinds & kind) != 0;

        // Single source of truth for "is this a legal target for me", used both by the sensor when it
        // offers a candidate and by Update to decide whether to keep the current one. Splitting those
        // two is how a target ends up stuck: acquired under one rule, never dropped under another.
        public bool CanEngage(PlayerHealth player)
        {
            return player != null && player.IsTargetable && Targets(TurretTargets.Players);
        }

        public bool CanEngage(CarController car)
        {
            if (car == null || !car.VehicleIsAlive || !Targets(TurretTargets.Vehicles))
                return false;
            return car.HasDriver || onDriverExit == DriverExitBehavior.ShootCar;
        }

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

            if (targetKinds == TurretTargets.None)
                Debug.LogWarning($"[EnemyTurret] {name}: targetKinds is None — this turret will never engage anything.");
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
                if (!CanEngage(currentPlayerTarget))
                {
                    ServerClearTarget();
                    return;
                }
            }
            else if (currentCarTarget != null)
            {
                if (!CanEngage(currentCarTarget))
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

        // The target-kind filter is enforced here, not only in the sensor: the turret is the
        // authority on what it shoots, so a mis-wired trigger can't push a forbidden target onto it.
        [Server]
        public void ServerAcquireTarget(PlayerHealth player)
        {
            if (!CanEngage(player))
                return;
            currentPlayerTarget = player;
            currentCarTarget = null;
            // detectionRenderer.material = attacking;
        }

        [Server]
        public void ServerAcquireTarget(CarController car)
        {
            if (!CanEngage(car))
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
