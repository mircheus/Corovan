using Mirror;
using UnityEngine;

namespace FPSGame.Player
{
    // Makes the downed state readable from the outside. PlayerInputHandler only reacts on the owner,
    // so without this a downed teammate would still be standing upright with a rifle in their hands
    // on everyone else's screen.
    //
    // Swaps the standing model for a lying one. Driven by PlayerHealth's life-state events, which
    // fire on every client; the current state is also applied on spawn, so a client joining
    // mid-match sees bodies that went down before it connected.
    //
    // Both models must sit under one of LocalPlayerBodyHider's bodyRoots, or the owner will see
    // their own corpse in first person — that component assigns the hidden layer once, on spawn,
    // and does reach inactive children.
    [RequireComponent(typeof(PlayerHealth))]
    public class PlayerDownedVisual : NetworkBehaviour
    {
        [Header("Models")]
        [Tooltip("Standing model, shown while alive.")]
        [SerializeField] private GameObject aliveVisual;
        [Tooltip("Lying model, shown while downed and after death.")]
        [SerializeField] private GameObject downedVisual;

        [Header("Marker")]
        [Tooltip("World-space icon shown above a downed teammate so others can find them. " +
                 "Never shown to the downed player themselves.")]
        [SerializeField] private GameObject downedMarker;

        private PlayerHealth health;

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            health.OnDowned += ApplyState;
            health.OnRevived += ApplyState;
            health.OnDeath += ApplyState;
            health.OnCrashed += ApplyState;
            health.OnRespawn += ApplyState;

            // Late join: reflect whatever state this player is already in.
            ApplyState();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            health.OnDowned -= ApplyState;
            health.OnRevived -= ApplyState;
            health.OnDeath -= ApplyState;
            health.OnCrashed -= ApplyState;
            health.OnRespawn -= ApplyState;
        }

        private void ApplyState()
        {
            // A player who crashed onto the road is gone until the respawn — the body waits, frozen,
            // inside the fall volume, where nobody should see it.
            bool crashed = health.IsCrashed;

            // A dead player stays on the ground rather than snapping upright.
            bool onTheGround = health.IsIncapacitated && !crashed;

            if (aliveVisual != null)
                aliveVisual.SetActive(!onTheGround && !crashed);

            if (downedVisual != null)
                downedVisual.SetActive(onTheGround);

            // The marker lives above the head — it would sit inside the owner's own camera, and
            // there is nothing left to revive once the player is dead.
            if (downedMarker != null)
                downedMarker.SetActive(health.IsDowned && !isOwned);
        }
    }
}
