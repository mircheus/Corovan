using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace FPSGame.Environment
{
    public class InteractObjective : NetworkBehaviour
    {
        [SerializeField] private InteractableObject[] targets;

        [Tooltip("If true, the UnityEvent fires on every interaction (e.g. door toggle) and the objective never latches. If false, the event fires once after requiredCount interactions.")]
        [SerializeField] private bool repeatable = false;

        [Tooltip("How many interactions are required to trigger the objective. 0 = targets.Length. Repeated interactions with the same object also count. Ignored when Repeatable is true.")]
        [SerializeField] private int requiredCount = 0;

        [Tooltip("Non-repeatable only: interactions after completion fire the event again (e.g. restart an auto-closing door's timer). The objective stays completed until ResetObjective.")]
        [SerializeField] private bool refireWhileCompleted = false;

        [Space]
        [SerializeField] private UnityEvent onObjectiveCompleted;

        [SyncVar(hook = nameof(OnCompletedChanged))]
        private bool isCompleted;

        private int interactionCount;

        public bool IsCompleted => isCompleted;

        public override void OnStartServer()
        {
            base.OnStartServer();
            foreach (var target in targets)
            {
                if (target != null)
                    target.OnInteracted += HandleInteracted;
            }
        }

        public override void OnStopServer()
        {
            foreach (var target in targets)
            {
                if (target != null)
                    target.OnInteracted -= HandleInteracted;
            }
            base.OnStopServer();
        }

        [Server]
        private void HandleInteracted(GameObject instigator)
        {
            if (repeatable)
            {
                RpcInvokeObjective();
                return;
            }

            if (isCompleted)
            {
                if (refireWhileCompleted)
                    RpcInvokeObjective();
                return;
            }

            interactionCount++;

            int required = requiredCount > 0 ? requiredCount : targets.Length;
            if (interactionCount >= required)
                isCompleted = true;
        }

        // Re-arms a latched objective: the next requiredCount interactions complete it again.
        // Wire e.g. DoorOpener.onClosed here so an auto-closed door can be opened again.
        [Server]
        public void ResetObjective()
        {
            interactionCount = 0;
            isCompleted = false;
        }

        // Runs on all clients via SyncVar hook — invokes the UnityEvent so any
        // locally-wired response (animation, door open, UI, etc.) fires everywhere.
        private void OnCompletedChanged(bool oldValue, bool newValue)
        {
            if (newValue)
                onObjectiveCompleted?.Invoke();
        }

        // Repeatable path: fire on every interaction across all clients.
        [ClientRpc]
        private void RpcInvokeObjective()
        {
            onObjectiveCompleted?.Invoke();
        }
    }
}
