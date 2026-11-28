namespace SMSModForge.Shared
{
    /// <summary>What a navigator button says: shared by the plugin (NavigatorRuntime) and the place preview's
    /// interface (Rendering/GameHud), so the preview names a button as the game does.</summary>
    public static class NavigatorLabels
    {
        /// <summary>
        /// Something readable for a button whose author left the label empty.
        /// <para/>
        /// It used to print the raw token, so a button to the bedroom read
        /// "vanilla:5_MyRoom" on screen. Nobody wants that, and NavigatorButtonDef
        /// already promised otherwise -- "display name (or the vanilla in-game
        /// name)".
        /// <para/>
        /// The registry does not carry display names, so this works from the
        /// token: drop the scheme, drop a pack id, drop the numeric prefix the
        /// game's own level objects carry, and put spaces back between words
        /// that were run together. "vanilla:5_MyRoom" becomes "My Room".
        /// <para/>
        /// A guess, and deliberately so -- an author who wants exact wording
        /// types a label. This only has to beat showing them a token.
        /// </summary>
        public static string Fallback(string targetToken)
        {
            if (string.IsNullOrEmpty(targetToken)) return "";

            string s = targetToken;

            int colon = s.IndexOf(':');
            if (colon >= 0 && colon < s.Length - 1) s = s.Substring(colon + 1);

            // pack:<packId>.<placeKey> keeps only the place.
            int dot = s.LastIndexOf('.');
            if (dot >= 0 && dot < s.Length - 1) s = s.Substring(dot + 1);

            // Vanilla levels are named 5_MyRoom, 14_Beach, and so on.
            int underscore = s.IndexOf('_');
            if (underscore > 0 && underscore < s.Length - 1)
            {
                bool allDigits = true;
                for (int i = 0; i < underscore; i++)
                    if (!char.IsDigit(s[i])) { allDigits = false; break; }
                if (allDigits) s = s.Substring(underscore + 1);
            }

            s = s.Replace('_', ' ');

            // Split runs-together words: MyRoom -> My Room. Only on a
            // lower-to-upper boundary, so an acronym stays whole.
            var sb = new System.Text.StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]) && s[i - 1] != ' ')
                    sb.Append(' ');
                sb.Append(s[i]);
            }
            s = sb.ToString().Trim();

            if (s.Length == 0) return targetToken;   // nothing left; the token beats blank
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
