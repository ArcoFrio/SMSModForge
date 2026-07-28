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
    /// <c>TMPWordColorizer</c> on the currently-active <see cref="SpeechUI"/>.
    /// The colorizer drives the colour of the speaker label text by matching
    /// the rendered name against its private <c>wordColors</c> list
    /// — the host mod uses the same pattern in
    /// <c>Dialogues.AddActorColorToSpeechUI</c>. The list is private and
    /// has no add-API so we reflect to it.
    /// <para/>
    /// We re-apply at the start of every pack dialogue (rather than at
    /// plugin load) because <c>SpeechUI.Current</c> doesn't exist until
    /// the DialogueUI has instantiated its speech-skin prefab, which only
    /// happens after the first dialogue plays. Re-applying is cheap
    /// (idempotent — duplicate entries for the same word are removed
    /// before adding the new one).
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

        // Remembers the last hierarchy we populated, so the usual case (same
        // speech UI, same colours) costs one reference compare per node rather
        // than a rebuild of every colorizer's list.
        private static Object _appliedFor;
        private static int _appliedCount = -1;

        public static void Apply(RuntimeActorFactory factory, ManualLogSource log)
        {
            if (factory == null) return;
            if (!ResolveTypes(log)) return;

            var speech = SpeechUI.Current;
            int colorCount = 0;
            foreach (var _ in factory.EnumerateColors()) colorCount++;
            if (colorCount == 0) return;
            if (ReferenceEquals(_appliedFor, speech) && _appliedCount == colorCount) return;

            foreach (var colorizer in FindColorizers(speech))
                ApplyTo(colorizer, factory);

            _appliedFor = speech;
            _appliedCount = colorCount;
        }

        /// <summary>
        /// Every colorizer worth populating.
        /// <para/>
        /// The speaker label is a GC2 <c>TextReference</c>, so the TMP object it
        /// resolves to is NOT guaranteed to sit under the SpeechUI component —
        /// searching only that subtree finds nothing on a skin that keeps the
        /// actor panel elsewhere, which reads in-game as every name staying the
        /// colorizer's default white. So the subtree is the fast path and a
        /// scene-wide sweep is the fallback.
        /// <para/>
        /// Populating extra colorizers is harmless: the component matches a
        /// pair's word against its ENTIRE text, so an actor name can never
        /// match a line of dialogue.
        /// </summary>
        private static IEnumerable<Object> FindColorizers(SpeechUI speech)
        {
            var found = new List<Object>();
            if (speech != null)
            {
                foreach (var c in speech.GetComponentsInChildren(_colorizerType, true))
                    found.Add(c);
            }
            if (found.Count == 0)
            {
                foreach (var c in Resources.FindObjectsOfTypeAll(_colorizerType))
                    found.Add(c);
            }
            return found;
        }

        private static void ApplyTo(Object colorizer, RuntimeActorFactory factory)
        {
            if (colorizer == null) return;
            if (!(_wordColorsField.GetValue(colorizer) is IList list)) return;

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
