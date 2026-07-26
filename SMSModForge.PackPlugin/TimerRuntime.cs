using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// State behind the <c>Timer</c> condition — a real-time cooldown on an
    /// integration rule.
    /// <para/>
    /// Semantics, which mirror how a roaming schedule is normally hand-written
    /// (<c>if (Time.time &lt; nextRoamTime) continue; … nextRoamTime = Time.time
    /// + Random.Range(min, max);</c>):
    /// <list type="bullet">
    ///   <item>The condition is a pure read — "has the deadline passed?". It
    ///   never mutates state during evaluation, so the branch cascade can
    ///   probe it freely without burning the pulse.</item>
    ///   <item>The deadline is pushed forward only when the owning rule branch
    ///   actually <em>fires</em> (<see cref="Rearm"/>, called from
    ///   <see cref="UpdateRulesRegistry.Tick"/>). So a timer that has elapsed
    ///   stays hot while the rule's other conditions are false, and fires the
    ///   instant they pass — the "wait 15-45s, but don't move her while the
    ///   player is watching that room" behaviour.</item>
    ///   <item>An unseen timer starts already elapsed, so a rule fires once
    ///   promptly after load and then on the interval. A fresh load otherwise
    ///   leaves everything frozen for the first full interval.</item>
    /// </list>
    /// State is in-memory and keyed by pack + the id stamped on the condition
    /// by <see cref="UpdateRulesFactory"/>; it is deliberately not persisted,
    /// so reloading a save re-randomises rather than resuming a stale deadline.
    /// </summary>
    internal static class TimerRuntime
    {
        /// <summary>Params key holding the derived per-condition identity.
        /// Underscored to mark it runtime-injected rather than authored.</summary>
        public const string IdParam = "_timerId";

        private static readonly Dictionary<string, float> _deadlines = new Dictionary<string, float>();

        private static string Key(string packId, string timerId) => (packId ?? "?") + "/" + timerId;

        /// <summary>True once the interval has elapsed. First sight of a timer
        /// registers it as already elapsed (see class remarks) — unless the
        /// condition asks to <c>stagger</c>, in which case the FIRST wait is a
        /// fresh roll from the same interval.
        /// <para/>
        /// Staggering exists because several rules created at the same moment
        /// (one per character, say) would otherwise all start elapsed and fire
        /// together on the first tick. Their subsequent waits already diverge —
        /// each rule owns its own deadline and re-rolls independently — so this
        /// only breaks up that initial simultaneous burst, and it keeps the same
        /// authored interval throughout.</summary>
        public static bool IsElapsed(string packId, string timerId, JObject p)
        {
            if (string.IsNullOrEmpty(timerId)) return true;
            string k = Key(packId, timerId);
            if (!_deadlines.TryGetValue(k, out float deadline))
            {
                deadline = ParseBool(p?["stagger"]) ? Time.time + NextInterval(p) : Time.time;
                _deadlines[k] = deadline;
            }
            return Time.time >= deadline;
        }

        /// <summary>Restart the interval, rolling a fresh randomized wait when
        /// the condition asks for one. Called when the rule fires.</summary>
        public static void Rearm(string packId, string timerId, JObject p)
        {
            if (string.IsNullOrEmpty(timerId)) return;
            _deadlines[Key(packId, timerId)] = Time.time + NextInterval(p);
        }

        /// <summary>The wait to use for the next cycle, in seconds.</summary>
        private static float NextInterval(JObject p)
        {
            if (p == null) return 0f;
            if (ParseBool(p["randomize"]))
            {
                float min = ParseFloat(p["minSeconds"], 0f);
                float max = ParseFloat(p["maxSeconds"], min);
                if (max < min) { var t = min; min = max; max = t; }   // tolerate a swapped range
                return Random.Range(min, max);
            }
            return ParseFloat(p["seconds"], 0f);
        }

        /// <summary>Drop every timer belonging to a pack — called when the pack's
        /// context is torn down so a reload doesn't inherit stale deadlines.</summary>
        public static void ResetPack(string packId)
        {
            string prefix = (packId ?? "?") + "/";
            var doomed = new List<string>();
            foreach (var k in _deadlines.Keys)
                if (k.StartsWith(prefix, System.StringComparison.Ordinal)) doomed.Add(k);
            for (int i = 0; i < doomed.Count; i++) _deadlines.Remove(doomed[i]);
        }

        /// <summary>Walk a rule branch's condition array (groups included) and
        /// rearm every Timer in it.</summary>
        public static void RearmAllIn(JArray conditions, string packId)
        {
            if (conditions == null) return;
            foreach (var c in conditions)
            {
                if (!(c is JObject o)) continue;
                if ((string)o["type"] == "Timer")
                    Rearm(packId, (string)(o["params"] as JObject)?[IdParam], o["params"] as JObject);
                // Timers can sit inside AND/OR groups, which nest their children
                // under `conditions` rather than `params`.
                RearmAllIn(o["conditions"] as JArray, packId);
            }
        }

        /// <summary>
        /// Stamp a stable identity onto every Timer condition in a rule branch
        /// so its state can be looked up across frames. Derived from the owning
        /// rule key + an ordinal, the same way DailyChance derives its roll id —
        /// no authored name to keep in sync.
        /// <para/>
        /// The identity deliberately does NOT include the branch index: the
        /// ordinal restarts per branch, so the first Timer in every branch of a
        /// rule shares one id, and therefore one cooldown. That is what makes a
        /// branch cascade behave like a single rate-limited rule — whichever
        /// branch wins restarts the interval, so the wait is measured from the
        /// last time the rule DID something. Keying per branch instead gave each
        /// branch its own independent cooldown, so moving into a branch whose
        /// timer was already elapsed fired again immediately.
        /// </summary>
        public static void StampIds(JArray conditions, string ruleKey, int branch, ref int ordinal)
        {
            if (conditions == null) return;
            foreach (var c in conditions)
            {
                if (!(c is JObject o)) continue;
                if ((string)o["type"] == "Timer")
                {
                    var p = o["params"] as JObject;
                    if (p == null) { p = new JObject(); o["params"] = p; }
                    ordinal++;
                    p[IdParam] = ruleKey + "#" + ordinal.ToString(CultureInfo.InvariantCulture);
                }
                StampIds(o["conditions"] as JArray, ruleKey, branch, ref ordinal);
            }
        }

        /// <summary>
        /// Append a suffix to every Timer identity in a condition array. Used
        /// when a parameterized rule is expanded per value, so each value's
        /// copy of the rule owns an independent cooldown instead of them all
        /// sharing the authored rule's single deadline.
        /// </summary>
        public static void SuffixIds(JArray conditions, string suffix)
        {
            if (conditions == null || string.IsNullOrEmpty(suffix)) return;
            foreach (var c in conditions)
            {
                if (!(c is JObject o)) continue;
                if ((string)o["type"] == "Timer")
                {
                    var p = o["params"] as JObject;
                    if (p != null && p[IdParam] != null)
                        p[IdParam] = (string)p[IdParam] + suffix;
                }
                SuffixIds(o["conditions"] as JArray, suffix);
            }
        }

        private static bool ParseBool(JToken t)
        {
            if (t == null) return false;
            string s = (string)t;
            return !string.IsNullOrEmpty(s) &&
                   s.Equals("true", System.StringComparison.OrdinalIgnoreCase);
        }

        private static float ParseFloat(JToken t, float fallback)
        {
            if (t == null) return fallback;
            return float.TryParse((string)t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v : fallback;
        }
    }
}
