using UnityEngine;
using Mirror;

namespace FPSGame.Environment
{
    /// <summary>Lifecycle phase of a prop that has fallen onto the scrolling road.</summary>
    public enum RoadPhase
    {
        OnTruck,   // still in the trailer; MotionDirector drives it, RoadSurface does nothing
        InAir,     // detached and flying; only the airstream acts on it
        OnRoad,    // touching the road; conveyor friction + roughness act at the contact points
        Settled    // caught up to the belt; server stops replicating, clients extrapolate
    }

    /// <summary>
    /// The core of the "objects on a moving road" system. A prop that falls off the convoy
    /// lands on what is, in the truck's frame, a moving conveyor belt. From there the physics
    /// does the work on its own:
    ///
    ///  1. The contact point has a velocity relative to the belt (slip).
    ///  2. Coulomb friction opposes that slip.
    ///  3. Because friction is applied AT THE CONTACT POINT (offset from the centre of mass),
    ///     it produces a torque — so the object tumbles off whichever edge it landed on. This
    ///     is emergent; it is NOT scripted. (See road-physics-spec.md §1 and §9.)
    ///  4. Once the prop reaches belt speed the slip → 0, friction → 0, the tumble decays and
    ///     it simply rides backward. In the ground frame it has come to rest.
    ///
    /// Server-authoritative, no prediction: the object isn't player-controlled, so lag on a
    /// tumbling prop is invisible, but it is gameplay-relevant (a barrel can explode under a
    /// pursuer) so the simulation and all damage decisions stay on the server. Clients are
    /// pure visual proxies.
    ///
    /// Feel parameters live in a shared <see cref="RoadBodyData"/> profile so a family of
    /// props can be tuned in one place.
    ///
    /// Networking (road-physics-spec.md §6.2, the main optimisation): a single
    /// <see cref="NetworkTransformBase"/> (a NetworkRigidbody/NetworkTransform Unreliable
    /// component on this same object) is switched OFF while the prop is Settled and back ON in
    /// every other phase. In this Mirror build all of that component's sending and interpolation
    /// runs from Unity's Update/LateUpdate/FixedUpdate, so disabling the behaviour cleanly stops
    /// the traffic; the client then drives position analytically from <see cref="RoadScroll"/>
    /// (see the Settled branch of <see cref="Update"/>). Pooling (§6.4) and interest management
    /// (§6.5) are deferred until a runtime spawner actually churns many of these props.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RoadBody : NetworkBehaviour
    {
        [Header("Profile")]
        [Tooltip("Shared feel/tuning asset. If left empty, built-in defaults are used and a " +
                 "warning is logged.")]
        [SerializeField] private RoadBodyData profile;

        [Header("Testing")]
        [Tooltip("Start detached (InAir) on the server instead of OnTruck. Lets a standalone " +
                 "test prop dropped in the editor tumble without a trailer to launch it " +
                 "(road-physics-spec.md step 2). Leave off for real trailer-launched props.")]
        [SerializeField] private bool startDetached = false;

        [Header("Networking")]
        [Tooltip("The NetworkTransform/NetworkRigidbody (Unreliable) on this object. Enabled " +
                 "during the tumble (InAir/OnRoad), disabled while Settled so clients extrapolate " +
                 "from RoadScroll instead of receiving snapshots. Auto-grabbed in Awake if empty.")]
        [SerializeField] private NetworkTransformBase netSync;

        [Header("VFX (played locally on every client, no RPC)")]
        [Tooltip("Optional. Spawned once when the prop first slams onto the road. Should " +
                 "self-destroy (e.g. a one-shot ParticleSystem). road-physics-spec.md §6.3.")]
        [SerializeField] private GameObject landingEffectPrefab;
        [Tooltip("Optional. Spawned once when the prop settles onto the belt. Should self-destroy.")]
        [SerializeField] private GameObject settleEffectPrefab;

        // Analytic extrapolation data for the Settled phase. Declared BEFORE _phase so that, in a
        // batched SyncVar update, clients deserialize the settled pose/distance first and the
        // _phase hook (which flips replication) fires with the pose already in place.
        [SyncVar] private Vector3 _settledPos;
        [SyncVar] private Quaternion _settledRot;
        [SyncVar] private double _settledDistance;

        [SyncVar(hook = nameof(OnPhaseChanged))] private RoadPhase _phase = RoadPhase.OnTruck;

        // Cached from the profile in Awake (defaults apply when no profile is assigned).
        private float _mu = 0.6f;
        private float _maxAngularVelocity = 30f;
        private float _angularDamping = 0.1f;
        private Vector3 _centerOfMassOffset = Vector3.zero;
        private float _bumpImpulseScale = 1.0f;
        private float _dragCoefficient = 0.02f;
        private float _settleEpsilon = 0.35f;
        private int _settleFrames = 10;
        private float _despawnZ = -160f;

        private Rigidbody _rb;
        private int _settleCounter;
        private bool _contactLastStep;   // any collision touched us during the previous physics step

        public RoadPhase Phase => _phase;

        // Bullets don't shove road props: FPSF's impact impulse only ever ran on the host (client copies are
        // kinematic) and sent light props spinning. Grenade blasts still push them.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterNoImpactForce()
        {
            Akila.FPSFramework.Firearm.NoImpactForce = body => body.TryGetComponent(out RoadBody _);
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (netSync == null) netSync = GetComponent<NetworkTransformBase>();
            CacheProfile();

            _rb.maxAngularVelocity = _maxAngularVelocity;
            _rb.angularDamping = _angularDamping;
            // Offset the collider-derived centre of mass (only when asked — reading and
            // reassigning centerOfMass freezes PhysX's auto-recompute, so leave it alone at zero).
            if (_centerOfMassOffset != Vector3.zero)
                _rb.centerOfMass += _centerOfMassOffset;
            // On 25 m/s at 50 Hz the prop travels 0.5 m per step — continuous detection is
            // mandatory or it tunnels through the road (and through a pursuer at the exact
            // moment a barrel should explode). See road-physics-spec.md §7.
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private void CacheProfile()
        {
            if (profile == null)
            {
                Debug.LogWarning($"{name}: RoadBody has no RoadBodyData profile assigned; using defaults.", this);
                return;
            }

            _mu = profile.Mu;
            _maxAngularVelocity = profile.MaxAngularVelocity;
            _angularDamping = profile.AngularDamping;
            _centerOfMassOffset = profile.CenterOfMassOffset;
            _bumpImpulseScale = profile.BumpImpulseScale;
            _dragCoefficient = profile.DragCoefficient;
            _settleEpsilon = profile.SettleEpsilon;
            _settleFrames = profile.SettleFrames;
            _despawnZ = profile.DespawnZ;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // A RoadBodyRelease holds the body in place until its holders are shot away; it launches it itself.
            if (startDetached && GetComponent<RoadBodyRelease>() == null)
                SetPhase(RoadPhase.InAir);   // step-2 editor drop-test: tumble without a trailer
            ApplyReplicationMode(_phase);    // seed the initial mode (SetPhase already did it if we launched)
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!isServer)
                _rb.isKinematic = true;   // remote clients are pure proxies
            // Match the sync component to whatever phase we spawned in — a prop that spawns
            // already Settled must not have its transform fought over by an enabled NetworkTransform.
            ApplyReplicationMode(_phase);
        }

        // ---------- SERVER ----------

        [ServerCallback]
        private void FixedUpdate()
        {
            // The airstream is a headwind on AIRBORNE props only. The phase can still read
            // InAir while the body is actually resting on a non-road surface (there is no
            // "landed off-road" transition), so gate on real contact rather than on the phase
            // label — otherwise the headwind would drift a RoadBody backward on ANY surface it
            // happens to sit on. The conveyor's -Z pull comes solely from RoadSurface contact
            // (OnCollisionStay, already gated to OnRoad), never from touching something else.
            bool airborne = !_contactLastStep;
            _contactLastStep = false;   // set again by this step's collision callbacks

            if (airborne && (_phase == RoadPhase.InAir || _phase == RoadPhase.OnRoad))
                ApplyAirstream();

            // Settled included: the server body keeps drifting at belt speed (zero-friction road), so it
            // does pass despawnZ — skipping that phase left every settled prop on the server forever.
            if (transform.position.z < _despawnZ)
                Despawn();
        }

        [ServerCallback]
        private void OnCollisionStay(Collision c)
        {
            if (!enabled) return;      // collision messages reach disabled behaviours too (dormant EnemyCar wreck)
            _contactLastStep = true;   // any contact means we're resting, not airborne
            if (_phase != RoadPhase.OnRoad) return;
            if (!c.collider.TryGetComponent(out RoadSurface road)) return;

            float dt = Time.fixedDeltaTime;
            float maxSlip = 0f;

            for (int i = 0; i < c.contactCount; i++)
            {
                ContactPoint contact = c.GetContact(i);
                Vector3 n = contact.normal;

                // Normal force: use what PhysX actually resolved last step, split across contacts.
                float N = Mathf.Abs(Vector3.Dot(c.impulse, n)) / dt / c.contactCount;

                // Slip of the contact point relative to the belt, projected onto the contact plane.
                Vector3 pointVel = _rb.GetPointVelocity(contact.point);
                Vector3 beltVel = road.GetSurfaceVelocity(contact.point);
                Vector3 slip = Vector3.ProjectOnPlane(pointVel - beltVel, n);

                maxSlip = Mathf.Max(maxSlip, slip.magnitude);
                if (slip.sqrMagnitude < 1e-4f) continue;

                // Coulomb friction AT THE CONTACT POINT — this is where the tumble is born.
                Vector3 F = -slip.normalized * (_mu * N);
                _rb.AddForceAtPosition(F, contact.point, ForceMode.Force);

                // Roughness: a vertical kick, also at the contact point (→ extra torque).
                float rough = road.GetRoughness(contact.point.x, contact.point.z);
                float bump = rough * slip.magnitude * _bumpImpulseScale;
                if (bump > 0f)
                    _rb.AddForceAtPosition(n * bump, contact.point, ForceMode.Impulse);
            }

            TrySettle(maxSlip);
        }

        [Server]
        private void ApplyAirstream()
        {
            // In the truck's frame everything airborne feels a -V headwind.
            Vector3 airVel = RoadScroll.Instance != null ? RoadScroll.Instance.BeltVelocity : Vector3.zero;
            Vector3 rel = _rb.linearVelocity - airVel;
            _rb.AddForce(-rel * (rel.magnitude * _dragCoefficient), ForceMode.Force);
        }

        [Server]
        private void TrySettle(float slip)
        {
            if (slip > _settleEpsilon) { _settleCounter = 0; return; }
            if (++_settleCounter < _settleFrames) return;

            _settledPos = transform.position;
            _settledRot = transform.rotation;
            _settledDistance = RoadScroll.Instance != null ? RoadScroll.Instance.Distance : 0.0;
            SetPhase(RoadPhase.Settled);
        }

        [ServerCallback]
        private void OnCollisionEnter(Collision c)
        {
            if (!enabled) return;
            bool isRoad = c.collider.GetComponent<RoadSurface>() != null;

            // Touching the RoadSurface means the prop is on the road by definition — no matter
            // how it got here. A launched prop arrives via InAir; one that simply slid/fell off
            // the bed (or a standalone editor test prop) is still OnTruck. Both become OnRoad.
            if ((_phase == RoadPhase.InAir || _phase == RoadPhase.OnTruck) && isRoad)
            {
                SetPhase(RoadPhase.OnRoad);
            }
            // Wake-up: a pursuer rams the resting prop / an explosion nudges it.
            else if (_phase == RoadPhase.Settled && !isRoad)
            {
                _rb.isKinematic = false;
                SetPhase(RoadPhase.OnRoad);
            }
        }

        /// <summary>
        /// Called by the trailer when the prop is thrown or falls out of the cargo trigger.
        /// Server only.
        /// </summary>
        [Server]
        public void ServerLaunch(Vector3 initialVelocity)
        {
            _rb.isKinematic = false;
            _rb.linearVelocity = initialVelocity;
            SetPhase(RoadPhase.InAir);
        }

        /// <summary>
        /// Called when a player picks the prop up (<see cref="CarryableObject"/>). Leaves the road
        /// so replication is back on; kinematics stay with the carry code. Dropping or throwing it
        /// lands it on the road again through the normal InAir → OnRoad path. Server only.
        /// </summary>
        [Server]
        public void ServerLift()
        {
            SetPhase(RoadPhase.InAir);
        }

        /// <summary>
        /// Back to "on the truck": no airstream, no road friction, replication on. Used when loot is
        /// stowed in a car or returned to its vault after a missed throw. Server only.
        /// </summary>
        [Server]
        public void ServerResetToTruck()
        {
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            SetPhase(RoadPhase.OnTruck);
        }

        [Server]
        private void SetPhase(RoadPhase p)
        {
            _phase = p;
            _settleCounter = 0;
            ApplyReplicationMode(p);   // server stops/starts its own broadcast; §6.2
        }

        [Server]
        private void Despawn()
        {
            // TODO (networking pass): once a runtime spawner exists, return to the server pool
            // instead of destroying (RegisterSpawnHandler + a pool) — see road-physics-spec.md §6.4.
            NetworkServer.Destroy(gameObject);
        }

        // ---------- SHARED ----------

        /// <summary>
        /// The §6.2 two-mode toggle. Every phase but Settled replicates the transform normally;
        /// Settled disables the sync component so no snapshots flow and the client extrapolates
        /// analytically. Runs on the server (from <see cref="SetPhase"/>) and on every client
        /// (from <see cref="OnPhaseChanged"/> and the OnStart* seeds) — it just sets a bool, so
        /// calling it from several entry points is harmless and idempotent.
        /// </summary>
        private void ApplyReplicationMode(RoadPhase p)
        {
            if (netSync == null) return;
            netSync.enabled = p != RoadPhase.Settled;
        }

        // ---------- CLIENT ----------

        private void OnPhaseChanged(RoadPhase previous, RoadPhase now)
        {
            ApplyReplicationMode(now);   // §6.2: flip snapshot replication on/off for this client
            PlayPhaseVfx(previous, now); // §6.3: clients play their own effects, no per-effect RPC
        }

        /// <summary>
        /// Local, un-networked effects driven purely off the phase transition (road-physics-spec.md
        /// §6.3 — no RPC). Prefabs are optional; each should manage its own lifetime.
        /// </summary>
        private void PlayPhaseVfx(RoadPhase previous, RoadPhase now)
        {
            if (previous == RoadPhase.InAir && now == RoadPhase.OnRoad && landingEffectPrefab != null)
                Instantiate(landingEffectPrefab, transform.position, transform.rotation);
            else if (now == RoadPhase.Settled && settleEffectPrefab != null)
                Instantiate(settleEffectPrefab, transform.position, transform.rotation);
        }

        // ---------- EDITOR ----------

        private void OnDrawGizmosSelected()
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null) return;

