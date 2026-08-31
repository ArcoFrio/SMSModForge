using System;
using System.Collections.Generic;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Per-frame input state for the <c>InputKey</c> condition.
    /// <para/>
    /// <b>Why this exists rather than calling Input.GetKeyDown from the
    /// evaluator.</b> Unity's edge functions are true for exactly one frame, and
    /// they are only meaningful when read during that frame's Update. Neither
    /// holds for the things that evaluate pack conditions:
    /// <list type="bullet">
    ///   <item>Integration rules skip whole frames on purpose — they are held
    ///   until the vanilla save settles, and again around the day turnover (see
    ///   Plugin.Update). A press during one of those windows would be gone
    ///   before any rule looked.</item>
    ///   <item>A dialogue node's conditions are checked when GC2 reaches the
    ///   node, which is not tied to a frame boundary at all.</item>
    /// </list>
    /// So the plugin samples once per frame, early, and everything else reads
    /// what was sampled. An edge is then a fact about the frame rather than a
    /// question that has to be asked at the right instant.
    /// <para/>
    /// <b>How long an edge lasts.</b> One sampled frame — the same as Unity's
    /// own behaviour, so a press is true once and every consumer that runs in
    /// that frame agrees about it. Deliberately NOT latched until somebody
    /// consumes it: two rules watching the same key would then race to eat the
    /// press, and which one won would depend on pack load order.
    /// </summary>
    internal static class InputRuntime
    {
        private sealed class State
        {
            public bool Down;
            public bool Pressed;
            public bool Released;
        }

        /// <summary>Only the keys some loaded pack actually asks about. Packs
        /// name a handful between them; polling all ~320 KeyCodes every frame to
        /// answer questions nobody asked is work for nothing.</summary>
        private static readonly Dictionary<KeyCode, State> _watched =
            new Dictionary<KeyCode, State>();

        /// <summary>Parsed once per distinct token. Enum.Parse on a string is not
        /// expensive, but it is per-condition per-frame, and a mistyped token
        /// would throw on every one of them.</summary>
        private static readonly Dictionary<string, KeyCode> _parsed =
            new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Tokens already reported as unresolvable, so a hand-edited
        /// manifest naming a key that does not exist says so once instead of
        /// once per frame forever.</summary>
        private static readonly HashSet<string> _warned =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Clear everything on a reload — the watch list belongs to the
        /// packs that were loaded, not to the process.</summary>
        public static void Reset()
        {
            _watched.Clear();
            _parsed.Clear();
            _warned.Clear();
        }

        /// <summary>
        /// Resolve a KeyCode name, or null when it names nothing. Registers the
        /// key for sampling the first time it is seen, so the watch list builds
        /// itself out of the conditions that actually run.
        /// </summary>
        public static KeyCode? Resolve(string token, BepInEx.Logging.ManualLogSource log)
        {
            if (string.IsNullOrEmpty(token)) return null;

            if (_parsed.TryGetValue(token, out var known)) return known;

            // Enum.TryParse would also accept "324" and any comma-separated
            // combination, neither of which is a key an author picked.
            if (!Enum.IsDefined(typeof(KeyCode), token))
            {
                if (_warned.Add(token))
                    log?.LogWarning("[SMSModForge.PackPlugin] InputKey: '" + token +
                                    "' is not a key name. Nothing will match it.");
                return null;
            }

            var code = (KeyCode)Enum.Parse(typeof(KeyCode), token);
            _parsed[token] = code;
            if (!_watched.ContainsKey(code)) _watched[code] = new State();
            return code;
        }

        /// <summary>
        /// Read this frame's input for every watched key. Call once per frame,
        /// before anything that evaluates conditions.
        /// </summary>
        public static void Sample()
        {
            // The dictionary is mutated by Resolve while conditions run, so the
            // keys are copied before iterating.
            if (_watched.Count == 0) return;
            var codes = new KeyCode[_watched.Count];
            _watched.Keys.CopyTo(codes, 0);

            for (int i = 0; i < codes.Length; i++)
            {
                var st = _watched[codes[i]];
                bool down = Input.GetKey(codes[i]);
                st.Pressed = down && !st.Down;
                st.Released = !down && st.Down;
                st.Down = down;
            }
        }

        /// <summary>
        /// Whether the key is in the asked-about phase right now.
        /// <para/>
        /// A key registered mid-frame has not been sampled yet, so it reads as
        /// up and not pressed — right for the frame it was first seen, and
        /// correct from the next one on.
        /// </summary>
        public static bool Test(KeyCode code, string phase)
        {
            if (!_watched.TryGetValue(code, out var st)) return phase == "Up";

            switch (phase)
            {
                case "Pressed": return st.Pressed;
                case "Down": return st.Down;
                case "Released": return st.Released;
                case "Up": return !st.Down;
                // An unrecognised phase is a hand-edited manifest. Answering
                // "no" is the quiet failure; the validator catches it in the
                // editor, which is where it can be fixed.
                default: return false;
            }
        }
    }
}
