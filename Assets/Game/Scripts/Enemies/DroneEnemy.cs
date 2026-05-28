using FPSGame.Environment;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
using FPSGame.Player;
using FPSGame.Vehicle;

namespace FPSGame.Enemies
{
    /// <summary>
    /// Flying drone enemy. Uses a dedicated airborne NavMeshSurface (baked on a
    /// plane above the level) for horizontal pathfinding around walls. Vertical
    /// position is driven manually: cruise height from NavMeshAgent.baseOffset,
    /// combat correction toward the target's Y, plus a sine bob.
    ///
    /// Prefab requirements:
    ///   • NetworkIdentity + NetworkTransformUnreliable
    ///   • NavMeshAgent (Agent Type = "Drone", updateRotation off, baseOffset = hoverHeight)
    ///   • SphereCollider for hit detection (not trigger)
    ///   • firePoint child Transform
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class DroneEnemy : NetworkBehaviour
    {
        private enum DroneState { Idle = 0, Chasing = 1, Attacking = 2, Dead = 3 }

        [Header("Detection")]
        [SerializeField] private float detectionRadius = 25f;
        [SerializeField] private float attackRadius = 18f;
        [Tooltip("Distance the drone tries to keep from the target while attacking.")]
        [SerializeField] private float preferredStandoff = 12f;
        [SerializeField] private LayerMask targetLayerMask = ~0;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 8f;
        [SerializeField] private float rotationSpeed = 6f;

        [Header("Vertical")]
        [Tooltip("Cruise height above the airborne NavMesh — should match NavMeshAgent.baseOffset.")]
        [SerializeField] private float hoverHeight = 6f;
        [Tooltip("Max vertical deviation from cruise height to track a target.")]
        [SerializeField] private float combatYRange = 4f;
        [Tooltip("Vertical offset above the target's pivot when tracking.")]
        [SerializeField] private float combatYOffset = 1.5f;
        [SerializeField] private float verticalLerpSpeed = 3f;
        [SerializeField] private float bobAmplitude = 0.15f;
        [SerializeField] private float bobFrequency = 1.5f;
        [Tooltip("Minimum clearance above ground / below ceiling.")]
        [SerializeField] private float minClearance = 1.5f;
        [SerializeField] private LayerMask clearanceMask = ~0;

        [Header("Combat")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float fireRate = 2f;
        [SerializeField] private int projectileDamage = 8;

        [Header("Health")]
        [SerializeField] private int maxHealth = 40;

        [Header("Animator")]
        [SerializeField] private Animator animator;
        [SerializeField] private string stateParameterName = "State";

        [Header("Shooting FX")]
        [SerializeField] private ParticleSystem shotEffect;

        [Header("Death FX")]
        [SerializeField] private GameObject deathEffect;
        [SerializeField] private float deathEffectDuration = 3f;
        [SerializeField] private GameObject objectToHideOnDeath;
        [SerializeField] private Collider bodyCollider;

        [SyncVar(hook = nameof(OnStateChanged))]
        private int syncedState = (int)DroneState.Idle;

        [SyncVar(hook = nameof(OnAliveChanged))]
        private bool isAlive;

        [SyncVar]
        private int health;

        private NavMeshAgent agent;
        private PlayerHealth currentPlayerTarget;
        private CarController currentCarTarget;
        private float fireCooldown;
        private int stateParamHash;
        private float currentVerticalOffset;
        private static readonly int ShotHash = Animator.StringToHash("Shot");

        public bool IsAlive => isAlive;
        public int Health => health;
        public int MaxHealth => maxHealth;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            stateParamHash = Animator.StringToHash(stateParameterName);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            health = maxHealth;
            isAlive = true;
            agent.speed = moveSpeed;
            agent.updateRotation = false;
            agent.baseOffset = hoverHeight;
            agent.stoppingDistance = preferredStandoff * 0.9f;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!isServer)
                agent.enabled = false;
            ApplyAnimatorState(syncedState);
        }

