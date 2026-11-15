using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
using FPSGame.Config;
using FPSGame.Player;

namespace FPSGame.Environment
{
    /// <summary>
    /// Drives to a place it can shoot the target from, turns the gun toward it and shoots
    /// <see cref="EnemyProjectile"/>s. The original EnemyCar behaviour.
    /// <para>Shots need a clear line from <see cref="firePoint"/> (a sphere probe the size of the projectile).
    /// Movement has two layers:</para>
    /// <list type="bullet">
    /// <item><b>Firing positions</b> (<see cref="useFiringPositions"/>): every <see cref="searchInterval"/> it samples
    /// rings around where the target will be in <see cref="leadTime"/>, keeps the NavMesh points with a clear shot,
    /// and drives to the cheapest by path length + distance from <see cref="preferredRange"/>. The spot is kept
    /// relative to the target, so it follows a moving car. Staying put wins when it already has a clear shot.</item>
    /// <item><b>Chase</b> (fallback when no spot is found, or firing positions are off): drive at the target and
    /// hold once it is within <see cref="stopDistance"/> <i>and</i> in sight — never stop behind a wall.</item>
    /// </list>
    /// </summary>
    public class GunnerCarBehaviour : EnemyCarBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 8f;
        [SerializeField] private float angularSpeed = 0f;
        [SerializeField] private float acceleration = 12f;
        [Tooltip("Chase: hold position once within this distance AND the target is in sight.")]
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

        [Header("Line of Sight")]
        [Tooltip("Radius of the sight probe. Match the projectile's radius so a clear line also lets the shot through.")]
        [SerializeField] private float sightProbeRadius = 0.3f;
        [Tooltip("Seconds between line-of-sight checks from the fire point.")]
        [SerializeField] private float sightCheckInterval = 0.1f;
        [Tooltip("Chase: keep holding this long after the line closes before driving again (flicker guard).")]
        [SerializeField] private float lostSightGrace = 0.4f;
        [Tooltip("Chase: a hold ends once the target is farther than stopDistance × this.")]
        [SerializeField] private float holdReleaseFactor = 1.15f;

        [Header("Firing Positions")]
        [SerializeField] private bool useFiringPositions = true;
        [Tooltip("Seconds between searches.")]
        [SerializeField] private float searchInterval = 0.5f;
        [Tooltip("Inner ring radius, also the closest the car drives to the target while chasing (no ramming).")]
        [SerializeField] private float minFiringRange = 10f;
        [Tooltip("Outer ring radius (horizontal). Capped just inside attackRange.")]
        [SerializeField] private float maxFiringRange = 26f;
        [Tooltip("Distance to the target the car likes best.")]
        [SerializeField] private float preferredRange = 18f;
        [SerializeField, Range(1, 5)] private int ringCount = 3;
        [SerializeField, Range(4, 32)] private int samplesPerRing = 12;
        [Tooltip("Rings are centred where the target will be in this many seconds (its measured speed).")]
        [SerializeField] private float leadTime = 0.5f;
        [Tooltip("How far a ring point may snap to reach the NavMesh.")]
        [SerializeField] private float navMeshSnap = 2f;
        [Tooltip("At most this many candidates get a real NavMesh path per search (cheapest first).")]
        [SerializeField] private int pathChecks = 4;
        [Tooltip("Cost per metre of path to the spot.")]
        [SerializeField] private float pathCostWeight = 1f;
        [Tooltip("Cost per metre away from preferredRange.")]
        [SerializeField] private float rangeCostWeight = 1.5f;
        [Tooltip("Cost discount (m) of the spot already chosen, so the car doesn't flip between near-equal spots.")]
        [SerializeField] private float keepSpotBonus = 6f;
        [Tooltip("Cost discount (m) for staying put when the car already has a clear shot.")]
        [SerializeField] private float stayBonus = 8f;
        [Tooltip("The agent stops this close to its firing spot.")]
        [SerializeField] private float arriveTolerance = 1.5f;
        [Tooltip("Draw the last search's candidates (Scene view, selected): green = clear shot, red = blocked, cyan = chosen.")]
        [SerializeField] private bool drawDebug;

        [Header("Shooting FX")]
        [SerializeField] private ParticleSystem shotEffect;

        private enum Plan { Chase, Hold, Spot }

        private const float MaxTrackedTargetSpeed = 60f;

        private static readonly RaycastHit[] sightHits = new RaycastHit[32];

        private float repathCooldown;
        private float fireCooldown;
        private float sightCooldown;
        private float searchCooldown;

        private bool clearShot;
        private float lastClearShotTime = float.NegativeInfinity;

