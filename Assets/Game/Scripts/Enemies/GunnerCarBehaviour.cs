using Mirror;
using UnityEngine;
using FPSGame.Config;

namespace FPSGame.Environment
{
    /// <summary>
    /// Chases the target over the NavMesh up to <see cref="stopDistance"/>, turns the gun toward it and
    /// shoots <see cref="EnemyProjectile"/>s. The original EnemyCar behaviour.
    /// </summary>
    public class GunnerCarBehaviour : EnemyCarBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 8f;
        [SerializeField] private float angularSpeed = 0f;
        [SerializeField] private float acceleration = 12f;
        [Tooltip("Stop chasing once within this distance (no ramming).")]
        [SerializeField] private float stopDistance = 12f;
        [Tooltip("Re-issue SetDestination this often (seconds) while target is moving.")]
        [SerializeField] private float repathInterval = 0.25f;

        [Header("Combat")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;
        [Tooltip("Shots per second.")]
        [SerializeField] private float fireRate = 0.5f;
        [SerializeField] private float attackRange = 30f;
        [SerializeField] private int projectileDamage = 10;
        [SerializeField] private float projectileSpeed = 20f;

        [Header("Gun Aim")]
        [Tooltip("Root transform of the machine-gun that yaws toward the target. FirePoint should be its child so it rotates along.")]
        [SerializeField] private Transform gunPivot;
        [Tooltip("Degrees per second the gun rotates toward the target.")]
        [SerializeField] private float gunRotationSpeed = 180f;

        [Header("Shooting FX")]
        [SerializeField] private ParticleSystem shotEffect;

        private float repathCooldown;
        private float fireCooldown;

        [SyncVar] private float gunYaw;

        protected override void Awake()
        {
            base.Awake();
            // The controller's Awake may not have run yet, so don't go through Car.Agent here.
            var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            agent.speed = moveSpeed;
            agent.angularSpeed = angularSpeed;
            agent.acceleration = acceleration;
            agent.stoppingDistance = stopDistance;
            agent.autoBraking = true;
        }

        public override void ServerApplyBalance(EnemyCarBalance balance)
        {
            projectileDamage = balance.projectileDamage;
            projectileSpeed = balance.projectileSpeed;
        }

        public override void ServerOnTargetChanged()
        {
            repathCooldown = 0f; // plan a path this frame
        }

        public override void ServerIdle()
        {
            var agent = Car.Agent;
            if (agent.isOnNavMesh && agent.hasPath)
                agent.ResetPath();
        }

        public override void ServerTick()
        {
            UpdatePathServer();
            UpdateGunAimServer();
            TryFire();
        }

        private void UpdatePathServer()
        {
            var agent = Car.Agent;
            if (!agent.isOnNavMesh)
                return;

            repathCooldown -= Time.deltaTime;
            if (repathCooldown > 0f)
                return;

            // Navigate to the car's pivot (on the ground), not its raised aim point.
            agent.SetDestination(Car.TargetPosition);
            repathCooldown = repathInterval;
        }

        private void LateUpdate()
        {
            // All clients (and host) apply the synced yaw locally so remote
            // viewers see the gun track its target. A wreck's gun stays put on the tumbling body.
            if (!Car.IsDead)
                ApplyGunYaw();
        }

        private void UpdateGunAimServer()
        {
            if (gunPivot == null)
                return;

            Vector3 toTarget = Car.TargetAimPosition - gunPivot.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f)
                return;

            float desiredYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            gunYaw = Mathf.MoveTowardsAngle(gunYaw, desiredYaw, gunRotationSpeed * Time.deltaTime);
        }

        private void ApplyGunYaw()
        {
            if (gunPivot == null)
                return;
            // Yaw is in world space; the body never rotates, so a world-space
            // rotation on the pivot is unambiguous and avoids parent-rotation drift.
            gunPivot.rotation = Quaternion.Euler(0f, gunYaw, 0f);
        }

        private void TryFire()
        {
            fireCooldown -= Time.deltaTime;
            if (fireCooldown > 0f)
                return;

            Vector3 targetPos = Car.TargetAimPosition;
            float dist = Vector3.Distance(firePoint != null ? firePoint.position : transform.position, targetPos);
            if (dist > attackRange)
                return;

            FireAt(targetPos);
            fireCooldown = 1f / Mathf.Max(0.0001f, fireRate);
        }

        private void FireAt(Vector3 targetPos)
        {
            if (projectilePrefab == null)
            {
                Debug.LogError($"[EnemyCar] {name}: projectilePrefab is not assigned!");
                return;
            }
            if (firePoint == null)
            {
                Debug.LogError($"[EnemyCar] {name}: firePoint is not assigned!");
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
            RpcPlayShotFx(direction);
        }

        [ClientRpc]
        private void RpcPlayShotFx(Vector3 direction)
        {
            if (shotEffect != null)
                shotEffect.Play();

            // The projectile prefab owns its look; the car only asks it for the muzzle flash.
            if (projectilePrefab != null && firePoint != null &&
                projectilePrefab.TryGetComponent(out EnemyProjectile shot) && shot.Fx != null)
                shot.Fx.PlayMuzzle(firePoint.position, direction);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0f, 1f);
            Gizmos.DrawWireSphere(transform.position, attackRange);
            Gizmos.color = new Color(1f, 0f, 0f, 1f);
            Gizmos.DrawWireSphere(transform.position, stopDistance);
        }
    }
}
