using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Dialogue;
using GameCreator.Runtime.Dialogue.UnityUI;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Writes a pack's typing voice onto the game's OWN Actor assets.
    /// <para/>
    /// The voice half of what <see cref="VanillaBustOverrides"/> does for
    /// textures, and it exists for the same reason. A pack's typewriter used to
    /// reach only the Actor <see cref="RuntimeActorFactory"/> synthesises for
    /// its own lines, so an author who slowed one of the game's characters down
    /// and raised their pitch heard it in the pack's conversations and nowhere
    /// else — every scene the game plays itself went on using the voice it
    /// shipped, which reads exactly like the setting doing nothing.
    /// <para/>
    /// Only characters the pack marks <c>bustSource: Vanilla</c>, and only the
    /// ones carrying a <c>typewriter</c> object: the editor writes that object
    /// only once an author has changed something, so a character they left
    /// alone is a character this never touches.
    /// <para/>
    /// The game's Actor for a character is found the same way the extractor
    /// that built <c>VanillaSpeech</c> found it — by keying the name the Actor
    /// speaks. There is no other handle: the assets carry no id, and their
    /// object names are not the character names.
    /// <para/>
    /// Asked for at load and asked for again when a conversation starts, for as
    /// long as anything is still missing. An Actor asset is loaded because
    /// something in the scene points at it, and this runs early enough in the
    /// load that assuming they are all there would be a guess — whereas by the
    /// time a conversation is on screen they certainly are. Nothing is scanned
    /// once every character asked for has been found, which is the ordinary
    /// case after the first attempt.
    /// <para/>
    /// What each Actor said before is kept and put back by <see cref="Restore"/>
    /// on scene unload. These are the game's own assets rather than anything
    /// this plugin made, and a voice left behind by an unloaded pack would be a
    /// change with nobody left to explain it.
    /// </summary>
    internal static class VanillaVoiceOverrides
    {
        /// <summary>A character a pack has re-voiced, before we have found the
        /// Actor the game speaks them with.</summary>
        private sealed class Wanted
        {
            public string PackId;
            public string Key;
            public bool Enabled;
            public int Frequency;
            public float PitchMin;
            public float PitchMax;
            public bool Complained;
        }

        /// <summary>One Actor as the game had it, before a pack spoke for it.</summary>
        private struct Was
        {
            public Actor Actor;
            public bool Enabled;
            public int Frequency;
            public Vector2 Pitch;
        }

        private static readonly List<Wanted> _wanted = new List<Wanted>();
        private static readonly List<Was> _wasThere = new List<Was>();

        /// <summary>Actors already restated this scene, so a second pack
        /// changing the same character does not save the FIRST pack's voice as
        /// "what the game had".</summary>
        private static readonly HashSet<Actor> _touched = new HashSet<Actor>();

        /// <summary>The speech UI the last retry ran against, so a retry costs
        /// one reference compare per frame rather than a scan of every loaded
        /// Actor.</summary>
        private static Object _triedFor;
        private static bool _triedOnce;

        public static void ApplyAll(PackManifest pack, ManualLogSource log)
        {
            var characters = pack != null ? pack.Characters : null;
            if (characters == null) return;

            foreach (var ch in characters)
            {
                var charObj = ch as JObject;
                if (charObj == null) continue;

                // A pack's own character speaks through the Actor we mint for
                // it, which already carries this. There is no game asset here
                // to write to.
                if ((string)charObj["bustSource"] != "Vanilla") continue;

                var tw = charObj["typewriter"] as JObject;
                if (tw == null) continue;      // nothing changed; leave the game's voice

                string key = (string)charObj["key"];
                if (string.IsNullOrEmpty(key)) continue;

                // Per-field fallbacks match RuntimeActorFactory's, so a
                // typewriter that omits a key lands where the editor says it
                // will rather than somewhere this file decided.
                _wanted.Add(new Wanted
                {
                    PackId = pack.PackId,
                    Key = key,
                    Enabled = tw["enabled"] == null || (bool)tw["enabled"],
                    Frequency = tw["frequency"] != null ? (int)tw["frequency"] : RuntimeActorFactory.DefaultFrequency,
                    PitchMin = tw["pitchMin"] != null ? (float)tw["pitchMin"] : RuntimeActorFactory.DefaultPitchMin,
                    PitchMax = tw["pitchMax"] != null ? (float)tw["pitchMax"] : RuntimeActorFactory.DefaultPitchMax,
                });
            }

            Resolve(log);
        }

        /// <summary>
        /// Try again for anything still missing, once per conversation.
        /// <para/>
        /// Free — a list-count check — once every character asked for has been
        /// found, which is the ordinary case.
        /// </summary>
        public static void Tick(ManualLogSource log)
        {
            if (_wanted.Count == 0) return;

            var speech = SpeechUI.Current;
            if (speech == null)
            {
                if (_triedOnce) return;
            }
            else if (ReferenceEquals(_triedFor, speech)) return;

            _triedFor = speech == null ? null : speech;
            _triedOnce = true;
            Resolve(log);
        }

        /// <summary>
        /// Find the game's Actor for everything still wanted, and write to it.
        /// One scan of the loaded Actor assets however many characters are
        /// waiting, because the scan is the expensive half.
        /// </summary>
        private static void Resolve(ManualLogSource log)
        {
            if (_wanted.Count == 0) return;

            var byKey = TheGamesOwn(log);
            int written = 0;

            for (int i = _wanted.Count - 1; i >= 0; i--)
            {
                var want = _wanted[i];
                Actor actor;
                if (!byKey.TryGetValue(want.Key, out actor) || actor == null)
                {
                    if (!want.Complained)
                    {
                        want.Complained = true;
                        log?.LogWarning("[SMSModForge.PackPlugin] " + want.PackId + " gives '"
                                        + want.Key + "' a typing voice, but no actor of the game's "
                                        + "answers to that name yet — it will be heard on this "
                                        + "pack's own lines, and tried again when a conversation "
                                        + "starts.");
                    }
                    continue;
                }

                if (_touched.Add(actor))
                {
                    bool wasEnabled;
                    int wasFreq;
                    Vector2 wasPitch;
                    if (RuntimeActorFactory.ReadVoice(actor, out wasEnabled, out wasFreq, out wasPitch))
                        _wasThere.Add(new Was
                        {
                            Actor = actor,
                            Enabled = wasEnabled,
                            Frequency = wasFreq,
                            Pitch = wasPitch,
                        });
                }

                if (RuntimeActorFactory.WriteVoice(actor, want.Enabled, want.Frequency,
                                                   want.PitchMin, want.PitchMax))
                {
                    written++;
                    _wanted.RemoveAt(i);
                }
            }

            if (written > 0)
                log?.LogInfo("[SMSModForge.PackPlugin] Gave " + written + " of the game's "
                             + "character(s) a new typing voice.");
        }

        /// <summary>Put every Actor back the way the game had it.</summary>
        public static void Restore()
        {
            foreach (var was in _wasThere)
            {
                if (was.Actor == null) continue;
                RuntimeActorFactory.WriteVoice(was.Actor, was.Enabled, was.Frequency,
                                               was.Pitch.x, was.Pitch.y);
            }
            _wasThere.Clear();
            _touched.Clear();
            _wanted.Clear();
            _triedFor = null;
            _triedOnce = false;
        }

        /// <summary>
        /// Every Actor asset the game has loaded, under the key the editor
        /// would give the character it speaks for.
        /// <para/>
        /// Matched on the name the Actor speaks, run through the same key rule
        /// the editor derives a character's key with, because that is the only
        /// thing the two ends share. Our own synthesised actors are skipped by
        /// name — the one Actor in the scene that would match by construction
        /// is ours, and writing to it would leave the game's untouched while
        /// looking like it had worked.
        /// </summary>
        private static Dictionary<string, Actor> TheGamesOwn(ManualLogSource log)
        {
            var found = new Dictionary<string, Actor>(System.StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var a in Resources.FindObjectsOfTypeAll<Actor>())
                {
                    if (a == null) continue;
                    if (a.name != null && a.name.StartsWith(RuntimeActorFactory.MintedPrefix)) continue;

                    string spoken;
                    try { spoken = a.GetName(Args.EMPTY); }
                    catch (System.Exception) { continue; }

                    string key = SMSModForge.Shared.VanillaCastData.KeyFor(spoken);
                    if (key.Length == 0) continue;
                    if (!found.ContainsKey(key)) found[key] = a;
                }
            }
            catch (System.Exception ex)
            {
                log?.LogWarning("[SMSModForge.PackPlugin] Actor scan failed: " + ex.Message);
            }
            return found;
        }
    }
}
