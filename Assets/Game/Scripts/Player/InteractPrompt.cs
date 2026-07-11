using System;

namespace FPSGame.Player
{
    /// <summary>
    /// What the "[E] …" HUD line should say right now. Either an <see cref="Action"/> the player can
    /// take ("Взять" → shown as "[E] Взять") or a <see cref="Warn"/> that a requirement isn't met
    /// ("Требуется красная карта" → shown as-is, in a warning colour). <see cref="None"/> hides it.
    /// </summary>
    public readonly struct InteractPrompt : IEquatable<InteractPrompt>
    {
        public readonly string Text;

        /// <summary>True = requirement not met (a warning); false = an action the player can perform.</summary>
        public readonly bool Blocked;

        public InteractPrompt(string text, bool blocked)
        {
            Text = text;
            Blocked = blocked;
        }

        public bool HasValue => !string.IsNullOrEmpty(Text);

        public static readonly InteractPrompt None = default;
        public static InteractPrompt Action(string text) => new InteractPrompt(text, false);
        public static InteractPrompt Warn(string text) => new InteractPrompt(text, true);

        public bool Equals(InteractPrompt other) => Text == other.Text && Blocked == other.Blocked;
        public override bool Equals(object obj) => obj is InteractPrompt other && Equals(other);
        public override int GetHashCode() => (Text, Blocked).GetHashCode();
        public static bool operator ==(InteractPrompt a, InteractPrompt b) => a.Equals(b);
        public static bool operator !=(InteractPrompt a, InteractPrompt b) => !a.Equals(b);
    }
}
