using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
using FPSGame.Combat;
using FPSGame.Config;
using FPSGame.Player;
using FPSGame.Vehicle;

namespace FPSGame.Environment
{
    public enum DriverExitBehavior { ShootPlayer, ShootCar }

    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyCarController : NetworkBehaviour, IDamageReceiver, IAttackerListener
    {
        int IDamageReceiver.CurrentHealth => currentHealth;
        int IDamageReceiver.MaxHealth => maxHealth;
        bool IDamageReceiver.IsDead => isDead;
        void IDamageReceiver.TakeDamage(int damage) => TakeDamage(damage);

        [Header("Health")]
        [Tooltip("Total weapon damage needed to destroy the car.")]
        [SerializeField, Min(1)] private int maxHealth = 300;
        [Tooltip("An idle car (no target) turns on whoever shoots it: their car if they drive, else the player on foot.")]
        [SerializeField] private bool aggroOnDamage = true;

        [Header("Destruction FX (played locally on every client)")]
        [Tooltip("Inactive children (Play On Awake) activated once when the car is destroyed — explosion, fire, " +
                 "a wreck model. The car's own renderers and colliders are hidden. Empty = none.")]
        [SerializeField] private GameObject[] destructionEffects;

        [Header("Damage FX (played locally on every client)")]
        [Tooltip("Particle systems on the car (sparks, smoke puff) restarted on every non-lethal hit. Empty = none.")]
        [SerializeField] private ParticleSystem[] damageEffects;
        [Tooltip("Minimum seconds between two damage effects — shotgun pellets and machine-gun bursts send one RPC, not one per hit.")]
        [SerializeField, Min(0f)] private float damageEffectCooldown = 0.1f;

        [Header("Wreck")]
        [Tooltip("Swapped onto every mesh slot of the car when it dies. Empty = keep the original materials.")]
        [SerializeField] private Material burntMaterial;
        [Tooltip("Inactive children with looping Play On Awake particles (fire, smoke), switched on for good when the car dies. " +
                 "State, not a one-shot: late joiners see the wreck burning too. Empty = none.")]
        [SerializeField] private GameObject[] burningEffects;
        [Tooltip("Child objects deactivated when the car dies (glass, lights, gun — whatever the wreck shouldn't show). " +
                 "Applied from the hook, so late joiners see the same wreck. Empty = none.")]
        [SerializeField] private GameObject[] hideOnDeath;
        [Tooltip("Disabled RoadBody on this object. Enabled on death: the wreck launches, lands on the road and drifts back. " +
                 "Auto-grabbed in Awake if empty.")]
        [SerializeField] private RoadBody roadBody;
        [Tooltip("Random upward launch speed (m/s), min..max.")]
        [SerializeField] private Vector2 launchUpSpeed = new(6f, 9f);
        [Tooltip("Max random sideways launch speed (m/s), either direction along world X.")]
        [SerializeField, Min(0f)] private float launchSideSpeed = 1.5f;
        [Tooltip("Max random spin (rad/s) per axis given at launch.")]
        [SerializeField, Min(0f)] private float launchSpin = 1.5f;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 8f;
        [SerializeField] private float angularSpeed = 0f;
        [SerializeField] private float acceleration = 12f;
        [Tooltip("Stop chasing once within this distance (no ramming).")]
        [SerializeField] private float stopDistance = 12f;
        [Tooltip("Re-issue SetDestination this often (seconds) while target is moving.")]
        [SerializeField] private float repathInterval = 0.25f;

        [Header("Targeting")]
        [Tooltip("After the player leaves their car, who to keep shooting at — the player on foot, or the abandoned car.")]
        [SerializeField] private DriverExitBehavior onDriverExit = DriverExitBehavior.ShootPlayer;

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

        private Rigidbody rb;
        private NavMeshAgent agent;
        private CarController currentCarTarget;
        private PlayerHealth currentPlayerTarget;
        private readonly HashSet<CarController> candidates = new();
        // Remembers the last driver of each candidate car so we can keep
        // shooting at the player on foot after they leave the seat.
        private readonly Dictionary<CarController, PlayerHealth> lastDriverByCar = new();
        private float repathCooldown;
        private float fireCooldown;
        private double nextDamageEffectTime;

        [SyncVar] private float gunYaw;

        [SyncVar(hook = nameof(OnDeadChanged))]
        private bool isDead;

        [SyncVar]
        private int currentHealth;

        // Cached while the effects are still inactive, so the burnt material never lands on them.
        private Renderer[] renderers;

        public bool IsDead => isDead;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            if (roadBody == null) roadBody = GetComponent<RoadBody>();
            // Dormant while alive: its despawn check and phase logic only make sense for the wreck.
            if (roadBody != null) roadBody.enabled = false;

            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;

            agent = GetComponent<NavMeshAgent>();
            // Body stays orientation-locked; only the gun pivot rotates.
            agent.updateRotation = false;
            agent.speed = moveSpeed;
            agent.angularSpeed = angularSpeed;
            agent.acceleration = acceleration;
            agent.stoppingDistance = stopDistance;
            agent.autoBraking = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ApplyBalance();
            currentHealth = maxHealth;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // A late joiner gets isDead without the hook firing.
            if (isDead)
                ApplyWreckState();
        }

