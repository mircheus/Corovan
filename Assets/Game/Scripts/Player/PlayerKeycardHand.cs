using Mirror;
using UnityEngine;
using FPSGame.Environment;

namespace FPSGame.Player
{
    // Lets a player pull a collected keycard "into hand" (number key 0). While the card
    // is in hand it behaves like carrying loot — normal shooting/ADS is suppressed and
    // LMB throws the card a short, configurable distance. Throwing removes the key from
    // PlayerKeyring and spawns a physical KeycardPickup in the world that anyone can grab.
    //
    // The keys themselves live in PlayerKeyring (a bitmask); this component is purely the
    // "hold + throw" front-end and never stores key state of its own.
    public class PlayerKeycardHand : NetworkBehaviour
    {
        [Header("Held card visual")]
        [Tooltip("Model shown in-hand while a keycard is equipped. Toggled on all clients.")]
        [SerializeField] private GameObject heldCardVisual;

        [Header("Throw")]
        [Tooltip("Prefab spawned when the card is thrown. Needs KeycardPickup + NetworkIdentity " +
                 "+ Rigidbody + NetworkTransformUnreliable, and must be registered as a spawnable " +
                 "prefab on the NetworkManager.")]
        [SerializeField] private GameObject keycardPickupPrefab;
        [Tooltip("Launch speed of the thrown card along the aim direction (m/s). Controls throw distance.")]
        [SerializeField] private float throwSpeed = 6f;
        [Tooltip("Extra upward launch speed for a natural arc (m/s).")]
        [SerializeField] private float throwUpSpeed = 2f;
        [Tooltip("Height above the player's feet the card is released from.")]
        [SerializeField] private float throwHeight = 1.2f;
        [Tooltip("Forward offset from the player so the card clears the body on release.")]
        [SerializeField] private float releaseForwardOffset = 0.6f;

        private PlayerCamera playerCamera;
        private PlayerLoadout loadout;
        private PlayerKeyring keyring;

        // Owner-side hand state.
        private bool hasEquipped;
        private KeycardId equippedKey;

        // Synced so remote clients show/hide the in-hand card model too.
        [SyncVar(hook = nameof(OnEquippedChanged))]
        private bool cardEquipped;

        public bool IsEquipped => hasEquipped;

        private void Awake()
        {
            playerCamera = GetComponentInChildren<PlayerCamera>();
            loadout = GetComponent<PlayerLoadout>();
            keyring = GetComponent<PlayerKeyring>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Late-join safe: reflect the currently-synced state onto the visual.
            if (heldCardVisual != null)
                heldCardVisual.SetActive(cardEquipped);
        }

        // Called from PlayerInputHandler when the "0" key is pressed on foot.
        public void ToggleEquip()
        {
            if (!isOwned)
                return;

            if (hasEquipped)
            {
                Unequip();
                return;
            }

            // Nothing to hold if the keyring is empty.
            if (keyring == null || !keyring.TryGetAnyHeldKey(out KeycardId id))
                return;

            hasEquipped = true;
            equippedKey = id;
            if (heldCardVisual != null)
                heldCardVisual.SetActive(true);
            loadout?.SetCarryingMode(true);
            CmdSetEquipped(true);
        }

        // Puts the card away without throwing it (weapon switch, vehicle enter, death).
        public void Unequip()
        {
            if (!isOwned || !hasEquipped)
                return;

            hasEquipped = false;
            if (heldCardVisual != null)
                heldCardVisual.SetActive(false);
            loadout?.SetCarryingMode(false);
            CmdSetEquipped(false);
        }

        // Called from PlayerInputHandler when LMB is pressed while the card is in hand.
        public void TryThrow()
        {
            if (!isOwned || !hasEquipped)
                return;

            Vector3 aimDir = playerCamera != null ? playerCamera.GetAimRay().direction : transform.forward;

            hasEquipped = false;
            if (heldCardVisual != null)
                heldCardVisual.SetActive(false);
            loadout?.SetCarryingMode(false);

            CmdThrowKey(equippedKey, aimDir);
        }

        [Command]
        private void CmdSetEquipped(bool equipped)
        {
            cardEquipped = equipped;
        }

        [Command]
        private void CmdThrowKey(KeycardId id, Vector3 aimDir)
        {
            // Must actually hold the key being thrown.
            if (keyring == null || !keyring.HasKey(id))
            {
                cardEquipped = false;
                return;
            }

            keyring.ServerRevokeKey(id);
            cardEquipped = false;

            if (keycardPickupPrefab == null)
                return;

            if (aimDir.sqrMagnitude < 0.001f)
                aimDir = transform.forward;
            aimDir.Normalize();

            // Flattened aim for the release offset so the card always spawns clear of
            // the body, even when the player is looking straight up or down.
            Vector3 flat = new Vector3(aimDir.x, 0f, aimDir.z);
            flat = flat.sqrMagnitude < 0.001f ? transform.forward : flat.normalized;

            Vector3 release = transform.position + Vector3.up * throwHeight + flat * releaseForwardOffset;
            Vector3 velocity = aimDir * throwSpeed + Vector3.up * throwUpSpeed;

            GameObject go = Instantiate(keycardPickupPrefab, release, Quaternion.identity);
            var pickup = go.GetComponent<KeycardPickup>();
            if (pickup != null)
                pickup.ServerSetKey(id);
            NetworkServer.Spawn(go);
            // Launch after spawn so the initial velocity replicates via NetworkTransform.
            if (pickup != null)
                pickup.ServerThrow(velocity);
        }

        private void OnEquippedChanged(bool oldValue, bool newValue)
        {
            // Drives the in-hand model on every client (including this owner as a
            // confirmation of the optimistic local toggle).
            if (heldCardVisual != null)
                heldCardVisual.SetActive(newValue);
        }
    }
}
