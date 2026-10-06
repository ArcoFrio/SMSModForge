using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The rest of an action list, waiting its turn (1.7.0).
    /// <para/>
    /// A list used to run in one go, all in the same frame: a Wait started a
    /// coroutine that waited and then did nothing, so the actions after it ran
    /// at once, and so would anything after a transition - changing the scene
    /// before the screen was covered, in full view. Now an action can hold the
    /// list: what follows it is kept here and run when the time comes.
    /// <para/>
    /// Not a coroutine, on purpose. A coroutine belongs to the object it was
    /// started on and stops when that object is switched off - a dialogue's
    /// host is switched off when the dialogue ends, mid-wait - and it counts
    /// game time, which stands still whenever the game sets its clock to zero.
    /// This is ticked from the plugin's own frame, on the real clock, and only
    /// let go of when the scene itself goes.
    /// </summary>
    internal static class ActionSequence
    {
        private sealed class Pending
        {
            public JArray Actions;
            public int From;
            public PackContext Ctx;
            public float At;
        }

        private static readonly List<Pending> _pending = new List<Pending>();

        /// <summary>Run <paramref name="actions"/> from <paramref name="from"/>
        /// on, <paramref name="afterSeconds"/> from now.</summary>
        public static void Resume(JArray actions, int from, PackContext ctx, float afterSeconds)
        {
            if (actions == null || from >= actions.Count) return;
            _pending.Add(new Pending
            {
                Actions = actions, From = from, Ctx = ctx,
                At = Time.unscaledTime + Mathf.Max(0f, afterSeconds),
            });
        }

        /// <summary>Run what is due. Called every frame of the game scene.</summary>
        public static void Tick()
        {
            if (_pending.Count == 0) return;
            float now = Time.unscaledTime;
            // A resumed list may hold again and add itself back: taken out
            // first, and the list walked by index, so that is safe.
            for (int i = 0; i < _pending.Count;)
            {
                var p = _pending[i];
                if (p.At > now) { i++; continue; }
                _pending.RemoveAt(i);
                ActionRuntime.ExecuteFrom(p.Actions, p.From, p.Ctx);
            }
        }

        /// <summary>Forget everything waiting: the scene it belonged to is gone.</summary>
        public static void Clear()
        {
            _pending.Clear();
        }
    }
}