        private void Update()
        {
            if (!isServer || !isAlive)
                return;

            ValidateTargets();

            if (currentPlayerTarget == null && currentCarTarget == null)
                AcquireTarget();

            if (currentPlayerTarget == null && currentCarTarget == null)
            {
                TransitionTo(DroneState.Idle);
                agent.isStopped = true;
                return;
            }

            Vector3 targetPos = GetTargetPosition();
            float horizontalDist = HorizontalDistance(transform.position, targetPos);

            if (horizontalDist > detectionRadius)
            {
                ClearTargets();
                return;
            }

            UpdateMovement(targetPos, horizontalDist);
            FaceTarget(targetPos);

            if (horizontalDist <= attackRadius)
                UpdateAttack(targetPos);
            else
                TransitionTo(DroneState.Chasing);
        }

        private void LateUpdate()
        {
            if (!isServer || !isAlive)
                return;

            UpdateVertical();
        }

        // ── Targeting ─────────────────────────────────────────────────────

        private void ValidateTargets()
        {
            if (currentPlayerTarget != null && currentPlayerTarget.IsDead)
                currentPlayerTarget = null;
            if (currentCarTarget != null && (!currentCarTarget.HasDriver || !currentCarTarget.VehicleIsAlive))
                currentCarTarget = null;
        }

