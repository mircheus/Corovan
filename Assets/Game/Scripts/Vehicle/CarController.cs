using UnityEngine;
using Mirror;
using FPSGame.Combat;
using FPSGame.Environment;
using FPSGame.Networking;

namespace FPSGame.Vehicle
{
    /// <summary>Strategy for how the car behaves after its driver leaves the seat.</summary>
    public enum DriverlessBehavior
    {
        /// <summary>The car slowly rolls backward along its local -Z (original behavior).</summary>
        RollBackward,
        /// <summary>The car stays exactly where the driver left it.</summary>
        StayInPlace
    }

    public class CarController : NetworkBehaviour, IDamageReceiver
    {
        int IDamageReceiver.CurrentHealth => vehicleHealth;
        int IDamageReceiver.MaxHealth => maxVehicleHealth;
        bool IDamageReceiver.IsDead => !vehicleIsAlive;

        [Header("References: ")]
        [SerializeField] private Rigidbody rb;
        [Tooltip("The car's NetworkTransform. Its sync direction is flipped at runtime: ClientToServer while a driver holds authority, ServerToClient while the server drives it (idle / driverless drift / autopilot). Auto-resolved from this GameObject if left empty.")]
        [SerializeField] private NetworkTransformBase networkTransform;
        
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 15f;
        [SerializeField] private float strafeSpeed = 10f;
        [SerializeField] private float acceleration = 8f;
        [SerializeField] private float deceleration = 12f;

        [Header("Dash")]
        [Tooltip("Sideways speed (units/sec, along the car's local X) added instantly on a side dash (Shift + A/D).")]
        [SerializeField] private float dashForce = 25f;
        [Tooltip("Forward speed (units/sec, along the car's local Z) added instantly on a forward dash.")]
        [SerializeField] private float dashForwardForce = 25f;
        [Tooltip("Backward speed (units/sec, along the car's local -Z) added instantly on a backward dash.")]
        [SerializeField] private float dashBackwardForce = 25f;
        [Tooltip("How fast the dash speed bleeds off, in units/sec². Higher = shorter, snappier dash.")]
        [SerializeField] private float dashDecay = 60f;
        [Tooltip("Seconds between two dashes.")]
        [SerializeField] private float dashCooldown = 1f;
        [Tooltip("Degrees added to the driver's FOV at the start of a dash.")]
        [SerializeField] private float dashFovPunch = 8f;
        [Tooltip("Seconds the FOV punch takes to fade back out.")]
        [SerializeField] private float dashFovPunchDuration = 0.35f;

        [Header("Driverless Behavior")]
        [Tooltip("What the car does once a driver has gotten out: roll backward, or stay put.")]
        [SerializeField] private DriverlessBehavior driverlessBehavior = DriverlessBehavior.RollBackward;
        [Tooltip("Speed at which the car slowly rolls backward (local -Z) once a driver has gotten out. Only used when Driverless Behavior is RollBackward.")]
        [SerializeField] private float driverlessReverseSpeed = 2f;

        [Header("Driver")]
        [SerializeField] private Transform driverSeat;
        [SerializeField] private Transform exitPoint;
        [SerializeField] private float interactDistance = 4f;
        [Tooltip("Stand-in driver model (looping Driving animation) inside the car. Shown on every client except the driver's own while the seat is taken. Leave it inactive in the prefab.")]
        [SerializeField] private GameObject driverVisual;

        [Header("Ejection")]
        [Tooltip("Where the driver appears when thrown out through the windshield. Place it in front of and above the windshield, clear of the car's colliders. Empty = 2.5 m ahead and 1.5 m up from the car's pivot.")]
        [SerializeField] private Transform ejectPoint;
        [Tooltip("Launch speed (units/sec) along the car's forward when the driver is thrown out.")]
        [SerializeField] private float ejectForwardSpeed = 14f;
        [Tooltip("Launch speed (units/sec) straight up when the driver is thrown out.")]
        [SerializeField] private float ejectUpSpeed = 7f;

        [Header("Enemy Aim")]
        [Tooltip("Point enemies aim their shots at (e.g. the cabin / roof). Empty = the car's pivot.")]
        [SerializeField] private Transform aimPoint;

        [Header("Safe")]
        [SerializeField] private SafeMount safeMount;