            Vector3 offset = profile != null ? profile.CenterOfMassOffset : Vector3.zero;

            // In play mode Awake already folded the offset into rb.centerOfMass, so that IS the
            // effective COM and we subtract to recover the auto value. In edit mode rb.centerOfMass
            // is still the untouched auto value and we add the offset to preview the result.
            Vector3 autoLocal, effectiveLocal;
            if (Application.isPlaying)
            {
                effectiveLocal = rb.centerOfMass;
                autoLocal = effectiveLocal - offset;
            }
            else
            {
                autoLocal = rb.centerOfMass;
                effectiveLocal = autoLocal + offset;
            }

            Vector3 autoWorld = transform.TransformPoint(autoLocal);
            Vector3 effWorld = transform.TransformPoint(effectiveLocal);

            Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);   // collider-derived COM (reference)
            Gizmos.DrawWireSphere(autoWorld, 0.08f);

            if (offset != Vector3.zero)
                Gizmos.DrawLine(autoWorld, effWorld);

            Gizmos.color = Color.cyan;                          // effective COM used by physics
            Gizmos.DrawSphere(effWorld, 0.09f);
        }

        [ClientCallback]
        private void Update()
        {
            if (isServer || _phase != RoadPhase.Settled) return;

            // Analytic extrapolation: the prop is motionless relative to the ground while the
            // ground scrolls backward. RoadScroll.Distance is identical for everyone, so the
            // client stays frame-for-frame in step with the server with no traffic.
            if (RoadScroll.Instance == null) return;
            float dz = (float)(RoadScroll.Instance.Distance - _settledDistance);
            transform.SetPositionAndRotation(
                new Vector3(_settledPos.x, _settledPos.y, _settledPos.z - dz),
                _settledRot);
        }
    }
}