        private void AcquireTarget()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, detectionRadius, targetLayerMask);
            float bestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                var ph = hit.GetComponentInParent<PlayerHealth>();
                if (ph != null && !ph.IsDead)
                {
                    float d = HorizontalDistance(transform.position, ph.transform.position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        currentPlayerTarget = ph;
                        currentCarTarget = null;
                    }
                    continue;
                }

                var car = hit.GetComponentInParent<CarController>();
                if (car != null && car.HasDriver && car.VehicleIsAlive)
                {
                    float d = HorizontalDistance(transform.position, car.transform.position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        currentCarTarget = car;
                        currentPlayerTarget = null;
                    }
                }
            }
        }

        private void ClearTargets()
        {
            currentPlayerTarget = null;
            currentCarTarget = null;
        }

        private Vector3 GetTargetPosition()
        {
            if (currentPlayerTarget != null)
                return currentPlayerTarget.transform.position + Vector3.up;
            return currentCarTarget.transform.position + Vector3.up;
        }

        // ── Movement ──────────────────────────────────────────────────────

        private void UpdateMovement(Vector3 targetPos, float horizontalDist)
        {
            agent.isStopped = false;

            // Hold standoff distance from the target while attacking.
            Vector3 flatTarget = new Vector3(targetPos.x, transform.position.y, targetPos.z);
            Vector3 toTarget = flatTarget - transform.position;
            Vector3 dirFromTarget = -toTarget.normalized;
            Vector3 destination = horizontalDist > attackRadius
                ? flatTarget
                : flatTarget + dirFromTarget * preferredStandoff;

            agent.SetDestination(destination);
        }

        private void UpdateVertical()
        {
            // NavMeshAgent.baseOffset places the transform at cruise height above
            // the airborne navmesh. Combat correction pulls it toward the target's
            // Y within ±combatYRange.
            float cruiseY = transform.position.y;

            if (currentPlayerTarget != null || currentCarTarget != null)
            {
                Vector3 t = GetTargetPosition();
                float desiredOffset = Mathf.Clamp(
                    t.y + combatYOffset - cruiseY,
                    -combatYRange,
                    combatYRange);
                currentVerticalOffset = Mathf.Lerp(
                    currentVerticalOffset,
                    desiredOffset,
                    verticalLerpSpeed * Time.deltaTime);
            }
            else
            {
                currentVerticalOffset = Mathf.Lerp(
                    currentVerticalOffset, 0f, verticalLerpSpeed * Time.deltaTime);
            }

            float bob = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
            float finalY = cruiseY + currentVerticalOffset + bob;

            // Clamp to keep clearance from ground and ceiling.
            Vector3 probe = new Vector3(transform.position.x, finalY, transform.position.z);
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit downHit, 50f, clearanceMask, QueryTriggerInteraction.Ignore))
            {
                float minY = downHit.point.y + minClearance;
                if (finalY < minY) finalY = minY;
            }
            if (Physics.Raycast(probe, Vector3.up, out RaycastHit upHit, 20f, clearanceMask, QueryTriggerInteraction.Ignore))
            {
                float maxY = upHit.point.y - minClearance;
                if (finalY > maxY) finalY = maxY;
            }

            transform.position = new Vector3(transform.position.x, finalY, transform.position.z);
        }

        private void FaceTarget(Vector3 targetPosition)
        {
            Vector3 dir = targetPosition - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f)
                return;

            float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float newY = Mathf.LerpAngle(transform.eulerAngles.y, targetAngle, rotationSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, newY, 0f);
        }

        // ── Combat ────────────────────────────────────────────────────────

        private void UpdateAttack(Vector3 targetPos)
        {
            TransitionTo(DroneState.Attacking);

            fireCooldown -= Time.deltaTime;
            if (fireCooldown > 0f)
                return;

            if (!HasLineOfSight(targetPos))
                return;

            FireAt(targetPos);
            fireCooldown = 1f / fireRate;
        }

        private bool HasLineOfSight(Vector3 targetPos)
        {
            if (firePoint == null)
                return true;

            Vector3 dir = targetPos - firePoint.position;
            float dist = dir.magnitude;
            if (dist < 0.1f)
                return true;

            if (Physics.Raycast(firePoint.position, dir.normalized, out RaycastHit hit, dist, clearanceMask, QueryTriggerInteraction.Ignore))
            {
                // Hit something before reaching the target — wall in the way.
                bool isTarget = hit.collider.GetComponentInParent<PlayerHealth>() == currentPlayerTarget && currentPlayerTarget != null
                    || hit.collider.GetComponentInParent<CarController>() == currentCarTarget && currentCarTarget != null;
                return isTarget;
            }
            return true;
        }

        private void FireAt(Vector3 targetPos)
        {
            if (projectilePrefab == null || firePoint == null)
            {
                Debug.LogError($"[DroneEnemy] {name}: projectilePrefab or firePoint not assigned");
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
            if (animator != null)
                animator.Play(ShotHash);
        }

        // ── Damage / death ────────────────────────────────────────────────

        [Server]
        public void TakeDamage(int damage)
        {
            if (!isAlive || damage <= 0)
                return;

            health = Mathf.Max(0, health - damage);
            if (health <= 0)
                Die();
        }

        [Server]
        private void Die()
        {
            isAlive = false;
            agent.isStopped = true;
            TransitionTo(DroneState.Dead);
        }

        // ── State / hooks ─────────────────────────────────────────────────

        private void TransitionTo(DroneState newState)
        {
            if (syncedState == (int)newState)
                return;
            syncedState = (int)newState;
        }

        private void OnStateChanged(int oldState, int newState)
        {
            ApplyAnimatorState(newState);
        }

        private void ApplyAnimatorState(int state)
        {
            if (animator != null)
                animator.SetInteger(stateParamHash, state);
        }

        private void OnAliveChanged(bool oldValue, bool newValue)
        {
            if (oldValue && !newValue)
                HandleDeath();
        }

        private void HandleDeath()
        {
            if (deathEffect != null)
            {
                deathEffect.SetActive(true);
                if (deathEffectDuration > 0f)
                {
                    CancelInvoke(nameof(HideDeathEffect));
                    Invoke(nameof(HideDeathEffect), deathEffectDuration);
                }
            }
            if (objectToHideOnDeath != null)
                objectToHideOnDeath.SetActive(false);
            if (bodyCollider != null)
                bodyCollider.enabled = false;
        }

        private void HideDeathEffect()
        {
            if (deathEffect != null)
                deathEffect.SetActive(false);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0f, 1f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 1f);
            Gizmos.DrawWireSphere(transform.position, attackRadius);
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
            Gizmos.DrawWireSphere(transform.position, preferredStandoff);
        }
    }
}
