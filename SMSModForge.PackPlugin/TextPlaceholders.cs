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