        // Prefab values stay unless the scene has a BalanceConfig.
        [Server]
        private void ApplyBalance()
        {
            BalanceConfig config = SceneBalance.Current;
            if (config == null)
                return;

            EnemyCarBalance balance = config.enemyCar;
            maxHealth = balance.maxHealth;
            projectileDamage = balance.projectileDamage;
            projectileSpeed = balance.projectileSpeed;
        }

        private void Update()
        {
            if (!isServer || isDead)
                return;

            if (!HasValidTarget())
            {
                PickTarget();
                repathCooldown = 0f;
            }

            if (!HasTarget())
            {
                if (agent.isOnNavMesh && agent.hasPath)
                    agent.ResetPath();
                return;
            }

            UpdatePathServer();
            UpdateGunAimServer();
            TryFire();
        }

        // Called by EnemyAggroZone on the server when a player car enters its trigger.
        [Server]
        public void RegisterTarget(CarController car)
        {
            if (car == null)
                return;
            if (candidates.Add(car))
                car.ServerOnDriverExited += OnCarDriverExited;
        }

        // Called by EnemyAggroZone on the server when a player car exits its trigger.
        [Server]
        public void UnregisterTarget(CarController car)
        {
            if (car == null)
                return;
            if (candidates.Remove(car))
                car.ServerOnDriverExited -= OnCarDriverExited;
            lastDriverByCar.Remove(car);
            if (currentCarTarget == car)
                currentCarTarget = null;
        }

        [Server]
        public void TakeDamage(int damage)
        {
            if (isDead || damage <= 0)
                return;

            currentHealth = Mathf.Max(0, currentHealth - damage);
            if (currentHealth == 0)
            {
                ServerDie(); // destruction effects take over
                return;
            }

            if (damageEffects != null && damageEffects.Length > 0 && NetworkTime.time >= nextDamageEffectTime)
            {
                nextDamageEffectTime = NetworkTime.time + damageEffectCooldown;
                RpcPlayDamageEffects();
            }
        }

        // Unreliable: a dropped puff of sparks doesn't matter, and late joiners never replay it.
        [ClientRpc(channel = Channels.Unreliable)]
        private void RpcPlayDamageEffects()
        {
            if (isDead)
                return;

            foreach (ParticleSystem effect in damageEffects)
            {
                if (effect == null)
                    continue;
                // Stop emitting (live particles finish) and restart, so a burst replays on every hit.
                effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                effect.Play(true);
            }
        }

        [Server]
        private void ServerDie()
        {
            currentCarTarget = null;
            currentPlayerTarget = null;
            // Keep the chase momentum: the wreck carries on from where the car was going.
            Vector3 velocity = agent.enabled ? agent.velocity : Vector3.zero;
            if (agent.isOnNavMesh)
                agent.ResetPath();
            agent.enabled = false;

            // An RPC (not the hook) so a client joining later doesn't replay the effects.
            RpcPlayDestructionEffects();
            isDead = true;

            ServerLaunchWreck(velocity);
        }

        // From here on RoadBody owns the wreck: flight, landing, drift back, despawn.
        [Server]
        private void ServerLaunchWreck(Vector3 velocity)
        {
            if (roadBody == null)
            {
                Debug.LogWarning($"[EnemyCar] {name}: no RoadBody, the wreck stays where it died.", this);
                return;
            }

            rb.constraints = RigidbodyConstraints.None;
            rb.useGravity = true;
            roadBody.enabled = true;

            velocity.y = Random.Range(launchUpSpeed.x, launchUpSpeed.y);
            velocity.x += Random.Range(-launchSideSpeed, launchSideSpeed);
            roadBody.ServerLaunch(velocity); // makes the body dynamic
            rb.angularVelocity = Random.insideUnitSphere * launchSpin;
        }

        [ClientRpc]
        private void RpcPlayDestructionEffects()
        {
            DestructionEffects.Play(destructionEffects);
        }

        private void OnDeadChanged(bool oldValue, bool newValue)
        {
            if (newValue)
                ApplyWreckState();
        }

