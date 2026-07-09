using Mirror;
using UnityEngine;
using UnityEngine.AI;
using FPSGame.Player;

namespace FPSGame.Environment
{
    /// <summary>
    /// Networked zombie enemy with three states: Idle → Chasing → Attacking.
    ///
    /// Prefab setup requirements:
    ///   • NavMeshAgent component (speed is overridden by moveSpeed at runtime)
    ///   • Animator with an Integer parameter matching stateParameterName (default "State"):
    ///       0 = Idle, 1 = Chasing (walk/run), 2 = Attacking (punch)
    ///   • NetworkIdentity + NetworkTransformUnreliable (syncs position from server to clients)
    ///   • NavMesh must be baked in the scene
    ///   • playerLayerMask should be set to the Player layer
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class ZombieEnemy : NetworkBehaviour
    {
        private enum ZombieState { Idle = 0, Chasing = 1, Attacking = 2, Dead = 3 }

        [Header("Detection")]
        [SerializeField] private float detectionRadius = 15f;
        [SerializeField] private float attackRadius = 2f;
        [SerializeField] private LayerMask playerLayerMask = ~0;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float rotationSpeed = 10f;

        [Header("Combat")]
        [SerializeField] private int attackDamage = 15;
        [SerializeField] private float attackCooldown = 1.5f;
        [Tooltip("Seconds after the attack animation starts before damage is applied.")]
        [SerializeField] private float attackDamageDelay = 0.5f;

        [Header("Health")]
        [SerializeField] private int maxHealth = 80;

        [Header("Animator")]
        [SerializeField] private Animator animator;
        [SerializeField] private string stateParameterName = "State";

        [Header("Hit Effects")]
        [SerializeField] private ParticleSystem[] hitEffects;

        // ── Synced state ──────────────────────────────────────────────────

        [SyncVar(hook = nameof(OnStateChanged))]
        private int syncedState = (int)ZombieState.Idle;

        [SyncVar(hook = nameof(OnAliveChanged))]
        private bool isAlive;

        [SyncVar]
        private int health;

        // ── Server-only fields ────────────────────────────────────────────

        private NavMeshAgent agent;
        private CapsuleCollider capsuleCollider;
        private PlayerHealth currentTarget;
        // True when the target was handed in externally (e.g. by ZombieSpawner). A
        // commanded target is chased from any distance and is only dropped when it dies,
        // so a zombie spawned to hunt a specific player won't idle just because that
        // player is beyond detectionRadius.
        private bool targetCommanded;
        private float attackTimer;
        private int stateParamHash;

        // ── Public accessors ──────────────────────────────────────────────

        public bool IsAlive => isAlive;
        public int Health => health;
        public int MaxHealth => maxHealth;

        // ── Unity / Mirror lifecycle ──────────────────────────────────────

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            capsuleCollider = GetComponent<CapsuleCollider>();

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
            agent.stoppingDistance = attackRadius * 0.8f;
            agent.updateRotation = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // NavMeshAgent drives transform on the server only.
            // Clients receive position via NetworkTransformUnreliable.
            if (!isServer)
                agent.enabled = false;

            // Apply the already-synced state for late-joiners.
            ApplyAnimatorState(syncedState);
        }

        // ── Server update loop ────────────────────────────────────────────

        private void Update()
        {
            if (!isServer || !isAlive)
                return;

            ValidateTarget();

            if (currentTarget == null)
                currentTarget = FindNearestPlayer();

            if (currentTarget == null)
            {
                TransitionTo(ZombieState.Idle);
                agent.isStopped = true;
                return;
            }

            float dist = Vector3.Distance(transform.position, currentTarget.transform.position);

            if (dist <= attackRadius)
                UpdateAttacking();
            else if (targetCommanded || dist <= detectionRadius)
                UpdateChasing();
            else
                LoseTarget();
        }

        private void UpdateChasing()
        {
            TransitionTo(ZombieState.Chasing);
            agent.isStopped = false;
            agent.SetDestination(currentTarget.transform.position);
            FaceTarget(currentTarget.transform.position);
        }

