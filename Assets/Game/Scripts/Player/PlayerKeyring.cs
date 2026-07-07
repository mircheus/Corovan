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

        // Lowest-id key currently held, if any. Used to decide which card the player
        // pulls "into hand" (PlayerKeycardHand). Reads the synced mask, so it works
        // on the owning client too.
        public bool TryGetAnyHeldKey(out KeycardId id)
        {
            for (int i = 0; i < 32; i++)
            {
                if ((heldKeysMask & (1 << i)) != 0)
                {
                    id = (KeycardId)i;
                    return true;
                }
            }

            id = default;
            return false;
        }

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

        // Removes a key from the ring (e.g. when the player throws the card back into
        // the world). No-op if the key isn't held.
        [Server]
        public void ServerRevokeKey(KeycardId id)
        {
            heldKeysMask &= ~(1 << (int)id);
        }

        private void OnKeysChanged(int oldMask, int newMask)
        {
            KeysChanged?.Invoke(newMask);
        }
    }
}
