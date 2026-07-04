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
                var entry = new UpdateRulesRegistry.Entry
                {
                    Key = key,
                    Conditions = ro["conditions"] as JArray ?? new JArray(),
                    Actions = ro["actions"] as JArray ?? new JArray(),
                    Mode = ParseMode((string)ro["triggerMode"]),
                };
                registry.Register(entry);
                built++;
            }
            if (built > 0)
                log?.LogInfo("[SMSModForge.PackPlugin] Pack '" + pack.PackId +
                             "' registered " + built + " integration rule(s).");
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
