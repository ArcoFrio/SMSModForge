using BepInEx.Logging;
using Newtonsoft.Json.Linq;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Builds the per-pack <see cref="UpdateRulesRegistry"/> from the
    /// manifest's <c>integrationRules</c> array. One entry per rule;
    /// the runtime keeps the original JSON for the action / condition
    /// arrays so the existing evaluator + runtime functions can chew
    /// on them without translation.
    /// </summary>
    public static class UpdateRulesFactory
    {
        public static void BuildAll(PackManifest pack, UpdateRulesRegistry registry,
                                    ManualLogSource log)
        {
            var rules = pack.Root["integrationRules"] as JArray;
            if (rules == null || rules.Count == 0) return;

            int built = 0;
            foreach (var r in rules)
            {
                if (!(r is JObject ro)) continue;
                string key = (string)ro["key"];
                if (string.IsNullOrEmpty(key))
                {
                    log?.LogWarning("[SMSModForge.PackPlugin] Integration rule with no key " +
                                    "in pack " + pack.PackId + " — skipped.");
                    continue;
                }
                string forEachAs = (string)ro["forEachAs"];
                var entry = new UpdateRulesRegistry.Entry
                {
                    Key = key,
                    Mode = ParseMode((string)ro["triggerMode"]),
                    // Optional parameterization: run the rule once per value.
                    ForEach = (string)ro["forEach"] ?? "",
                    ForEachAs = string.IsNullOrEmpty(forEachAs) ? "item" : forEachAs,
                    DebugConditions = (bool?)ro["debugConditions"] ?? false,
                };
                // Branch 0 = the rule's own conditions + actions (the IF);
                // the optional "branches" array is the else-if chain, tried
                // in order — first passing branch wins. See the registry's
                // Tick for the selection/edge semantics.
                entry.Branches.Add(new UpdateRulesRegistry.Branch
                {
                    Conditions = ro["conditions"] as JArray ?? new JArray(),
                    Actions = ro["actions"] as JArray ?? new JArray(),
                });
                if (ro["branches"] is JArray branches)
                    foreach (var b in branches)
                        if (b is JObject bo)
                            entry.Branches.Add(new UpdateRulesRegistry.Branch
                            {
                                Conditions = bo["conditions"] as JArray ?? new JArray(),
                                Actions = bo["actions"] as JArray ?? new JArray(),
                            });

                // Give every Timer condition in the rule a stable identity so
                // its deadline survives across frames. In-memory edit of the
                // parsed manifest — the pack file is never written back.
                for (int bi = 0; bi < entry.Branches.Count; bi++)
                {
                    int ordinal = 0;
                    TimerRuntime.StampIds(entry.Branches[bi].Conditions, key, bi, ref ordinal);
                }

                registry.Register(entry);
                built++;
            }
            if (built > 0)
                log?.LogInfo("[SMSModForge.PackPlugin] Pack '" + pack.PackId +
                             "' registered " + built + " integration rule(s).");
        }

        /// <summary>
        /// A screen's "Opens when" list, as a rule of its own: when every
        /// condition becomes met, the screen opens - the moment it happens, and
        /// again each time they stop being met and are met once more. Closing
        /// it is the player's or an action's, so a screen they shut stays shut
        /// while nothing changes.
        /// <para/>
        /// A rule rather than a ticker of its own, because that is exactly what
        /// a rising-edge rule already is - the same evaluator, the same wait for
        /// the day to settle and the save warning to be answered, the same edge
        /// from the first look (met when the game loads opens it then), and a
        /// Timer that restarts when it fires.
        /// <para/>
        /// No conditions means nothing here: such a screen opens only when
        /// something opens it, or at load when it starts open.
        /// </summary>
        public static void BuildUiOpeners(PackManifest pack, UpdateRulesRegistry registry,
                                          ManualLogSource log)
        {
            var uis = pack?.Uis;
            if (uis == null) return;

            int built = 0;
            foreach (var token in uis)
            {
                // Only a screen of the pack's own has an id to open it by.
                if (!(token is JObject ui) || !string.IsNullOrEmpty((string)ui["source"])) continue;
                if (!(ui["openConditions"] is JArray conditions) || conditions.Count == 0) continue;

                string id = UiFactory.IdOf(ui);
                string key = "ui:" + id + ":opens";
                var entry = new UpdateRulesRegistry.Entry
                {
                    Key = key,
                    Mode = UpdateRulesRegistry.TriggerMode.OnRisingEdge,
                };
                entry.Branches.Add(new UpdateRulesRegistry.Branch
                {
                    Conditions = conditions,
                    Actions = new JArray(new JObject
                    {
                        ["type"] = "SetGameObjectActive",
                        ["params"] = new JObject { ["kind"] = "UI", ["target"] = id, ["active"] = "true" },
                    }),
                });
                int ordinal = 0;
                TimerRuntime.StampIds(conditions, key, 0, ref ordinal);

                registry.Register(entry);
                built++;
            }
            if (built > 0)
                log?.LogInfo("[SMSModForge.PackPlugin] Pack '" + pack.PackId +
                             "' has " + built + " screen(s) that open by their conditions.");
        }

        private static UpdateRulesRegistry.TriggerMode ParseMode(string s)
        {
            if (string.IsNullOrEmpty(s)) return UpdateRulesRegistry.TriggerMode.OnRisingEdge;
            // Tolerant parse — unknown values fall back to the default
            // so manifest typos don't crash pack init.
            if (System.Enum.TryParse<UpdateRulesRegistry.TriggerMode>(s, true, out var mode))
                return mode;
            return UpdateRulesRegistry.TriggerMode.OnRisingEdge;
        }
    }
}
