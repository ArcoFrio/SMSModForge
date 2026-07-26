using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Per-pack registry of Integration-tab Update rules — the
    /// pack-runtime equivalent of dropping an <c>if (cond) { do }</c>
    /// block into <c>MonoBehaviour.Update</c>. Each rule is a
    /// condition list + action list + trigger mode (mirror of
    /// <c>SMSModForge.Model.UpdateRuleDef</c>).
    /// <para/>
    /// Rules are evaluated by <see cref="Plugin"/>'s per-frame Tick
    /// after the dialogue dispatchers have run, so any state a
    /// dialogue node just mutated is visible to the rule on the same
    /// frame. Each rule's last-tick condition snapshot is held here
    /// so edge-trigger modes (rising / falling) don't need to walk
    /// the source list.
    /// </summary>
    public sealed class UpdateRulesRegistry
    {
        /// <summary>One conditions-gated action group. A rule is an ordered
        /// list of these: index 0 is the rule's own conditions+actions (the
        /// IF), the rest are the authored else-if chain. Raw JSON arrays kept
        /// as-is so they can be passed straight to the existing
        /// <see cref="ConditionEvaluator"/> / <see cref="ActionRuntime.ExecuteList"/>.</summary>
        public sealed class Branch
        {
            public JArray Conditions;
            public JArray Actions;
        }

        public sealed class Entry
        {
            /// <summary>Pack-local key — used in log lines and edge tracking.</summary>
            public string Key;

            /// <summary>Ordered if / else-if / else chain. Selection = the FIRST
            /// branch whose conditions all pass (empty conditions always pass,
            /// i.e. a trailing plain Else). Never empty — the factory always
            /// registers branch 0 from the rule's own conditions/actions.</summary>
            public List<Branch> Branches = new List<Branch>();

            /// <summary>Trigger mode parsed from the manifest string.</summary>
            public TriggerMode Mode;

            /// <summary>Optional parameter source — a literal CSV, a <c>$ListVar</c>,
            /// or a <c>$StringVar</c> holding CSV. Empty = the rule runs once,
            /// unparameterized (the original behavior).</summary>
            public string ForEach = "";

            /// <summary>Placeholder name substituted per value, written <c>{name}</c>.</summary>
            public string ForEachAs = "item";

            /// <summary>Per-value expansion of <see cref="Branches"/> with the
            /// placeholder substituted, built lazily and reused. Keyed by value,
            /// so a value that leaves and returns keeps its identity.</summary>
            public readonly Dictionary<string, List<Branch>> Expanded =
                new Dictionary<string, List<Branch>>();

            // ── Per-tick mutable state ───────────────────────────────────
            /// <summary>Index of the branch selected on the previous tick, or
            /// -1 when none passed. Edge modes diff this against the current
            /// selection: for a single-branch rule -1↔0 reproduces the old
            /// boolean rising/falling edges exactly, and with an else-if chain
            /// OnRisingEdge fires once each time the WINNING branch changes.
            /// <para/>
            /// Keyed by parameter value so each value edges independently —
            /// an unparameterized rule just uses the single "" key.</summary>
            public readonly Dictionary<string, int> LastSelected = new Dictionary<string, int>();

            public int GetLastSelected(string item)
                => LastSelected.TryGetValue(item, out var v) ? v : -1;

            /// <summary>Set true after the rule fires once for <see cref="TriggerMode.OnSceneLoad"/>
            /// / <see cref="TriggerMode.OnDayChange"/> so it only fires once per
            /// trigger event (re-armed by the dispatcher when the event recurs).</summary>
            public bool ArmedForOneShot;
        }

        /// <summary>
        /// Mirror of <see cref="SMSModForge.Model.UpdateRuleTriggerMode"/>.
        /// Kept independent so the plugin assembly doesn't need a
        /// reference back to the editor model.
        /// </summary>
        public enum TriggerMode
        {
            WhilePassing,
            OnRisingEdge,
            OnFallingEdge,
            OnSceneLoad,
            OnDayChange,
        }

        private readonly List<Entry> _entries = new List<Entry>();
        public IReadOnlyList<Entry> All => _entries;

        public void Register(Entry e) { if (e != null) _entries.Add(e); }
        public void Reset() => _entries.Clear();

        /// <summary>
        /// Arm every one-shot rule of the given <paramref name="mode"/>
        /// — called by the dispatcher when the underlying event
        /// occurs (scene load, day change). Conditions still have to
        /// pass for the rule to actually fire; arming just signals
        /// that the event happened.
        /// </summary>
        public void ArmOneShots(TriggerMode mode)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Mode == mode) _entries[i].ArmedForOneShot = true;
        }

        /// <summary>
        /// Per-frame evaluation. Walks every rule, evaluates its
        /// conditions, and fires actions according to the rule's
        /// trigger mode. Identical condition evaluator + action
        /// runtime as dialogue nodes use — there's no separate
        /// vocabulary for rules.
        /// </summary>
        public void Tick(PackContext ctx, ManualLogSource log)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];

                // Unparameterized rules run once under the "" key; a forEach
                // rule runs once per value, each with its own branches (the
                // placeholder substituted), edge state and Timer deadlines.
                if (string.IsNullOrEmpty(e.ForEach))
                {
                    TickOne(e, "", e.Branches, ctx, log);
                    continue;
                }

                var items = ActionRuntime.ReadListParam(e.ForEach, ctx);
                for (int n = 0; n < items.Count; n++)
                {
                    string item = items[n];
                    if (string.IsNullOrEmpty(item)) continue;
                    TickOne(e, item, ExpandFor(e, item), ctx, log);
                }
            }
        }

        /// <summary>Substituted copy of a rule's branches for one parameter
        /// value, built once and cached. Timer identities get the value
        /// appended so each value keeps an independent cooldown.</summary>
        private static List<Branch> ExpandFor(Entry e, string item)
        {
            if (e.Expanded.TryGetValue(item, out var cached)) return cached;

            string token = "{" + e.ForEachAs + "}";
            var expanded = new List<Branch>(e.Branches.Count);
            foreach (var b in e.Branches)
            {
                var conditions = (JArray)b.Conditions.DeepClone();
                var actions = (JArray)b.Actions.DeepClone();
                SubstituteAll(conditions, token, item);
                SubstituteAll(actions, token, item);
                TimerRuntime.SuffixIds(conditions, "@" + item);
                expanded.Add(new Branch { Conditions = conditions, Actions = actions });
            }
            e.Expanded[item] = expanded;
            return expanded;
        }

        /// <summary>Replace every occurrence of <paramref name="token"/> in every
        /// string value of a JSON tree. Deliberately blunt: it covers condition
        /// params, action params, nested condition groups and nested action
        /// branches (a DiceRoll's) without needing to know any of their shapes.</summary>
        private static void SubstituteAll(JToken node, string token, string value)
        {
            if (node == null) return;
            switch (node.Type)
            {
                case JTokenType.Object:
                    foreach (var prop in (JObject)node)
                        SubstituteAll(prop.Value, token, value);
                    break;
                case JTokenType.Array:
                    foreach (var child in (JArray)node)
                        SubstituteAll(child, token, value);
                    break;
                case JTokenType.String:
                    string s = (string)node;
                    if (!string.IsNullOrEmpty(s) && s.IndexOf(token, System.StringComparison.Ordinal) >= 0)
                        ((JValue)node).Value = s.Replace(token, value);
                    break;
            }
        }

        /// <summary>Evaluate + fire one rule instance (one parameter value, or the
        /// whole rule when unparameterized).</summary>
        private void TickOne(Entry e, string item, List<Branch> branches,
                             PackContext ctx, ManualLogSource log)
        {
            {
                // Cascade selection: first branch whose conditions all pass.
                // Empty condition lists pass (ConditionEvaluator.All treats
                // an empty array as true) — a trailing conditions-less
                // branch is therefore a plain Else.
                int selected = -1;
                for (int b = 0; b < branches.Count; b++)
                {
                    if (ConditionEvaluator.All(branches[b].Conditions, ctx.Vars, log, ctx.PackId))
                    {
                        selected = b;
                        break;
                    }
                }

                // Which branch's actions fire this tick (-1 = none). Edge
                // modes key off the SELECTION CHANGING, which for a rule
                // without else-branches degenerates to the old boolean
                // rising/falling edge (-1 ↔ 0).
                int lastSelected = e.GetLastSelected(item);
                int fireBranch = -1;
                switch (e.Mode)
                {
                    case TriggerMode.WhilePassing:
                        fireBranch = selected;
                        break;
                    case TriggerMode.OnRisingEdge:
                        // Fires once whenever a DIFFERENT branch wins —
                        // including from "nothing" (-1). Schedule cascades
                        // re-fire on every slot change, single-branch rules
                        // keep their old fire-once-on-true behavior.
                        if (selected >= 0 && selected != lastSelected)
                            fireBranch = selected;
                        break;
                    case TriggerMode.OnFallingEdge:
                        // Fires the branch that WAS active when the whole
                        // cascade stops matching (single-branch: identical
                        // to the old true→false edge).
                        if (selected < 0 && lastSelected >= 0)
                            fireBranch = lastSelected;
                        break;
                    case TriggerMode.OnSceneLoad:
                    case TriggerMode.OnDayChange:
                        // Armed by the dispatcher when the underlying
                        // event happens. Conditions still gate the fire.
                        // Auto-disarms after firing so the rule only
                        // runs once per event.
                        if (e.ArmedForOneShot && selected >= 0)
                        {
                            fireBranch = selected;
                            e.ArmedForOneShot = false;
                        }
                        break;
                }
                e.LastSelected[item] = selected;

                if (fireBranch >= 0)
                {
                    // Restart any Timer gates in the branch that just won.
                    // Doing it here (rather than inside the evaluator) is what
                    // makes a Timer a cooldown on FIRING rather than on being
                    // looked at — an elapsed timer stays hot until the branch's
                    // other conditions actually let it through.
                    TimerRuntime.RearmAllIn(branches[fireBranch].Conditions, ctx.PackId);
                    try
                    {
                        ActionRuntime.ExecuteList(branches[fireBranch].Actions, ctx);
                    }
                    catch (System.Exception ex)
                    {
                        log?.LogError("[SMSModForge.PackPlugin] Integration rule '" +
                            e.Key + "'" + (string.IsNullOrEmpty(item) ? "" : " [" + item + "]") +
                            " in " + ctx.PackId + " (branch " + fireBranch +
                            ") threw: " + ex.Message);
                    }
                }
            }
        }
    }
}