        [Header("Vehicle Health")]
        [SerializeField] private int maxVehicleHealth = 200;

        [Header("Destruction FX")]
        [SerializeField] private GameObject fireEffect;
        [SerializeField] private GameObject explosionEffect;
        [SerializeField] private float explosionDelay = 2f;
        [SerializeField] private float explosionDuration = 2f;

        [Header("Collision Damage")]
        [SerializeField] private LayerMask obstacleLayers;
        [SerializeField] private float minImpactSpeed = 3f;
        [SerializeField] private float damagePerImpactSpeed = 2f;
        [SerializeField] private float collisionDamageCooldown = 0.5f;

        [Header("Obstacle Push")]
        [Tooltip("A push (see ApplyPush) keeps acting this long after its last refresh, so it survives a frame the trigger " +
                 "doesn't report the overlap. Seconds.")]
        [SerializeField, Min(0.02f)] private float pushHoldTime = 0.1f;

        [Header("Autopilot")]
        [SerializeField] private float autopilotSpeed = 8f;
        [SerializeField] private float autopilotArriveThreshold = 0.3f;

        [Header("Chaotic Movement")]
        [Tooltip("Master switch: when off, the car drives perfectly straight (no jitter at all).")]
        [SerializeField] private bool enableChaoticMovement = false;
        [Tooltip("Strength of the chaotic jitter, in units/sec added to the car's velocity.")]
        [SerializeField] private float chaosStrength = 3f;
        [Tooltip("How fast the chaotic direction changes. Low = slow drifting wobble, high = frantic twitching.")]
        [SerializeField] private float chaosFrequency = 2f;
        [Tooltip("Multiplier for the left/right (strafe) component of the jitter.")]
        [SerializeField] private float chaosStrafeScale = 1f;
        [Tooltip("Multiplier for the forward/backward component of the jitter.")]
        [SerializeField] private float chaosForwardScale = 0.5f;
        [Tooltip("When on, the car only jitters while actually moving; when off, it twitches even while standing still.")]
        [SerializeField] private bool chaosOnlyWhileMoving = true;

        [Header("Wall Blocking")]
        [Tooltip("Layers a driven car can't move through. Before each move step the car's colliders are swept along " +
                 "it (any direction: forward, sideways dash, diagonal) and the step stops at the first hit — " +
                 "MovePosition alone teleports, and depenetration can't keep up with a dash.")]
        [SerializeField] private LayerMask blockingLayers = (1 << 0) | (1 << 3) | (1 << 8) | (1 << 10); // Default, Ground, Obstacles, Floor
        [Tooltip("Gap (m) the car keeps from a blocking surface. Must stay below the contact offsets so OnCollisionEnter still fires.")]
        [SerializeField, Min(0.001f)] private float blockSkin = 0.01f;
        [Tooltip("Obstacles whose top is lower than this above the car's bottom are ignored (road seams, curbs), as are " +
                 "floor-like surfaces (normal pointing up). Measured on the obstacle's collider bounds, not the contact " +
                 "point — a flat side against a flat wall reports an arbitrary point, often at the car's bottom edge.")]
        [SerializeField, Min(0f)] private float blockStepHeight = 0.3f;
        [Tooltip("Log every sweep hit during a dash and why it was taken or skipped. For hunting a wall the car still passes.")]
        [SerializeField] private bool debugWallBlocking;

        private const float FloorNormalY = 0.7f;
        private Collider[] ownColliders;
        // Speed the car had at its last move step before a wall cancelled it — the collision damage uses it,
        // since by OnCollisionEnter the velocity into the wall is already gone.
        private float lastStepSpeed;

        private float lastCollisionDamageTime;

        [SyncVar(hook = nameof(OnDriverChanged))]
        private uint driverNetId;

        [SyncVar(hook = nameof(OnVehicleHealthHook))]
        private int vehicleHealth;

        [SyncVar]
        private bool vehicleIsAlive = true;

        [SyncVar]
        private bool autopilotActive;

        [SyncVar]
        private Vector3 autopilotTarget;

        // 0 = align on world X, 1 = align on world Z, 2 = arrived/idle
        [SyncVar]
        private int autopilotPhase;

