using Mirror;
using UnityEngine;
using FPSGame.Player;

namespace FPSGame.Environment
{
    // Key-card slot / operator layer that drives a BreachConsole. Formally outside the
    // v0 TЗ scope (the slot/carry system) — it exists so the console is testable on a
    // scene. Put it on the same GameObject as BreachConsole (the console's NetworkIdentity).
    //
    // Interaction model: an operator standing within interactRange presses Interact to
    // toggle the card in/out. The card stays in while that operator is alive and in range.
    // The three "card removed" sources from the TЗ all funnel into one SetCardInSlot(false):
    //   1. player action  -> operator presses Interact again
    //   2. operator down   -> operator's PlayerHealth reports dead
    //   3. operator leaves  -> operator moves beyond interactRange
    //                          (only if BreachConsole.RequireOperatorNearby is on)
    // Insertion requires the operator to hold the matching key-card (PlayerKeyring).
    public class BreachConsoleSlot : NetworkBehaviour
    {
        [SerializeField] private BreachConsole console;
        [Tooltip("Key-card the operator must hold to insert it into this console.")]
        [SerializeField] private KeycardId requiredKey = KeycardId.Red;
        [Tooltip("Radius the operator must stay within, or the card is pulled out.")]
        [SerializeField] private float interactRange = 3f;

        // Server-only: the player who currently holds the card in the slot (null = empty).
        private NetworkIdentity operatorIdentity;

        public float InteractRange => interactRange;
        public KeycardId RequiredKey => requiredKey;

        private void Awake()
        {
            if (console == null)
                console = GetComponent<BreachConsole>();
            if (console == null)
                console = GetComponentInParent<BreachConsole>();
        }

        // Called server-side from the interaction layer (PlayerInputHandler.CmdToggleBreachConsole).
        [Server]
        public void ServerToggle(GameObject player)
        {
            if (console == null || player == null)
                return;

            // Slot blocked during overheat — insertion ignored (removal isn't relevant:
            // the console already ejected the card on entering Overheat).
            if (console.State == ConsoleState.Overheat)
                return;

            var pid = player.GetComponent<NetworkIdentity>();
            if (pid == null)
                return;

            // Same operator pressing again -> pull the card out (player action).
            if (operatorIdentity == pid)
            {
                ServerPullOut();
                return;
            }

            // Occupied by someone else -> ignore.
            if (operatorIdentity != null)
                return;

            // Empty slot -> try to insert. Requires the matching key, alive, in range.
            var keyring = player.GetComponent<PlayerKeyring>();
            if (keyring == null || !keyring.HasKey(requiredKey))
                return;

            var health = player.GetComponent<PlayerHealth>();
            if (health != null && health.IsIncapacitated)
                return;

            if (!InRange(player.transform.position))
                return;

            operatorIdentity = pid;
            console.SetCardInSlot(true);
        }

        [Server]
        private void ServerPullOut()
        {
            operatorIdentity = null;
            console.SetCardInSlot(false);
        }

        [ServerCallback]
        private void Update()
        {
            if (console == null)
                return;

            // The console ejected the card on its own (overheat). Drop our operator so a
            // fresh press is required once the cooldown ends.
            if (console.State == ConsoleState.Overheat)
            {
                operatorIdentity = null;
                return;
            }

            if (operatorIdentity == null)
                return;

            // Operator disconnected / despawned.
            if (operatorIdentity.gameObject == null)
            {
                ServerPullOut();
                return;
            }

            // Operator down.
            var health = operatorIdentity.GetComponent<PlayerHealth>();
            if (health != null && health.IsIncapacitated)
            {
                ServerPullOut();
                return;
            }

            // Operator left the interaction zone (skipped when the console lets them walk away).
            if (console.RequireOperatorNearby && !InRange(operatorIdentity.transform.position))
            {
                ServerPullOut();
            }
        }

        private bool InRange(Vector3 point) =>
            Vector3.Distance(transform.position, point) <= interactRange;
    }
}
