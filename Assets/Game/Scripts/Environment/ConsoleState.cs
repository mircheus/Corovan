namespace FPSGame.Environment
{
    // Breach-console state machine states. See BreachConsole for the transition table.
    public enum ConsoleState
    {
        Idle,       // no card in slot, vault closed, heat falling
        Breaching,  // card inserted, warm-up running, vault closed, heat falling
        Open,       // vault open, heat rising
        Overheat    // forced lock after overheat, vault closed, heat falling
    }
}
