namespace FPSGame.Networking
{
    /// <summary>
    /// One requirement the squad has to satisfy to win the match. Implement on a MonoBehaviour and
    /// drop it into <see cref="GameStateManager"/>'s win-condition list.
    /// <para>
    /// Polled on the server only, so the implementation is free to read server-authoritative state
    /// directly. It must be side-effect free: <see cref="IsMet"/> is called repeatedly, and the
    /// match may already be over by the time the next poll lands.
    /// </para>
    /// </summary>
    public interface IWinCondition
    {
        /// <summary>True once this requirement has been satisfied.</summary>
        bool IsMet { get; }
    }
}
