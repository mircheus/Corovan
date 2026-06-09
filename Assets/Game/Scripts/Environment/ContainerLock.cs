using System;
using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    // Shootable lock that hangs on a container door. Has its own HP (SyncVar).
    // PlayerShooting.CmdFire calls TakeDamage on the server when a raycast hits it.
    // When HP reaches 0 the lock prop is hidden on every client and the parent
    // ContainerDoor is notified (server-side) so it can swing the doors open.
    //
    // This is a child NetworkBehaviour: it lives on a child GameObject and shares
    // the NetworkIdentity on the container root (Mirror resolves the identity via
    // GetComponentInParent — no nested NetworkIdentity needed).
    public class ContainerLock : NetworkBehaviour
    {
        [SerializeField] private int maxHealth = 30;

        [Tooltip("Visual prop hidden when the lock breaks. Defaults to this GameObject.")]
        [SerializeField] private GameObject lockProp;

        [SyncVar] private int currentHealth;

        [SyncVar(hook = nameof(OnBrokenChanged))]
        private bool isBroken;

        // Fired on the server the instant the lock breaks. ContainerDoor subscribes
        // to this in its OnStartServer.
        public event Action OnBroken;

        public bool IsBroken => isBroken;
        public int CurrentHealth => currentHealth;
        public int MaxHealth => maxHealth;

        public override void OnStartServer()
        {
            base.OnStartServer();
            currentHealth = maxHealth;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Late joiners: lock may already be broken — hide the prop without waiting
            // for a hook (which only fires on change).
            if (isBroken)
                HideProp();
        }

        // Called server-side from PlayerShooting.CmdFire on hit.
        [Server]
        public void TakeDamage(int amount)
        {
            Debug.Log($"TakingDamage {amount}");
            
            if (isBroken || amount <= 0)
                return;

            currentHealth -= amount;

            if (currentHealth <= 0)
            {
                currentHealth = 0;
                isBroken = true;     // hook hides the prop on every client
                OnBroken?.Invoke();  // tell the parent container to open
            }
        }

        private void OnBrokenChanged(bool oldValue, bool newValue)
        {
            if (newValue)
                HideProp();
        }

        private void HideProp()
        {
            GameObject prop = lockProp != null ? lockProp : gameObject;
            prop.SetActive(false);
        }
    }
}
