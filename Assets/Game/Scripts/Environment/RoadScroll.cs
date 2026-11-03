using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    /// <summary>
    /// The single source of truth for how far the convoy has "travelled".
    ///
    /// The world is static (the truck stands still, the sense of motion is faked). In the
    /// truck's frame of reference the road surface moves backward at <c>-Speed</c> like a
    /// conveyor belt. Everything that depends on that illusion — the road texture scroll,
    /// the roughness field, <see cref="MotionDirector"/> shake, and <see cref="RoadBody"/>
    /// extrapolation — must read <see cref="Distance"/> here. No other script should keep its
    /// own <c>distance += V * dt</c> accumulator: that would drift between clients within
    /// minutes and desync every consumer at once.
    ///
    /// Distance is derived ANALYTICALLY from server time. Only the speed break-points are
    /// synced (a handful of SyncVars), so the value is identical on the server and every
    /// client with zero per-frame traffic.
    ///
    /// Scene singleton NetworkBehaviour (needs a NetworkIdentity), same shape as
    /// <see cref="FPSGame.Networking.ScoreManager"/>.
    /// </summary>
    public class RoadScroll : NetworkBehaviour
    {
        public static RoadScroll Instance { get; private set; }

        [Header("Initial convoy speed")]
        [Tooltip("Belt speed (m/s) the server starts with. Change at runtime only via SetSpeed.")]
        [SerializeField] private float startSpeed = 20f;

        [Header("Road visual (shader scroll)")]
        [Tooltip("Road material (e.g. Road_shader). Its _ScrollSpeedY is set once on scene start.")]
        [SerializeField] private Material roadMaterial;
        [Tooltip("Written as-is into the material's _ScrollSpeedY (UV units/sec). 0 = road texture stands still.")]
        [SerializeField] private float scrollSpeedY = -0.3f;

        private static readonly int ScrollSpeedYId = Shader.PropertyToID("_ScrollSpeedY");

        // _t0    : NetworkTime.time of the last speed change.
        // _d0    : distance already accumulated at _t0.
        // _speed : current convoy speed V (m/s).
        [SyncVar] private double _t0;
        [SyncVar] private double _d0;
        [SyncVar] private float _speed;

        /// <summary>Distance travelled by the convoy. Identical on server and all clients.</summary>
        public double Distance => _d0 + _speed * (NetworkTime.time - _t0);

        /// <summary>Current convoy speed (m/s).</summary>
        public float Speed => _speed;

        /// <summary>Belt velocity in world space (the truck's frame of reference).</summary>
        public Vector3 BeltVelocity => new Vector3(0f, 0f, -_speed);

        private void Awake()
        {
            Instance = this;
            // Every peer (host, client) applies it locally — the shader is purely visual.
            if (roadMaterial != null)
                roadMaterial.SetFloat(ScrollSpeedYId, scrollSpeedY);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Seed the analytic clock so Distance advances from t=0 without needing an
            // explicit first SetSpeed call.
            _t0 = NetworkTime.time;
            _d0 = 0.0;
            _speed = startSpeed;
        }

        /// <summary>
        /// Change the convoy speed. Server only, and only on a REAL speed change (accel,
        /// braking, scripted event) — never every frame. Locks in the accumulated distance
        /// before the break so the analytic curve stays continuous.
        /// </summary>
        [Server]
        public void SetSpeed(float newSpeed)
        {
            _d0 = Distance;              // freeze what we've accumulated BEFORE the change
            _t0 = NetworkTime.time;
            _speed = newSpeed;
        }

        /// <summary>
        /// Convert a world Z into the road's own frame ("ground Z"). The truck sits at a
        /// fixed world Z but its ground Z grows over time; a prop that has caught up to the
        /// belt has a constant ground Z. Used to sample a roughness field that scrolls with
        /// the road rather than with world space.
        /// </summary>
        public double ToGroundZ(float worldZ) => worldZ + Distance;
    }
}