        private void UpdateAttacking()
        {
            if ((ZombieState)syncedState != ZombieState.Attacking)
            {
                TransitionTo(ZombieState.Attacking);
                agent.isStopped = true;
                // Fire immediately on entering attack state.
                attackTimer = attackCooldown;
            }

            FaceTarget(currentTarget.transform.position);

            attackTimer += Time.deltaTime;
            if (attackTimer >= attackCooldown)
            {
                attackTimer = 0f;
                Invoke(nameof(DealDamage), attackDamageDelay);
            }
        }

        private void LoseTarget()
        {
            currentTarget = null;
            targetCommanded = false;
            TransitionTo(ZombieState.Idle);
            agent.isStopped = true;
        }

        // Assign a target from outside (server-only). The zombie chases it regardless of
        // detectionRadius until the target dies. Used by ZombieSpawner to hand a zombie
        // the player standing in the spawner's aggro zone the moment it spawns.
        [Server]
        public void ServerSetTarget(PlayerHealth target)
        {
            if (!isAlive || target == null || !target.IsTargetable)
                return;
            currentTarget = target;
            targetCommanded = true;
        }

        // ── Server helpers ────────────────────────────────────────────────

        private void ValidateTarget()
        {
            if (currentTarget != null && !currentTarget.IsTargetable)
            {
                currentTarget = null;
                targetCommanded = false;
            }
        }

        private PlayerHealth FindNearestPlayer()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, detectionRadius, playerLayerMask);
            PlayerHealth nearest = null;
            float nearestDist = float.MaxValue;

            foreach (var hit in hits)
            {
                var ph = hit.GetComponentInParent<PlayerHealth>();
                if (ph == null || !ph.IsTargetable)
                    continue;

                float dist = Vector3.Distance(transform.position, ph.transform.position);
                if (dist < nearestDist)
                {
                    nearest = ph;
                    nearestDist = dist;
                }
            }

            return nearest;
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

        private void TransitionTo(ZombieState newState)
        {
            if (syncedState == (int)newState)
                return;
            syncedState = (int)newState;
        }

        [Server]
        private void DealDamage()
        {
            if (currentTarget == null || !currentTarget.IsTargetable)
                return;

            // Re-validate distance — player may have moved away during the animation delay.
            float dist = Vector3.Distance(transform.position, currentTarget.transform.position);
            if (dist > attackRadius * 1.3f)
                return;

            currentTarget.TakeDamage(attackDamage);
        }

        [Server]
        public void TakeDamage(int damage)
        {
            Debug.Log($"Damage: {damage}");
            
            if (!isAlive || damage <= 0)
                return;

            health = Mathf.Max(0, health - damage);
            if (health <= 0)
                Die();
        }

        [Server]
        public void ServerPlayHitEffect(Vector3 point, Vector3 normal)
        {
            RpcPlayHitEffect(point, normal);
        }

        [ClientRpc]
        private void RpcPlayHitEffect(Vector3 point, Vector3 normal)
        {
            ParticleSystem effect = GetAvailableHitEffect();
            if (effect == null)
                return;

            effect.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal));
            effect.Play();
        }

        private ParticleSystem GetAvailableHitEffect()
        {
            if (hitEffects == null || hitEffects.Length == 0)
                return null;

            foreach (var ps in hitEffects)
                if (ps != null && !ps.isPlaying)
                    return ps;

            return null;
        }

        [Server]
        private void Die()
        {
            isAlive = false;
            CancelInvoke(nameof(DealDamage));
            agent.isStopped = true;
            TransitionTo(ZombieState.Dead);
        }

        // ── SyncVar hooks — fire on all clients ───────────────────────────

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
            if (!newValue)
            {
                ApplyAnimatorState((int)ZombieState.Dead);
                if (capsuleCollider != null)
                    capsuleCollider.enabled = false;
            }
        }

        // ── Gizmos ────────────────────────────────────────────────────────

        private void OnDrawGizmosSelected()
        {
            // Detection radius — yellow
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.12f);
            Gizmos.DrawSphere(transform.position, detectionRadius);
            Gizmos.color = new Color(1f, 0.8f, 0f, 1f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);

            // Attack radius — red
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
            Gizmos.DrawSphere(transform.position, attackRadius);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 1f);
            Gizmos.DrawWireSphere(transform.position, attackRadius);
        }
    }
}
