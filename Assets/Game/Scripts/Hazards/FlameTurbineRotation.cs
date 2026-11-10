using System;
using UnityEngine;
using Mirror;

namespace FPSGame.Hazards
{
    /// <summary>
    /// Fire turbines in a tunnel: one <see cref="FlameHazard"/> per zone, all lit but one. The zone
    /// that rests is picked at random each phase (never the same twice in a row); between phases
    /// every turbine is out for <see cref="pauseDuration"/>. One lamp per zone shows the state:
    /// burning, safe, or blinking — "this zone lights up within <see cref="warningTime"/> seconds".
    /// <para>
    /// Started and stopped from outside (<see cref="StartRotation"/> / <see cref="StopRotation"/>,
    /// safe to wire to UnityEvents that fire on every peer). Before the start and after a stop every
    /// turbine is out.
    /// </para>
    /// <para>
    /// A <see cref="NetworkBehaviour"/>, unlike <see cref="FlameHazardController"/>: the random pick
    /// and its deadline must reach clients for the lamps. So it needs a <see cref="NetworkIdentity"/>,
    /// and the turbines (which have their own) must NOT be its children — Mirror forbids nested
    /// identities. Lamps are plain renderers and can be children.
    /// </para>
    /// </summary>
    public class FlameTurbineRotation : NetworkBehaviour
    {
        public enum Phase : byte { Idle, Burning, Pause }

        [Serializable]
        public class Lamp
        {
            [Tooltip("Lamp mesh (a primitive is fine). Its color is set through a MaterialPropertyBlock: _BaseColor / _Color, and _EmissionColor if the material has emission on.")]
            public Renderer renderer;
            [Tooltip("Optional light that takes the same color.")]
            public Light light;
        }

        private const int None = -1;

        [Tooltip("One turbine per zone. Index = zone = lamp. Not children of this object (nested NetworkIdentity). Leave their Use Cycle off.")]
        [SerializeField] private FlameHazard[] turbines = new FlameHazard[3];

        [Header("Timings (seconds)")]
        [Tooltip("Delay between StartRotation and the first ignition. Turbines are out, the lamps of the first burning zones blink.")]
        [Min(0f)]
        [SerializeField] private float startDelay = 3f;
        [Tooltip("How long the turbines burn in each phase (all zones but the resting one).")]
        [Min(0.1f)]
        [SerializeField] private float burnDuration = 6f;
        [Tooltip("Every turbine is out between phases. 0 = switch straight to the next zone.")]
        [Min(0f)]
        [SerializeField] private float pauseDuration = 2f;
        [Tooltip("A zone that is out now but lights up next starts blinking this many seconds before its ignition.")]
        [Min(0f)]
        [SerializeField] private float warningTime = 2f;

        [Header("Start")]
        [Tooltip("Start by itself once the turbines are spawned. Off = waits for StartRotation (external event).")]
        [SerializeField] private bool startAutomatically;

        [Header("Lamps (index = zone)")]
        [SerializeField] private Lamp[] lamps = new Lamp[3];
        [SerializeField] private Color burningColor = new Color(1f, 0.15f, 0.05f);
        [SerializeField] private Color safeColor = new Color(0.1f, 1f, 0.2f);
        [SerializeField] private Color warningColor = new Color(1f, 0.75f, 0f);
        [Tooltip("Second color of the warning blink.")]
        [SerializeField] private Color warningBlinkOffColor = new Color(0.1f, 0.1f, 0.1f);
        [Tooltip("Lamp color while the rotation is stopped.")]
        [SerializeField] private Color idleColor = new Color(0.1f, 0.1f, 0.1f);
        [Tooltip("Blinks per second while warning.")]
        [Min(0.1f)]
        [SerializeField] private float warningBlinkRate = 4f;
        [Tooltip("Emission multiplier (only if the lamp material has emission enabled).")]
        [Min(0f)]
        [SerializeField] private float emissionIntensity = 2f;

        // Everything the lamps need, set together on the server so one sync carries it all.
        [SyncVar] private Phase phase = Phase.Idle;
        [SyncVar] private int restingZone = None;       // out during the current Burning phase
        [SyncVar] private int nextRestingZone = None;   // out during the next Burning phase
        [SyncVar] private double phaseEndTime;          // NetworkTime
        [SyncVar] private double nextIgnitionTime;      // NetworkTime: start of the next Burning phase

        private bool turbinesReady;
        private bool startRequested;

        private MaterialPropertyBlock block;
        private Color[] shownColors;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public Phase CurrentPhase => phase;
        public int RestingZone => phase == Phase.Burning ? restingZone : None;
        public bool IsRunning => phase != Phase.Idle;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            shownColors = new Color[lamps.Length];
            for (int i = 0; i < shownColors.Length; i++)
                shownColors[i] = new Color(-1f, -1f, -1f, -1f); // forces the first apply

            if (turbines.Length < 2)
                Debug.LogWarning($"[FlameTurbineRotation] '{name}': needs at least 2 turbines.", this);
            if (lamps.Length != turbines.Length)
                Debug.LogWarning($"[FlameTurbineRotation] '{name}': {lamps.Length} lamps for {turbines.Length} turbines — lamp i shows zone i.", this);
            foreach (FlameHazard turbine in turbines)
            {
                if (turbine != null && turbine.transform.IsChildOf(transform))
                    Debug.LogError($"[FlameTurbineRotation] '{name}': turbine '{turbine.name}' is a child of this object — nested NetworkIdentity, move it out.", this);
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            startRequested = startAutomatically;
        }

