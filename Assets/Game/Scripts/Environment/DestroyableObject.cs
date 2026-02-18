using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    public class DestroyableObject : NetworkBehaviour
    {
        [SerializeField] private int hitsToDestroy = 3;

        [SyncVar(hook = nameof(OnDestroyedChanged))]
        private bool isDestroyed;

        [SyncVar]
        private int currentHits;

        [Server]
        public void TakeHit()
        {
            if (isDestroyed)
                return;

            currentHits++;

            if (currentHits >= hitsToDestroy)
                isDestroyed = true;
        }

        private void OnDestroyedChanged(bool oldValue, bool newValue)
        {
            if (newValue)
                gameObject.SetActive(false);
        }
    }
}
