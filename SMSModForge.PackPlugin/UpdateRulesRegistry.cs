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
        public sealed class Entry
        {
            /// <summary>Pack-local key — used in log lines and edge tracking.</summary>
            public string Key;

            /// <summary>Raw JSON arrays kept as-is so they can be passed straight
            /// to the existing <see cref="ConditionEvaluator"/> and
            /// <see cref="ActionRuntime.ExecuteList"/>.</summary>
            public JArray Conditions;
            public JArray Actions;

            /// <summary>Trigger mode parsed from the manifest string.</summary>
            public TriggerMode Mode;

            // ── Per-tick mutable state ───────────────────────────────────
            /// <summary>Whether the conditions passed on the previous tick. Edge
            /// modes diff this against the current result.</summary>
            public bool LastPassing;

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
                bool passing = ConditionEvaluator.All(e.Conditions, ctx.Vars, log, ctx.PackId);
                bool fire;
                switch (e.Mode)
                {
                    case TriggerMode.WhilePassing:
                        fire = passing;
                        break;
                    case TriggerMode.OnRisingEdge:
                        fire = passing && !e.LastPassing;
                        break;
                    case TriggerMode.OnFallingEdge:
                        fire = !passing && e.LastPassing;
                        break;
                    case TriggerMode.OnSceneLoad:
                    case TriggerMode.OnDayChange:
                        // Armed by the dispatcher when the underlying
                        // event happens. Conditions still gate the fire.
                        // Auto-disarms after firing so the rule only
                        // runs once per event.
                        fire = e.ArmedForOneShot && passing;
                        if (fire) e.ArmedForOneShot = false;
                        break;
                    default:
                        fire = false;
                        break;
                }
                e.LastPassing = passing;

                if (fire)
                {
                    try
                    {
                        ActionRuntime.ExecuteList(e.Actions, ctx);
                    }
                    catch (System.Exception ex)
                    {
                        log?.LogError("[SMSModForge.PackPlugin] Integration rule '" +
                            e.Key + "' in " + ctx.PackId + " threw: " + ex.Message);
                    }
                }
            }
        }
    }
}
