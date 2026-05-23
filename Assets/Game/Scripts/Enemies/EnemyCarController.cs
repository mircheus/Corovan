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

        [Header("Shooting FX")]
        [SerializeField] private ParticleSystem shotEffect;

        private Rigidbody rb;
        private CarController currentTarget;
        private float retargetCooldown;
        private float fireCooldown;
        private float fixedY;

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

            TryFire();
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
            var cars = FindObjectsByType<CarController>(FindObjectsSortMode.None);
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
