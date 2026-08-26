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

                // ── List reads ────────────────────────────────────────────
                // Pack-only: GC2 globals have no list type, so there's no
                // 'source' toggle on either of these. A missing / non-List
                // variable reads as an empty list (GetList's own fallback),
                // so Contains is false and Count is 0 rather than throwing.
                case "ListContains":
                    {
                        string list = (string)p["list"];
                        if (string.IsNullOrEmpty(list) || vars == null) return false;
                        string needle = DerefValue((string)p["value"] ?? "", vars);
                        var items = vars.GetList(list);
                        for (int i = 0; i < items.Count; i++)
                            if (string.Equals(items[i], needle, System.StringComparison.Ordinal))
                                return true;
                        return false;
                    }

                case "ListCount":
                    {
                        string list = (string)p["list"];
                        if (string.IsNullOrEmpty(list) || vars == null) return false;
                        int count = vars.GetList(list).Count;
                        if (!float.TryParse((string)p["value"] ?? "0", NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out var target))
                            return false;
                        switch ((string)p["comparison"] ?? "equals")
                        {
                            case "greater than":     return count >  target;
                            case "greater or equal": return count >= target;
                            case "less than":        return count <  target;
                            case "less or equal":    return count <= target;
                            default:                 return count == target;
                        }
                    }

                // Real-time cooldown on an integration rule. Pure read: the
                // interval is restarted by UpdateRulesRegistry when the rule
                // FIRES, not here, so probing the branch cascade doesn't
                // consume the pulse and an elapsed timer stays hot until the
                // rule's other conditions let it through. See TimerRuntime.
                case "Timer":
                    return TimerRuntime.IsElapsed(thisPackId, (string)p[TimerRuntime.IdParam], p);

                // Prefix test on a variable's string value. Honours the same
                // pack/vanilla 'source' toggle as the other Variable*
                // conditions. Exists because compound identifiers are common —
                // a "<Room><Slot>" location means "which room is she in" is a
                // prefix test, not an equality test, and expressing it as a
                // pile of negated equals doesn't survive adding a location.
                case "VariableStartsWith":
                    {
                        string name = (string)p["name"];
                        if (string.IsNullOrEmpty(name)) return false;
                        string prefix = DerefValue((string)p["value"] ?? "", vars);
                        // An empty prefix matches everything, which is almost
                        // certainly an unfinished row rather than an intent.
                        if (prefix.Length == 0) return false;

                        string actual;
                        if (IsVanilla(p))
                        {
                            object g = GameVariableBridge.Get(name);
                            actual = g?.ToString() ?? "";
                        }
                        else
                        {
                            actual = vars?.GetString(name) ?? "";
                        }

                        bool ignoreCase = (bool?)p["ignoreCase"] ?? false;
                        return actual.StartsWith(prefix, ignoreCase
                            ? System.StringComparison.OrdinalIgnoreCase
                            : System.StringComparison.Ordinal);
                    }

                // Current weather, read from the vanilla rainy-day / snowy-day
                // game variables. 'negate' (handled by Evaluate) gives "clear".
                case "Weather":
                    switch ((string)p["state"] ?? "BadWeather")
                    {
                        case "Raining": return WeatherRuntime.IsRaining;
                        case "Snowing": return WeatherRuntime.IsSnowing;
                        default:        return WeatherRuntime.IsBadWeather;
                    }

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
                        string value = DerefValue((string)p["value"] ?? "", vars);
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
                        string value = DerefValue((string)p["value"] ?? "", vars);
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
                        // Deprecated — re-rolls on every evaluation. Kept so packs
                        // authored before DailyChance keep working; the editor no
                        // longer offers it (see NodeConditionTypes.Random).
                        if (!float.TryParse((string)p["chance"] ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var chance))
                            chance = 0f;
                        return Random.value < chance;
                    }
                case "DailyChance":
                    {
                        // 'chance' is a whole percentage (0..100) — the editor
                        // renders it with a trailing % and stores the bare number.
                        if (!float.TryParse((string)p["chance"] ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var dpercent))
                            dpercent = 0f;
                        float dchance = dpercent / 100f;
                        if (dchance <= 0f) return false;
                        if (dchance >= 1f) return true;
                        // Derived, not stored: the roll is a pure function of
                        // (pack, roll id, in-game day), so it's identical for
                        // every evaluation on that day — safe to poll
                        // per-frame — and it survives scene reloads and
                        // save/reload (no save-scumming) while changing at each
                        // day roll-over. The id is stamped onto the condition at
                        // load by DailyChanceRegistry from its position in the
                        // manifest, so each gate rolls independently without the
                        // author naming anything.
                        string rollId = (string)p[DailyChanceRegistry.IdParam] ?? "";
                        int rollDay = CurrentRollDay();
                        float rolled = StableRoll(thisPackId, rollId, rollDay, vars?.RollSeed ?? 0);
                        LogRollOnce(thisPackId, rollId, (string)p[DailyChanceRegistry.LabelParam],
                                    rolled, dpercent, rollDay, log);
                        return rolled < dchance;
                    }
                default:
                    log?.LogWarning("[SMSModForge.PackPlugin] Unknown condition type '" + type + "' — treating as false");
                    return false;
            }
        }

        /// <summary>
        /// Report a DailyChance the first time it is consulted on a given day.
        /// <para/>
        /// The value is derived, not rolled at a moment, and these conditions are
        /// polled every frame by the rules tick — logging each evaluation would
        /// bury the console within seconds. Keying on (pack, roll, day) reports
        /// every gate exactly once per in-game day, at the point something
        /// actually asked, which is what "when it rolled" means for a value that
        /// is computed on demand. The day is part of the key, so a new day
        /// reports afresh with nothing to reset.
        /// </summary>
        private static void LogRollOnce(string packId, string rollId, string label,
                                        float rolled, float percent, int day, ManualLogSource log)
        {
            if (log == null) return;
            string key = packId + "|" + rollId + "|" + day;
            lock (_loggedRolls) { if (!_loggedRolls.Add(key)) return; }

            var inv = CultureInfo.InvariantCulture;
            log.LogInfo("[SMSModForge.PackPlugin] " + packId + ": DailyChance " +
                        (string.IsNullOrEmpty(label) ? rollId : label) +
                        " (" + percent.ToString("0.##", inv) + "%) — rolled " +
                        (rolled * 100f).ToString("0.#", inv) + "% → " +
                        (rolled * 100f < percent ? "PASS" : "fail") +
                        " for day " + day + ".");
        }

        private static readonly HashSet<string> _loggedRolls = new HashSet<string>();

        /// <summary>Record that a roll has already been reported for this day,
        /// so the first evaluation doesn't print it a second time. Called by the
        /// whole-day calendar report, which covers every registered gate at
        /// once; anything it didn't cover still speaks for itself when asked.</summary>
        internal static void MarkRollLogged(string packId, string rollId, int day)
        {
            lock (_loggedRolls) _loggedRolls.Add(packId + "|" + rollId + "|" + day);
        }

        /// <summary>Forget which rolls have been reported. Called on scene load
        /// so the set can't grow without bound across a long session.</summary>
        internal static void ResetRollLog() { lock (_loggedRolls) _loggedRolls.Clear(); }

        /// <summary>The in-game day a DailyChance roll is keyed to.
        /// <c>DaysPassed</c> (monotonic), not <c>Day</c> (the 1-7 weekday),
        /// which would repeat the same roll every Monday.</summary>
        internal static int CurrentRollDay() => (int)GameVariableBridge.GetNumber("DaysPassed");

        /// <summary>
        /// Deterministic pseudo-random value in [0,1) derived from
        /// (save seed, pack, roll id, day) via an FNV-1a hash. Deterministic
        /// on purpose: the same inputs always give the same number, so a
        /// DailyChance returns one stable answer for the whole in-game day no
        /// matter how often it's polled, needs nothing persisted per-roll, and
        /// can't be re-rolled by reloading a save.
        /// <para/>
        /// <paramref name="saveSeed"/> is what keeps that determinism from
        /// becoming a fixed calendar: it's minted at random per save (see
        /// <see cref="PackVariableStore.RollSeed"/>), so two playthroughs pass
        /// on entirely different days even though each is internally stable.
        /// <para/>
        /// Internal rather than private so the day-change reporter can show
        /// the exact value the condition will compare against — a separate
        /// implementation there could drift from this one.
        /// </summary>
        internal static float StableRoll(string packId, string key, int day, int saveSeed)
        {
            unchecked
            {
                const uint prime = 16777619u;
                uint h = 2166136261u;
                string s = saveSeed.ToString(CultureInfo.InvariantCulture) + "|" +
                           (packId ?? "") + "|" + (key ?? "") + "|" +
                           day.ToString(CultureInfo.InvariantCulture);
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= prime; }
                // Avalanche (xorshift finisher) — FNV alone leaves adjacent
                // day numbers highly correlated in the low bits, which would
                // make consecutive days' rolls track each other.
                h ^= h >> 16; h *= 2246822507u;
                h ^= h >> 13; h *= 3266489909u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;   // 24 bits → [0,1)
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

        /// <summary>
        /// Resolves a <c>$varName</c> reference in a comparison VALUE to that
        /// variable's current value; anything else is returned verbatim.
        /// <para/>
        /// Mirrors the action side, so "is this list holding the value this
        /// other variable currently has" is expressible as a condition rather
        /// than only as an action. Without it a rule can compare against
        /// literals only, which is what forces the awkward "one negated equals
        /// per possible value" shape.
        /// <para/>
        /// <c>$$</c> escapes a literal dollar; an unknown variable resolves to
        /// empty, matching the stores' own reads.
        /// </summary>
        private static string DerefValue(string raw, PackVariableStore vars)
        {
            if (string.IsNullOrEmpty(raw) || raw[0] != '$') return raw;
            if (raw.Length > 1 && raw[1] == '$') return raw.Substring(1);
            return vars?.GetString(raw.Substring(1)) ?? "";
        }

        // ── Author-facing diagnostics ────────────────────────────────────

        /// <summary>
        /// Log one condition's pass/fail with its params and, for a variable
        /// comparison, the live value — so the reason is on the line rather than
        /// something to go and look up. Groups recurse, indented.
        /// <para/>
        /// Shared by the dialogue F12 dump and the integration-rule debug flag:
        /// both answer the same question ("why didn't this fire?"), and a second
        /// copy of the formatting would drift from this one.
        /// </summary>
        public static void DumpCondition(JObject c, PackContext ctx, string indent)
        {
            if (c == null || ctx == null) return;
            string type = (string)c["type"] ?? "?";
            bool negate = (bool?)c["negate"] ?? false;
            bool pass = Evaluate(c, ctx.Vars, ctx.Log, ctx.PackId);
            string flag = (pass ? "[PASS] " : "[FAIL] ") + (negate ? "NOT " : "");

            // Groups recurse; the group's own PASS/FAIL is the combined verdict.
            if (type == "All" || type == "Any")
            {
                ctx.Log?.LogInfo("[CondDebug] " + indent + flag + "group " + type);
                if (c["conditions"] is JArray kids)
                    foreach (var k in kids) DumpCondition(k as JObject, ctx, indent + "  ");
                return;
            }

            string detail = "";
            if (c["params"] is JObject p)
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var prop in p.Properties())
                    parts.Add(prop.Name + "=" + prop.Value);
                detail = " { " + string.Join(", ", parts.ToArray()) + " }";

                // For variable comparisons, append the live value so the author
                // sees WHY it passed/failed without checking anything else.
                string name = (string)p["name"];
                if (!string.IsNullOrEmpty(name))
                {
                    bool vanilla = string.Equals((string)p["source"], "vanilla",
                                       System.StringComparison.OrdinalIgnoreCase) ||
                                   type.StartsWith("GameVariable");
                    string current;
                    if (vanilla)
                    {
                        object g = GameVariableBridge.Get(name);
                        current = g != null ? g.ToString() : "<not found>";
                    }
                    else
                        current = ctx.Vars != null ? ctx.Vars.GetString(name) : "<no store>";
                    detail += " current=" + current;
                }
            }
            ctx.Log?.LogInfo("[CondDebug] " + indent + flag + type + detail);
        }
    }
}
