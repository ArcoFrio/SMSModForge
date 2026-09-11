using BepInEx.Logging;
using GameCreator.Runtime.Dialogue.UnityUI;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Pushes a pack's actor name colours into the vanilla
    /// <c>TMPWordColorizer</c>. The colorizer drives the colour of the speaker
    /// label text by matching the rendered name against its private
    /// <c>wordColors</c> list — the host mod uses the same pattern in
    /// <c>Dialogues.AddActorColorToSpeechUI</c>. The list is private and has no
    /// add-API so we reflect to it.
    /// <para/>
    /// This used to run only from <see cref="DialogueDispatcher"/>, once per
    /// line of a pack's own dialogue, which meant a colour an author gave one
    /// of the GAME's characters appeared in the pack's conversations and
    /// nowhere else: the game's own scenes went on writing the name in the
    /// colour the game shipped. <see cref="Tick"/> is the answer — it watches
    /// for a speech UI to appear from any source, pack or game, and paints the
    /// colorizers it can reach before the first name is drawn.
    /// <para/>
    /// "The colorizers it can reach" is deliberately every loaded one rather
    /// than the ones under the live <see cref="SpeechUI"/>. The speech-skin
    /// PREFAB is in that sweep, so an instance made later is born carrying
    /// these colours instead of being repainted after its first name is
    /// already on screen — and the label is a GC2 <c>TextReference</c>, so the
    /// TMP object it resolves to is not guaranteed to sit under the SpeechUI
    /// component in the first place. Painting extra colorizers is harmless:
    /// the component matches a pair's word against its ENTIRE text, so an
    /// actor name can never match a line of dialogue.
    /// <para/>
    /// Because that reaches shared, non-scene objects, what was in each list
    /// before we touched it is kept and put back by <see cref="Forget"/> on
    /// scene unload. Otherwise the game's own colour for a name would be gone
    /// for the rest of the process, and the pack that replaced it would go on
    /// doing so after it had been unloaded.
    /// </summary>
    internal static class SpeechColorApplier
    {
        // We resolve the colorizer type + field lazily by name — we never
        // referenced its DLL directly so a hard compile-time link would
        // be brittle. The type lives at the top-level (no namespace).
        private static System.Type _colorizerType;
        private static System.Type _wordColorPairType;
        private static FieldInfo _wordColorsField;
        private static FieldInfo _wordField;
        private static FieldInfo _colorField;
        private static bool _resolved;

        // Remembers the last hierarchy the DISPATCHER populated, so the usual
        // case (same pack, same speech UI, same colours) costs three reference
        // compares per line rather than a rebuild of every colorizer's list.
        // The factory is part of the key because two packs with the same number
        // of colours are not the same colours, and leaving it out let the
        // second pack to speak be skipped entirely.
        private static RuntimeActorFactory _appliedBy;
        private static Object _appliedFor;
        private static int _appliedCount = -1;

        // ...and the same for the per-frame watch, which paints every loaded
        // pack at once and so is keyed on the total instead.
        private static Object _paintedFor;
        private static int _paintedColors = -1;
        private static bool _paintedOnce;

        /// <summary>What each colorizer's list held before this plugin first
        /// wrote to it. See the type doc: these are shared objects, and one of
        /// them is a prefab.</summary>
        private static readonly Dictionary<Object, object[]> _wasThere =
            new Dictionary<Object, object[]>();

        public static void Apply(RuntimeActorFactory factory, ManualLogSource log)
        {
            if (factory == null) return;
            if (!ResolveTypes(log)) return;

            var speech = SpeechUI.Current;
            int colorCount = factory.ColorCount;
            if (colorCount == 0) return;
            if (ReferenceEquals(_appliedBy, factory)
                && ReferenceEquals(_appliedFor, speech)
                && _appliedCount == colorCount) return;

            foreach (var colorizer in FindColorizers())
                ApplyTo(colorizer, factory);

            _appliedBy = factory;
            _appliedFor = speech;
            _appliedCount = colorCount;
        }

        /// <summary>
        /// Paint every loaded pack's colours as soon as there is somewhere to
        /// paint them, and again each time a new speech UI appears.
        /// <para/>
        /// Called once a frame. The whole cost in the ordinary case is a
        /// reference compare and a count, because the sweep only happens when
        /// the live <see cref="SpeechUI"/> is one this has not painted — which
        /// is once at load and once per conversation, including the game's own.
        /// </summary>
        public static void Tick(IReadOnlyList<PackContext> contexts, ManualLogSource log)
        {
            if (contexts == null || contexts.Count == 0) return;

            int total = 0;
            for (int i = 0; i < contexts.Count; i++)
            {
                var factory = contexts[i] != null ? contexts[i].ActorFactory : null;
                if (factory != null) total += factory.ColorCount;
            }
            if (total == 0) return;

            var speech = SpeechUI.Current;

            // Unity's null: no conversation is up, or the last one's UI has
            // been destroyed. Either way there is nothing new to paint, and
            // without this the sweep would run every frame between
            // conversations rather than once.
            if (speech == null)
            {
                if (_paintedOnce && _paintedColors == total) return;
            }
            else if (ReferenceEquals(_paintedFor, speech) && _paintedColors == total) return;

            if (!ResolveTypes(log)) return;

            var colorizers = FindColorizers();
            for (int i = 0; i < contexts.Count; i++)
            {
                var factory = contexts[i] != null ? contexts[i].ActorFactory : null;
                if (factory == null || factory.ColorCount == 0) continue;
                foreach (var colorizer in colorizers) ApplyTo(colorizer, factory);
            }

            _paintedFor = speech == null ? null : speech;
            _paintedColors = total;
            _paintedOnce = true;
        }

        /// <summary>
        /// Put every list back the way it was and forget what was painted.
        /// Called when the scene the packs were loaded into goes away.
        /// </summary>
        public static void Forget()
        {
            if (_wordColorsField != null)
            {
                foreach (var kv in _wasThere)
                {
                    if (kv.Key == null) continue;             // destroyed with its scene
                    if (!(_wordColorsField.GetValue(kv.Key) is IList list)) continue;
                    list.Clear();
                    foreach (var item in kv.Value) list.Add(item);
                }
            }
            _wasThere.Clear();

            _appliedBy = null;
            _appliedFor = null;
            _appliedCount = -1;
            _paintedFor = null;
            _paintedColors = -1;
            _paintedOnce = false;
        }

        /// <summary>Every colorizer worth populating — see the type doc for
        /// why that is all of them rather than the live speech UI's.</summary>
        private static List<Object> FindColorizers()
        {
            var found = new List<Object>();
            foreach (var c in Resources.FindObjectsOfTypeAll(_colorizerType))
                found.Add(c);
            return found;
        }

        private static void ApplyTo(Object colorizer, RuntimeActorFactory factory)
        {
            if (colorizer == null) return;
            if (!(_wordColorsField.GetValue(colorizer) is IList list)) return;

            if (!_wasThere.ContainsKey(colorizer))
            {
                var was = new object[list.Count];
                for (int i = 0; i < list.Count; i++) was[i] = list[i];
                _wasThere[colorizer] = was;
            }

            foreach (var kv in factory.EnumerateColors())
            {
                // Drop any existing entry for the same word (case-insensitive
                // — matches the colorizer's own match behaviour) before
                // appending, so the list stays compact across re-applies.
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var item = list[i];
                    if (item == null) continue;
                    var w = _wordField.GetValue(item) as string ?? "";
                    if (string.Equals(w, kv.Key, System.StringComparison.OrdinalIgnoreCase))
                        list.RemoveAt(i);
                }

                var pair = System.Activator.CreateInstance(_wordColorPairType);
                _wordField.SetValue(pair, kv.Key);
                _colorField.SetValue(pair, kv.Value);
                list.Add(pair);
            }
        }

        private static bool ResolveTypes(ManualLogSource log)
        {
            if (_resolved) return _colorizerType != null;
            _resolved = true;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("TMPWordColorizer");
                if (t == null) continue;
                _colorizerType = t;
                _wordColorsField = t.GetField("wordColors",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _wordColorPairType = t.GetNestedType("WordColorPair");
                if (_wordColorPairType != null)
                {
                    _wordField  = _wordColorPairType.GetField("word",  BindingFlags.Public | BindingFlags.Instance);
                    _colorField = _wordColorPairType.GetField("color", BindingFlags.Public | BindingFlags.Instance);
                }
                break;
            }
            if (_colorizerType == null || _wordColorsField == null ||
                _wordColorPairType == null || _wordField == null || _colorField == null)
            {
                log?.LogWarning("[SMSModForge.PackPlugin] TMPWordColorizer / WordColorPair shape not found — actor name colours will not apply.");
                _colorizerType = null;
                return false;
            }
            return true;
        }
    }
}
