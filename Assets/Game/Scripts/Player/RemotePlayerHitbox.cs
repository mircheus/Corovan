using UnityEngine;
using Mirror;

namespace FPSGame.Player
{
    /// <summary>
    /// Gives other players something to shoot. A remote copy of a player has its CharacterController
    /// (its only body collider) disabled, so without this nothing could hit it — no friendly fire.
    ///
    /// The capsule sits on the PlayerHitbox layer, which collides with nothing: it never blocks
    /// movement or fires triggers, it only catches weapon rays. The owner's own copy keeps it off,
    /// so a player can never shoot themselves.
    /// </summary>
    public class RemotePlayerHitbox : NetworkBehaviour
    {
        [SerializeField] private Collider hitbox;

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Dedicated server: every copy is someone else's (host is handled in OnStartClient).
            if (!NetworkClient.active && hitbox != null)
                hitbox.enabled = true;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (hitbox != null)
                hitbox.enabled = !isOwned;
        }
    }
}
