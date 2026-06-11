using System.Collections.Generic;
using Mirror;
using UnityEngine;
using FPSGame.Player;
using FPSGame.Vehicle;

namespace FPSGame.Environment
{
    // Proximity lock that drives a DoorOpener. Place on a child trigger volume of the
    // door's NetworkIdentity (same pattern as ContainerLock / CargoTrigger). The door
    // opens automatically while a key-holder is inside the trigger — whether the player
    // walks up on foot or drives a car up to it (the car's current driver is checked).
    //
    // Server-authoritative: the trigger and key checks run only on the server; the door
    // state is replicated to clients through DoorOpener's SyncVar.
    [RequireComponent(typeof(Collider))]
    public class KeycardLock : NetworkBehaviour
    {
        [SerializeField] private KeycardId requiredKey;

        [Tooltip("Door to open/close. Falls back to a DoorOpener on a parent object.")]
        [SerializeField] private DoorOpener door;

        [Tooltip("Close the door again once no key-holder remains in the trigger.")]
        [SerializeField] private bool closeWhenEmpty = true;

        // Entities (player or car) currently overlapping the trigger, with a collider
        // ref-count so multi-collider bodies (cars) don't prematurely clear on exit.
        private readonly Dictionary<NetworkIdentity, int> overlapping =
            new Dictionary<NetworkIdentity, int>();

        private readonly List<NetworkIdentity> scratch = new List<NetworkIdentity>();

        private void Awake()
        {
            if (door == null)
                door = GetComponentInParent<DoorOpener>();
        }

        [ServerCallback]
        private void OnTriggerEnter(Collider other)
        {
            var ident = ResolveEntity(other);
            if (ident == null)
                return;

            overlapping.TryGetValue(ident, out int count);
            overlapping[ident] = count + 1;
        }

        [ServerCallback]
        private void OnTriggerExit(Collider other)
        {
            var ident = ResolveEntity(other);
            if (ident == null)
                return;

            if (overlapping.TryGetValue(ident, out int count))
            {
                if (count <= 1)
                    overlapping.Remove(ident);
                else
                    overlapping[ident] = count - 1;
            }
        }

        [ServerCallback]
        private void Update()
        {
            if (door == null || overlapping.Count == 0)
            {
                // Nobody around — make sure we don't keep a stale-open door open.
                if (door != null && closeWhenEmpty && overlapping.Count == 0)
                    door.CloseDoor();
                return;
            }

            bool anyKeyHolder = false;

            // Re-evaluating every frame also handles the case where a player picks up
            // the key while already standing in the trigger. Door ops are idempotent.
            scratch.Clear();
            foreach (var ident in overlapping.Keys)
            {
                if (ident == null)
                {
                    scratch.Add(ident); // destroyed/despawned — purge below
                    continue;
                }

                if (EntityHasKey(ident))
                    anyKeyHolder = true;
            }

            for (int i = 0; i < scratch.Count; i++)
                overlapping.Remove(scratch[i]);

            if (anyKeyHolder)
                door.OpenDoor();
            else if (closeWhenEmpty)
                door.CloseDoor();
        }

        // Maps a collider to the player/car NetworkIdentity it belongs to, or null.
        [Server]
        private NetworkIdentity ResolveEntity(Collider other)
        {
            var keyring = other.GetComponentInParent<PlayerKeyring>();
            if (keyring != null)
                return keyring.netIdentity;

            var car = other.GetComponentInParent<CarController>();
            if (car != null)
                return car.netIdentity;

            return null;
        }

        // True if this entity holds the required key: directly for a player on foot,
        // or via the current driver for a car.
        [Server]
        private bool EntityHasKey(NetworkIdentity ident)
        {
            var keyring = ident.GetComponent<PlayerKeyring>();
            if (keyring != null)
                return keyring.HasKey(requiredKey);

            var car = ident.GetComponent<CarController>();
            if (car != null)
            {
                var driver = car.GetCurrentDriverServer();
                if (driver != null)
                {
                    var driverKeyring = driver.GetComponent<PlayerKeyring>();
                    if (driverKeyring != null)
                        return driverKeyring.HasKey(requiredKey);
                }
            }

            return false;
        }
    }
}
