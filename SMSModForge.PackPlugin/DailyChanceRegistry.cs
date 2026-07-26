using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Assigns each <c>DailyChance</c> condition its own roll identity and
    /// indexes them for the day-change report.
    /// <para/>
    /// The condition authors no name: every occurrence is an independent
    /// gate, and its identity is derived from where it sits in the manifest
    /// (owning dialogue / rule / place key + an ordinal within that entity).
    /// That identity is stamped onto the condition's params as
    /// <see cref="IdParam"/> at load — an in-memory edit of the parsed
    /// manifest, never written back to the pack — and the evaluator seeds its
    /// roll from it.
    /// <para/>
    /// Deriving instead of authoring means two gates can't accidentally
    /// share a roll, and the log line names the thing the author recognises
    /// ("AmberDialogueDefault #1") rather than a hand-maintained key. The
    /// trade-off is that the identity — and so the roll — shifts if the
    /// entity is renamed or its conditions reordered; that only changes
    /// which days a gate passes on, and only when the pack itself changed.
    /// </summary>
    public sealed class DailyChanceRegistry
    {
        /// <summary>Params key holding the derived roll identity. Underscored
        /// to mark it as runtime-injected rather than authored.</summary>
        public const string IdParam = "_rollId";

        public sealed class Entry
        {
            /// <summary>Seed for <see cref="ConditionEvaluator.StableRoll"/>.</summary>
            public string Id;
            /// <summary>Human label for the log — the owning entity, plus an
            /// ordinal when that entity has more than one gate.</summary>
            public string Label;
            /// <summary>Chance as a whole percentage (0..100).</summary>
            public float Percent;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        public IReadOnlyList<Entry> All => _entries;

        public void Reset() => _entries.Clear();

        /// <summary>
        /// Walk the manifest, stamping every DailyChance condition with a
        /// unique identity and recording it for reporting. The sweep is
        /// blind-recursive because these conditions can sit anywhere a
        /// condition list can (dialogue start conditions, node conditions,
        /// integration rules and their else-branches, level hooks, button
        /// visibility) and nest inside AND/OR groups; the enclosing entity is
        /// tracked by remembering the nearest ancestor carrying a "key".
        /// </summary>
        public void CollectFrom(JToken root)
        {
            if (root == null) return;
            _perEntity.Clear();
            Walk(root, "pack");
            // Only add the "#n" suffix where an entity actually has several
            // gates — a lone gate reads better as just its entity name.
            foreach (var e in _entries)
                if (_perEntity.TryGetValue(EntityOf(e.Id), out var n) && n == 1)
                    e.Label = EntityOf(e.Id);
        }

        private readonly Dictionary<string, int> _perEntity = new Dictionary<string, int>();

        private static string EntityOf(string id)
        {
            int hash = id.LastIndexOf('#');
            return hash < 0 ? id : id.Substring(0, hash);
        }

        private void Walk(JToken t, string entity)
        {
            if (t is JObject o)
            {
                // Entering a keyed entity (dialogue, rule, place…) renames the
                // scope for everything beneath it.
                var keyTok = o["key"];
                if (keyTok != null && keyTok.Type == JTokenType.String)
                {
                    string k = (string)keyTok;
                    if (!string.IsNullOrWhiteSpace(k)) entity = k;
                }

                if ((string)o["type"] == "DailyChance")
                {
                    var p = o["params"] as JObject;
                    if (p == null) { p = new JObject(); o["params"] = p; }
                    if (!float.TryParse((string)p["chance"] ?? "0", NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out var percent))
                        percent = 0f;

                    _perEntity.TryGetValue(entity, out int n);
                    n++;
                    _perEntity[entity] = n;

                    string id = entity + "#" + n.ToString(CultureInfo.InvariantCulture);
                    p[IdParam] = id;   // in-memory only — the pack file is untouched
                    _entries.Add(new Entry { Id = id, Label = entity + " #" + n, Percent = percent });
                }

                foreach (var prop in o.Properties()) Walk(prop.Value, entity);
            }
            else if (t is JArray a)
            {
                foreach (var item in a) Walk(item, entity);
            }
        }
    }
}
