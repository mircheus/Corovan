using UnityEngine;
using Mirror;
using Akila.FPSFramework;

namespace FPSGame.Player
{
    /// <summary>
    /// Keeps the FPS Framework on the player prefab local to its owner. FPSF is single-player code:
    /// every component reads the keyboard through its own Controls instance and drives cameras,
    /// UI and weapon motion. On anyone else's copy of this player all of it is switched off before
    /// it gets a Start — remote players are driven by NetworkTransform and our own synced state.
    /// </summary>
    public class FPSFPlayerBridge : NetworkBehaviour
    {
        [Tooltip("FPSF weapon overlay camera (stacked on the main camera). Disabled on remote players.")]
        [SerializeField] private Camera overlayCamera;

        private void Awake()
        {
            // FPSF's FirstPersonController normally wires this; we use our own PlayerController.
            var inventory = GetComponentInChildren<Inventory>(true);
            if (inventory != null)
                inventory.characterManager = GetComponent<CharacterManager>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Dedicated server: no client side at all, so no copy of this player is anyone's.
            if (!NetworkClient.active)
                DisableFrameworkLocalComponents();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!isOwned)
                DisableFrameworkLocalComponents();
        }

        private void DisableFrameworkLocalComponents()
        {
            foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || !IsFrameworkLocalComponent(behaviour))
                    continue;
                behaviour.enabled = false;
            }

            if (overlayCamera != null)
                overlayCamera.enabled = false;
        }

        // Everything from the framework is owner-only except the ragdoll, which every client plays.
        private static bool IsFrameworkLocalComponent(MonoBehaviour behaviour)
        {
            var type = behaviour.GetType();
            return type.Namespace != null
                && type.Namespace.StartsWith("Akila.FPSFramework")
                && !(behaviour is Ragdoll);
        }
    }
}
