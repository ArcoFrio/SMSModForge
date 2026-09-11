using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Dialogue;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Mints runtime <see cref="Actor"/> ScriptableObjects from the pack's
    /// declared actors so the GC2 DialogueUI shows the speaker header (the
    /// bold name above the line, populated by
    /// <c>SpeechUI.OnStartText</c> calling <c>node.Actor.GetName(args)</c>).
    /// We never load Actor assets from an asset bundle — they're
    /// synthesised here and live for the CoreGameScene lifetime.
    /// <para/>
    /// <c>Actor.m_Actant</c> is a private serialised <see cref="Actant"/>;
    /// <see cref="Actant"/> holds the name in a private serialised
    /// <c>m_Name</c> of type <see cref="PropertyGetString"/>. We use
    /// reflection to reach the field but construct
    /// <c>new PropertyGetString(name)</c> directly (its public ctor is
    /// the typed factory for a literal-string property).
    /// </summary>
    public sealed class RuntimeActorFactory
    {
        private readonly Dictionary<string, Actor> _byKey = new Dictionary<string, Actor>();
        private readonly Dictionary<string, Color> _colorByDisplayName = new Dictionary<string, Color>();
        private readonly ManualLogSource _log;

        private static readonly FieldInfo _fldActorActant =
            typeof(Actor).GetField("m_Actant", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _fldActantName =
            typeof(Actant).GetField("m_Name", BindingFlags.Instance | BindingFlags.NonPublic);

        // GC2 Actor.Typewriter (m_Frequency int, m_Pitch Vector2 min/max,
        // m_Gibberish PropertyGetAudio = the typing blip clip). We drive these
        // via reflection to give pack actors an audible typewriter voice.
        private static readonly FieldInfo _fldActorTypewriter =
            typeof(Actor).GetField("m_Typewriter", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _fldTwUse =
            typeof(Typewriter).GetField("m_UseTypewriter", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _fldTwFreq =
            typeof(Typewriter).GetField("m_Frequency", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _fldTwPitch =
            typeof(Typewriter).GetField("m_Pitch", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _fldTwGibberish =
            typeof(Typewriter).GetField("m_Gibberish", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// What a character with no authored <c>typewriter</c> gets. Kept equal
        /// to the editor's TypewriterDef defaults on purpose: the editor shows
        /// those numbers in the Voice panel whether or not they were ever
        /// written to the manifest, so anything else here makes the panel lie.
        /// </summary>
        public const int DefaultFrequency = 45;
        public const float DefaultPitchMin = 1.0f;
        public const float DefaultPitchMax = 1.5f;

        /// <summary>
        /// What every Actor this factory mints is named with. Anything wearing
        /// it is ours rather than the game's, which is the only way a scan of
        /// loaded Actor assets can tell the two apart — see
        /// <see cref="FindDonorGibberish"/> and <see cref="VanillaVoiceOverrides"/>.
        /// </summary>
        public const string MintedPrefix = "SMSModForge_Actor_";

        private struct TwConfig { public bool Enabled; public int Frequency; public float PitchMin; public float PitchMax; }
        private readonly Dictionary<string, TwConfig> _twByKey = new Dictionary<string, TwConfig>();

        // The typing blip clip source, borrowed once from any vanilla actor so
        // pack actors don't have to ship one. Cached (with a "already looked"
        // flag) so the Resources scan runs at most once per scene.
        private PropertyGetAudio _donorGibberish;
        private bool _donorSearched;

        public RuntimeActorFactory(ManualLogSource log) { _log = log; }

        /// <summary>
        /// Return the cached Actor for <paramref name="key"/>, or build a
        /// fresh one named <paramref name="displayName"/>. Null/empty keys
        /// return null (the caller leaves the node unwired so the
        /// dialogue UI's speaker header stays hidden for narrator lines).
        /// </summary>
        public Actor GetOrCreate(string key, string displayName)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_byKey.TryGetValue(key, out var existing) && existing != null) return existing;

            string nameToShow = string.IsNullOrEmpty(displayName) ? key : displayName;

            var actor = ScriptableObject.CreateInstance<Actor>();
            actor.name = MintedPrefix + key;
            actor.hideFlags = HideFlags.HideAndDontSave;
            SetActorName(actor, nameToShow);
            ApplyTypewriter(actor, key);
            _byKey[key] = actor;
            return actor;
        }

        /// <summary>
        /// Register an actor's typewriter voice (from the pack manifest) so
        /// <see cref="GetOrCreate"/> can stamp it onto the synthesised Actor.
        /// </summary>
        public void RegisterTypewriter(string key, bool enabled, int frequency, float pitchMin, float pitchMax)
        {
            if (string.IsNullOrEmpty(key)) return;
            _twByKey[key] = new TwConfig { Enabled = enabled, Frequency = frequency, PitchMin = pitchMin, PitchMax = pitchMax };
        }

        /// <summary>
        /// Record an actor's display-name → speech-bubble-name colour so
        /// the runtime <c>TMPWordColorizer</c> on the active speech UI
        /// can paint the speaker label in that colour. Stored by
        /// <em>display name</em> (not key) because the colorizer matches
        /// against the text it sees in the name field, which is whatever
        /// <see cref="Actant.GetName"/> returns.
        /// </summary>
        public void RegisterColor(string displayName, Color color)
        {
            if (string.IsNullOrEmpty(displayName)) return;
            _colorByDisplayName[displayName] = color;
        }

        /// <summary>Enumerate every registered colour. Consumed by the speech-UI colour applier.</summary>
        public IEnumerable<KeyValuePair<string, Color>> EnumerateColors() => _colorByDisplayName;

        /// <summary>How many colours are registered. The applier checks this
        /// once a frame, so it is a count rather than a walk of the list.</summary>
        public int ColorCount { get { return _colorByDisplayName.Count; } }

        /// <summary>Forget everything. Called on scene unload by <see cref="Plugin"/>.</summary>
        public void Reset()
        {
            foreach (var a in _byKey.Values)
                if (a != null) Object.Destroy(a);
            _byKey.Clear();
            _colorByDisplayName.Clear();
            _twByKey.Clear();
            _donorGibberish = null;
            _donorSearched = false;
        }

        // ── Typewriter voice wiring ───────────────────────────────────────

        /// <summary>
        /// Read a voice off an Actor. False when the GC2 shape this reflects
        /// into is not the one we expect, so a caller keeping originals knows
        /// it has nothing worth putting back rather than putting zeroes back.
        /// </summary>
        internal static bool ReadVoice(Actor actor, out bool enabled, out int frequency,
                                       out Vector2 pitch)
        {
            enabled = true;
            frequency = DefaultFrequency;
            pitch = new Vector2(DefaultPitchMin, DefaultPitchMax);
            if (actor == null) return false;
            if (_fldTwUse == null || _fldTwFreq == null || _fldTwPitch == null) return false;

            var tw = actor.Typewriter;
            if (tw == null) return false;

            enabled = (bool)_fldTwUse.GetValue(tw);
            frequency = (int)_fldTwFreq.GetValue(tw);
            pitch = (Vector2)_fldTwPitch.GetValue(tw);
            return true;
        }

        /// <summary>
        /// Write a voice onto an Actor this factory did not mint — one of the
        /// game's own, so that a character an author re-voiced sounds re-voiced
        /// in the game's scenes and not only in the pack's.
        /// <para/>
        /// <see cref="Typewriter"/> is a class, so what this changes is the
        /// Actor's own instance rather than a copy of it. That is the whole
        /// reason this can be done at all, and it was worth checking: were it a
        /// struct, every one of these writes would land on a boxed copy and
        /// disappear, which reads exactly like the game ignoring the setting.
        /// <para/>
        /// Only the three fields the editor offers. The blip clip above all is
        /// left alone: it is the character's own, and a pack that says nothing
        /// about it must not silence them.
        /// </summary>
        internal static bool WriteVoice(Actor actor, bool enabled, int frequency,
                                        float pitchMin, float pitchMax)
        {
            if (actor == null) return false;
            if (_fldTwUse == null || _fldTwFreq == null || _fldTwPitch == null) return false;

            var tw = actor.Typewriter;
            if (tw == null) return false;

            _fldTwUse.SetValue(tw, enabled);
            if (enabled)
            {
                _fldTwFreq.SetValue(tw, frequency);
                _fldTwPitch.SetValue(tw, new Vector2(pitchMin, pitchMax));
            }
            return true;
        }

        /// <summary>
        /// Stamp the actor's GC2 <see cref="Typewriter"/> with the pack-authored
        /// cadence + pitch (or a neutral default when the pack didn't set any),
        /// and make sure it has a blip clip so the typing is actually audible —
        /// borrowing one from a vanilla actor when ours is empty.
        /// </summary>
        private void ApplyTypewriter(Actor actor, string key)
        {
            if (_fldActorTypewriter == null) return;

            bool hasCfg = _twByKey.TryGetValue(key, out var cfg);
            bool enabled = !hasCfg || cfg.Enabled;

            object tw = _fldActorTypewriter.GetValue(actor);
            if (tw == null)
            {
                // Fresh Actors normally come with a default Typewriter, but be
                // defensive — synthesise one if the field is null.
                try { tw = System.Activator.CreateInstance(typeof(Typewriter)); _fldActorTypewriter.SetValue(actor, tw); }
                catch (System.Exception ex) { _log?.LogWarning("[SMSModForge.PackPlugin] Could not create Typewriter for actor '" + key + "': " + ex.Message); return; }
            }

            _fldTwUse?.SetValue(tw, enabled);
            if (!enabled) return;

            // The fallback has to be what the EDITOR shows for a character that
            // has never had its voice touched, which is TypewriterDef's own
            // defaults: 45, 1.0–1.5. It used to be 1.0–1.0, and min == max is a
            // monotone blip — so a character with no typewriter object read as
            // having a pitch range in the editor and had none in the game.
            //
            // ...except for one of the GAME's characters, who has a voice of
            // their own. A pack line spoken by Adrian went out at 45 and pitch
            // 1.0–1.5 while the game speaks him at 40 and 0.8–1.2, so he sounded
            // like somebody else the moment a mod gave him a line. The editor
            // now shows his real numbers, which makes matching them here the
            // difference between a panel that describes the game and one that
            // lies about it.
            var theirs = hasCfg ? null : SMSModForge.Shared.VanillaSpeech.For(key);

            int freq = hasCfg ? cfg.Frequency : theirs != null ? theirs.Frequency : DefaultFrequency;
            float pmin = hasCfg ? cfg.PitchMin : theirs != null ? theirs.PitchMin : DefaultPitchMin;
            float pmax = hasCfg ? cfg.PitchMax : theirs != null ? theirs.PitchMax : DefaultPitchMax;
            _fldTwFreq?.SetValue(tw, freq);
            _fldTwPitch?.SetValue(tw, new Vector2(pmin, pmax));

            // Without a blip clip the typewriter runs silently. If the actor's
            // gibberish slot is empty, reuse the game's own typing clip.
            var gib = _fldTwGibberish?.GetValue(tw) as PropertyGetAudio;
            if (IsGibberishEmpty(gib))
            {
                var donor = FindDonorGibberish();
                if (donor != null) _fldTwGibberish?.SetValue(tw, donor);
            }
        }

        /// <summary>
        /// Find any vanilla actor's typewriter blip clip (a
        /// <see cref="PropertyGetAudio"/>) so pack actors can inherit it.
        /// Scans loaded Actor assets once and caches the result — skipping our
        /// own synthesised actors (name-prefixed <c>SMSModForge_Actor_</c>).
        /// </summary>
        private PropertyGetAudio FindDonorGibberish()
        {
            if (_donorSearched) return _donorGibberish;
            _donorSearched = true;
            if (_fldActorTypewriter == null || _fldTwGibberish == null) return null;

            try
            {
                foreach (var a in Resources.FindObjectsOfTypeAll<Actor>())
                {
                    if (a == null) continue;
                    if (a.name != null && a.name.StartsWith(MintedPrefix)) continue;
                    var tw = _fldActorTypewriter.GetValue(a);
                    if (tw == null) continue;
                    var gib = _fldTwGibberish.GetValue(tw) as PropertyGetAudio;
                    if (!IsGibberishEmpty(gib)) { _donorGibberish = gib; break; }
                }
            }
            catch (System.Exception ex)
            {
                _log?.LogWarning("[SMSModForge.PackPlugin] Typewriter blip donor scan failed: " + ex.Message);
            }

            if (_donorGibberish == null)
                _log?.LogWarning("[SMSModForge.PackPlugin] No vanilla actor with a typewriter blip found — pack actors' typing may be silent.");
            return _donorGibberish;
        }

        /// <summary>True when the audio property yields no clip (so it's worth replacing).</summary>
        private static bool IsGibberishEmpty(PropertyGetAudio gib)
        {
            if (gib == null) return true;
            try { return gib.Get(Args.EMPTY) == null; }
            catch { return false; } // couldn't evaluate — assume it has something, don't clobber
        }

        // ── Actor name wiring ─────────────────────────────────────────

        private void SetActorName(Actor actor, string displayName)
        {
            if (_fldActorActant == null || _fldActantName == null)
            {
                _log?.LogWarning("[SMSModForge.PackPlugin] Actor.m_Actant or Actant.m_Name field not found via reflection — speaker names won't show.");
                return;
            }
            var actant = _fldActorActant.GetValue(actor);
            if (actant == null) return;
            _fldActantName.SetValue(actant, new PropertyGetString(displayName));
        }
    }
}
