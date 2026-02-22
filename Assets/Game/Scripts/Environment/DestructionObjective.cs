using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace FPSGame.Environment
{
    public class DestructionObjective : NetworkBehaviour
    {
        [SerializeField] private DestructionTarget[] targets;

        [Tooltip("How many targets must be destroyed to trigger the objective. 0 = all targets.")]
        [SerializeField] private int requiredCount = 0;

        [Space]
        [SerializeField] private UnityEvent onObjectiveCompleted;

        [SyncVar(hook = nameof(OnCompletedChanged))]
        private bool isCompleted;

        public bool IsCompleted => isCompleted;

        public override void OnStartServer()
        {
            base.OnStartServer();
            foreach (var target in targets)
            {
                if (target != null)
                    target.OnTargetDestroyed += CheckObjective;
            }
        }

        [Server]
        private void CheckObjective()
        {
            if (isCompleted) return;

            int required = requiredCount > 0 ? requiredCount : targets.Length;
            int destroyedCount = 0;
            foreach (var target in targets)
            {
                if (target != null && target.IsDestroyed)
                    destroyedCount++;
            }

            if (destroyedCount >= required)
                isCompleted = true;
        }

        // Runs on all clients via SyncVar hook — invokes the UnityEvent so any
        // locally-wired response (animation, door open, UI, etc.) fires everywhere.
        private void OnCompletedChanged(bool oldValue, bool newValue)
        {
            if (newValue)
                onObjectiveCompleted?.Invoke();
        }
    }
}
