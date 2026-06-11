using Mirror;
using UnityEngine;
using FPSGame.Player;

namespace FPSGame.Environment
{
    // Doom-style keycard lying in the world. Walk over it on foot (trigger overlap)
    // to add the key to your PlayerKeyring; it then despawns. Doesn't use the carry
    // or weapon inventory. Requires a trigger Collider and a NetworkIdentity.
    [RequireComponent(typeof(Collider))]
    public class KeycardPickup : NetworkBehaviour
    {
        [SerializeField] private KeycardId keyId;

        [Header("Visual")]
        [Tooltip("Optional spinning model for flair. Rotated client-side only.")]
        [SerializeField] private Transform visual;
        [SerializeField] private float spinSpeed = 90f;

        public KeycardId KeyId => keyId;

        private void Update()
        {
            // Purely cosmetic; runs on every client so the pickup spins in place.
            if (visual != null && spinSpeed != 0f)
                visual.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        }

        [ServerCallback]
        private void OnTriggerEnter(Collider other)
        {
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
