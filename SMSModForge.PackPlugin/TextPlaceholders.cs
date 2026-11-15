using System.Text.RegularExpressions;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Shared <c>[PV:name]</c> token substitution against a pack's variable
    /// store. Two consumers with different cadences:
    /// <list type="bullet">
    ///   <item><see cref="DialogueBuilder"/> — a line's text, read by the
    ///   game as the line is said (<see cref="GetStringPackLine"/>).</item>
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
        /// <paramref name="vars"/>, then every <c>{name:key}</c> with that
        /// character's name as it is shown now (1.9.0). Null store leaves tokens
        /// verbatim.</summary>
        public static string Resolve(string text, PackVariableStore vars)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.IndexOf('[') >= 0)
                text = Rx.Replace(text, m =>
                {
                    string name = m.Groups[1].Value.Trim();
                    return vars != null ? vars.GetString(name) : m.Value;
                });
            return vars == null ? text : ResolveNames(text, vars.PackId);
        }

        /// <summary>
        /// Every <c>{name:key}</c> in a pack's text, as the name its character is
        /// shown by: the pack's own character under that key, or the game's, with
        /// a new name some pack gave them (<see cref="NewNames"/>). A key that is
        /// nobody's stays as written, so the typo shows.
        /// </summary>
        public static string ResolveNames(string text, string packId)
        {
            if (!SMSModForge.Shared.NameTokens.MayHave(text)) return text;
            var actors = string.IsNullOrEmpty(packId) ? null : Plugin.TryGetPackActors(packId);
            return SMSModForge.Shared.NameTokens.Resolve(text, key =>
            {
                var entry = actors != null ? actors.ForNameToken(key) : null;
                if (entry != null) return entry.ShownName;
                // No pack to ask (or it has nobody by that key): the game's cast.
                string cast = SMSModForge.Shared.VanillaCastData.NameOfKey(key);
                return cast == null ? null : NewNames.NameOf(key) ?? cast;
            });
        }
    }
}
