using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Stateless evaluator for pack-defined conditions (the typed JSON
    /// objects with <c>type</c> + <c>params</c> + <c>negate</c>). Used by
    /// both the dispatcher (dialogue-start conditions) and by the custom
    /// <c>Condition</c> subclass attached to nodes for per-node gating.
    /// <para/>
    /// Resolves variable references through the supplied
    /// <see cref="PackVariableStore"/>; scene queries go directly to
    /// <see cref="GameObject.Find"/>. GC2 global-variable reads go through
    /// reflection on <c>GlobalNameVariablesManager</c> so we don't need a
    /// compile-time reference to its DLL.
    /// <para/>
    /// <c>thisPackId</c> is threaded through so the <c>LevelActive</c>
    /// condition can resolve <c>place:&lt;key&gt;</c> tokens (which the
    /// editor emits) into the right entry in <see cref="PlaceRegistry"/>.
    /// </summary>
    public static class ConditionEvaluator
    {
        /// <summary>Evaluates an AND-conjunction of conditions. Empty/null list = true.</summary>
        public static bool All(JArray conditions, PackVariableStore vars, ManualLogSource log, string thisPackId = null)
        {
            if (conditions == null) return true;
            foreach (var c in conditions)
                if (!Evaluate((JObject)c, vars, log, thisPackId)) return false;
            return true;
        }

        /// <summary>
        /// Evaluates an OR-disjunction of conditions — true if any child passes.
        /// An empty/null list imposes no constraint (returns true), matching
        /// <see cref="All"/> so a freshly-added, not-yet-filled group never
        /// silently blocks the thing it gates.
        /// </summary>
        public static bool Any(JArray conditions, PackVariableStore vars, ManualLogSource log, string thisPackId = null)
        {
            if (conditions == null || conditions.Count == 0) return true;
            foreach (var c in conditions)
                if (Evaluate((JObject)c, vars, log, thisPackId)) return true;
            return false;
        }

        /// <summary>Evaluates one condition object, applying its <c>negate</c> flag.</summary>
        public static bool Evaluate(JObject c, PackVariableStore vars, ManualLogSource log, string thisPackId = null)
        {
            if (c == null) return true;
            bool result = EvaluateInner(c, vars, log, thisPackId);
            bool negate = (bool?)c["negate"] ?? false;
            return negate ? !result : result;
        }

        private static bool EvaluateInner(JObject c, PackVariableStore vars, ManualLogSource log, string thisPackId)
        {
            string type = (string)c["type"];
            var p = c["params"] as JObject ?? new JObject();

            switch (type)
            {
                case "AlwaysTrue":
                    return true;

                // ── Logical groups (parentheses) ──────────────────────────
                // A group carries a nested `conditions` array instead of
                // `params`. `negate` (handled in Evaluate) inverts the whole
                // group, so NOT(...) is just a negated group.
                case "All":
                    return All(c["conditions"] as JArray, vars, log, thisPackId);
                case "Any":
                    return Any(c["conditions"] as JArray, vars, log, thisPackId);

                // The Variable* conditions carry an optional 'source' param
                // ("pack" default | "vanilla"). Pack reads the per-pack store;
                // vanilla reads the GC2 GlobalNameVariable through the bridge
                // (the same path the legacy GameVariable* cases below use).
                case "VariableEquals":
                    {
                        string name = (string)p["name"];
                        string value = (string)p["value"] ?? "";
                        if (string.IsNullOrEmpty(name)) return false;
                        if (IsVanilla(p))
                        {
                            object g = GameVariableBridge.Get(name);
                            return g != null && string.Equals(g.ToString(), value, System.StringComparison.Ordinal);
                        }
                        return vars.Compare(name, value) == 0;
                    }
                case "VariableGreaterThan":
                    {
                        string name = (string)p["name"];
                        if (string.IsNullOrEmpty(name)) return false;
                        if (IsVanilla(p)) return CompareGameVarNumber(p, (a, b) => a > b);
                        return vars.Compare(name, (string)p["value"] ?? "0") > 0;
                    }
                case "VariableLessThan":
                    {
                        string name = (string)p["name"];
                        if (string.IsNullOrEmpty(name)) return false;
                        if (IsVanilla(p)) return CompareGameVarNumber(p, (a, b) => a < b);
                        return vars.Compare(name, (string)p["value"] ?? "0") < 0;
                    }
                case "VariableGreaterOrEqual":
                    {
                        string name = (string)p["name"];
                        if (string.IsNullOrEmpty(name)) return false;
                        if (IsVanilla(p)) return CompareGameVarNumber(p, (a, b) => a >= b);
                        return vars.Compare(name, (string)p["value"] ?? "0") >= 0;
                    }
                case "VariableLessOrEqual":
                    {
                        string name = (string)p["name"];
                        if (string.IsNullOrEmpty(name)) return false;
                        if (IsVanilla(p)) return CompareGameVarNumber(p, (a, b) => a <= b);
                        return vars.Compare(name, (string)p["value"] ?? "0") <= 0;
                    }
                case "VariableExists":
                    {
                        string name = (string)p["name"];
                        if (string.IsNullOrEmpty(name)) return false;
                        if (IsVanilla(p)) return GameVariableBridge.Get(name) != null;
                        return vars.Exists(name);
                    }
                case "GameVariableEquals":
                    {
                        string name = (string)p["name"];
                        string value = (string)p["value"] ?? "";
                        object got = GameVariableBridge.Get(name);
                        if (got == null) return false;
                        return string.Equals(got.ToString(), value, System.StringComparison.Ordinal);
                    }
                case "GameVariableNumberGreaterThan":
                    return CompareGameVarNumber(p, (a, b) => a >  b);
                case "GameVariableNumberGreaterOrEqual":
                    return CompareGameVarNumber(p, (a, b) => a >= b);
                case "GameVariableNumberLessThan":
                    return CompareGameVarNumber(p, (a, b) => a <  b);
                case "GameVariableNumberLessOrEqual":
                    return CompareGameVarNumber(p, (a, b) => a <= b);
                case "LevelActive":
                    {
                        string token = (string)p["level"];
                        if (string.IsNullOrEmpty(token)) return false;
                        var level5 = GameObject.Find("5_Levels")?.transform;
                        if (level5 == null) return false;

                        // The editor emits scheme-prefixed tokens
                        // (vanilla:<goName> / place:<key>) so the same picker
                        // can drive both kinds of references. Translate
                        // place: → self: for PlaceRegistry, then fall back to
                        // a literal name lookup for legacy / hand-edited
                        // bare tokens.
                        if (HasScheme(token))
                        {
                            string resolveToken = token.StartsWith("place:")
                                ? "self:" + token.Substring("place:".Length)
                                : token;
                            var entry = PlaceRegistry.Resolve(resolveToken, thisPackId, level5);
                            if (entry?.Level != null) return entry.Level.activeSelf;
                            // Fall through to literal lookup if registry resolution
                            // failed — covers the case where the user typed a custom
                            // scheme-prefixed name that points at no registered place.
                            return false;
                        }

                        // Bare GO name under 5_Levels (back-compat).
                        var t = level5.Find(token);
                        return t != null && t.gameObject.activeSelf;
                    }
                case "GameObjectActive":
                    {
                        string path = (string)p["path"];
                        if (string.IsNullOrEmpty(path)) return false;
                        // Try absolute path first, then a recursive search.
                        var go = GameObject.Find(path);
                        return go != null && go.activeInHierarchy;
                    }
                case "Random":
                    {
                        if (!float.TryParse((string)p["chance"] ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var chance))
                            chance = 0f;
                        return Random.value < chance;
                    }
                default:
                    log?.LogWarning("[SMSModForge.PackPlugin] Unknown condition type '" + type + "' — treating as false");
                    return false;
            }
        }

        /// <summary>
        /// Numeric comparison against a GC2 global. The GNV value is
        /// pulled through <see cref="GameVariableBridge.GetNumber"/>
        /// (returns 0 when missing — the comparison then runs against
        /// the threshold and is usually false, matching the
        /// "missing = doesn't pass" semantic the rest of the
        /// evaluator uses).
        /// </summary>
        private static bool CompareGameVarNumber(JObject p, System.Func<double, double, bool> op)
        {
            string name = (string)p["name"];
            if (string.IsNullOrEmpty(name)) return false;
            if (!double.TryParse((string)p["value"] ?? "0", NumberStyles.Float,
                                  CultureInfo.InvariantCulture, out var threshold))
                threshold = 0d;
            double current = GameVariableBridge.GetNumber(name);
            return op(current, threshold);
        }

        private static bool HasScheme(string token)
        {
            int colon = token.IndexOf(':');
            return colon > 0 && colon < token.Length - 1;
        }

        /// <summary>A Variable* condition whose 'source' param selects the vanilla
        /// GC2 GlobalNameVariable store rather than the per-pack store.</summary>
        private static bool IsVanilla(JObject p)
            => string.Equals((string)p["source"], "vanilla", System.StringComparison.OrdinalIgnoreCase);
    }
}
