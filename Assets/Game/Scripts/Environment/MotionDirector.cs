using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSGame.Environment
{
    // Single source of "how the convoy shakes". Does NOT move anything itself:
    // it only computes a lateral + vertical signal that every physical thing in the
    // world reads. See convoy_raid_physics_v0.md.
    //
    // Signal layers:
    //  - Idle sway  : constant low-amplitude sine, sold locally, deterministic from NetworkTime
    //                 so every peer is in phase with zero replication traffic.
    //  - Swerve     : discrete lateral lurch-and-return. Heading never changes. Server-authored,
    //                 broadcast as a timestamped event; all peers evaluate the same curve.
    //  - Bump       : discrete upward jolt. Same replication model as swerve.
    //
    // Consumers (platform tilt, camera shake, player offset) read the public signal LOCALLY.
    // Loot physics (AddForce on Rigidbodies) is applied SERVER-side only and replicates via
    // each loot object's NetworkTransform.
    public class MotionDirector : NetworkBehaviour
    {
        public static MotionDirector Instance { get; private set; }

        [Header("Lateral axis (world)")]
        [Tooltip("World-space direction of 'sideways'. Convoy travels +Z, so lateral is X by default.")]
        [SerializeField] private Vector3 lateralAxis = Vector3.right;

        [Header("Idle sway (cosmetic, deterministic)")]
        [SerializeField] private float idleFrequency = 0.25f;
        [SerializeField] private float idleAmplitude = 0.15f;

        [Header("Swerve shape")]
        [Tooltip("Evaluated over 0..1 of the swerve duration. Should start and end at 0 (lurch then return).")]
        [SerializeField] private AnimationCurve swerveCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f));
        [SerializeField] private float swerveMagnitude = 1.0f;
        [SerializeField] private float swerveDuration = 0.8f;
        [Tooltip("Warning time before a swerve actually hits. Consumers read the telegraph state during this window.")]
        [SerializeField] private float telegraphLead = 1.0f;

        [Header("Bump shape")]
        [Tooltip("Evaluated over 0..1 of the bump duration. Vertical jolt, starts and ends at 0.")]
        [SerializeField] private AnimationCurve bumpCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0f));
        [SerializeField] private float bumpMagnitude = 1.0f;
        [SerializeField] private float bumpDuration = 0.45f;

        [Header("Loot physics (server-side)")]
        [Tooltip("Force scale applied to registered loot Rigidbodies from the lateral signal.")]
        [SerializeField] private float lootForceScale = 8f;

        [Header("Fall recovery")]
        [Tooltip("Scene Transform players teleport to when a FallHazardVolume is set to the " +
                 "FixedPoint recovery mode. Assign a spawn marker in the scene.")]
        [SerializeField] private Transform fallRespawnPoint;

        // Shared scene respawn marker read by PlayerFallHazard in FixedPoint mode. Null when unset.
        public Transform FallRespawnPoint => fallRespawnPoint;

        [Header("Telegraph audio (optional, local)")]
        [Tooltip("Played on every client the moment a swerve is scheduled — the warning siren.")]
        [SerializeField] private AudioSource telegraphAudio;
        [SerializeField] private AudioClip telegraphClip;

        [Header("Auto trigger (server-side, for playtest)")]
        [SerializeField] private bool autoSwerve = true;
        [SerializeField] private float autoMinInterval = 4f;
        [SerializeField] private float autoMaxInterval = 8f;

        [Header("Debug keyboard trigger (server-side)")]
        [SerializeField] private bool debugKeys = true;

        // Active discrete events. -1 / +1 direction along lateralAxis. swerveStartTime is the
        // (possibly future) NetworkTime at which the lurch begins; during the lead window before
        // it, the swerve is "telegraphed" but the signal is still 0.
        private double swerveStartTime = double.NegativeInfinity;
        private int swerveDir;
        private double bumpStartTime = double.NegativeInfinity;

        // Fired when a swerve is scheduled (client-side), for one-shot telegraph cues
        // (siren, edge highlight). Args: direction (-1/+1), lead seconds.
        public event System.Action<int, float> OnSwerveScheduled;

        private float autoTimer;

        // Loot rigidbodies that react to the lateral field. Populated server-side.
        private readonly List<Rigidbody> reactiveLoot = new List<Rigidbody>();

        // ---- Public signal (read by local consumers) ----------------------------------

        // World-space lateral direction (normalized).
        public Vector3 LateralAxis => lateralAxis.sqrMagnitude > 0.0001f ? lateralAxis.normalized : Vector3.right;

        // Combined sideways signal (idle sway + active swerve). Sign = direction along LateralAxis.
        public float LateralSignal
        {
            get
            {
                float idle = Mathf.Sin((float)(NetworkTime.time * idleFrequency * Mathf.PI * 2.0)) * idleAmplitude;
                return idle + EvaluateSwerve();
            }
        }

        // Upward jolt signal from the active bump (0 when none).
        public float VerticalSignal => EvaluateBump();

        // ---- Telegraph state (read by consumers during the lead window) ---------------

        public float TelegraphLead => telegraphLead;

        // Direction of the pending/active swerve (-1 / +1).
        public int SwerveDir => swerveDir;

        // Seconds until the scheduled swerve begins. >0 while telegraphed, <=0 once it hits.
        public float SwerveCountdown => (float)(swerveStartTime - NetworkTime.time);

        // True during the lead window before a swerve strikes.
        public bool SwerveTelegraphed
        {
            get { float c = SwerveCountdown; return c > 0f && c <= telegraphLead; }
        }

        // 0..1 ramp across the telegraph window (0 at schedule, 1 the instant the swerve hits).
        public float TelegraphProgress
        {
            get
            {
                if (!SwerveTelegraphed || telegraphLead <= 0f)
                    return 0f;
                return Mathf.Clamp01(1f - SwerveCountdown / telegraphLead);
            }
        }

        private float EvaluateSwerve()
        {
            double t = NetworkTime.time - swerveStartTime;
            if (t < 0.0 || t > swerveDuration)
                return 0f;
            float t01 = (float)(t / swerveDuration);
            return swerveCurve.Evaluate(t01) * swerveMagnitude * swerveDir;
        }

        private float EvaluateBump()
        {
            double t = NetworkTime.time - bumpStartTime;
            if (t < 0.0 || t > bumpDuration)
                return 0f;
            float t01 = (float)(t / bumpDuration);
            return bumpCurve.Evaluate(t01) * bumpMagnitude;
        }

        // ---- Lifecycle ----------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            autoTimer = Random.Range(autoMinInterval, autoMaxInterval);
        }

        // ---- Loot registration (server-side) ------------------------------------------

        [Server]
        public void RegisterLoot(Rigidbody rb)
        {
            if (rb != null && !reactiveLoot.Contains(rb))
                reactiveLoot.Add(rb);
        }

        [Server]
        public void UnregisterLoot(Rigidbody rb)
        {
            reactiveLoot.Remove(rb);
        }

        // ---- Server-side driving ------------------------------------------------------

        private void Update()
        {
            if (!isServer)
                return;

            if (debugKeys && Keyboard.current != null)
            {
                if (Keyboard.current.jKey.wasPressedThisFrame) ServerScheduleSwerve(-1);
                if (Keyboard.current.kKey.wasPressedThisFrame) ServerScheduleSwerve(1);
                if (Keyboard.current.lKey.wasPressedThisFrame) ServerTriggerBump();
            }

            if (autoSwerve)
            {
                autoTimer -= Time.deltaTime;
                if (autoTimer <= 0f)
                {
                    ServerScheduleSwerve(Random.value < 0.5f ? -1 : 1);
                    autoTimer = Random.Range(autoMinInterval, autoMaxInterval);
                }
            }
        }

        private void FixedUpdate()
        {
            if (!isServer)
                return;

            // Apply the lateral field to free (non-carried) loot. Carried loot is kinematic
            // and simply ignored — it slides only when resting on the platform.
            float signal = LateralSignal;
            if (Mathf.Abs(signal) < 0.0001f)
                return;

            Vector3 force = LateralAxis * (signal * lootForceScale);
            for (int i = 0; i < reactiveLoot.Count; i++)
            {
                Rigidbody rb = reactiveLoot[i];
                if (rb != null && !rb.isKinematic)
                    rb.AddForce(force, ForceMode.Acceleration);
            }
        }

        // ---- Discrete event triggers (server → all clients) ---------------------------

        // Schedule a swerve telegraphLead seconds into the future. The single broadcast carries
        // both the telegraph (consumers react during the lead) and the swerve schedule itself —
        // EvaluateSwerve stays 0 until the start time arrives.
        [Server]
        public void ServerScheduleSwerve(int direction)
        {
            int dir = direction >= 0 ? 1 : -1;
            double startTime = NetworkTime.time + telegraphLead;
            // Set on the server too so server-side loot physics sees the swerve even in
            // dedicated-server mode (where the ClientRpc doesn't run locally).
            swerveStartTime = startTime;
            swerveDir = dir;
            RpcScheduleSwerve(startTime, dir);
        }

        [Server]
        public void ServerTriggerBump()
        {
            bumpStartTime = NetworkTime.time;
            RpcBump(bumpStartTime);
        }

        [ClientRpc(includeOwner = true)]
        private void RpcScheduleSwerve(double startTime, int direction)
        {
            swerveStartTime = startTime;
            swerveDir = direction;
            if (telegraphAudio != null && telegraphClip != null)
                telegraphAudio.PlayOneShot(telegraphClip);
            OnSwerveScheduled?.Invoke(direction, (float)(startTime - NetworkTime.time));
        }

        [ClientRpc(includeOwner = true)]
        private void RpcBump(double startTime)
        {
            bumpStartTime = startTime;
        }
    }
}
