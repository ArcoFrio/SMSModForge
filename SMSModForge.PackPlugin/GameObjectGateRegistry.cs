using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Per-pack registry of GameObjects whose active state is driven by
    /// authored conditions (<c>activeConditions</c> on a place's
    /// <c>gameObjects</c> node).
    /// <para/>
    /// This is the declarative alternative to gating an object from an
    /// integration rule: the condition lives on the object it controls, so a
    /// set of objects with mutually exclusive conditions takes turns on its
    /// own — no if/else-if cascade, no "remember what I last switched on"
    /// bookkeeping, and no ordering between rules to get right.
    /// <para/>
    /// Registered by <see cref="PlaceFactory"/> as each object is built and
    /// evaluated once per frame from <see cref="Plugin"/>'s tick, alongside the
    /// integration rules. Keyed by pack id (the same way
    /// <see cref="TimerRuntime"/> is) because objects are built before the
    /// pack's <see cref="PackContext"/> exists.
    /// </summary>
    public sealed class GameObjectGateRegistry
    {
        private sealed class Gate
        {
            public GameObject Go;
            public JArray Conditions;
            /// <summary>False = latch on first pass and never switch back off.</summary>
            public bool DeactivateWhenUnmet;
            /// <summary>Previous evaluation, for the latch's rising edge.</summary>
            public bool LastPassed;
            /// <summary>Log-only label for diagnosing a gate that never passes.</summary>
            public string Label;
        }

        private readonly List<Gate> _gates = new List<Gate>();

        private static readonly Dictionary<string, GameObjectGateRegistry> _byPack =
            new Dictionary<string, GameObjectGateRegistry>();

        /// <summary>The registry for a pack, created on first use.</summary>
        public static GameObjectGateRegistry ForPack(string packId)
        {
            string k = packId ?? "?";
            if (!_byPack.TryGetValue(k, out var reg))
            {
                reg = new GameObjectGateRegistry();
                _byPack[k] = reg;
            }
            return reg;
        }

        /// <summary>Drop a pack's gates — called before its places are rebuilt so
        /// a scene reload doesn't accumulate stale GameObject references.</summary>
        public static void ResetPack(string packId)
        {
            if (_byPack.TryGetValue(packId ?? "?", out var reg)) reg._gates.Clear();
        }

        public void Register(GameObject go, JArray conditions, bool deactivateWhenUnmet, string label)
        {
            if (go == null || conditions == null || conditions.Count == 0) return;
            _gates.Add(new Gate
            {
                Go = go,
                Conditions = conditions,
                DeactivateWhenUnmet = deactivateWhenUnmet,
                // Seed from the object's built state so a latch that is already
                // on doesn't count the first passing tick as a fresh edge.
                LastPassed = go.activeSelf,
                Label = label,
            });
        }

        public int Count => _gates.Count;

        /// <summary>
        /// Evaluate every gate. A continuously-gated object is driven to match
        /// its conditions each tick — compared against <c>activeSelf</c> rather
        /// than a cached flag, so it also self-corrects if something else toggled
        /// it. A latched object is only ever switched on, and only on the tick
        /// its conditions start passing.
        /// </summary>
        public void Tick(PackContext ctx, ManualLogSource log)
        {
            for (int i = _gates.Count - 1; i >= 0; i--)
            {
                var g = _gates[i];
                if (g.Go == null) { _gates.RemoveAt(i); continue; }   // level destroyed

                bool passed = ConditionEvaluator.All(g.Conditions, ctx.Vars, log, ctx.PackId);

                if (g.DeactivateWhenUnmet)
                {
                    if (passed != g.Go.activeSelf) g.Go.SetActive(passed);
                }
                else if (passed && !g.LastPassed && !g.Go.activeSelf)
                {
                    g.Go.SetActive(true);
                }
                g.LastPassed = passed;
            }
        }
    }
}
