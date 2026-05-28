using Mirror;
using UnityEngine;

namespace FPSGame.Environment
{
    public class DoorOpener : NetworkBehaviour
    {
        [SerializeField] private Collider doorCollider;
        [SerializeField] private MeshRenderer doorRenderer;

        [SyncVar(hook = nameof(OnOpenedStateChange))]
        private bool isOpened;

        [Server]
        public void OpenDoor()
        {
            if (isOpened)
                return;

            isOpened = true;
        }

        [Server]
        public void CloseDoor()
        {
            if (!isOpened)
                return;

            isOpened = false;
        }

        [Server]
        public void ToggleDoor()
        {
            isOpened = !isOpened;
        }

        private void OnOpenedStateChange(bool oldValue, bool newValue)
        {
            ApplyOpenedState(newValue);
        }

        public override void OnStartClient()
        {
            ApplyOpenedState(isOpened);
        }

        private void ApplyOpenedState(bool opened)
        {
            if (doorCollider != null)
                doorCollider.enabled = !opened;

            if (doorRenderer != null)
                doorRenderer.enabled = !opened;
        }
    }
}
