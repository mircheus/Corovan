using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    public class DoorOpener : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnOpenedStateChange))]
        private bool isOpened;
        
        [Server]
        public void OpenDoor()
        {
            if (isOpened)
                return;
            
            isOpened = true;
        }

        private void OnOpenedStateChange(bool oldValue, bool newValue)
        {
            if (newValue)
            {
                gameObject.SetActive(false);
            }
        }
    }
}