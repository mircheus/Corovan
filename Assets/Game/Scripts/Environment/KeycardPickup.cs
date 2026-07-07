using Mirror;
using UnityEngine;
using FPSGame.Player;

namespace FPSGame.Environment
{
    // Doom-style keycard lying in the world. Walk over it on foot (trigger overlap)
    // to add the key to your PlayerKeyring; it then despawns. Doesn't use the carry
    // or weapon inventory.
    //
    // Colliders: needs a NON-trigger collider so it lands on the floor when thrown
    // (a trigger collider would fall straight through), plus a trigger collider for
    // the pickup overlap. A Rigidbody drives the physical throw — freeze rotation on
    // all axes in its Constraints so the card doesn't tumble. Add a
    // NetworkTransformUnreliable so the in-flight position syncs to every client.
    [RequireComponent(typeof(Rigidbody))]
    public class KeycardPickup : NetworkBehaviour
    {
        // SyncVar so a card thrown from a player's hand (spawned at runtime via
        // PlayerKeycardHand) carries its colour to every client. Scene-placed pickups
        // keep their inspector value, which the server syncs unchanged.
        [SyncVar]
        [SerializeField] private KeycardId keyId;

        [Header("Visual")]
        [Tooltip("Optional spinning model for flair. Rotated client-side only.")]
        [SerializeField] private Transform visual;
        [SerializeField] private float spinSpeed = 90f;

        [Header("Throw")]
        [Tooltip("Seconds after being thrown before the card can be picked up again. " +
                 "Stops the thrower from instantly re-collecting it. Ignored for scene-placed cards.")]
        [SerializeField] private float pickupArmDelay = 0.5f;

        public KeycardId KeyId => keyId;

        private Rigidbody rb;
        // Server-only: pickups are ignored until this time. 0 = armed immediately
        // (scene-placed cards); a thrown card sets it to now + pickupArmDelay.
        private float armedAtTime;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        // Set by PlayerKeycardHand before NetworkServer.Spawn when a thrown card
        // needs to become a world pickup of a specific colour.
        [Server]
        public void ServerSetKey(KeycardId id)
        {
            keyId = id;
        }

        // Launches the card as a physics object. Called by PlayerKeycardHand right
        // after NetworkServer.Spawn; the server simulates the Rigidbody and
        // NetworkTransformUnreliable replicates the flight to clients.
        [Server]
        public void ServerThrow(Vector3 velocity)
        {
            armedAtTime = Time.time + pickupArmDelay;
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.linearVelocity = velocity;
            }
        }

        private void Update()
        {
            // Purely cosmetic; runs on every client so the pickup spins in place.
            if (visual != null && spinSpeed != 0f)
                visual.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        }

        [ServerCallback]
        private void OnTriggerEnter(Collider other)
        {
            // Freshly thrown cards are briefly inert so the thrower doesn't re-grab them.
            if (Time.time < armedAtTime)
                return;

            // Only on-foot players collect keys (a car has no keyring). The driver's
            // own collider is disabled while driving, so you can't grab keys from a car.
            var keyring = other.GetComponentInParent<PlayerKeyring>();
            if (keyring == null)
                return;

            // Leave the pickup for others if this player already holds the key.
            if (keyring.ServerGrantKey(keyId))
                NetworkServer.Destroy(gameObject);
        }
    }
}
