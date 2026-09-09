using System;
using System.Text;

namespace SMSModForge.Shared
{
    /// <summary>
    /// Putting variables into a string: <c>Gifting_Gifted_$Gifting_Target</c>
    /// becomes <c>Gifting_Gifted_Anis</c>.
    /// <para/>
    /// Compiled into BOTH the editor and the runtime from this one file, rather
    /// than written twice. A pack that reads one way while it is being authored
    /// and another way while it is being played is worse than either behaviour
    /// on its own, and two copies of a parser drift.
    /// <para/>
    /// Substitution happens where the value is USED, never once and kept, so a
    /// name built from a variable follows that variable: pick Anis and it reads
    /// Gifting_Gifted_Anis, pick Amber a moment later and the same setting reads
    /// Gifting_Gifted_Amber.
    /// </summary>
    public static class VarText
    {
        /// <summary>
        /// A name starts with a letter or an underscore.
        /// <para/>
        /// Not a digit, and that matters: a pack is full of prices written
        /// "$1100", and reading those as variables would turn every one of them
        /// into nothing. Money is the commonest literal dollar there is.
        /// </summary>
        public static bool CanStartName(char c)
            => c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        /// <summary>The rest of a name. Hyphens are in because the game's own
        /// variables use them - Body-Oil, red-meat, Inv-energydrink.</summary>
        public static bool CanContinueName(char c)
            => CanStartName(c) || c == '-' || (c >= '0' && c <= '9');

        /// <summary>
        /// Replace every <c>$name</c> and <c>${name}</c> in
        /// <paramref name="raw"/> with what <paramref name="lookup"/> says.
        /// <para/>
        /// A name nothing has set becomes empty rather than being left as it was
        /// written. That is deliberate: "the recipient has not been chosen yet"
        /// should read as an empty answer, not as a list literally called
        /// <c>Gifting_Gifted_$Gifting_Target</c> that nothing will ever match.
        /// <para/>
        /// <c>$$</c> is a literal dollar. A dollar in front of anything that
        /// cannot start a name is left exactly where it is.
        /// </summary>
        /// <param name="lookup">Given a name, its value - or null when there is
        /// no such variable.</param>
        public static string Resolve(string raw, Func<string, string> lookup)
        {
            if (string.IsNullOrEmpty(raw) || raw.IndexOf('$') < 0) return raw;

            var built = new StringBuilder(raw.Length);

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c != '$') { built.Append(c); continue; }

                // "$$" - one literal dollar, and skip the second.
                if (i + 1 < raw.Length && raw[i + 1] == '$')
                {
                    built.Append('$');
                    i++;
                    continue;
                }

                // "${name}" - braces say where the name ends, for the times the
                // text carries straight on: "${Target}-suffix".
                if (i + 1 < raw.Length && raw[i + 1] == '{')
                {
                    int close = raw.IndexOf('}', i + 2);
                    if (close > i + 2)
                    {
                        string braced = raw.Substring(i + 2, close - i - 2);
                        built.Append(Value(lookup, braced));
                        i = close;
                        continue;
                    }
                    // No closing brace: nothing to read, so it stays as typed.
                    built.Append(c);
                    continue;
                }

                if (i + 1 >= raw.Length || !CanStartName(raw[i + 1]))
                {
                    built.Append(c);          // "$100", "$ ", a trailing "$"
                    continue;
                }

                int end = i + 1;
                while (end < raw.Length && CanContinueName(raw[end])) end++;

                built.Append(Value(lookup, raw.Substring(i + 1, end - i - 1)));
                i = end - 1;
            }

            return built.ToString();
        }

        private static string Value(Func<string, string> lookup, string name)
        {
            if (lookup == null || name.Length == 0) return "";
            string got = lookup(name);
            return got ?? "";
        }
    }
}
