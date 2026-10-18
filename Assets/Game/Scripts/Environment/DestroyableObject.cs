using Mirror;
using FPSGame.Combat;
using UnityEngine;

namespace FPSGame.Environment
{
    public class DestroyableObject : NetworkBehaviour, IDamageReceiver
    {
        // Counts hits, not HP: any weapon hit is one hit, whatever its damage.
        int IDamageReceiver.CurrentHealth => Mathf.Max(0, hitsToDestroy - currentHits);
        int IDamageReceiver.MaxHealth => hitsToDestroy;
        bool IDamageReceiver.IsDead => isDestroyed;
        void IDamageReceiver.TakeDamage(int damage) => TakeHit();

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