        private Plan plan;
        private bool chaseHolding;
        private Vector3 spotOffset;     // firing spot relative to the predicted target position

        private Vector3 lastTargetPos;
        private bool hasLastTargetPos;
        private Vector3 targetVelocity;

        private NavMeshPath path;

        // Search scratch, grown on demand.
        private Vector3[] candPos = new Vector3[0];
        private float[] candKey = new float[0];
        private int[] candIndex = new int[0];
        private bool[] candKept = new bool[0];

        // Debug: last search.
        private readonly List<Vector3> debugClear = new List<Vector3>();
        private readonly List<Vector3> debugBlocked = new List<Vector3>();

        [SyncVar] private float gunYaw;

        protected override void Awake()
        {
            base.Awake();
            // The controller's Awake may not have run yet, so don't go through Car.Agent here.
            var agent = GetComponent<NavMeshAgent>();
            agent.speed = moveSpeed;
            agent.angularSpeed = angularSpeed;
            agent.acceleration = acceleration;
            agent.stoppingDistance = minFiringRange;
            agent.autoBraking = true;
            path = new NavMeshPath();
        }

        public override void ServerApplyBalance(EnemyCarBalance balance)
        {
            projectileDamage = balance.projectileDamage;
            projectileSpeed = balance.projectileSpeed;
        }

        public override void ServerOnTargetChanged()
        {
            // Plan, look and measure from scratch this frame.
            repathCooldown = 0f;
            sightCooldown = 0f;
            searchCooldown = 0f;
            clearShot = false;
            lastClearShotTime = float.NegativeInfinity;
            hasLastTargetPos = false;
            targetVelocity = Vector3.zero;
            chaseHolding = false;
            SetPlan(Plan.Chase);
        }

        public override void ServerIdle()
        {
            var agent = Car.Agent;
            if (agent.isOnNavMesh && agent.hasPath)
                agent.ResetPath();
            chaseHolding = false;
            SetPlan(Plan.Chase);
        }

        public override void ServerTick()
        {
            TrackTargetVelocity();
            UpdateSight();
            UpdateMovementServer();
            UpdateGunAimServer();
            TryFire();
        }

        // ───────────────────────── Target tracking ─────────────────────────

        // The target's own velocity isn't available on the server (client-authoritative car, NetworkTransform),
        // so measure it. NetworkTransform moves it in steps, hence the smoothing.
        private void TrackTargetVelocity()
        {
            Vector3 p = Car.TargetPosition;
            float dt = Time.deltaTime;
            if (hasLastTargetPos && dt > 0f)
            {
                Vector3 v = (p - lastTargetPos) / dt;
                v.y = 0f;
                if (v.sqrMagnitude > MaxTrackedTargetSpeed * MaxTrackedTargetSpeed)
                    v = Vector3.zero; // teleport / respawn
                targetVelocity = Vector3.Lerp(targetVelocity, v, 1f - Mathf.Exp(-8f * dt));
            }
            lastTargetPos = p;
            hasLastTargetPos = true;
        }

        private Vector3 PredictedTargetPosition => Car.TargetPosition + targetVelocity * leadTime;

        // ───────────────────────── Line of sight (A) ─────────────────────────

        private Vector3 GunOrigin => firePoint != null ? firePoint.position : transform.position + Vector3.up * GunHeight;

        // Height of the fire point above the NavMesh surface under the car.
        private float GunHeight
        {
            get
            {
                float surfaceY = transform.position.y - Car.Agent.baseOffset;
                return firePoint != null ? firePoint.position.y - surfaceY : 1f;
            }
        }

        private bool SightRecent => Time.time - lastClearShotTime <= lostSightGrace;

        private void UpdateSight()
        {
            sightCooldown -= Time.deltaTime;
            if (sightCooldown > 0f)
                return;
            sightCooldown = sightCheckInterval;

            clearShot = HasClearShot(GunOrigin, Car.TargetAimPosition);
            if (clearShot)
                lastClearShotTime = Time.time;
        }

        /// <summary>
        /// True if a projectile-sized sphere gets from <paramref name="from"/> to <paramref name="to"/>, or the first
        /// thing it meets is the target (or any player). Skips this car, triggers and live projectiles — the same
        /// things <see cref="EnemyProjectile"/> flies through. Other enemy cars block: no shooting through friends.
        /// </summary>
        private bool HasClearShot(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 0.01f)
                return true;

            int n = Physics.SphereCastNonAlloc(from, sightProbeRadius, delta / dist, sightHits, dist,
                ~0, QueryTriggerInteraction.Ignore);