        // ── Public control ────────────────────────────────────────────────
        // Plain methods with an isServer guard (not [Server]) so UnityEvents firing on every peer are fine.

        /// <summary>Starts the rotation (from the start delay). Ignored while already running.</summary>
        public void StartRotation()
        {
            if (!isServer || phase != Phase.Idle)
                return;
            // Waits in Update until the turbines are spawned: before that their TurnOn does nothing.
            startRequested = true;
        }

        /// <summary>Stops the rotation and puts every turbine out.</summary>
        public void StopRotation()
        {
            if (!isServer)
                return;

            startRequested = false;
            phase = Phase.Idle;
            restingZone = None;
            nextRestingZone = None;
            if (turbinesReady)
                SetAllOut();
        }

        /// <summary>Stop + start: a fresh random sequence from the start delay.</summary>
        public void RestartRotation()
        {
            StopRotation();
            StartRotation();
        }

        // ── Tick ──────────────────────────────────────────────────────────

        private void Update()
        {
            if (isServer)
                ServerTick();

            if (isClient)
                UpdateLamps();
        }

        private void ServerTick()
        {
            if (!turbinesReady)
            {
                if (!TurbinesSpawned())
                    return;
                turbinesReady = true;
                SetAllOut(); // everything is out until the start, whatever Start Burning says
            }

            if (startRequested)
            {
                startRequested = false;
                Begin();
                return;
            }

            if (phase == Phase.Idle || NetworkTime.time < phaseEndTime)
                return;

            // Chaining from phaseEndTime (not now) keeps the rhythm from drifting a frame per phase.
            if (phase == Phase.Burning && pauseDuration > 0f)
                EnterPause(phaseEndTime);
            else
                EnterBurning(phaseEndTime);
        }

        private bool TurbinesSpawned()
        {
            foreach (FlameHazard turbine in turbines)
            {
                if (turbine != null && !turbine.isServer)
                    return false;
            }
            return true;
        }

        private void Begin()
        {
            double now = NetworkTime.time;
            nextRestingZone = PickRestingZone(None);

            if (startDelay > 0f)
            {
                phase = Phase.Pause;
                restingZone = None;
                phaseEndTime = now + startDelay;
                nextIgnitionTime = phaseEndTime;
                SetAllOut();
            }
            else
            {
                EnterBurning(now);
            }
        }

        private void EnterBurning(double start)
        {
            restingZone = nextRestingZone;
            nextRestingZone = PickRestingZone(restingZone);
            phase = Phase.Burning;
            phaseEndTime = start + burnDuration;
            nextIgnitionTime = phaseEndTime + pauseDuration;

            for (int i = 0; i < turbines.Length; i++)
            {
                if (turbines[i] == null)
                    continue;
                // TurnOn / TurnOff also take over from a turbine's own cycle.
                if (i == restingZone)
                    turbines[i].TurnOff();
                else
                    turbines[i].TurnOn();
            }
        }

        private void EnterPause(double start)
        {
            phase = Phase.Pause;
            phaseEndTime = start + pauseDuration;
            // nextIgnitionTime already equals phaseEndTime (set when the burning phase began).
            SetAllOut();
        }

        private void SetAllOut()
        {
            foreach (FlameHazard turbine in turbines)
            {
                if (turbine != null)
                    turbine.TurnOff();
            }
        }

        // Random zone, never the one that rested last time (if there is any other choice).
        private int PickRestingZone(int previous)
        {
            int count = turbines.Length;
            if (count <= 1)
                return 0;
            if (previous < 0 || previous >= count)
                return UnityEngine.Random.Range(0, count);

            int pick = UnityEngine.Random.Range(0, count - 1);
            return pick >= previous ? pick + 1 : pick;
        }

        // ── Lamps (every client, from the SyncVars) ───────────────────────

        private void UpdateLamps()
        {
            for (int i = 0; i < lamps.Length; i++)
                ShowLamp(i, LampColor(i));
        }

        private Color LampColor(int zone)
        {
            if (phase == Phase.Idle)
                return idleColor;

            if (phase == Phase.Burning && zone != restingZone)
                return burningColor;

            // Out now. Will it light at the next ignition, and is that close?
            bool burnsNext = zone != nextRestingZone;
            double untilIgnition = nextIgnitionTime - NetworkTime.time;
            if (burnsNext && untilIgnition <= warningTime)
            {
                bool on = Mathf.Repeat(Time.time * warningBlinkRate, 1f) < 0.5f;
                return on ? warningColor : warningBlinkOffColor;
            }

            return safeColor;
        }

        private void ShowLamp(int index, Color color)
        {
            if (shownColors[index] == color)
                return;
            shownColors[index] = color;

            Lamp lamp = lamps[index];
            if (lamp == null)
                return;

            if (lamp.renderer != null)
            {
                lamp.renderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                block.SetColor(EmissionColorId, color * emissionIntensity);
                lamp.renderer.SetPropertyBlock(block);
            }

            if (lamp.light != null)
                lamp.light.color = color;
        }
    }
}
