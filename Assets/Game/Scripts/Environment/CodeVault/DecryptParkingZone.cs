using FPSGame.Vehicle;
using UnityEngine;

namespace FPSGame.Environment
{
    /// <summary>Why decryption is (not) running right now. Synced by <see cref="CodeVaultController"/>.</summary>
    public enum ParkingStatus : byte
    {
        Active,      // a driven car stands in the zone, close enough to the anchor, not dashing
        NoCar,       // no car inside the zone volume
        NoDriver,    // a car is in the zone, but nobody is at the wheel
        TooFar,      // driven car in the zone, but further than parkingTolerance from the anchor
        Dashing      // driven car in place, but it dashed less than (dash time + dashCooldownBlock) ago
    }

    /// <summary>
    /// Parking spot next to the truck (handoff F1). A box volume plus an anchor point; the server asks
    /// <see cref="Evaluate"/> every tick. Plain MonoBehaviour — no NetworkIdentity, the result is synced by
    /// the <see cref="CodeVaultController"/> that polls it.
    ///
    /// Geometric test, not OnTrigger: the car is moved by its driver's NetworkTransform on the server, and a
    /// point-in-box check doesn't care about trigger bookkeeping (enter/exit pairs, multi-collider cars).
    /// The car's pivot is the point tested. Trucks stand still in world space, so world position is enough.
    /// </summary>
    public class DecryptParkingZone : MonoBehaviour
    {
        [Tooltip("Volume the car's pivot must be in. May be a disabled collider or a trigger; only its shape is read.")]
        [SerializeField] private BoxCollider zone;

        [Tooltip("Ideal parking point. Empty = this transform.")]
        [SerializeField] private Transform anchor;

        [Tooltip("Max horizontal (XZ) distance from the car's pivot to the anchor, metres.")]
        [SerializeField, Min(0f)] private float parkingTolerance = 2f;

        [Tooltip("Seconds after the dash has ended during which parking still counts as broken.")]
        [SerializeField, Min(0f)] private float dashCooldownBlock = 1f;

        [Tooltip("How often the list of cars in the scene is refreshed, seconds.")]
        [SerializeField, Min(0.1f)] private float carScanInterval = 1f;

        [Tooltip("Server: once a second, log every car's pivot (world + zone-local) and whether it counts as inside.")]
        [SerializeField] private bool debugLog;

        private CarController[] cars = new CarController[0];
        private float nextScanTime;
        private float nextLogTime;

        public Transform Anchor => anchor != null ? anchor : transform;
        public float ParkingTolerance => parkingTolerance;

        private void Awake()
        {
            if (zone == null)
                zone = GetComponent<BoxCollider>();
            if (zone != null)
                zone.isTrigger = true;
        }

        /// <summary>Server: current parking status; <paramref name="parkedCar"/> = the car it is about (may be null).</summary>
        public ParkingStatus Evaluate(out CarController parkedCar)
        {
            parkedCar = null;

            if (Time.time >= nextScanTime)
            {
                nextScanTime = Time.time + carScanInterval;
                cars = FindObjectsByType<CarController>(FindObjectsSortMode.None);
            }

            // Best status wins: one driven car standing correctly is enough, whatever else is around.
            ParkingStatus best = ParkingStatus.NoCar;
            bool log = debugLog && Time.time >= nextLogTime;
            if (log)
            {
                nextLogTime = Time.time + 1f;
                Debug.Log($"[ParkingZone] {name}: zone={(zone != null ? zone.name : "NULL")}, cars found={cars.Length}", this);
            }
            foreach (CarController car in cars)
            {
                if (log && car != null)
                {
                    Vector3 p = car.transform.position;
                    Vector3 local = zone != null ? zone.transform.InverseTransformPoint(p) : Vector3.zero;
                    Debug.Log($"[ParkingZone]   {car.name}: alive={car.VehicleIsAlive}, pivot={p}, local={local}, " +
                              $"box center={zone?.center} size={zone?.size}, inside={Contains(p)}", car);
                }

                if (car == null || !car.VehicleIsAlive || !Contains(car.transform.position))
                    continue;

                ParkingStatus status = StatusOf(car);
                if (parkedCar == null || status < best)
                {
                    best = status;
                    parkedCar = car;
                }
            }

            return best;
        }

        private ParkingStatus StatusOf(CarController car)
        {
            if (!car.HasDriver)
                return ParkingStatus.NoDriver;

            Vector3 offset = car.transform.position - Anchor.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > parkingTolerance * parkingTolerance)
                return ParkingStatus.TooFar;

            if (Time.time - car.ServerLastDashTime < car.DashDuration + dashCooldownBlock)
                return ParkingStatus.Dashing;

            return ParkingStatus.Active;
        }

        public bool Contains(Vector3 worldPoint)
        {
            if (zone == null)
                return false;

            Vector3 local = zone.transform.InverseTransformPoint(worldPoint);
            return new Bounds(zone.center, zone.size).Contains(local);
        }

        private void OnDrawGizmos()
        {
            BoxCollider box = zone != null ? zone : GetComponent<BoxCollider>();
            if (box != null)
            {
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
                Gizmos.matrix = box.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(box.center, box.size);
                Gizmos.matrix = Matrix4x4.identity;
            }

            // Tolerance circle around the anchor.
            Vector3 c = Anchor.position;
            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.9f);
            const int segments = 32;
            Vector3 prev = c + new Vector3(parkingTolerance, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = c + new Vector3(Mathf.Cos(a) * parkingTolerance, 0f, Mathf.Sin(a) * parkingTolerance);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
            Gizmos.DrawSphere(c, 0.15f);
        }
    }
}
