using System;

namespace SMSModForge.Shared
{
    /// <summary>
    /// A bust's art field that borrows the game's own art instead of naming a
    /// file of the pack's (the author, 1.6.3): <c>game:&lt;bust&gt;</c> in an
    /// outfit's blink, mouth prefix or expression prefix means that part of
    /// the game's own bust of that name - its blink frame, its four mouth
    /// frames, its faces.
    /// <para/>
    /// For an outfit added to one of the game's characters, whose own default
    /// bust has these already. The editor ships copies of the game's art too,
    /// but made smaller; borrowed, the game uses its own, at full size.
    /// <para/>
    /// One spelling, for the editor that writes it and the plugin that reads
    /// it.
    /// </summary>
    public static class GameArt
    {
        public const string Prefix = "game:";

        /// <summary>Whether a field borrows the game's art rather than naming a file.</summary>
        public static bool IsBorrowed(string value)
            => value != null && value.StartsWith(Prefix, StringComparison.Ordinal)
               && value.Length > Prefix.Length;

        /// <summary>The game bust a borrowing field names, or null for one that
        /// names a file.</summary>
        public static string BustOf(string value)
            => IsBorrowed(value) ? value.Substring(Prefix.Length).Trim() : null;

        /// <summary>The field that borrows from <paramref name="bust"/>.</summary>
        public static string From(string bust) => Prefix + bust;
    }
}
