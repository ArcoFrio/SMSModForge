using Newtonsoft.Json.Linq;
using System.Globalization;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Parsed visibility-condition list for a navigator or World Map radial
    /// button. Shared by <see cref="NavigatorRuntime"/> and
    /// <see cref="RadialButtonRuntime"/> so both button families evaluate the
    /// manifest's <c>conditions</c> arrays identically.
    /// <para/>
    /// Button conditions now use the same vocabulary as dialogue / rule
    /// conditions and are evaluated through <see cref="ConditionEvaluator"/>,
    /// so they support typed comparisons and nested <c>All</c>/<c>Any</c>
    /// groups (AND/OR with parentheses). For backward compatibility the legacy
    /// button shape is normalised at parse time:
    /// <list type="bullet">
    ///   <item><c>{ "variable": "X" }</c> → <c>VariableEquals X == true</c>.</item>
    ///   <item><c>{ "variable": "X", "minValue": N }</c> →
    ///   <c>VariableGreaterOrEqual X &gt;= N</c>.</item>
    /// </list>
    /// The top-level array is an implicit AND (all must pass). Conditions
    /// reference <em>pack variables</em>.
    /// </summary>
    public sealed class ButtonConditionSet
    {
        private readonly JArray _conds;   // normalised to typed condition objects
        private readonly string _packId;

        private ButtonConditionSet(JArray conds, string packId)
        {
            _conds = conds;
            _packId = packId;
        }

        /// <summary>
        /// Parse a manifest <c>conditions</c> array. Returns null when the
        /// array is missing / empty / carries no usable entries — callers
        /// treat null as "unconditional".
        /// </summary>
        public static ButtonConditionSet Parse(JArray arr, string packId)
        {
            if (arr == null || arr.Count == 0) return null;
            var conds = new JArray();
            foreach (var c in arr)
            {
                var norm = Normalize(c as JObject);
                if (norm != null) conds.Add(norm);
            }
            return conds.Count > 0 ? new ButtonConditionSet(conds, packId) : null;
        }

        /// <summary>
        /// Maps a legacy <c>{variable, minValue?}</c> entry to its typed
        /// equivalent; passes typed/group entries (anything with a
        /// <c>type</c>) through untouched. Returns null for entries that are
        /// neither (e.g. a legacy entry with no variable name).
        /// </summary>
        private static JObject Normalize(JObject co)
        {
            if (co == null) return null;
            if (co["type"] != null) return co;   // already a typed leaf or All/Any group

            string variable = (string)co["variable"];
            if (string.IsNullOrEmpty(variable)) return null;

            int? minValue = (int?)co["minValue"];
            return new JObject
            {
                ["type"] = minValue.HasValue ? "VariableGreaterOrEqual" : "VariableEquals",
                ["params"] = new JObject
                {
                    ["name"] = variable,
                    ["value"] = minValue.HasValue
                        ? minValue.Value.ToString(CultureInfo.InvariantCulture)
                        : "true",
                },
            };
        }

        /// <summary>
        /// Evaluate against the owning pack's variable store. Fails closed
        /// while the store isn't resolvable (packs still mid-load) — the
        /// button appears within a frame once contexts exist, which beats
        /// flashing a locked destination.
        /// </summary>
        public bool Pass()
        {
            var vars = Plugin.TryGetPackVars(_packId);
            if (vars == null) return false;
            return ConditionEvaluator.All(_conds, vars, Plugin.Log, _packId);
        }
    }
}
