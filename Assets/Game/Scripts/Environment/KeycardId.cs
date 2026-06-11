namespace FPSGame.Environment
{
    // Doom-style keycard identifiers. Each value maps to one bit in PlayerKeyring's
    // held-keys mask, so keep this under 32 entries. A KeycardPickup and the
    // KeycardLock it unlocks must reference the same id.
    public enum KeycardId
    {
        Red = 0,
        Blue = 1,
        Yellow = 2,
        Green = 3
    }
}
