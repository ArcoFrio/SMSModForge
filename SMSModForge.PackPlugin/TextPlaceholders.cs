using System.Text.RegularExpressions;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Shared <c>[PV:name]</c> token substitution against a pack's variable
    /// store. Two consumers with different cadences:
    /// <list type="bullet">
    ///   <item><see cref="DialogueBuilder"/> — resolves dialogue text once
    ///   at build time (the GC2 node's text field is baked).</item>
    ///   <item><see cref="NavigatorRuntime"/> / <see cref="RadialButtonRuntime"/>
    ///   — resolve button labels <em>live</em> every Tick, so a label like
    ///   <c>[PV:MyVar_Label]</c> updates the moment an
    ///   Integration rule (or dialogue action) writes the variable.</item>
    /// </list>
    /// Unresolved tokens are left verbatim so authoring mistakes stay
    /// visible rather than silently vanishing.
    /// </summary>
    public static class TextPlaceholders
    {
        private static readonly Regex Rx =
            new Regex(@"\[PV:([^\]]+)\]", RegexOptions.Compiled);

        /// <summary>Cheap pre-check so per-frame callers can skip the regex
        /// for plain labels.</summary>
        public static bool HasAny(string text)
            => !string.IsNullOrEmpty(text) &&
               text.IndexOf("[PV:", System.StringComparison.Ordinal) >= 0;

        /// <summary>Whether <paramref name="text"/> may hold a token of either
        /// kind: the pack's <c>[PV:name]</c> or one of the game's words in
        /// braces (<see cref="GameTextTokens"/>).</summary>
        public static bool HasAnyToken(string text)
            => HasAny(text) || GameTextTokens.MayHave(text);

        /// <summary>
        /// Every token filled in: the pack's <c>[PV:name]</c> first - so a
        /// variable holding "{PC}" comes out as the name - then the game's
        /// words in braces. For anything a player reads that is not a line of
        /// dialogue: a line's braces are the game's to fill, which it does as
        /// the line is shown.
        /// </summary>
        public static string ResolveAll(string text, PackVariableStore vars)
            => GameTextTokens.Resolve(Resolve(text, vars));

        /// <summary>Substitute every <c>[PV:name]</c> token from
        /// <paramref name="vars"/>. Null store leaves tokens verbatim.</summary>
        public static string Resolve(string text, PackVariableStore vars)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('[') < 0) return text;
            return Rx.Replace(text, m =>
            {
                string name = m.Groups[1].Value.Trim();
                return vars != null ? vars.GetString(name) : m.Value;
            });
        }
    }
}
