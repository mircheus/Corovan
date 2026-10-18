using Mirror;
using UnityEngine;
using FPSGame.Player;

namespace FPSGame.Environment
{
    // World weapon pickup. The local player auto-collects it by walking into the
    // trigger; the trigger is detected on the owning client (its CharacterController
    // is enabled there), which sends a Command. The server validates range, grants
    // the item via PlayerLoadout, then despawns or hides+respawns the pickup.
    [RequireComponent(typeof(Collider))]
    public class WeaponPickup : NetworkBehaviour
    {
        [Tooltip("LoadoutItem id this pickup grants, e.g. \"pistol\", \"grapple\".")]
        [SerializeField] private string itemId;
        [Tooltip("Visual to hide while the pickup is on cooldown (respawning). Optional.")]
        [SerializeField] private GameObject visual;
        [Tooltip("Max distance the server accepts a claim from. Should match the trigger size.")]
        [SerializeField] private float pickupRange = 2.5f;
        [Tooltip("If enabled, the pickup reappears after respawnDelay instead of despawning.")]
        [SerializeField] private bool respawns = false;
        [SerializeField] private float respawnDelay = 15f;

        [SyncVar(hook = nameof(OnAvailableChanged))]
        private bool available = true;

        private Collider triggerCollider;

        public string ItemId => itemId;
        public bool Available => available;
        public float PickupRange => pickupRange;

        private void Awake()
        {
            triggerCollider = GetComponent<Collider>();
            triggerCollider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            // Runs on every client, but only the local player has an enabled
            // CharacterController, so only the local player generates overlaps here.
            if (!available)
                return;

            var loadout = other.GetComponentInParent<PlayerLoadout>();
            if (loadout == null || !loadout.isLocalPlayer)
                return;

            loadout.RequestPickup(this);
        }

        [Server]
        public bool ServerTryTake(PlayerLoadout loadout)
        {
            if (!available || string.IsNullOrEmpty(itemId) || loadout == null)
                return false;

            if (!loadout.ServerGrant(itemId))
                return false;

            available = false;
            ApplyAvailable(false);

            if (respawns)
                Invoke(nameof(ServerRespawn), respawnDelay);
            else
                NetworkServer.Destroy(gameObject);

            return true;
        }

        [Server]
        private void ServerRespawn()
        {
            available = true;
            ApplyAvailable(true);
        }

        private void OnAvailableChanged(bool oldValue, bool newValue)
        {
            ApplyAvailable(newValue);
        }

        // Toggles the pickup's collider + visual so it can't be re-collected while
        // on cooldown. Applied on both server and clients (dedicated server safe).
        private void ApplyAvailable(bool isAvailable)
        {
            if (triggerCollider != null)
                triggerCollider.enabled = isAvailable;
            if (visual != null)
                visual.SetActive(isAvailable);
        }
    }
}
