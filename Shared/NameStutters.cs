namespace SMSModForge.Shared
{
    /// <summary>
    /// A name said with a stutter - "A-Anna", "K-K-Kirby" - keeps it when the name is
    /// replaced (ModForge 1.9.0, renaming the game's characters): Anna renamed Bella
    /// reads "B-Bella", not "A-Bella". 31 of the game's mentions are stuttered.
    /// <para/>
    /// A stutter is the name's first letter, alone as a word, then a hyphen, right
    /// before the name; there may be several. Each takes the new name's first letter,
    /// in the stutter's own capitals. A new name that starts with no letter leaves
    /// them as they are. Compiled into both projects; the plugin uses it.
    /// </summary>
    public static class NameStutters
    {
        /// <summary>
        /// <paramref name="text"/> with the stutters right before <paramref name="start"/>,
        /// where <paramref name="oldName"/> begins, given <paramref name="newName"/>'s first
        /// letter. Only the stutters change, letter for letter, so every position in the
        /// text stays where it was.
        /// </summary>
        public static string Carry(string text, int start, string oldName, string newName)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return text;
            if (start < 2 || start > text.Length) return text;
            char was = oldName[0], now = newName[0];
            if (!char.IsLetter(was) || !char.IsLetter(now)) return text;
            if (char.ToUpperInvariant(was) == char.ToUpperInvariant(now)) return text;

            char[] chars = null;
            int at = start;
            while (at >= 2 && text[at - 1] == '-'
                   && char.ToUpperInvariant(text[at - 2]) == char.ToUpperInvariant(was)
                   && (at - 2 == 0 || !char.IsLetterOrDigit(text[at - 3])))
            {
                if (chars == null) chars = text.ToCharArray();
                char stutter = text[at - 2];
                chars[at - 2] = char.IsUpper(stutter) ? char.ToUpperInvariant(now) : char.ToLowerInvariant(now);
                at -= 2;
            }
            return chars == null ? text : new string(chars);
        }

        /// <summary>Whether the name at <paramref name="start"/> is said with a stutter.</summary>
        public static bool Stuttered(string text, int start, string name)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name) || start < 2 || start > text.Length) return false;
            return text[start - 1] == '-'
                   && char.ToUpperInvariant(text[start - 2]) == char.ToUpperInvariant(name[0])
                   && (start - 2 == 0 || !char.IsLetterOrDigit(text[start - 3]));
        }
    }
}
