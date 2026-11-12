using System.Collections.Generic;
using FPSGame.Player;
using Mirror;
using UnityEngine;

namespace FPSGame.Vehicle
{
    /// <summary>
    /// Minimal passenger state for a car (handoff F7). E takes this seat when the driver seat is already taken
    /// and the player is in the boarding box above the car (on the roof or mid-jump) or within the car's
    /// interact distance. The passenger is pinned to the seat on every peer and the camera simply rides along
    /// (it is a child of the player). E gets them out, popped up above the car so they can jump or grapple back
    /// onto the platform; missing it is the ordinary fall respawn.
    ///
    /// The owner (<see cref="PlayerInputHandler"/>) boards locally and asks the server; the seat itself is
    /// server state: <see cref="passengerNetId"/>.
    /// The passenger keeps their weapons; the car's driver seat and its authority are untouched.
    /// </summary>
    public class CarPassengerSeat : NetworkBehaviour
    {
        private static readonly List<CarPassengerSeat> all = new List<CarPassengerSeat>();
        public static IReadOnlyList<CarPassengerSeat> All => all;

        [Header("Seat")]
        [Tooltip("Where the passenger's pivot (feet) is held. Empty = the car's pivot + Seat Local Offset.")]
        [SerializeField] private Transform seatPoint;
        [SerializeField] private Vector3 seatLocalOffset = new Vector3(0f, 0.6f, -1.2f);

        [Header("Boarding box (car-local)")]
        [SerializeField] private Vector3 boardBoxCenter = new Vector3(0f, 2.2f, 0f);
        [SerializeField] private Vector3 boardBoxSize = new Vector3(3f, 3f, 5f);

        [Header("Exit")]
        [Tooltip("Where the passenger appears on exit. Empty = the car's pivot + Exit Local Offset.")]
        [SerializeField] private Transform exitPoint;
        [SerializeField] private Vector3 exitLocalOffset = new Vector3(0f, 2.5f, 0f);
        [Tooltip("Upward launch speed on exit, m/s.")]
        [SerializeField, Min(0f)] private float exitUpSpeed = 5f;

        [SyncVar] private uint passengerNetId;

        private CarController car;

        public CarController Car => car;
        public bool IsOccupied => passengerNetId != 0;
        public uint PassengerNetId => passengerNetId;
        public float ExitUpSpeed => exitUpSpeed;

        public Vector3 SeatPosition => seatPoint != null ? seatPoint.position : transform.TransformPoint(seatLocalOffset);
        public Vector3 ExitPosition => exitPoint != null ? exitPoint.position : transform.TransformPoint(exitLocalOffset);

        private void Awake()
        {
            car = GetComponent<CarController>();
        }

        private void OnEnable() => all.Add(this);
        private void OnDisable() => all.Remove(this);

        /// <summary>True if a world point (the player's pivot) is inside the boarding box above the car.</summary>
        public bool InBoardingBox(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return new Bounds(boardBoxCenter, boardBoxSize).Contains(local);
        }

        /// <summary>Whether the local player may try to board (seat free, car alive, not the driver).</summary>
        public bool CanBoard(uint playerNetId)
        {
            return !IsOccupied && car != null && car.VehicleIsAlive && car.DriverNetId != playerNetId;
        }

        [Server]
        public bool ServerBoard(NetworkIdentity player)
        {
            if (player == null || !CanBoard(player.netId))
                return false;

            // Same tolerance idea as entering a car: the owner reports, the server sanity-checks.
            float dist = Vector3.Distance(player.transform.position, transform.position);
            if (dist > boardBoxSize.magnitude + 2f)
                return false;

            passengerNetId = player.netId;
            return true;
        }

        [Server]
        public void ServerLeave(uint playerNetId)
        {
            if (passengerNetId == playerNetId)
                passengerNetId = 0;
        }

        [ServerCallback]
        private void Update()
        {
            if (passengerNetId == 0)
                return;

            // The passenger left the game or went down without telling us — free the seat.
            if (!NetworkServer.spawned.TryGetValue(passengerNetId, out NetworkIdentity p) || p == null)
            {
                passengerNetId = 0;
                return;
            }
            var health = p.GetComponent<PlayerHealth>();
            if (health != null && health.IsIncapacitated)
                passengerNetId = 0;
        }

        // Every peer pins a remote passenger to its own view of the car, so they never lag behind it.
        // The owner pins itself in PlayerInputHandler — it boards and leaves before the SyncVar round trip,
        // and pinning it here would yank a player who just got out back into the seat.
        private void LateUpdate()
        {
            if (passengerNetId == 0)
                return;

            var spawned = isServer ? NetworkServer.spawned : NetworkClient.spawned;
            if (spawned.TryGetValue(passengerNetId, out NetworkIdentity p) && p != null && !p.isOwned)
                p.transform.position = SeatPosition;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 1f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(boardBoxCenter, boardBoxSize);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawSphere(SeatPosition, 0.15f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(ExitPosition, 0.15f);
        }
    }
}