        private void ApplyWreckState()
        {
            // Clients run RoadBody too: its Settled phase extrapolates the wreck from RoadScroll.
            if (roadBody != null)
                roadBody.enabled = true;

            DestructionEffects.Play(burningEffects);

            if (hideOnDeath != null)
            {
                foreach (GameObject part in hideOnDeath)
                {
                    if (part != null)
                        part.SetActive(false);
                }
            }

            if (burntMaterial == null)
                return;
            foreach (Renderer r in renderers)
            {
                // Mesh renderers only — particle systems on the car keep their own materials.
                if (r is not MeshRenderer && r is not SkinnedMeshRenderer)
                    continue;
                var materials = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = burntMaterial;
                r.sharedMaterials = materials;
            }
        }

        void IAttackerListener.ServerOnHitBy(GameObject attacker)
        {
            if (!aggroOnDamage || isDead || HasValidTarget() || attacker == null)
                return;

            var player = attacker.GetComponent<PlayerHealth>();
            if (player == null || !player.IsTargetable)
                return;

            // A driver shoots with the car's machine gun; the hit reports the player, so find their car.
            CarController drivenCar = FindCarDrivenBy(player);
            if (drivenCar != null)
            {
                RegisterTarget(drivenCar);
                currentCarTarget = drivenCar;
                currentPlayerTarget = null;
            }
            else
            {
                currentCarTarget = null;
                currentPlayerTarget = player;
            }
            repathCooldown = 0f; // plan a path this frame
        }

        private CarController FindCarDrivenBy(PlayerHealth player)
        {
            foreach (var car in candidates)
            {
                if (car != null && car.GetCurrentDriverServer() == player)
                    return car;
            }

            // Shot from outside the aggro zone: rare, so a scene search is fine.
            foreach (var car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                if (car.GetCurrentDriverServer() == player)
                    return car;
            }
            return null;
        }

        private void OnCarDriverExited(CarController car, PlayerHealth exitingDriver)
        {
            // Snapshot only at the moment the driver leaves — no per-frame polling.
            if (exitingDriver != null)
                lastDriverByCar[car] = exitingDriver;
        }

        private void OnDestroy()
        {
            foreach (var car in candidates)
            {
                if (car != null)
                    car.ServerOnDriverExited -= OnCarDriverExited;
            }
        }

        private bool HasTarget() => currentCarTarget != null || currentPlayerTarget != null;

        private bool HasValidTarget()
        {
            if (currentPlayerTarget != null && currentPlayerTarget.IsTargetable)
                return true;
            if (currentCarTarget != null && currentCarTarget.VehicleIsAlive)
            {
                // Still valid only if it matches our exit-behavior expectation:
                // driver present → always valid; driver gone → only if we chose to keep shooting the car.
                if (currentCarTarget.HasDriver)
                    return true;
                if (onDriverExit == DriverExitBehavior.ShootCar)
                    return true;
            }
            return false;
        }

        private void PickTarget()
        {
            currentCarTarget = null;
            currentPlayerTarget = null;

            float bestSqr = float.PositiveInfinity;
            Vector3 myPos = transform.position;

            foreach (var car in candidates)
            {
                if (car == null || !car.VehicleIsAlive)
                    continue;

                Transform effective;
                CarController carPick = null;
                PlayerHealth playerPick = null;

                if (car.HasDriver)
                {
                    carPick = car;
                    effective = car.transform;
                }
                else if (onDriverExit == DriverExitBehavior.ShootCar)
                {
                    carPick = car;
                    effective = car.transform;
                }
                else
                {
                    // ShootPlayer mode: target the player who was driving.
                    lastDriverByCar.TryGetValue(car, out var driver);
                    if (driver == null || !driver.IsTargetable)
                        continue;
                    playerPick = driver;
                    effective = driver.transform;
                }

                float sqr = (effective.position - myPos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    currentCarTarget = carPick;
                    currentPlayerTarget = playerPick;
                }
            }
        }

        private Vector3 GetTargetPosition()
        {
            if (currentPlayerTarget != null)
                return currentPlayerTarget.transform.position + Vector3.up;
            return currentCarTarget.AimPosition;
        }

        private void UpdatePathServer()
        {
            if (!agent.isOnNavMesh)
                return;

            repathCooldown -= Time.deltaTime;
            if (repathCooldown > 0f)
                return;

            // Navigate to the car's pivot (on the ground), not its raised aim point.
            agent.SetDestination(currentPlayerTarget != null
                ? currentPlayerTarget.transform.position
                : currentCarTarget.transform.position);
            repathCooldown = repathInterval;
        }

        private void LateUpdate()
        {
            // All clients (and host) apply the synced yaw locally so remote
            // viewers see the gun track its target. A wreck's gun stays put on the tumbling body.
            if (!isDead)
                ApplyGunYaw();
        }

        private void UpdateGunAimServer()
        {
            if (gunPivot == null)
                return;

            Vector3 toTarget = GetTargetPosition() - gunPivot.position;
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

            Vector3 targetPos = GetTargetPosition();
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
