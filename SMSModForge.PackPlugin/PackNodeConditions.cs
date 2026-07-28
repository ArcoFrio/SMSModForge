using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Dialogue;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Evaluates a pack node's conditions OUTSIDE GC2's condition machinery.
    /// <para/>
    /// GC2 does not check <c>Node.m_Conditions</c> in place. <c>RunConditionsList.Check</c>
    /// builds a template GameObject holding the list, then evaluates on a pooled
    /// runner <c>Instantiate</c>d from that template — so what actually runs is a
    /// CLONE of each condition. A built-in GC2 condition survives that, because
    /// everything it needs is a serialized field. A pack condition does not: its
    /// binding (the authored JSON plus the pack context) is runtime-only state
    /// that Unity's clone cannot carry, and both
    /// <c>ConditionList.Check</c> (which skips null entries and returns true for
    /// an all-skipped AND list) and <c>PackCondition.Run</c> (which returns true
    /// when unbound) fail OPEN. The result was that every authored condition on
    /// every pack node passed, so lines played and choices appeared with their
    /// conditions plainly unmet.
    /// <para/>
    /// Rather than fight the clone, pack nodes are registered here and their
    /// conditions answered directly from the authored JSON. GC2 already asks at
    /// exactly the right moments — <c>Story</c>'s run loop as each node is
    /// popped, and the choice / random filters — so patching
    /// <see cref="Node.CanRun"/> keeps the evaluation timing the engine intends
    /// and only changes where the answer comes from. Vanilla nodes are not
    /// registered and fall straight through to the original.
    /// </summary>
    internal static class PackNodeConditions
    {
        private sealed class Binding
        {
            public JArray Conditions;
            public PackContext Ctx;
        }

        // Weak keys: dialogues are rebuilt on every CoreGameScene entry, and the
        // previous run's Nodes should be collectable without us having to know
        // when that happened.
        private static readonly ConditionalWeakTable<Node, Binding> Bound =
            new ConditionalWeakTable<Node, Binding>();

        public static void Register(Node node, JArray conditions, PackContext ctx)
        {
            if (node == null || conditions == null || conditions.Count == 0) return;
            Bound.Remove(node);
            Bound.Add(node, new Binding { Conditions = conditions, Ctx = ctx });
        }

        /// <summary>
        /// True when this node is pack-owned AND carries conditions, with
        /// <paramref name="passes"/> set to their combined (AND) result. False
        /// for anything the pack didn't author, so vanilla behaviour is untouched.
        /// </summary>
        public static bool TryEvaluate(Node node, out bool passes)
        {
            passes = true;
            if (node == null || !Bound.TryGetValue(node, out var b) || b.Ctx == null) return false;

            foreach (var c in b.Conditions)
            {
                if (!(c is JObject co)) continue;
                if (!ConditionEvaluator.Evaluate(co, b.Ctx.Vars, b.Ctx.Log, b.Ctx.PackId))
                {
                    passes = false;
                    break;
                }
            }
            return true;
        }

        // ── Patches ──────────────────────────────────────────────────────

        public static void Install(Harmony harmony, ManualLogSource log)
        {
            try
            {
                harmony.PatchAll(typeof(CanRunPatch));
                harmony.PatchAll(typeof(GetChoicesPatch));
            }
            catch (System.Exception e)
            {
                log?.LogError("[SMSModForge.PackPlugin] Could not patch node conditions — " +
                              "pack conditions on dialogue nodes will not be enforced. " + e.Message);
            }
        }

        [HarmonyPatch(typeof(Node), nameof(Node.CanRun))]
        private static class CanRunPatch
        {
            private static bool Prefix(Node __instance, ref bool __result)
            {
                if (!TryEvaluate(__instance, out bool passes)) return true;   // vanilla node
                __result = passes;
                return false;
            }
        }

        /// <summary>
        /// Choices need their own pass. <c>NodeTypeChoice.GetChoices</c> only drops
        /// an unavailable option when the dialogue skin's "hide unavailable" is on
        /// (or the caller asks), so a correct <see cref="Node.CanRun"/> alone still
        /// leaves a failing pack choice on screen and selectable. Filtering here
        /// applies to pack-authored options only — a vanilla choice keeps whatever
        /// the skin specifies.
        /// </summary>
        [HarmonyPatch(typeof(NodeTypeChoice), nameof(NodeTypeChoice.GetChoices))]
        private static class GetChoicesPatch
        {
            private static void Postfix(Story story, List<int> __result)
            {
                if (story == null || __result == null) return;
                for (int i = __result.Count - 1; i >= 0; i--)
                {
                    var node = story.Content.Get(__result[i]);
                    if (node != null && TryEvaluate(node, out bool passes) && !passes)
                        __result.RemoveAt(i);
                }
            }
        }
    }
}
