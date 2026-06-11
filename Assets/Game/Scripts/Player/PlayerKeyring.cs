using Mirror;
using FPSGame.Environment;

namespace FPSGame.Player
{
    // Doom-style keyring. Stores the set of collected keycards as a bitmask SyncVar.
    // Keys are deliberately kept out of the weapon/carry inventory — collecting one
    // never occupies a slot. Server grants keys; clients read the mask for HUD.
    public class PlayerKeyring : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnKeysChanged))]
        private int heldKeysMask;

        // Subscribe in HUD scripts to refresh the on-screen key icons.
        public event System.Action<int> KeysChanged; // payload: new mask

        public int HeldKeysMask => heldKeysMask;

        public bool HasKey(KeycardId id) => (heldKeysMask & (1 << (int)id)) != 0;

        // Returns true if the key was newly added (false if already held).
        [Server]
        public bool ServerGrantKey(KeycardId id)
        {
            int bit = 1 << (int)id;
            if ((heldKeysMask & bit) != 0)
                return false;

            heldKeysMask |= bit;
            return true;
        }

        private void OnKeysChanged(int oldMask, int newMask)
        {
            KeysChanged?.Invoke(newMask);
        }
    }
}
