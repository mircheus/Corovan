using Mirror;
using UnityEngine;
using FPSGame.Vehicle;

namespace FPSGame.Environment
{
    [RequireComponent(typeof(Rigidbody))]
    public class EnemyCarController : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 8f;
        [Tooltip("Stop chasing once within this distance (no ramming).")]
        [SerializeField] private float stopDistance = 12f;
        [Tooltip("Re-scan for a target this often (seconds).")]
        [SerializeField] private float retargetInterval = 1f;

        [Header("Combat")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;
        [Tooltip("Shots per second.")]
        [SerializeField] private float fireRate = 0.5f;
        [SerializeField] private float attackRange = 30f;
        [SerializeField] private int projectileDamage = 10;

        [Header("Gun Aim")]
        [Tooltip("Root transform of the machine-gun that yaws toward the target. FirePoint should be its child so it rotates along.")]
        [SerializeField] private Transform gunPivot;
        [Tooltip("Degrees per second the gun rotates toward the target.")]
        [SerializeField] private float gunRotationSpeed = 180f;

        [Header("Shooting FX")]
        [SerializeField] private ParticleSystem shotEffect;

        private Rigidbody rb;
        private CarController currentTarget;
        private float retargetCooldown;
        private float fireCooldown;
        private float fixedY;

        [SyncVar] private float gunYaw;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            fixedY = transform.position.y;
        }

        private void Update()
        {
            if (!isServer)
                return;

            retargetCooldown -= Time.deltaTime;
            if (currentTarget == null || !IsValidTarget(currentTarget) || retargetCooldown <= 0f)
            {
                currentTarget = FindNearestTarget();
                retargetCooldown = retargetInterval;
            }

            if (currentTarget == null)
                return;

            UpdateGunAimServer();
            TryFire();
        }

        private void LateUpdate()
        {
            // All clients (and host) apply the synced yaw locally so remote
            // viewers see the gun track its target.
            ApplyGunYaw();
        }

        private void UpdateGunAimServer()
        {
            if (gunPivot == null)
                return;

            Vector3 toTarget = currentTarget.transform.position - gunPivot.position;
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

        private void FixedUpdate()
        {
            if (!isServer || currentTarget == null)
                return;

            Vector3 pos = rb.position;
            Vector3 targetPos = currentTarget.transform.position;
            Vector3 delta = targetPos - pos;
            delta.y = 0f;

            float dist = delta.magnitude;
            if (dist <= stopDistance || dist < 0.001f)
                return;

            Vector3 dir = delta / dist;
            Vector3 step = dir * moveSpeed * Time.fixedDeltaTime;
            // Clamp so we don't overshoot stopDistance in one step.
            float maxStep = dist - stopDistance;
            if (step.magnitude > maxStep)
                step = dir * maxStep;

            Vector3 newPos = pos + step;
            newPos.y = fixedY;
            rb.MovePosition(newPos);
        }

        private CarController FindNearestTarget()
        {
            CarController best = null;
            float bestSqr = float.PositiveInfinity;
            var cars = FindObjectsByType<CarController>(FindObjectsSortMode.None); // TODO: Remove FindObjectsByType
            Vector3 myPos = transform.position;
            foreach (var car in cars)
            {
                if (!IsValidTarget(car))
                    continue;
                float sqr = (car.transform.position - myPos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = car;
                }
            }
            return best;
        }

        private bool IsValidTarget(CarController car)
        {
            return car != null && car.HasDriver && car.VehicleIsAlive;
        }

        private void TryFire()
        {
            fireCooldown -= Time.deltaTime;
            if (fireCooldown > 0f)
                return;

            Vector3 targetPos = currentTarget.transform.position + Vector3.up;
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
                projectile.Initialize(direction, projectileDamage);
                foreach (var col in GetComponentsInChildren<Collider>())
                    projectile.IgnoreCollider(col);
            }

            NetworkServer.Spawn(obj);
            RpcPlayShotFx();
        }

        [ClientRpc]
        private void RpcPlayShotFx()
        {
            if (shotEffect != null)
                shotEffect.Play();
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
