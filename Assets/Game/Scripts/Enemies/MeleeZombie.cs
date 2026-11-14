using Mirror;
using UnityEngine;
using UnityEngine.AI;
using FPSGame.Combat;
using FPSGame.Config;
using FPSGame.Environment;
using FPSGame.Player;

namespace FPSGame.Enemies
{
    /// <summary>
    /// Walking melee zombie built on the RagdollBot: notices a player on foot, runs the NavMeshAgent
    /// to them and scratches them when close. Health, death and the ragdoll stay in
    /// <see cref="RagdollEnemy"/>; this component is only the brain and decides on the server.
    ///
    /// The hit lands on the <c>AttackHit</c> animation event of the scratch clip, not on a timer, so
    /// it follows the animation. Events fire on every peer that plays the clip, but only the server
    /// deals damage. Legs (Idle/Walk) are fed by <see cref="DroidAnimator"/> from the root's speed;
    /// only the attack swing travels as an RPC.
    ///
    /// Prefab requirements:
    ///   • RagdollEnemy (+ Ragdoll, FPSFDamageable, HealthGroups on the bones)
    ///   • NetworkIdentity + NetworkTransformUnreliable (server to client)
    ///   • NavMeshAgent — a NavMesh must be baked in the scene
    ///   • Animator on this object with an "Attack" trigger; the scratch clip calls <see cref="AttackHit"/>
    /// </summary>
    [RequireComponent(typeof(RagdollEnemy))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class MeleeZombie : NetworkBehaviour, IAttackerListener
    {
        private static readonly int AttackTrigger = Animator.StringToHash("Attack");
        private static readonly RaycastHit[] rayBuffer = new RaycastHit[16];

        [Header("Detection")]
        [SerializeField] private float detectionRadius = 15f;
        [Tooltip("Only notice a player the zombie can actually see. Off = notices through walls.")]
        [SerializeField] private bool requireSightToDetect = true;
        [Tooltip("How often an idle zombie looks around for a player.")]
        [SerializeField] private float retargetInterval = 0.5f;
        [Tooltip("Height above the zombie's and the player's pivots the sight check runs between.")]
        [SerializeField] private float eyeHeight = 1.5f;
        [Tooltip("What blocks the zombie's sight.")]
        [SerializeField] private LayerMask sightMask = ~0;

        [Header("Losing the target")]
        [Tooltip("Drop the target once it is farther than this. 0 = never drop it by distance.")]
        [SerializeField] private float leashRadius = 30f;

        [Header("Aggro")]
        [Tooltip("An idle zombie hit by a player turns on that player, wherever they stand.")]
        [SerializeField] private bool aggroOnDamage = true;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private float rotationSpeed = 8f;
        [Tooltip("How often the path to the target is re-planned.")]
        [SerializeField] private float thinkInterval = 0.2f;

        [Header("Attack")]
        [SerializeField] private int attackDamage = 15;
        [Tooltip("Starts a swing when the target is this close.")]
        [SerializeField] private float attackRange = 1.8f;
        [Tooltip("The hit still lands if the target stepped back, up to attackRange x this.")]
        [SerializeField] private float hitRangeMultiplier = 1.3f;
        [Tooltip("Seconds from the start of one swing to the start of the next.")]
        [SerializeField] private float attackCooldown = 1.8f;
        [Tooltip("Seconds the zombie stands still and faces the target for a swing (about the clip length).")]
        [SerializeField] private float attackDuration = 1.2f;

        private RagdollEnemy body;
        private NavMeshAgent agent;
        private Animator animator;
        private AgentLinkJumper linkJumper; // optional: jumps NavMesh links instead of gliding

        private PlayerHealth target;
        private float thinkTimer;
        private float retargetTimer;
        private float attackCooldownLeft;
        private float attackLockLeft;

        public PlayerHealth Target => target;
        public bool IsDead => body.IsDead;

        private void Awake()
        {
            body = GetComponent<RagdollEnemy>();
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();
            linkJumper = GetComponent<AgentLinkJumper>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ApplyBalance();
            agent.speed = moveSpeed;
            agent.stoppingDistance = attackRange * 0.8f;
            agent.updateRotation = false;
            body.ServerDied += OnServerDied;
            body.ServerRespawned += OnServerRespawned;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            body.ServerDied -= OnServerDied;
            body.ServerRespawned -= OnServerRespawned;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Only the server walks the zombie; clients follow its NetworkTransform.
            if (!isServer)
                agent.enabled = false;
        }

        // Prefab values stay unless the scene has a BalanceConfig.
        [Server]
        private void ApplyBalance()
        {
            BalanceConfig config = SceneBalance.Current;
            if (config == null)
                return;

            ZombieBalance balance = config.zombie;
            body.ServerSetMaxHealth(balance.maxHealth);
            attackDamage = balance.attackDamage;
        }

        private void Update()
        {
            if (!isServer || body.IsDead)
                return;

            attackCooldownLeft -= Time.deltaTime;
            attackLockLeft -= Time.deltaTime;

            // Airborne on a NavMesh link: the jumper owns the body; no re-pathing, facing or swings.
            if (linkJumper != null && linkJumper.IsJumping)
                return;

            if (target != null && !ShouldKeepTarget())
                ClearTarget();

            if (target == null)
            {
                UpdateIdle();
                return;
            }

            if (!agent.enabled || !agent.isOnNavMesh)
                return;

            bool swinging = attackLockLeft > 0f;

            // Planting the feet for the swing; the agent resumes when the lock runs out.
            agent.isStopped = swinging;

            if (!swinging)
            {
                thinkTimer -= Time.deltaTime;
                if (thinkTimer <= 0f)
                {
                    thinkTimer = thinkInterval;
                    agent.SetDestination(target.transform.position);
                }
            }

            Face();

            if (!swinging && attackCooldownLeft <= 0f && DistanceToTarget() <= attackRange)
                StartAttack();
        }

        // ── Targeting ─────────────────────────────────────────────────────

        /// <summary>Server: hand the zombie a target (a spawner aggroing a fresh zombie).</summary>
        [Server]
        public void ServerSetTarget(PlayerHealth player)
        {
            if (!body.IsDead && player != null && player.IsTargetable)
                SetTarget(player);
        }

        void IAttackerListener.ServerOnHitBy(GameObject attacker)
        {
            if (!aggroOnDamage || body.IsDead || target != null || attacker == null)
                return;

            var player = attacker.GetComponent<PlayerHealth>();
            if (player != null && player.IsTargetable)
                SetTarget(player);
        }

        private void SetTarget(PlayerHealth player)
        {
            target = player;
            thinkTimer = 0f; // plan a path this frame
        }

        private void ClearTarget()
        {
            target = null;
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
        }

        private bool ShouldKeepTarget()
        {
            // IsTargetable is false for a downed, dead or driving player: only players on foot.
            if (!target.IsTargetable)
                return false;
            return leashRadius <= 0f || DistanceToTarget() <= leashRadius;
        }

        private void UpdateIdle()
        {
            // Re-sweeping (not a trigger) also re-acquires a revived player who never left the radius.
            retargetTimer -= Time.deltaTime;
            if (retargetTimer > 0f)
                return;
            retargetTimer = retargetInterval;

            PlayerHealth nearest = null;
            float bestSqr = detectionRadius * detectionRadius;

            foreach (PlayerHealth player in ShooterDroid.ServerPlayers())
            {
                if (!player.IsTargetable)
                    continue;

                float sqr = (player.transform.position - transform.position).sqrMagnitude;
                if (sqr > bestSqr || (requireSightToDetect && !HasLineOfSight(player)))
                    continue;

                bestSqr = sqr;
                nearest = player;
            }

            if (nearest != null)
                SetTarget(nearest);
        }

        private float DistanceToTarget() => Vector3.Distance(transform.position, target.transform.position);

        private bool HasLineOfSight(PlayerHealth player)
        {
            Vector3 from = transform.position + Vector3.up * eyeHeight;
            Vector3 toTarget = player.transform.position + Vector3.up * eyeHeight - from;
            float distance = toTarget.magnitude;
            if (distance < 0.1f)
                return true;

            int count = Physics.RaycastNonAlloc(
                from, toTarget / distance, rayBuffer, distance, sightMask, QueryTriggerInteraction.Ignore);

            // The nearest solid thing that is neither a monster nor a shot decides.
            float nearest = float.MaxValue;
            Collider blocker = null;
            for (int i = 0; i < count; i++)
            {
                Collider col = rayBuffer[i].collider;
                if (rayBuffer[i].distance < nearest && IsSightBlocker(col))
                {
                    nearest = rayBuffer[i].distance;
                    blocker = col;
                }
            }
            return blocker == null || blocker.GetComponentInParent<PlayerHealth>() == player;
        }

        private static bool IsSightBlocker(Collider col)
        {
            return col.GetComponentInParent<MeleeZombie>() == null &&
                   col.GetComponentInParent<ShooterDroid>() == null &&
                   col.GetComponentInParent<EnemyProjectile>() == null;
        }

        private void Face()
        {
            Vector3 dir = target.transform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f)
                return;

            Quaternion look = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, rotationSpeed * Time.deltaTime);
        }

