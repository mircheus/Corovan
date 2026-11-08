using System.Collections.Generic;
using UnityEngine;
using Mirror;
using FPSGame.Enemies;
using FPSGame.Player;
using FPSGame.Vehicle;

namespace FPSGame.Hazards
{
    /// <summary>
    /// A jet of fire that burns players on foot and cars inside its trigger collider, once per
    /// <see cref="damageInterval"/>. Can pulse on and off by itself (<see cref="useCycle"/>) and
    /// be driven from outside through <see cref="TurnOn"/> / <see cref="TurnOff"/>.
    /// <para>
    /// Everything is decided on the server; clients only receive <c>isBurning</c> for the visual.
    /// The collider stays enabled while the flame is out, so a car already standing in it is
    /// still tracked when the flame comes back.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class FlameHazard : NetworkBehaviour
    {
        [Header("Damage")]
        [SerializeField] private bool damagePlayers = true;
        [SerializeField] private int playerDamage = 10;
        [SerializeField] private bool damageCars = true;
        [SerializeField] private int carDamage = 10;
        [Tooltip("Seconds between hits on the same target. The first hit lands on contact, so a car driving straight through still burns.")]
        [Min(0.05f)]
        [SerializeField] private float damageInterval = 1f;

        [Header("On / Off")]
        [Tooltip("Whether the flame is lit when the level starts.")]
        [SerializeField] private bool startBurning = true;
        [Tooltip("Pulse on and off by itself. Off = the flame only changes through TurnOn / TurnOff.")]
        [SerializeField] private bool useCycle;
        [Tooltip("Seconds the flame stays lit in each cycle.")]
        [Min(0.05f)]
        [SerializeField] private float onDuration = 3f;
        [Tooltip("Seconds the flame stays out in each cycle.")]
        [Min(0.05f)]
        [SerializeField] private float offDuration = 2f;

        [Header("Visuals")]
        [Tooltip("Root of the fire effect. Keep it active: its Particle Systems (children included) are played and stopped, and what is already in the air burns out by itself.")]
        [SerializeField] private GameObject flameVisual;

        [SyncVar(hook = nameof(OnBurningChanged))]
        private bool isBurning;

        private Collider zone;
        private ParticleSystem[] flameParticles;
        private bool cycling;
        private float nextCycleSwitch;

        // Per collider, because a car's colliders enter and exit one at a time (see ExtractionZone).
        private readonly Dictionary<Collider, CarController> carColliders = new Dictionary<Collider, CarController>();

        // Earliest Time.time each target can be hit again. Kept when a target leaves, so stepping
        // out and back in can't earn an extra hit, and one entry per car covers all its colliders.
        private readonly Dictionary<Component, float> nextHitTime = new Dictionary<Component, float>();

        public bool IsBurning => isBurning;
        public bool IsCycling => cycling;

        private void Awake()
        {
            zone = GetComponent<Collider>();
            zone.isTrigger = true;
            flameParticles = flameVisual != null
                ? flameVisual.GetComponentsInChildren<ParticleSystem>(true)
                : new ParticleSystem[0];
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            isBurning = startBurning;
            if (useCycle)
                StartCycle();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Initial state, no fade: a late joiner shouldn't see a flame that went out long ago
            // die away, and Play On Awake may already have emitted something.
            ApplyVisual(isBurning, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // ── Public control ────────────────────────────────────────────────
        // Plain methods with an isServer guard rather than [Server], so they can be wired to
        // UnityEvents that fire on every peer (InteractableObject); clients simply do nothing.

        /// <summary>Lights the flame and holds it lit. Takes over from the cycle until <see cref="StartCycle"/>.</summary>
        public void TurnOn() => SetManual(true);

        /// <summary>Puts the flame out and keeps it out. Takes over from the cycle until <see cref="StartCycle"/>.</summary>
        public void TurnOff() => SetManual(false);

        /// <summary>Starts pulsing from the current state: the running phase gets its full duration.</summary>
        public void StartCycle()
        {
            if (!isServer) return;
            cycling = true;
            ScheduleNextSwitch();
        }

        /// <summary>Stops pulsing and leaves the flame as it is right now.</summary>
        public void StopCycle()
        {
            if (!isServer) return;
            cycling = false;
        }

        private void SetManual(bool burning)
        {
            if (!isServer) return;
            cycling = false;
            isBurning = burning;
        }

        // ── Server tick ───────────────────────────────────────────────────

        [ServerCallback]
        private void Update()
        {
            if (cycling && Time.time >= nextCycleSwitch)
            {
                isBurning = !isBurning;
                ScheduleNextSwitch();
            }

            if (!isBurning)
                return;

            if (damageCars)
                BurnCars();
            if (damagePlayers)
                BurnPlayers();
        }

        private void ScheduleNextSwitch()
        {
            nextCycleSwitch = Time.time + (isBurning ? onDuration : offDuration);
        }

        private void BurnCars()
        {
            foreach (CarController car in carColliders.Values)
            {
                // A destroyed car leaves a null here (its colliders never raise OnTriggerExit).
                if (car != null && car.VehicleIsAlive && ConsumeHit(car))
                    car.TakeDamage(carDamage);
            }
        }

        private void BurnPlayers()
        {
            foreach (PlayerHealth player in ShooterDroid.ServerPlayers())
            {
                // IsTargetable also skips a driver: their body is parked where they got in, the car burns instead.
                if (player.IsTargetable && IsInsideZone(player) && ConsumeHit(player))
                    player.TakeDamage(playerDamage);
            }
        }

        private bool ConsumeHit(Component target)
        {
            if (nextHitTime.TryGetValue(target, out float allowedAt) && Time.time < allowedAt)
                return false;

            nextHitTime[target] = Time.time + damageInterval;
            return true;
        }

        // Geometry instead of OnTriggerEnter: on the server another player's copy has its
        // CharacterController disabled and fires no triggers, so only the host would ever burn.
        // The disabled controller still describes the body: test spheres at the feet, middle and head.
        private bool IsInsideZone(PlayerHealth player)
        {
            if (!player.TryGetComponent(out CharacterController body))
                return false;

            Vector3 center = body.transform.TransformPoint(body.center);
            float reach = Mathf.Max(0f, body.height * 0.5f - body.radius);
            float sqrRadius = body.radius * body.radius;

            return SphereTouchesZone(center, sqrRadius)
                || SphereTouchesZone(center + Vector3.up * reach, sqrRadius)
                || SphereTouchesZone(center - Vector3.up * reach, sqrRadius);
        }

        // ClosestPoint returns the point itself when it is inside the collider.
        private bool SphereTouchesZone(Vector3 point, float sqrRadius)
        {
            return (zone.ClosestPoint(point) - point).sqrMagnitude <= sqrRadius;
        }

        // ── Car tracking ──────────────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            // Skips the car's own triggers (CargoTrigger): they reach further than the body.
            if (!NetworkServer.active || other.isTrigger)
                return;

            CarController car = other.GetComponentInParent<CarController>();
            if (car != null)
                carColliders[other] = car;
        }

        private void OnTriggerExit(Collider other)
        {
            if (NetworkServer.active)
                carColliders.Remove(other);
        }

        // ── Visual ────────────────────────────────────────────────────────

        private void OnBurningChanged(bool oldValue, bool newValue)
        {
            ApplyVisual(newValue, ParticleSystemStopBehavior.StopEmitting);
        }

        private void ApplyVisual(bool burning, ParticleSystemStopBehavior stopBehavior)
        {
            // withChildren = false: the array already holds every system in the hierarchy.
            foreach (ParticleSystem particles in flameParticles)
            {
                if (burning)
                    particles.Play(false);
                else
                    particles.Stop(false, stopBehavior);
            }
        }
    }
}