            Collider blocker = null;
            float nearest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Collider col = sightHits[i].collider;
                if (col == null || col.transform.IsChildOf(transform))
                    continue;
                if (col.GetComponentInParent<EnemyProjectile>() != null)
                    continue;
                if (sightHits[i].distance < nearest)
                {
                    nearest = sightHits[i].distance;
                    blocker = col;
                }
            }

            return blocker == null || IsTargetCollider(blocker);
        }

        private bool IsTargetCollider(Collider col)
        {
            var targetCar = Car.TargetCar;
            if (targetCar != null && col.transform.IsChildOf(targetCar.transform))
                return true;
            // A player in the way (target on foot, or a passenger on the target car) takes the shot too.
            return col.GetComponentInParent<PlayerHealth>() != null;
        }

        // ───────────────────────── Movement (B + C) ─────────────────────────

        private void SetPlan(Plan next)
        {
            if (plan == next)
                return;
            plan = next;
            repathCooldown = 0f;
            // Chasing stops short of the target (no ramming); a firing spot is driven onto.
            Car.Agent.stoppingDistance = next == Plan.Spot ? arriveTolerance : minFiringRange;
        }

        private void UpdateMovementServer()
        {
            var agent = Car.Agent;
            if (!agent.isOnNavMesh)
                return;

            if (useFiringPositions)
            {
                searchCooldown -= Time.deltaTime;
                // Staying put only makes sense while the shot is open; look for a new spot right away otherwise.
                if (plan == Plan.Hold && !SightRecent)
                    searchCooldown = 0f;
                if (searchCooldown <= 0f)
                {
                    searchCooldown = searchInterval;
                    SearchFiringPosition();
                }
            }
            else if (plan != Plan.Chase)
            {
                SetPlan(Plan.Chase);
            }

            switch (plan)
            {
                case Plan.Hold:
                    StopAgent();
                    break;
                case Plan.Spot:
                    DriveTo(PredictedTargetPosition + spotOffset);
                    break;
                default:
                    UpdateChaseHold();
                    if (chaseHolding)
                        StopAgent();
                    else
                        DriveTo(Car.TargetPosition);
                    break;
            }
        }

        // B: hold only with the target close enough AND in sight; a closed line sends the car on.
        private void UpdateChaseHold()
        {
            Vector3 flat = Car.TargetPosition - transform.position;
            flat.y = 0f;
            float dist = flat.magnitude;

            if (chaseHolding)
            {
                if (!SightRecent || dist > stopDistance * holdReleaseFactor)
                    chaseHolding = false;
            }
            else if (clearShot && dist <= stopDistance)
            {
                chaseHolding = true;
            }
        }

        private void DriveTo(Vector3 destination)
        {
            repathCooldown -= Time.deltaTime;
            if (repathCooldown > 0f)
                return;
            Car.Agent.SetDestination(destination);
            repathCooldown = repathInterval;
        }

        private void StopAgent()
        {
            var agent = Car.Agent;
            if (agent.hasPath || agent.pathPending)
                agent.ResetPath();
            repathCooldown = 0f;
        }

        // C: pick where to shoot from. Leaves the plan at Hold (stay here), Spot (drive to spotOffset) or Chase (none).
        private void SearchFiringPosition()
        {
            var agent = Car.Agent;
            Vector3 predicted = PredictedTargetPosition;
            Vector3 predictedAim = predicted + (Car.TargetAimPosition - Car.TargetPosition);
            Vector3 carPos = transform.position;
            float gunHeight = GunHeight;
            float carSurfaceY = carPos.y - agent.baseOffset;

            float maxR = Mathf.Min(maxFiringRange, attackRange * 0.95f);
            float minR = Mathf.Clamp(minFiringRange, 0f, maxR);
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };

            if (drawDebug)
            {
                debugClear.Clear();
                debugBlocked.Clear();
            }

            // Staying put: exact cost (no path), the bar every spot has to beat.
            float bestCost = float.PositiveInfinity;
            bool bestIsStay = false;
            Vector3 bestPos = default;

            float hereDist = FlatDistance(carPos, predicted);
            if (clearShot && hereDist >= minR && hereDist <= maxR)
            {
                bestCost = Mathf.Abs(hereDist - preferredRange) * rangeCostWeight - stayBonus;
                bestIsStay = true;
            }

            // Candidates: the spot already chosen (if any) + the rings.
            int capacity = ringCount * samplesPerRing + 1;
            EnsureCapacity(capacity);
            int count = 0;

            if (plan == Plan.Spot)
                TryAddCandidate(predicted + spotOffset, true);

            float step = 360f / samplesPerRing;
            for (int ring = 0; ring < ringCount; ring++)
            {
                float radius = Mathf.Lerp(minR, maxR, (ring + 0.5f) / ringCount);
                float phase = (ring % 2) * step * 0.5f; // stagger the rings
                for (int s = 0; s < samplesPerRing; s++)
                {
                    float a = (phase + s * step) * Mathf.Deg2Rad;
                    TryAddCandidate(predicted + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius, false);
                }
            }

            // Cheapest lower bound first: the straight line is never longer than the path.
            System.Array.Sort(candKey, candIndex, 0, count);
            int checks = 0;
            for (int k = 0; k < count && checks < pathChecks; k++)
            {
                if (candKey[k] >= bestCost)
                    break;
                int i = candIndex[k];
                checks++;

                if (!agent.CalculatePath(candPos[i], path) || path.status != NavMeshPathStatus.PathComplete)
                    continue;

                float cost = Mathf.Abs(FlatDistance(candPos[i], predicted) - preferredRange) * rangeCostWeight
                             + PathLength(path) * pathCostWeight
                             - (candKept[i] ? keepSpotBonus : 0f);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestIsStay = false;
                    bestPos = candPos[i];
                }
            }

            if (float.IsPositiveInfinity(bestCost))
            {
                SetPlan(Plan.Chase);
            }
            else if (bestIsStay)
            {
                SetPlan(Plan.Hold);
            }
            else
            {
                spotOffset = bestPos - predicted;
                SetPlan(Plan.Spot);
            }

            if (drawDebug)
                debugChosen = bestIsStay ? carPos : bestPos;

            void TryAddCandidate(Vector3 wanted, bool kept)
            {
                if (!SampleNavMesh(wanted, carSurfaceY, filter, out Vector3 snapped))
                    return;

                float d = FlatDistance(snapped, predicted);
                if (d < minR * 0.8f || d > maxR)
                    return;

                Vector3 gun = snapped + Vector3.up * gunHeight;
                if ((predictedAim - gun).sqrMagnitude > attackRange * attackRange)
                    return;

                if (!HasClearShot(gun, predictedAim))
                {
                    if (drawDebug) debugBlocked.Add(snapped);
                    return;
                }
                if (drawDebug) debugClear.Add(snapped);

                candPos[count] = snapped;
                candKept[count] = kept;
                candIndex[count] = count;
                candKey[count] = Mathf.Abs(d - preferredRange) * rangeCostWeight
                                 + Vector3.Distance(carPos, snapped) * pathCostWeight
                                 - (kept ? keepSpotBonus : 0f);
                count++;
            }
        }

        // Ring points are laid at the target's height; when the target is up on a platform, retry at the car's.
        private bool SampleNavMesh(Vector3 wanted, float fallbackY, NavMeshQueryFilter filter, out Vector3 snapped)
        {
            if (NavMesh.SamplePosition(wanted, out NavMeshHit hit, navMeshSnap, filter))
            {
                snapped = hit.position;
                return true;
            }
            wanted.y = fallbackY;
            if (NavMesh.SamplePosition(wanted, out hit, navMeshSnap, filter))
            {
                snapped = hit.position;
                return true;
            }
            snapped = default;
            return false;
        }

        private void EnsureCapacity(int capacity)
        {
            if (candPos.Length >= capacity)
                return;
            candPos = new Vector3[capacity];
            candKey = new float[capacity];
            candIndex = new int[capacity];
            candKept = new bool[capacity];
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static float PathLength(NavMeshPath p)
        {
            var corners = p.corners;
            float length = 0f;
            for (int i = 1; i < corners.Length; i++)
                length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        // ───────────────────────── Gun ─────────────────────────

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

            // A: never waste a shot into a wall.
            if (!clearShot)
                return;

            Vector3 targetPos = Car.TargetAimPosition;
            float dist = Vector3.Distance(GunOrigin, targetPos);
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

        // ───────────────────────── Gizmos ─────────────────────────

        private Vector3 debugChosen;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0f, 1f);
            Gizmos.DrawWireSphere(transform.position, attackRange);
            Gizmos.color = new Color(1f, 0f, 0f, 1f);
            Gizmos.DrawWireSphere(transform.position, stopDistance);

            if (!drawDebug || !Application.isPlaying)
                return;

            Gizmos.color = Color.green;
            foreach (var p in debugClear)
                Gizmos.DrawSphere(p + Vector3.up * 0.3f, 0.3f);
            Gizmos.color = Color.red;
            foreach (var p in debugBlocked)
                Gizmos.DrawSphere(p + Vector3.up * 0.3f, 0.2f);
            if (plan != Plan.Chase)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(debugChosen + Vector3.up * 0.5f, 1f);
                Gizmos.DrawLine(transform.position, debugChosen);
            }
        }
    }
}