        private Vector2 moveInput;
        private Vector3 currentVelocity;
        // Kept apart from currentVelocity so acceleration/deceleration toward the input target
        // don't eat the dash; it decays on its own at dashDecay.
        private Vector3 dashVelocity;
        private float lastDashTime = float.NegativeInfinity;
        // Set by an obstacle every physics step it overlaps the car; added to the move step on top of the driver's input.
        private Vector3 pushVelocity;
        private float pushUntil = float.NegativeInfinity;
        private float pushActiveFrom;
        private bool isExploded;
        private bool hasBeenDriven; // server-side: true once any player has entered at least once

        // Independent Perlin-noise sample offsets so the X and Z jitter never correlate.
        private float chaosSeedX;
        private float chaosSeedZ;

        public bool HasDriver => driverNetId != 0;
        public uint DriverNetId => driverNetId;

        /// <summary>Server-only: resolves the current driver's PlayerHealth, or null if seat is empty.</summary>
        public FPSGame.Player.PlayerHealth GetCurrentDriverServer()
        {
            if (driverNetId == 0) return null;
            if (!NetworkServer.spawned.TryGetValue(driverNetId, out NetworkIdentity ident)) return null;
            return ident != null ? ident.GetComponent<FPSGame.Player.PlayerHealth>() : null;
        }
        public Transform DriverSeat => driverSeat;
        public Transform ExitPoint => exitPoint;
        public Vector3 EjectPosition => ejectPoint != null
            ? ejectPoint.position
            : transform.position + transform.forward * 2.5f + Vector3.up * 1.5f;
        /// <summary>World position enemies should shoot at: the Aim Point child, else the car's pivot.</summary>
        public Vector3 AimPosition => aimPoint != null ? aimPoint.position : transform.position;
        public float InteractDistance => interactDistance;
        public float DashFovPunch => dashFovPunch;
        public float DashFovPunchDuration => dashFovPunchDuration;
        public int VehicleHealth => vehicleHealth;
        public int MaxVehicleHealth => maxVehicleHealth;

        /// <summary>Client-side (host included): vehicle health changed (old, new).</summary>
        public event System.Action<int, int> OnVehicleHealthChanged;
        public bool VehicleIsAlive => vehicleIsAlive;

        /// <summary>Toggle the chaotic jitter on/off at runtime (e.g. from a power-up or settings menu).</summary>
        public bool ChaoticMovementEnabled
        {
            get => enableChaoticMovement;
            set => enableChaoticMovement = value;
        }

        /// <summary>Strategy applied once the driver leaves the seat. Can be changed at runtime.</summary>
        public DriverlessBehavior DriverlessMode
        {
            get => driverlessBehavior;
            set => driverlessBehavior = value;
        }

        private void Awake()
        {
            // rb = GetComponent<Rigidbody>();/**/
            rb.freezeRotation = true;

            if (networkTransform == null)
                networkTransform = GetComponent<NetworkTransformBase>();

            ownColliders = GetComponentsInChildren<Collider>(true);

            chaosSeedX = Random.value * 1000f;
            chaosSeedZ = Random.value * 1000f;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            vehicleHealth = maxVehicleHealth;
            vehicleIsAlive = true;

            // Server owns the car until a driver takes the seat. ServerToClient lets the
            // server broadcast its own movement (driverless drift, autopilot) to all clients.
            ServerSetClientDriven(false);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // A late joiner gets the car with a driver already in it; don't rely on the hook alone.
            RefreshDriverVisual();
        }

        /// <summary>
        /// Server-only: switches the NetworkTransform between client-authoritative driving and
        /// server-authoritative movement. Without this, a driverless car moved by the server
        /// (RollBackward drift, autopilot) never broadcasts its position — Mirror's
        /// NetworkTransform only sends server movement in ServerToClient mode — so remote
        /// clients see the car frozen while the host sees it move.
        /// </summary>
        [Server]
        private void ServerSetClientDriven(bool clientDriven)
        {
            if (networkTransform == null)
                return;

            networkTransform.syncDirection = clientDriven
                ? SyncDirection.ClientToServer
                : SyncDirection.ServerToClient;
        }

        [Server]
        public void ServerDetachSafe()
        {
            if (safeMount != null)
                safeMount.ServerDetach();
        }

        [Server]
        public void ServerDepositLoot(CarryableObject carryable)
        {
            if (carryable == null)
                return;

            int lootValue = carryable.Value;
            NetworkServer.Destroy(carryable.gameObject);

            if (ScoreManager.Instance != null)
                ScoreManager.Instance.AddScore(lootValue);
        }