        // ── Attack ────────────────────────────────────────────────────────

        private void StartAttack()
        {
            attackCooldownLeft = attackCooldown;
            attackLockLeft = attackDuration;
            PlaySwing();
            RpcSwing();
        }

        [ClientRpc(includeOwner = false)]
        private void RpcSwing()
        {
            if (!isServer) // the host already played it
                PlaySwing();
        }

        private void PlaySwing()
        {
            if (animator != null && animator.enabled)
                animator.SetTrigger(AttackTrigger);
        }

        /// <summary>
        /// Animation event on the scratch clip, at the moment the claws connect. Runs on every peer
        /// that plays the clip; only the server hurts the target. Re-checks the range, because the
        /// player may have stepped away during the swing.
        /// </summary>
        public void AttackHit()
        {
            if (!isServer || body.IsDead || target == null || !target.IsTargetable)
                return;

            if (DistanceToTarget() > attackRange * hitRangeMultiplier)
                return;

            target.TakeDamage(attackDamage);
        }

        // ── Death ─────────────────────────────────────────────────────────

        private void OnServerDied()
        {
            ClearTarget();
            attackLockLeft = 0f;
            agent.enabled = false; // the ragdoll owns the bones now; nothing may move the root
        }

        private void OnServerRespawned()
        {
            agent.enabled = true;
            agent.Warp(transform.position);
            retargetTimer = 0f;
            attackCooldownLeft = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0f, 1f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 1f);
            Gizmos.DrawWireSphere(transform.position, attackRange);
            if (leashRadius > 0f)
            {
                Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 1f);
                Gizmos.DrawWireSphere(transform.position, leashRadius);
            }
        }
    }
}
