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

    // Display names for HUD prompts / key icons. Kept next to the enum so a new card
    // is named in one place.
    public static class KeycardNames
    {
        public static string DisplayName(KeycardId id)
        {
            switch (id)
            {
                case KeycardId.Red: return "красная карта";
                case KeycardId.Blue: return "синяя карта";
                case KeycardId.Yellow: return "жёлтая карта";
                case KeycardId.Green: return "зелёная карта";
                default: return "карта";
            }
        }
    }
}