        [Server]
        public void TakeDamage(int damage)
        {
            Debug.Log($"[CAR] TakeDamage: {damage}");
            if (!vehicleIsAlive || damage <= 0)
                return;

            vehicleHealth = Mathf.Max(0, vehicleHealth - damage);
            Debug.Log($"[CAR] Health: {vehicleHealth}");
            ServerStopAutopilot();
            if (vehicleHealth <= 0)
                StartDestruction();
        }

        [Server]
        private void StartDestruction()
        {
            if (!vehicleIsAlive) return;
            vehicleIsAlive = false;
            ServerStopAutopilot();
            RpcStartFire();
            Invoke(nameof(ServerExplode), explosionDelay);
        }

        [Server]
        private void ServerExplode()
        {
            if (driverNetId != 0 &&
                NetworkServer.spawned.TryGetValue(driverNetId, out NetworkIdentity driverIdentity))
            {
                var health = driverIdentity.GetComponent<FPSGame.Player.PlayerHealth>();
                if (health != null)
                {
                    // The seat is gone with the car; clear the flag so a future respawn doesn't come
                    // back permanently untargetable.
                    health.ServerSetDriving(false);
                    health.ServerKillInstant();
                }
            }

            RpcPlayExplosion();
        }

        [ClientRpc]
        private void RpcStartFire()
        {
            if (fireEffect != null)
                fireEffect.SetActive(true);
        }

        [ClientRpc]
        private void RpcPlayExplosion()
        {
            if (fireEffect != null)
                fireEffect.SetActive(false);

            isExploded = true;
            currentVelocity = Vector3.zero;
            dashVelocity = Vector3.zero;

            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>())
                c.enabled = false;

            if (explosionEffect == null) return;
            explosionEffect.SetActive(true);
            CancelInvoke(nameof(HideExplosion));
            Invoke(nameof(HideExplosion), explosionDuration);
        }

        private void HideExplosion()
        {
            if (explosionEffect != null)
                explosionEffect.SetActive(false);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!isOwned || !HasDriver)
                return;

            if ((obstacleLayers.value & (1 << collision.gameObject.layer)) == 0)
                return;
            
            float impactSpeed = Mathf.Max(lastStepSpeed, (currentVelocity + dashVelocity).magnitude);
            Debug.Log($"impactSpeed: {impactSpeed}");
            if (impactSpeed < minImpactSpeed)
                return;

