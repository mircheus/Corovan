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

    /// <summary>
    /// Core of every enemy car: health, destruction, wreck, target registration and selection.
    /// What the car does with its target lives in an <see cref="EnemyCarBehaviour"/> on the same object.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyCarController : NetworkBehaviour, IDamageReceiver, IAttackerListener, IKnockbackReceiver
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
        [Tooltip("Seconds after death before the wreck is removed from the map (server, on every peer through Mirror). " +
                 "0 = never by timer, only RoadBody's own despawn (which doesn't fire on a road that isn't scrolling).")]
        [SerializeField, Min(0f)] private float wreckLifetime = 20f;

        [Header("Targeting")]
        [Tooltip("After the player leaves their car, who to keep targeting — the player on foot, or the abandoned car.")]
        [SerializeField] private DriverExitBehavior onDriverExit = DriverExitBehavior.ShootPlayer;

        [Header("Hunt (a player on foot shoots the car)")]
        [Tooltip("A player on foot (not a driver, not a passenger) who shoots this car becomes its target, even if it was " +
                 "busy with another car, and stays it for Hunt Duration. If they get into a car meanwhile, the car is hunted. " +
                 "Ends early when the player is downed or dead. Off on unarmed cars.")]
        [SerializeField] private bool huntOnFootAttacker = false;
        [Tooltip("Seconds the hunt lasts. Every new hit by the hunted player restarts it.")]
        [SerializeField, Min(0f)] private float huntDuration = 120f;

        [Header("Proximity aggro (in addition to EnemyAggroZone)")]
        [Tooltip("A player car closer than this (distance on the ground plane) becomes a target candidate. 0 = off.")]
        [SerializeField, Min(0f)] private float proximityAggroRadius = 0f;
        [Tooltip("A car found by proximity is forgotten again beyond this distance. Keep it above the aggro radius so the " +
                 "car doesn't flicker at the edge. Must exceed the largest gap a behaviour holds (e.g. the obstacle layer's max Z offset).")]
        [SerializeField, Min(0f)] private float proximityLoseRadius = 0f;
        [Tooltip("Seconds between two proximity scans.")]
        [SerializeField, Min(0.05f)] private float proximityScanInterval = 0.5f;

        [Header("Knockback (dash ram)")]
        [Tooltip("Multiplier on the knock speed a ram gives (heavier car = lower). 0 = the car can't be shoved.")]
        [SerializeField, Min(0f)] private float knockbackScale = 1f;
        [Tooltip("How fast the knock speed bleeds off, units/sec². Slide length ≈ speed² / (2 × decay).")]
        [SerializeField, Min(0.1f)] private float knockbackDecay = 30f;
        [Tooltip("Seconds the car stays stunned after the slide stops: no driving, no shooting, no obstacle drops.")]
        [SerializeField, Min(0f)] private float knockbackStun = 0.3f;

        private Rigidbody rb;
        private NavMeshAgent agent;
        private EnemyCarBehaviour behaviour;
        private CarController currentCarTarget;
        private PlayerHealth currentPlayerTarget;
        private Object notifiedTarget;
        private readonly HashSet<CarController> candidates = new();
        // Cars added by the proximity scan (not by a zone), the only ones the scan may remove again.
        private readonly HashSet<CarController> proximityCandidates = new();
        // Remembers the last driver of each candidate car so we can keep
        // targeting the player on foot after they leave the seat.
        private readonly Dictionary<CarController, PlayerHealth> lastDriverByCar = new();
        private float nextProximityScan;
        private double nextDamageEffectTime;
        // Server Time.timeAsDouble at which the wreck is destroyed; set on death when wreckLifetime > 0.
        private double wreckDespawnTime = double.PositiveInfinity;
        // Hunt: overrides normal target selection until huntEndTime (server Time.timeAsDouble).
        private PlayerHealth huntedPlayer;
        private CarController huntedCar;
        private double huntEndTime;
        // Knockback (server): the slide left, and when the stun ends. While knocked the behaviour isn't ticked.
        private Vector3 knockVelocity;
        private float knockEndTime;
        private bool knocked;

        [SyncVar(hook = nameof(OnDeadChanged))]
        private bool isDead;

        [SyncVar]
        private int currentHealth;

        // Cached while the effects are still inactive, so the burnt material never lands on them.
        private Renderer[] renderers;

        public bool IsDead => isDead;
        public NavMeshAgent Agent => agent;

        public bool HasTarget => currentCarTarget != null || currentPlayerTarget != null;
        public CarController TargetCar => currentCarTarget;
        public PlayerHealth TargetPlayer => currentPlayerTarget;

        /// <summary>The target's ground position — what to drive to. Only valid while <see cref="HasTarget"/>.</summary>
        public Vector3 TargetPosition => currentPlayerTarget != null
            ? currentPlayerTarget.transform.position
            : currentCarTarget.transform.position;

        /// <summary>The target's aim point — what to shoot at. Only valid while <see cref="HasTarget"/>.</summary>
        public Vector3 TargetAimPosition => currentPlayerTarget != null
            ? currentPlayerTarget.transform.position + Vector3.up
            : currentCarTarget.AimPosition;

        public float ProximityLoseRadius => Mathf.Max(proximityLoseRadius, proximityAggroRadius);

        /// <summary>Server: the car is hunting a player who shot it on foot.</summary>
        public bool IsHunting => huntedPlayer != null;

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
            // Body stays orientation-locked; a behaviour may rotate parts of it (the gun), never the root.
            agent.updateRotation = false;

            behaviour = GetComponent<EnemyCarBehaviour>();
            if (behaviour == null)
                Debug.LogError($"[EnemyCar] {name}: no EnemyCarBehaviour on the object, the car will just sit there.", this);
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

            maxHealth = config.enemyCar.maxHealth;
            if (behaviour != null)
                behaviour.ServerApplyBalance(config.enemyCar);
        }

        private void Update()
        {
            if (!isServer)
                return;

            if (isDead)
            {
                ServerTickWreck();
                return;
            }

            ProximityScan();

            if (!UpdateHunt() && !HasValidTarget())
                PickTarget();

            NotifyTargetChange();

            if (ServerTickKnockback() || behaviour == null)
                return;

            if (HasTarget)
                behaviour.ServerTick();
            else
                behaviour.ServerIdle();
        }

        // Tells the behaviour once per change of target (including "got one after having none").
        private void NotifyTargetChange()
        {
            Object current = currentPlayerTarget != null ? currentPlayerTarget : currentCarTarget;
            if (current == notifiedTarget)
                return;
            notifiedTarget = current;
            if (current != null && behaviour != null)
                behaviour.ServerOnTargetChanged();
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
            proximityCandidates.Remove(car);
            if (candidates.Remove(car))
                car.ServerOnDriverExited -= OnCarDriverExited;
            lastDriverByCar.Remove(car);
            if (currentCarTarget == car)
                currentCarTarget = null;
        }

        // Polled, not a trigger: the car keeps its band ahead of the player, so the player is never "entering" anything.
        private void ProximityScan()
        {
            if (proximityAggroRadius <= 0f || Time.time < nextProximityScan)
                return;
            nextProximityScan = Time.time + proximityScanInterval;

            float aggroSqr = proximityAggroRadius * proximityAggroRadius;
            float loseRadius = ProximityLoseRadius;
            float loseSqr = loseRadius * loseRadius;
            Vector3 myPos = transform.position;

            foreach (CarController car in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                float sqr = FlatSqrDistance(car.transform.position, myPos);
                if (car.VehicleIsAlive && sqr <= aggroSqr)
                {
                    if (!candidates.Contains(car))
                    {
                        RegisterTarget(car);
                        proximityCandidates.Add(car);
                    }
                }
                else if (sqr > loseSqr && proximityCandidates.Contains(car))
                {
                    UnregisterTarget(car);
                }
            }
        }

        /// <summary>
        /// Server: shove the car (a dash ram). It slides along <paramref name="velocity"/> (horizontal, × knockbackScale)
        /// through <c>agent.Move</c> — so it stays on the NavMesh and never passes a wall — decaying at knockbackDecay,
        /// then stays stunned for knockbackStun. A new knock replaces a weaker slide instead of adding up.
        /// </summary>
        [Server]
        public void ServerKnockback(Vector3 velocity)
        {
            if (isDead || knockbackScale <= 0f || !agent.enabled)
                return;

            velocity.y = 0f;
            velocity *= knockbackScale;
            if (velocity.sqrMagnitude > knockVelocity.sqrMagnitude)
                knockVelocity = velocity;
            if (knockVelocity.sqrMagnitude < 0.0001f)
                return;

            knockEndTime = Time.time + knockVelocity.magnitude / knockbackDecay + knockbackStun;
            knocked = true;
            // A path-following behaviour would steer against the slide: hold the agent until the stun is over.
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
        }

        // True while knocked back: the slide is applied and the behaviour must not run this frame.
        [Server]
        private bool ServerTickKnockback()
        {
            if (!knocked)
                return false;

            if (Time.time >= knockEndTime)
            {
                knocked = false;
                knockVelocity = Vector3.zero;
                if (agent.isOnNavMesh)
                    agent.isStopped = false;
                return false;
            }

            if (knockVelocity.sqrMagnitude > 0.0001f && agent.isOnNavMesh)
                agent.Move(knockVelocity * Time.deltaTime);
            knockVelocity = Vector3.MoveTowards(knockVelocity, Vector3.zero, knockbackDecay * Time.deltaTime);
            return true;
        }

        private static float FlatSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
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
            huntedPlayer = null;
            huntedCar = null;
            currentCarTarget = null;
            currentPlayerTarget = null;
            // Keep the chase momentum: the wreck carries on from where the car was going.
            Vector3 velocity = behaviour != null ? behaviour.ServerVelocity : Vector3.zero;
            // A ram that kills: the wreck flies off along the knock (applied just before the damage).
            if (knocked)
            {
                velocity = knockVelocity;
                knocked = false;
                knockVelocity = Vector3.zero;
            }
            if (agent.isOnNavMesh)
                agent.ResetPath();
            agent.enabled = false;

            if (behaviour != null)
                behaviour.ServerOnDied();

            // An RPC (not the hook) so a client joining later doesn't replay the effects.
            RpcPlayDestructionEffects();
            isDead = true;

            ServerLaunchWreck(velocity);

            if (wreckLifetime > 0f)
                wreckDespawnTime = Time.timeAsDouble + wreckLifetime;
        }

        // The wreck is a dead weight on the map: remove it once its time is up.
        [Server]
        private void ServerTickWreck()
        {
            if (Time.timeAsDouble < wreckDespawnTime)
                return;
            wreckDespawnTime = double.PositiveInfinity; // once: Destroy takes effect at the end of the frame
            NetworkServer.Destroy(gameObject);
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
            if (isDead || attacker == null)
                return;

            var player = attacker.GetComponent<PlayerHealth>();
            // Not IsTargetable: that is false for a driver, and a driver shooting the machine gun must still aggro.
            if (player == null || player.IsIncapacitated)
                return;

            // On foot = not driving, not in a passenger seat. A second player can't steal a running hunt.
            if (huntOnFootAttacker && !player.IsDriving && !IsPassenger(player)
                && (huntedPlayer == null || huntedPlayer == player))
            {
                StartHunt(player);
                return;
            }

            if (!aggroOnDamage || HasValidTarget())
                return;

            // A driver shoots with the car's machine gun; the hit reports the player, so find their car.
            CarController drivenCar = player.IsDriving ? FindCarDrivenBy(player) : null;
            if (drivenCar != null)
            {
                RegisterTarget(drivenCar);
                currentCarTarget = drivenCar;
                currentPlayerTarget = null;
            }
            else if (player.IsTargetable)
            {
                currentCarTarget = null;
                currentPlayerTarget = player;
            }
        }

        [Server]
        private void StartHunt(PlayerHealth player)
        {
            huntedPlayer = player;
            huntEndTime = Time.timeAsDouble + huntDuration;
            UpdateHunt(); // switch right away, not next frame
        }

        [Server]
        private void EndHunt()
        {
            // Drop the hunt's target too: otherwise HasValidTarget would keep chasing the player on foot forever.
            if (currentPlayerTarget == huntedPlayer)
                currentPlayerTarget = null;
            if (currentCarTarget != null && currentCarTarget == huntedCar)
                currentCarTarget = null;
            huntedPlayer = null;
            huntedCar = null;
        }

        // Points the targets at the hunted player (or the car they drive). False = no hunt, normal selection runs.
        [Server]
        private bool UpdateHunt()
        {
            if (huntedPlayer == null)
                return false;

            if (Time.timeAsDouble >= huntEndTime || huntedPlayer.IsIncapacitated)
            {
                EndHunt();
                return false;
            }

            if (huntedPlayer.IsDriving)
            {
                // Searched only when the player switches cars, not every frame.
                if (huntedCar == null || huntedCar.GetCurrentDriverServer() != huntedPlayer)
                    huntedCar = FindCarDrivenBy(huntedPlayer);
                if (huntedCar == null || !huntedCar.VehicleIsAlive)
                    return false;

                currentCarTarget = huntedCar;
                currentPlayerTarget = null;
                return true;
            }

            // On foot, or boarded as a passenger after the hunt started: the player themselves.
            huntedCar = null;
            currentCarTarget = null;
            currentPlayerTarget = huntedPlayer;
            return true;
        }

        private static bool IsPassenger(PlayerHealth player)
        {
            foreach (CarPassengerSeat seat in CarPassengerSeat.All)
            {
                if (seat != null && seat.PassengerNetId == player.netId)
                    return true;
            }
            return false;
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

        private bool HasValidTarget()
        {
            if (currentPlayerTarget != null && currentPlayerTarget.IsTargetable)
                return true;
            if (currentCarTarget != null && currentCarTarget.VehicleIsAlive)
            {
                // Still valid only if it matches our exit-behavior expectation:
                // driver present → always valid; driver gone → only if we chose to keep targeting the car.
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

        private void OnDrawGizmosSelected()
        {
            if (proximityAggroRadius <= 0f)
                return;
            Gizmos.color = new Color(1f, 0.9f, 0f, 1f);
            Gizmos.DrawWireSphere(transform.position, proximityAggroRadius);
            Gizmos.color = new Color(1f, 0.9f, 0f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, ProximityLoseRadius);
        }
    }
}
