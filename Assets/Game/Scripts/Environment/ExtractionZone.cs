using System.Collections.Generic;
using FPSGame.Networking;
using FPSGame.Vehicle;
using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    /// <summary>
    /// Win condition: park the convoy car — with a live driver aboard — inside this trigger and
    /// hold it there for <see cref="captureDuration"/> continuous seconds.
    /// <para>
    /// Reports through <see cref="IWinCondition"/> rather than ending the match itself, so it plugs
    /// into <see cref="GameStateManager"/>'s existing polling. Drop it into that component's
    /// win-condition list. All timing is server-authoritative; the countdown is synced as a deadline
    /// (same shape as bleedout) so clients can draw a bar without per-frame streaming.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ExtractionZone : NetworkBehaviour, IWinCondition
    {
        [Header("Capture")]
        [Tooltip("Continuous seconds the car must stay in the zone to win.")]
        [SerializeField] private float captureDuration = 30f;
        [Tooltip("The car counts only while someone is driving it. Off = an empty parked car also captures.")]
        [SerializeField] private bool requireDriver = true;

        // True once the hold has completed. Latched — GameStateManager reads this and wins the match.
        // Server-only; the win itself is what clients see, via MatchResult.
        private bool captured;

        // Whether the timer is currently running, and the NetworkTime deadline it will complete at.
        // Synced so every client can render the same countdown; reset the moment the car stops
        // qualifying, so progress is all-or-nothing rather than banked.
        [SyncVar(hook = nameof(OnCapturingChangedHook))]
        private bool capturing;
        [SyncVar]
        private double captureEndTime;

        // Every car collider currently overlapping the trigger, mapped to its car. A car has many
        // colliders that enter and exit one at a time; the car is "inside" while any of them remain,
        // which a per-collider set tracks without guessing at geometry. A car can also stop
        // qualifying without leaving at all (driver hops out, vehicle destroyed), so presence alone
        // isn't enough — HasQualifyingCar re-checks driver and health each tick.
        private readonly Dictionary<Collider, CarController> collidersInside = new Dictionary<Collider, CarController>();

        public bool IsMet => captured;

        public float CaptureDuration => captureDuration;
        public bool IsCapturing => capturing;

        /// <summary>0–1 hold progress for a UI bar. Zero whenever the timer isn't running.</summary>
        public float CaptureProgress
        {
            get
            {
                if (!capturing || captureDuration <= 0f)
                    return 0f;
                float remaining = Mathf.Max(0f, (float)(captureEndTime - NetworkTime.time));
                return Mathf.Clamp01(1f - remaining / captureDuration);
            }
        }

        /// <summary>Fires on clients when the capture starts (true) or stops/resets (false).</summary>
        public event System.Action<bool> OnCapturingChanged;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkServer.active)
                return;

            var car = other.GetComponentInParent<CarController>();
            if (car != null)
                collidersInside[other] = car;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!NetworkServer.active)
                return;

            collidersInside.Remove(other);
        }

        [ServerCallback]
        private void Update()
        {
            if (captured)
                return;

            bool qualifies = HasQualifyingCar();

            if (qualifies && !capturing)
            {
                capturing = true;
                captureEndTime = NetworkTime.time + captureDuration;
            }
            else if (!qualifies && capturing)
            {
                // Rolled out (or lost the driver / the car blew up) before the hold finished.
                capturing = false;
            }

            if (capturing && NetworkTime.time >= captureEndTime)
            {
                capturing = false;
                captured = true;
                // No ServerEndMatch here on purpose — GameStateManager polls IsMet and owns the
                // transition, so victory funnels through the one place, exactly like defeat.
            }
        }

        [Server]
        private bool HasQualifyingCar()
        {
            foreach (CarController car in collidersInside.Values)
            {
                // A destroyed car leaves a null here (its colliders never raise OnTriggerExit).
                if (car == null || !car.VehicleIsAlive)
                    continue;
                if (requireDriver && !car.HasDriver)
                    continue;
                return true;
            }
            return false;
        }

        private void OnCapturingChangedHook(bool oldValue, bool newValue)
        {
            OnCapturingChanged?.Invoke(newValue);
        }
    }
}
