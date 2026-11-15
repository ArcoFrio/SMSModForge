namespace SMSModForge.Shared
{
    /// <summary>
    /// <c>{name:key}</c>: a character's name as it is shown NOW, written into a pack's
    /// text instead of the name itself (ModForge 1.9.0).
    /// <para/>
    /// A pack can rename one of the game's characters, and the new name then shows
    /// wherever they speak. A line that names them in its words is a different matter:
    /// "Anna" typed into a line is four letters, and nothing can tell whether it means
    /// the game's Anna, a pack's own Anna or the word in somebody's nickname. A token
    /// says whom, so it can follow a rename and nothing else ever does.
    /// <para/>
    /// The key is the pack's own: one of its characters, or one of the game's by the
    /// key the editor gives them. Who that is gets decided by whoever reads the token,
    /// which knows the pack (<see cref="Resolve"/> takes the lookup). A key nobody
    /// answers to is left as written, so a typo shows rather than vanishing.
    /// <para/>
    /// Compiled into both projects: the editor draws and checks these, the plugin fills
    /// them in, and one parser keeps the two agreeing on what counts as one.
    /// </summary>
    public static class NameTokens
    {
        /// <summary>What every one starts with.</summary>
        public const string Opening = "{name:";

        private static readonly System.Text.RegularExpressions.Regex Rx =
            new System.Text.RegularExpressions.Regex(@"\{name:([^\s{}]+)\}",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>The token for <paramref name="key"/>.</summary>
        public static string Make(string key)
        {
            return Opening + (key ?? "") + "}";
        }

        /// <summary>Whether <paramref name="text"/> might hold one: worth the look.</summary>
        public static bool MayHave(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf(Opening, System.StringComparison.Ordinal) >= 0;
        }

        /// <summary>Whether <paramref name="braced"/> is exactly one token, and whose.</summary>
        public static bool TryParse(string braced, out string key)
        {
            key = null;
            if (string.IsNullOrEmpty(braced)) return false;
            var m = Rx.Match(braced);
            if (!m.Success || m.Index != 0 || m.Length != braced.Length) return false;
            key = m.Groups[1].Value;
            return true;
        }

        /// <summary>One token found in a text.</summary>
        public struct Found
        {
            public int Start;
            public int Length;
            public string Key;
        }

        /// <summary>Every token in <paramref name="text"/>, in order.</summary>
        public static System.Collections.Generic.List<Found> FindAll(string text)
        {
            var found = new System.Collections.Generic.List<Found>();
            if (!MayHave(text)) return found;
            foreach (System.Text.RegularExpressions.Match m in Rx.Matches(text))
                found.Add(new Found { Start = m.Index, Length = m.Length, Key = m.Groups[1].Value });
            return found;
        }

        /// <summary>
        /// <paramref name="text"/> with each token replaced by the name
        /// <paramref name="nameOf"/> gives its key. A key it returns null for is left as
        /// written.
        /// </summary>
        public static string Resolve(string text, System.Func<string, string> nameOf)
        {
            if (!MayHave(text) || nameOf == null) return text;
            return Rx.Replace(text, m =>
            {
                string name = nameOf(m.Groups[1].Value);
                return name ?? m.Value;
            });
        }
    }
}