            CmdReportCollision(impactSpeed);
        }

        [Command]
        private void CmdReportCollision(float impactSpeed)
        {
            if (Time.time - lastCollisionDamageTime < collisionDamageCooldown)
                return;

            if (impactSpeed < minImpactSpeed)
                return;

            lastCollisionDamageTime = Time.time;
            int damage = Mathf.FloorToInt((impactSpeed - minImpactSpeed) * damagePerImpactSpeed);
            TakeDamage(damage);
        }

        private void FixedUpdate()
        {
            if (isExploded)
                return;

            // Movement is all MovePosition; the body is dynamic only to collide. Drop any velocity
            // physics hands it (shell casings ejected into the cabin, FPSF bullet impact force) —
            // with no drag it would keep the car creeping forever. Vertical speed stays for gravity.
            if (!rb.isKinematic)
            {
                rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
                rb.angularVelocity = Vector3.zero;
            }

            if (HasDriver)
            {
                if (!isOwned) return;

                Vector3 targetVelocity = transform.forward * (moveInput.y * moveSpeed)
                                       + transform.right * (moveInput.x * strafeSpeed);

                float rate = targetVelocity.sqrMagnitude > currentVelocity.sqrMagnitude
                    ? acceleration
                    : deceleration;

                currentVelocity = Vector3.MoveTowards(currentVelocity, targetVelocity, rate * Time.fixedDeltaTime);
                dashVelocity = Vector3.MoveTowards(dashVelocity, Vector3.zero, dashDecay * Time.fixedDeltaTime);

                Vector3 push = PushActive ? pushVelocity : Vector3.zero;
                if (push.sqrMagnitude > 0f)
                {
                    // The crash: whatever speed the car had into the obstacle is gone, and it doesn't build
                    // back up while the obstacle holds it (so the car doesn't lurch forward once released).
                    CancelVelocityAgainst(ref currentVelocity, push);
                    CancelVelocityAgainst(ref dashVelocity, push);
                }

                // Chaos is applied only to this frame's move step, never stored into
                // currentVelocity, so it can't corrupt collision-impact speed or smoothing.
                Vector3 frameVelocity = currentVelocity + dashVelocity
                                      + GetChaosOffset(currentVelocity.sqrMagnitude >= 0.01f) + push;

                lastStepSpeed = (currentVelocity + dashVelocity).magnitude;
                if (frameVelocity.sqrMagnitude > 0.001f)
                    rb.MovePosition(rb.position + SweepBlockedStep(frameVelocity * Time.fixedDeltaTime));
            }
            else if (isServer && vehicleIsAlive && PushActive)
            {
                // An empty car (driver thrown out, or never driven) is carried by the obstacle; it overrides
                // the driverless drift and the autopilot for as long as it holds the car.
                currentVelocity = Vector3.zero;
                rb.MovePosition(rb.position + pushVelocity * Time.fixedDeltaTime);
            }
            else if (isServer && autopilotActive && vehicleIsAlive)
            {
                ServerTickAutopilot();
            }
            else if (isServer && hasBeenDriven && vehicleIsAlive
                     && driverlessBehavior == DriverlessBehavior.RollBackward
                     && driverlessReverseSpeed > 0f)
            {
                // After a driver has left, the car slowly rolls backward (its local -Z) on the server.
                // Pre-driven cars stay still; new driver entering halts the drift via HasDriver branch.
                // StayInPlace does nothing here, so the car remains where the driver left it.
                Vector3 step = -transform.forward * (driverlessReverseSpeed * Time.fixedDeltaTime);
                step += GetChaosOffset(true) * Time.fixedDeltaTime;
                rb.MovePosition(rb.position + step);
            }
            else if (isServer && vehicleIsAlive && enableChaoticMovement && !chaosOnlyWhileMoving)
            {
                // Standing car (never driven, or StayInPlace): chaos still twitches it when the
                // "only while moving" restriction is off.
                rb.MovePosition(rb.position + GetChaosOffset(false) * Time.fixedDeltaTime);
            }
        }

        /// <summary>
        /// Owner-only: an obstacle is shoving the car. While it keeps calling this (every physics step it
        /// overlaps), the car's motion into the obstacle is cancelled and it is carried along at
        /// <paramref name="velocity"/>; sideways and away-from-obstacle driving still work, so the driver can
        /// steer out of it. An empty car is pushed by the server, which moves it. No-op on every other peer.
        /// <paramref name="delay"/>: seconds of uninterrupted contact before a driven car's push starts to act
        /// (an empty car is pushed at once).
        /// </summary>
        public void ApplyPush(Vector3 velocity, float delay = 0f)
        {
            bool drivenHere = isOwned && HasDriver;
            bool emptyOnServer = isServer && !HasDriver;
            if ((!drivenHere && !emptyOnServer) || !vehicleIsAlive || isExploded)
                return;

            float now = Time.time;
            if (now > pushUntil)
                pushActiveFrom = now + delay; // a fresh contact, not a continuing one
            pushVelocity = velocity;
            pushUntil = now + pushHoldTime;
        }

        // A driven car waits out the push delay (the server must get the chance to eject the driver first);
        // an empty car has nothing left to eject, so it is pushed straight away.
        private bool PushActive => Time.time <= pushUntil && (!HasDriver || Time.time >= pushActiveFrom);

        /// <summary>
        /// Owner: cuts a move step at the first wall along it. The car's colliders are swept along the step
        /// (whatever its direction), the step stops <see cref="blockSkin"/> short of the hit, and the drive and
        /// dash speed into the wall are dropped — so the next steps slide along it instead of pressing in.
        /// </summary>
        private Vector3 SweepBlockedStep(Vector3 step)
        {
            float distance = step.magnitude;
            if (distance < 0.0001f)
                return step;

            Vector3 dir = step / distance;
            RaycastHit[] hits = rb.SweepTestAll(dir, distance + blockSkin, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
                return step;

            float bottom = float.PositiveInfinity;
            foreach (var c in ownColliders)
                if (c != null && c.enabled && !c.isTrigger && c.attachedRigidbody == rb)
                    bottom = Mathf.Min(bottom, c.bounds.min.y);
            float minTopY = bottom + blockStepHeight;
            bool log = debugWallBlocking && dashVelocity.sqrMagnitude > 0.01f;

            bool blocked = false;
            RaycastHit nearest = default;
            foreach (var hit in hits)
            {
                string skip = null;
                if (hit.collider == null || (blockingLayers.value & (1 << hit.collider.gameObject.layer)) == 0)
                    skip = "layer not in blockingLayers";
                else if (hit.collider.transform.IsChildOf(transform))
                    skip = "own child";
                else if (hit.normal.y > FloorNormalY)
                    skip = $"floor-like normal {hit.normal}";
                else if (hit.collider.bounds.max.y < minTopY)
                    skip = $"low obstacle (top {hit.collider.bounds.max.y:F2} < {minTopY:F2})"; // seams and curbs

                if (log)
                    Debug.Log($"[CAR] sweep hit '{(hit.collider != null ? hit.collider.name : "null")}' " +
                              $"d={hit.distance:F3}/{distance:F3} n={hit.normal} → {skip ?? "BLOCK"}", hit.collider);
                if (skip != null)
                    continue;

                if (!blocked || hit.distance < nearest.distance)
                {
                    nearest = hit;
                    blocked = true;
                }
            }

            if (!blocked)
                return step;

            Vector3 wallNormal = new Vector3(nearest.normal.x, 0f, nearest.normal.z).normalized;
            CancelVelocityAgainst(ref currentVelocity, wallNormal);
            CancelVelocityAgainst(ref dashVelocity, wallNormal);

            return dir * Mathf.Max(0f, nearest.distance - blockSkin);
        }

        // Removes the part of v that points against `direction` (into whatever moves along it).
        private static void CancelVelocityAgainst(ref Vector3 v, Vector3 direction)
        {
            Vector3 d = direction.normalized;
            float along = Vector3.Dot(v, d);
            if (along < 0f)
                v -= d * along;
        }

        /// <summary>
        /// Returns a chaotic per-frame velocity perturbation built from Perlin noise.
        /// Smoothly wandering at low <see cref="chaosFrequency"/>, frantically twitchy at high values.
        /// Returns zero when disabled, so a driver-controlled car drives perfectly straight.
        /// </summary>
        private Vector3 GetChaosOffset(bool isMoving)
        {
            if (!enableChaoticMovement || chaosStrength <= 0f)
                return Vector3.zero;

            if (chaosOnlyWhileMoving && !isMoving)
                return Vector3.zero;

            float t = Time.time * chaosFrequency;
            // Perlin noise is 0..1; remap to -1..1 so the jitter swings both ways.
            float noiseX = Mathf.PerlinNoise(chaosSeedX, t) * 2f - 1f;
            float noiseZ = Mathf.PerlinNoise(chaosSeedZ, t) * 2f - 1f;

            Vector3 offset = transform.right * (noiseX * chaosStrafeScale)
                           + transform.forward * (noiseZ * chaosForwardScale);

            return offset * chaosStrength;
        }

        [Server]
        private void ServerTickAutopilot()
        {
            Vector3 pos = rb.position;
            Vector3 step;

            if (autopilotPhase == 0)
            {
                float dx = autopilotTarget.x - pos.x;
                if (Mathf.Abs(dx) <= autopilotArriveThreshold)
                {
                    autopilotPhase = 1;
                    currentVelocity = Vector3.zero;
                    return;
                }
                step = new Vector3(Mathf.Sign(dx), 0f, 0f) * (autopilotSpeed * Time.fixedDeltaTime);
            }
            else
            {
                float dz = autopilotTarget.z - pos.z;
                float dx = autopilotTarget.x - pos.x;
                if (new Vector2(dx, dz).sqrMagnitude <= autopilotArriveThreshold * autopilotArriveThreshold)
                {
                    autopilotActive = false;
                    autopilotPhase = 2;
                    currentVelocity = Vector3.zero;
                    return;
                }
                step = new Vector3(0f, 0f, Mathf.Sign(dz)) * (autopilotSpeed * Time.fixedDeltaTime);
            }

            currentVelocity = step / Time.fixedDeltaTime;
            rb.MovePosition(pos + step + GetChaosOffset(true) * Time.fixedDeltaTime);
        }

        [Server]
        public void ServerStartAutopilot(Vector3 worldPoint)
        {
            if (HasDriver) return;
            if (!vehicleIsAlive) return;

            autopilotTarget = worldPoint;
            autopilotPhase = 0;
            autopilotActive = true;
        }

        [Server]
        public void ServerStopAutopilot()
        {
            if (!autopilotActive) return;
            autopilotActive = false;
            autopilotPhase = 2;
            currentVelocity = Vector3.zero;
        }

        [Server]
        public void ServerHalt()
        {
            ServerStopAutopilot();
            hasBeenDriven = false;
            currentVelocity = Vector3.zero;
            moveInput = Vector2.zero;
        }

        public void SetMoveInput(Vector2 input)
        {
            moveInput = input;
        }

        /// <summary>
        /// Owner-only: dash along one of the car's local axes, picked from <paramref name="direction"/>
        /// (the move input): the dominant axis wins, a tie goes sideways. X = sideways (dashForce),
        /// +Y = forward (dashForwardForce), -Y = backward (dashBackwardForce); all share one cooldown.
        /// The driver moves the car with client authority, so no server round trip — NetworkTransform
        /// syncs the result. Returns false while on cooldown, so the caller knows whether to play the FOV punch.
        /// </summary>
        public bool TryDash(Vector2 direction)
        {
            if (!isOwned || !HasDriver || !vehicleIsAlive || isExploded)
                return false;

            if (direction.sqrMagnitude < 0.0001f || Time.time - lastDashTime < dashCooldown)
                return false;

            Vector3 dash = Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
                ? transform.right * (Mathf.Sign(direction.x) * dashForce)
                : direction.y > 0f
                    ? transform.forward * dashForwardForce
                    : -transform.forward * dashBackwardForce;

            if (dash.sqrMagnitude < 0.0001f)
                return false; // that direction's force is set to 0 = disabled

            lastDashTime = Time.time;
            dashVelocity = dash;
            CmdReportDash();
            return true;
        }

        /// <summary>Server: Time.time of the last dash the driver reported. The dash itself runs on the owner
        /// only, so this is how server-side rules (DecryptParkingZone) learn about it.</summary>
        public float ServerLastDashTime { get; private set; } = float.NegativeInfinity;

        /// <summary>Seconds the longest dash keeps pushing the car (its speed bleeding off at dashDecay).</summary>
        public float DashDuration => dashDecay > 0f
            ? Mathf.Max(dashForce, dashForwardForce, dashBackwardForce) / dashDecay
            : 0f;

        [Command]
        private void CmdReportDash()
        {
            ServerLastDashTime = Time.time;
        }

        [Server]
        public void ServerEnter(NetworkConnectionToClient conn, NetworkIdentity player)
        {
            if (HasDriver)
            {
                return;
            }

            ServerStopAutopilot();
            netIdentity.AssignClientAuthority(conn);
            ServerSetClientDriven(true);
            driverNetId = player.netId;
            hasBeenDriven = true;

            // Enemies must stop treating the driver as a target on foot the moment they take the
            // seat — see PlayerHealth.IsTargetable.
            var driverHealth = player.GetComponent<FPSGame.Player.PlayerHealth>();
            if (driverHealth != null)
                driverHealth.ServerSetDriving(true);

            var tool = player.GetComponent<FPSGame.Player.RemoteVehicleControlTool>();
            if (tool != null)
                tool.ServerSetBoundCar(netId);
        }

        /// <summary>Server-only: fires when the driver leaves the seat, with the PlayerHealth that just exited.</summary>
        public event System.Action<CarController, FPSGame.Player.PlayerHealth> ServerOnDriverExited;

        [Server]
        public void ServerExit()
        {
            if (!HasDriver)
                return;

            uint oldDriverId = driverNetId;
            var exitingDriver = GetCurrentDriverServer();
            if (exitingDriver != null)
                exitingDriver.ServerSetDriving(false);
            driverNetId = 0;
            moveInput = Vector2.zero;
            currentVelocity = Vector3.zero;
            netIdentity.RemoveClientAuthority();
            // Hand movement authority back to the server so the driverless drift / autopilot
            // it performs from here on actually broadcasts to remote clients.
            ServerSetClientDriven(false);

            ServerOnDriverExited?.Invoke(this, exitingDriver);
        }

        /// <summary>Server-only: throws the driver out through the windshield with this car's Ejection settings.</summary>
        [Server]
        public bool ServerEjectDriver() => ServerEjectDriver(ejectForwardSpeed, ejectUpSpeed);

        /// <summary>
        /// Server-only: empties the seat and throws the driver forward/up on their own client.
        /// Returns false when nobody is driving.
        /// </summary>
        [Server]
        public bool ServerEjectDriver(float forwardSpeed, float upSpeed)
        {
            var driver = GetCurrentDriverServer();
            if (driver == null)
                return false;

            var input = driver.GetComponent<FPSGame.Player.PlayerInputHandler>();
            ServerExit();
            if (input != null)
                input.ServerEjectFromVehicle(this, forwardSpeed, upSpeed);
            return true;
        }

        private void OnVehicleHealthHook(int oldHealth, int newHealth)
        {
            OnVehicleHealthChanged?.Invoke(oldHealth, newHealth);
        }

        private void OnDriverChanged(uint oldId, uint newId)
        {
            // A dash left over from the previous driver must not shove the car for the next one.
            dashVelocity = Vector3.zero;

            RefreshDriverVisual();

            if (newId != 0)
            {
                // Driver entered
                if (NetworkClient.spawned.TryGetValue(newId, out NetworkIdentity driverIdentity))
                {
                    SetDriverVisible(driverIdentity.gameObject, false);
                }
            }
            else if (oldId != 0)
            {
                // Driver exited
                if (NetworkClient.spawned.TryGetValue(oldId, out NetworkIdentity driverIdentity))
                {
                    // Place the driver at the exit point BEFORE re-enabling its CharacterController.
                    // An enabled CharacterController caches its own position and overrides transform
                    // writes on its next internal step, snapping the player back to wherever the CC
                    // was disabled (the spot they entered the car) — a teleport to the entry point.
                    // Setting the position while the CC is still off (vehicle mode) makes it adopt
                    // the new spot when SetDriverVisible re-enables it.
                    // An owner who has already left the seat locally placed themselves (exit point, or
                    // the eject point mid-flight) — moving them again here would yank them back.
                    var input = driverIdentity.GetComponent<FPSGame.Player.PlayerInputHandler>();
                    bool placedByOwner = driverIdentity.isOwned && input != null && !input.IsInVehicle;
                    if (!placedByOwner)
                    {
                        if (exitPoint != null)
                            driverIdentity.transform.SetPositionAndRotation(exitPoint.position, exitPoint.rotation);
                        else
                            driverIdentity.transform.position = transform.position + transform.right * 2.5f;
                    }

                    SetDriverVisible(driverIdentity.gameObject, true);
                }
            }
        }

        // The driver sees the car from first person, so the stand-in is everyone else's view only.
        // Compared by netId, not by looking the driver up in NetworkClient.spawned: on a late joiner
        // the driver's player object may not be spawned yet when the car is.
        private void RefreshDriverVisual()
        {
            if (driverVisual == null)
                return;

            var localPlayer = NetworkClient.localPlayer;
            bool localIsDriver = localPlayer != null && localPlayer.netId == driverNetId;
            driverVisual.SetActive(driverNetId != 0 && !localIsDriver);
        }

        private void OnDrawGizmosSelected()
        {
            if (aimPoint == null)
                return;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(aimPoint.position, 0.3f);
        }

        private void SetDriverVisible(GameObject driver, bool visible)
        {
            var characterController = driver.GetComponent<CharacterController>();
            if (characterController != null)
                characterController.enabled = visible;

            var playerController = driver.GetComponent<FPSGame.Player.PlayerController>();
            if (playerController != null)
                playerController.SetVehicleMode(!visible);

            // Hide/show renderers on driver. Weapons are put away while driving (PlayerLoadout), and the
            // FPSF Inventory is left alone: attachment meshes there are switched off on purpose, and
            // re-enabling every renderer on exit would turn them all back on.
            var inventory = driver.GetComponentInChildren<Akila.FPSFramework.Inventory>(true);
            Transform weapons = inventory != null ? inventory.transform : null;

            foreach (var renderer in driver.GetComponentsInChildren<Renderer>())
            {
                if (weapons != null && renderer.transform.IsChildOf(weapons))
                    continue;

                renderer.enabled = visible;
            }
        }
    }
}
