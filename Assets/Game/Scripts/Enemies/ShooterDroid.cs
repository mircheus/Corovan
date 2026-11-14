using System.Collections.Generic;
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
    /// Walking, shooting droid built on the RagdollBot: notices a player on foot, walks toward them
    /// up to <see cref="standoffDistance"/> and fires EnemyProjectiles from a single fire point (the
    /// eyes) while it moves. Health, death and the ragdoll stay in <see cref="RagdollEnemy"/>; this
    /// component is only the brain and runs on the server. Clients just see the root move through
    /// NetworkTransform and the shot FX through an RPC.
    ///
    /// Losing a target is opt-in per droid: by distance (leash), by losing sight for a while, or
    /// never. Waking up on being shot and alerting nearby droids are optional as well.
    ///
    /// Prefab requirements:
    ///   • RagdollEnemy (+ Ragdoll, FPSFDamageable, HealthGroups on the bones)
    ///   • NetworkIdentity + NetworkTransformUnreliable (server to client)
    ///   • NavMeshAgent — a NavMesh must be baked in the scene
    ///   • firePoint child placed in front of the head, so a shot never starts inside a collider
    /// </summary>
    [RequireComponent(typeof(RagdollEnemy))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class ShooterDroid : NetworkBehaviour, IAttackerListener
    {
        public enum DroidState { Idle = 0, Chasing = 1, Attacking = 2, Dead = 3 }

        [Header("Detection")]
        [SerializeField] private float detectionRadius = 20f;
        [Tooltip("Only notice a player the droid can actually see. Off = notices through walls.")]
        [SerializeField] private bool requireSightToDetect = true;
        [Tooltip("How often an idle droid looks around for a player.")]
        [SerializeField] private float retargetInterval = 0.5f;
        [Tooltip("Height above the player's pivot the droid aims at and checks sight to.")]
        [SerializeField] private float aimHeight = 1.3f;

        [Header("Losing the target")]
        [Tooltip("Drop the target once it is farther than this. 0 = never drop it by distance.")]
        [SerializeField] private float leashRadius = 35f;
        [Tooltip("Drop the target after not seeing it for loseSightTimeout seconds.")]
        [SerializeField] private bool loseTargetOutOfSight;
        [SerializeField] private float loseSightTimeout = 5f;

        [Header("Aggro")]
        [Tooltip("An idle droid shot by a player turns on that player, wherever they stand.")]
        [SerializeField] private bool aggroOnDamage = true;
        [Tooltip("When this droid picks a target, idle droids within alertRadius take the same target.")]
        [SerializeField] private bool alertAllies;
        [SerializeField] private float alertRadius = 15f;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3.5f;
        [Tooltip("The droid stops this far from a target it can see. It never backs off a player who comes closer.")]
        [SerializeField] private float standoffDistance = 8f;
        [Tooltip("How close it walks when it has lost sight of the target, so it rounds the corner.")]
        [SerializeField] private float closeInDistance = 1.5f;
        [SerializeField] private float rotationSpeed = 8f;
        [Tooltip("How often sight is re-checked and the path re-planned.")]
        [SerializeField] private float thinkInterval = 0.2f;

        [Header("Combat")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;
        [Tooltip("Shots per second.")]
        [SerializeField] private float fireRate = 2f;
        [SerializeField] private int projectileDamage = 8;
        [SerializeField] private float projectileSpeed = 25f;
        [Tooltip("Fires only at a target closer than this.")]
        [SerializeField] private float fireRange = 20f;
        [Tooltip("Random cone around the aim direction, in degrees. 0 = dead accurate.")]
        [SerializeField] private float spreadAngle = 1.5f;
        [Tooltip("What blocks the droid's sight (and its shots).")]
        [SerializeField] private LayerMask sightMask = ~0;

        [Header("Shooting FX")]
        [SerializeField] private ParticleSystem shotEffect;

        [SyncVar] private DroidState state = DroidState.Idle;

        // Server-side roster, for alerting allies.
        private static readonly List<ShooterDroid> serverDroids = new List<ShooterDroid>();
        private static readonly RaycastHit[] rayBuffer = new RaycastHit[16];

        private RagdollEnemy body;
        private NavMeshAgent agent;
        private AgentLinkJumper linkJumper; // optional: jumps NavMesh links instead of gliding
        private Collider[] ownColliders;

        private PlayerHealth target;
        private bool hasSight;
        private float lastSeenTime;
        private float thinkTimer;
        private float retargetTimer;
        private float fireCooldown;

        public DroidState State => state;
        public bool IsDead => state == DroidState.Dead;
        public PlayerHealth Target => target;

        private void Awake()
        {
            body = GetComponent<RagdollEnemy>();
            agent = GetComponent<NavMeshAgent>();
            ownColliders = GetComponentsInChildren<Collider>(true);
            linkJumper = GetComponent<AgentLinkJumper>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ApplyBalance();
            agent.speed = moveSpeed;
            agent.updateRotation = false;
            serverDroids.Add(this);
            body.ServerDied += OnServerDied;
            body.ServerRespawned += OnServerRespawned;
        }

        // Prefab values stay unless the scene has a BalanceConfig.
        [Server]
        private void ApplyBalance()
        {
            BalanceConfig config = SceneBalance.Current;
            if (config == null)
                return;

            ShooterDroidBalance balance = config.shooterDroid;
            body.ServerSetMaxHealth(balance.maxHealth);
            projectileDamage = balance.projectileDamage;
            projectileSpeed = balance.projectileSpeed;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            serverDroids.Remove(this);
            body.ServerDied -= OnServerDied;
            body.ServerRespawned -= OnServerRespawned;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Only the server walks the droid; clients follow its NetworkTransform.
            if (!isServer)
                agent.enabled = false;
        }

        private void Update()
        {
            if (!isServer || state == DroidState.Dead)
                return;

            // Airborne on a NavMesh link: the jumper owns the body; no re-pathing, facing or shots.
            if (linkJumper != null && linkJumper.IsJumping)
                return;

            if (target != null && !ShouldKeepTarget())
                ClearTarget();

            if (target == null)
            {
                UpdateIdle();
                return;
            }

            thinkTimer -= Time.deltaTime;
            if (thinkTimer <= 0f)
            {
                thinkTimer = thinkInterval;
                Think();
            }

            Face();
            UpdateFiring();
        }

        // ── Targeting ─────────────────────────────────────────────────────

        /// <summary>Server: hand the droid a target (a spawner aggroing a fresh droid).</summary>
        [Server]
        public void ServerSetTarget(PlayerHealth player)
        {
            if (state != DroidState.Dead && player != null && player.IsTargetable)
                SetTarget(player, true);
        }

        void IAttackerListener.ServerOnHitBy(GameObject attacker)
        {
            if (!aggroOnDamage || state == DroidState.Dead || target != null || attacker == null)
                return;

            var player = attacker.GetComponent<PlayerHealth>();
            if (player != null && player.IsTargetable)
                SetTarget(player, true);
        }

        private void SetTarget(PlayerHealth player, bool alert)
        {
            target = player;
            lastSeenTime = Time.time;
            hasSight = HasLineOfSight(player);
            thinkTimer = 0f; // plan a path this frame

            if (!alert || !alertAllies)
                return;

            foreach (ShooterDroid ally in serverDroids)
            {
                if (ally != this && ally.target == null && ally.state != DroidState.Dead &&
                    (ally.transform.position - transform.position).sqrMagnitude <= alertRadius * alertRadius)
                    ally.SetTarget(player, false); // no chain: only this droid's neighbours wake
            }
        }

        private void ClearTarget()
        {
            target = null;
            hasSight = false;
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            state = DroidState.Idle;
        }

        private bool ShouldKeepTarget()
        {
            // IsTargetable is false for a downed, dead or driving player: only players on foot.
            if (!target.IsTargetable)
                return false;
            if (leashRadius > 0f &&
                (target.transform.position - transform.position).sqrMagnitude > leashRadius * leashRadius)
                return false;
            if (loseTargetOutOfSight && Time.time - lastSeenTime > loseSightTimeout)
                return false;
            return true;
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

            foreach (PlayerHealth player in ServerPlayers())
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
                SetTarget(nearest, true);
        }

        /// <summary>
        /// Server: every spawned player. Read from the connections rather than an overlap query —
        /// a pack of droids is a dozen bone colliders each and fills any fixed-size overlap buffer
        /// before the player ever shows up in it.
        /// </summary>
        public static IEnumerable<PlayerHealth> ServerPlayers()
        {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn != null && conn.identity != null &&
                    conn.identity.TryGetComponent(out PlayerHealth player))
                    yield return player;
            }
        }

        // ── Movement ──────────────────────────────────────────────────────

        private void Think()
        {
            hasSight = HasLineOfSight(target);
            if (hasSight)
                lastSeenTime = Time.time;

            if (!agent.enabled || !agent.isOnNavMesh)
                return;

            // Out of sight it walks right up, so it comes around the corner instead of parking behind it.
            agent.stoppingDistance = hasSight ? standoffDistance : closeInDistance;
            agent.isStopped = false;
            agent.SetDestination(target.transform.position);

            float distance = Vector3.Distance(transform.position, target.transform.position);
            state = hasSight && distance <= standoffDistance + 0.5f ? DroidState.Attacking : DroidState.Chasing;
        }

        private void Face()
        {
            // Looks at a target it can see, otherwise where it walks.
            Vector3 dir = hasSight ? target.transform.position - transform.position : agent.velocity;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f)
                return;

            Quaternion look = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, rotationSpeed * Time.deltaTime);
        }

        // ── Combat ────────────────────────────────────────────────────────

        private void UpdateFiring()
        {
            fireCooldown -= Time.deltaTime;
            if (fireCooldown > 0f || !hasSight)
                return;

            Vector3 aimPoint = AimPoint(target);
            if ((aimPoint - transform.position).sqrMagnitude > fireRange * fireRange)
                return;

            FireAt(aimPoint);
            fireCooldown = 1f / Mathf.Max(0.01f, fireRate);
        }

        private void FireAt(Vector3 aimPoint)
        {
            if (projectilePrefab == null || firePoint == null)
            {
                Debug.LogError($"[ShooterDroid] {name}: projectilePrefab or firePoint not assigned", this);
                return;
            }

            Vector3 direction = (aimPoint - firePoint.position).normalized;
            if (spreadAngle > 0f)
            {
                Vector2 offset = Random.insideUnitCircle * spreadAngle;
                direction = Quaternion.LookRotation(direction) * Quaternion.Euler(offset.x, offset.y, 0f) * Vector3.forward;
            }

            var obj = Instantiate(projectilePrefab, firePoint.position, Quaternion.LookRotation(direction));
            var projectile = obj.GetComponent<EnemyProjectile>();
            if (projectile != null)
            {
                projectile.Initialize(direction, projectileDamage, projectileSpeed);
                // Shots pass through every droid, this one included: they walk in a pack, and a fat
                // projectile grazes the head next to the line of fire long before a ray would.
                foreach (ShooterDroid droid in serverDroids)
                    foreach (Collider col in droid.ownColliders)
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

            // The projectile prefab owns its look; the droid only asks it for the muzzle flash.
            if (projectilePrefab != null && firePoint != null &&
                projectilePrefab.TryGetComponent(out EnemyProjectile shot) && shot.Fx != null)
                shot.Fx.PlayMuzzle(firePoint.position, direction);
        }

        private Vector3 AimPoint(PlayerHealth player) => player.transform.position + Vector3.up * aimHeight;

        // Sight is checked from the fire point, so "can see" also means "can hit".
        private bool HasLineOfSight(PlayerHealth player)
        {
            Vector3 from = firePoint != null ? firePoint.position : transform.position + Vector3.up * aimHeight;
            Vector3 toTarget = AimPoint(player) - from;
            float distance = toTarget.magnitude;
            if (distance < 0.1f)
                return true;

            int count = Physics.RaycastNonAlloc(
                from, toTarget / distance, rayBuffer, distance, sightMask, QueryTriggerInteraction.Ignore);

            // The nearest hit that a shot would not pass through decides: droids (shots go through
            // them) and shots in flight (its own rounds sit right on the line of fire) are skipped.
            float nearest = float.MaxValue;
            Collider blocker = null;
            for (int i = 0; i < count; i++)
            {
                Collider col = rayBuffer[i].collider;
                if (rayBuffer[i].distance < nearest && col.GetComponentInParent<ShooterDroid>() == null &&
                    col.GetComponentInParent<EnemyProjectile>() == null)
                {
                    nearest = rayBuffer[i].distance;
                    blocker = rayBuffer[i].collider;
                }
            }
            return blocker == null || blocker.GetComponentInParent<PlayerHealth>() == player;
        }

        // ── Death ─────────────────────────────────────────────────────────

        private void OnServerDied()
        {
            ClearTarget();
            state = DroidState.Dead;
            agent.enabled = false; // the ragdoll owns the bones now; nothing may move the root
        }

        private void OnServerRespawned()
        {
            agent.enabled = true;
            agent.Warp(transform.position);
            state = DroidState.Idle;
            retargetTimer = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0f, 1f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 1f);
            Gizmos.DrawWireSphere(transform.position, fireRange);
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
            Gizmos.DrawWireSphere(transform.position, standoffDistance);
            if (leashRadius > 0f)
            {
                Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 1f);
                Gizmos.DrawWireSphere(transform.position, leashRadius);
            }
            if (alertAllies)
            {
                Gizmos.color = new Color(1f, 0.4f, 1f, 1f);
                Gizmos.DrawWireSphere(transform.position, alertRadius);
            }
        }
    }
}
