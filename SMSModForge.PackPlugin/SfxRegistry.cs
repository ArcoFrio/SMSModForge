using BepInEx.Logging;
using GameCreator.Runtime.Common.UnityUI;
using GameCreator.Runtime.Dialogue;
using GameCreator.Runtime.Dialogue.UnityUI;
using Newtonsoft.Json.Linq;
using SMSModForge.Shared;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Per-pack SFX clip catalog + the shared <see cref="AudioSource"/>
    /// that plays them back. One registry instance hangs off each
    /// <see cref="PackContext"/>, populated lazily by
    /// <see cref="SfxFactory"/> as its async audio loads complete.
    /// <para/>
    /// Each <see cref="Entry"/> can carry:
    /// <list type="bullet">
    ///   <item>multiple <see cref="Entry.Clips"/> — base + auto-detected
    ///   variants — picked at random per play, mirroring
    ///   the host mod's <c>GetRandomAudioClipForSFX</c>.</item>
    ///   <item>one or more <see cref="Entry.TextPatterns"/> that, when
    ///   matched inside a dialogue node's text on node-start, auto-fire
    ///   the SFX (the dispatcher's port of the host mod's
    ///   <c>OnDialogueLineStart</c> → <c>ProcessSFXTriggersForText</c>
    ///   pipeline).</item>
    /// </list>
    /// SFX are one-shot — calls go through
    /// <see cref="AudioSource.PlayOneShot(AudioClip, float)"/> so two
    /// effects from the same node can play simultaneously without
    /// stomping each other.
    /// </summary>
    public sealed class SfxRegistry
    {
        public sealed class Entry
        {
            public string Key;
            public List<AudioClip> Clips = new List<AudioClip>(); // base + variants; may be empty until async loads land
            public List<string> TextPatterns = new List<string>();
            public float DefaultVolume = 1f;
        }

        private readonly Dictionary<string, Entry> _byKey = new Dictionary<string, Entry>();
        // Cache compiled regexes keyed by pattern string. Built lazily.
        private readonly Dictionary<string, Regex> _patternRegex = new Dictionary<string, Regex>();

        /// <summary>
        /// The AudioSource clips are played through. Created once at
        /// pack build time as a child of <c>12_AudioPlayer</c>.
        /// </summary>
        public AudioSource Source { get; set; }

        public Entry GetOrCreate(string key, float defaultVolume, List<string> textPatterns)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!_byKey.TryGetValue(key, out var e))
            {
                e = new Entry { Key = key };
                _byKey[key] = e;
            }
            e.DefaultVolume = defaultVolume;
            if (textPatterns != null)
            {
                e.TextPatterns.Clear();
                foreach (var p in textPatterns)
                    if (!string.IsNullOrEmpty(p)) e.TextPatterns.Add(p);
            }
            return e;
        }

        public void AddClip(string key, AudioClip clip)
        {
            if (clip == null || !_byKey.TryGetValue(key, out var e)) return;
            e.Clips.Add(clip);
        }

        public Entry Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _byKey.TryGetValue(key, out var e) ? e : null;
        }

        public IReadOnlyCollection<Entry> All => _byKey.Values;

        /// <summary>
        /// Whether the pack's lines say how their sounds play (1.8.0,
        /// <see cref="SoundCues"/>): a pack written by ModForge 1.8.0 or later,
        /// or one with any line that does. An older pack's sounds keep the
        /// timing they always had (<see cref="FireMatchingPatterns"/>) - the
        /// editor turns it into the new kind as the pack is opened, and a pack
        /// that was never opened again must not start sounding different.
        /// </summary>
        public bool LinesSayHowSoundsPlay { get; set; }

        /// <summary>Whether a pack's manifest says how its lines' sounds play.</summary>
        public static bool SaysHowSoundsPlay(JObject manifest)
        {
            if (manifest == null) return false;
            string stamp = (string)manifest["forgeVersion"] ?? "";
            if (ForgeVersion.Parse(stamp) != null && ForgeVersion.Compare(stamp, "1.8.0") >= 0) return true;
            var dialogues = manifest["dialogues"] as JArray;
            if (dialogues == null) return false;
            foreach (var d in dialogues)
            {
                var nodes = d["nodes"] as JArray;
                if (nodes == null) continue;
                foreach (var n in nodes)
                    if (n["cues"] is JArray cues && cues.Count > 0) return true;
            }
            return false;
        }

        /// <summary>Every sound's patterns, with the sound each plays.</summary>
        public List<KeyValuePair<string, string>> Patterns()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var e in _byKey.Values)
                foreach (var p in e.TextPatterns)
                    list.Add(new KeyValuePair<string, string>(p, e.Key));
            return list;
        }

        /// <summary>
        /// The line as the player reads it: <paramref name="shown"/> without
        /// the patterns its node hides, found in the pack's own words
        /// (<paramref name="ownWords"/>). Unchanged for an older pack.
        /// </summary>
        public string Shown(string shown, string ownWords, JToken cues)
        {
            if (!LinesSayHowSoundsPlay || string.IsNullOrEmpty(shown) || cues == null) return shown;
            var settings = SoundCues.Read(cues);
            if (settings.Count == 0) return shown;
            return SoundCues.Hide(shown, SoundCues.Find(ownWords ?? shown, Patterns()), settings);
        }

        /// <summary>
        /// A line's sounds as it starts: each pattern in
        /// <paramref name="text"/> (the pack's own words) plays its sound, at
        /// the volume and the time its node gives it - the plan of
        /// <see cref="SoundCues"/>, followed as the line plays: a sound that
        /// follows another starts from when that one, as it was picked, ends;
        /// one that waits for the typewriter starts from when the speech box
        /// has shown it. An older pack plays them the old way.
        /// <para/>
        /// Called as the node starts, which is before its text: GC2 runs the
        /// node's own On Start instructions first, and only then puts the
        /// line in the speech box. Given the <paramref name="node"/>, the
        /// sounds wait for that - the line starts when it can be read.
        /// </summary>
        public void PlayLine(string text, JToken cues, MonoBehaviour host, ManualLogSource log, Node node = null)
        {
            if (string.IsNullOrEmpty(text) || host == null) return;
            if (!LinesSayHowSoundsPlay)
            {
                FireMatchingPatterns(text, host, log);
                return;
            }
            var matches = SoundCues.Find(text, Patterns());
            if (matches.Count == 0) return;
            var settings = SoundCues.Read(cues);
            if (node == null)
            {
                host.StartCoroutine(RunLine(text, matches, settings, log));
                return;
            }

            // One wait per node: a line stopped before its text showed leaves
            // its wait behind, and the next time the node runs replaces it
            // rather than playing twice.
            System.Action<int> old;
            if (_waitingForText.TryGetValue(node, out old)) node.EventStartText -= old;
            System.Action<int> once = null;
            once = id =>
            {
                node.EventStartText -= once;
                _waitingForText.Remove(node);
                if (host != null) host.StartCoroutine(RunLine(text, matches, settings, log));
            };
            _waitingForText[node] = once;
            node.EventStartText += once;
        }

        private readonly Dictionary<Node, System.Action<int>> _waitingForText = new Dictionary<Node, System.Action<int>>();

        private IEnumerator RunLine(string text, List<SoundCues.Match> matches, List<SoundCues.Setting> settings,
                                    ManualLogSource log)
        {
            float lineStart = Time.unscaledTime;
            float previousEnd = lineStart;
            int startFrame = Time.frameCount;
            // The speech box's text for this line. Read a frame in: the box
            // takes the line from the same moment this starts, and may be told
            // after it.
            string shownLine = null;
            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var setting = SoundCues.For(match, settings);
                float delay = setting != null && setting.Delay.HasValue ? setting.Delay.Value : 0f;
                float target;
                if (setting != null && setting.Typed)
                {
                    // The speech box shows the line a character at a time;
                    // this one starts once its pattern's first letter is out.
                    int at = SoundCues.VisibleIndex(text, match.Index);
                    while (Time.frameCount == startFrame) yield return null;
                    if (shownLine == null) shownLine = ShownText();
                    while (true)
                    {
                        int total;
                        int shown = ShownSoFar(out total);
                        if (shown < 0) break;                        // no box to watch: play now
                        if (ShownText() != shownLine) yield break;   // the line moved on first
                        int own = RichText.VisibleLength(text);
                        int there = own > 0 && total > 0 && total != own ? (int)((long)at * total / own) : at;
                        if (shown > there) break;
                        yield return null;
                    }
                    target = Time.unscaledTime + Mathf.Max(0f, delay);
                }
                else if (i == 0) target = lineStart + Mathf.Max(0f, delay);
                else target = Mathf.Max(lineStart, previousEnd + delay);

                while (Time.unscaledTime < target) yield return null;

                var entry = Get(match.Sound);
                var clip = PickRandomClip(entry);
                float length = 0f;
                if (clip != null)
                {
                    GameAudio.PlayUi(clip, entry.DefaultVolume * SoundCues.VolumeOf(setting));
                    length = clip.length;
                }
#if DEBUG
                log?.LogInfo("[SMSModForge.PackPlugin] Line sounds: " + match.Pattern + " #" + match.Order + " at "
                             + (Time.unscaledTime - lineStart).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                             + " s, volume " + ((entry != null ? entry.DefaultVolume : 0f) * SoundCues.VolumeOf(setting))
                                 .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                             + ", length " + length.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
#endif
                previousEnd = Mathf.Max(target, Time.unscaledTime) + length;
            }
#if DEBUG
            // For the in-game check of the line's sounds (Tools/Game): the
            // line as the box shows it, once it has it - after the sounds, so
            // this changes none of their timing. Debug builds only - a
            // player's log does not need a line per sound.
            while (Time.frameCount == startFrame) yield return null;
            log?.LogInfo("[SMSModForge.PackPlugin] Line sounds: shown '" + (shownLine ?? ShownText()) + "'");
#endif
        }

        private static readonly FieldInfo _speechText =
            typeof(SpeechUI).GetField("m_Text", BindingFlags.Instance | BindingFlags.NonPublic);

        private static TextReference SpeechText()
        {
            var ui = SpeechUI.Current;
            return ui != null && _speechText != null ? _speechText.GetValue(ui) as TextReference : null;
        }

        /// <summary>How many characters the speech box shows, and how many its
        /// line has; -1 with no box to read.</summary>
        private static int ShownSoFar(out int total)
        {
            total = 0;
            var text = SpeechText();
            if (text == null) return -1;
            total = RichText.VisibleLength(text.Text ?? "");
            return text.CharactersVisible;
        }

        private static string ShownText()
        {
            var text = SpeechText();
            return text != null ? text.Text ?? "" : "";
        }

        /// <summary>
        /// Pick a random clip from <paramref name="entry"/>'s loaded
        /// variants. Returns null when the entry has none (e.g. async
        /// loads haven't completed yet) so callers can no-op safely.
        /// </summary>
        public AudioClip PickRandomClip(Entry entry)
        {
            if (entry == null || entry.Clips.Count == 0) return null;
            return entry.Clips[UnityEngine.Random.Range(0, entry.Clips.Count)];
        }

        /// <summary>
        /// Scan <paramref name="text"/> for every registered SFX text
        /// pattern; for each match, schedule a <see cref="AudioSource.PlayOneShot(AudioClip, float)"/>
        /// with a cumulative delay derived from the inter-match
        /// character spacing (matches the host mod cadence of
        /// roughly 0.22s per character between matches, so a line
        /// with three <c>*plap*</c> tokens reads paced rather than
        /// firing all at once on node start).
        /// <para/>
        /// Caller passes the plugin host so coroutines run on a
        /// stable MonoBehaviour — the dispatcher's <c>OnStartNext</c>
        /// is the natural hook (after pack-authored
        /// <c>actionsOnStart</c> have executed).
        /// </summary>
        public void FireMatchingPatterns(string text, MonoBehaviour host, ManualLogSource log)
        {
            if (string.IsNullOrEmpty(text)) return;

            foreach (var entry in _byKey.Values)
            {
                if (entry.TextPatterns.Count == 0 || entry.Clips.Count == 0) continue;

                foreach (var pattern in entry.TextPatterns)
                {
                    var regex = GetOrBuildRegex(pattern);
                    var matches = regex.Matches(text);
                    if (matches.Count == 0) continue;

                    // Cumulative delay between matches mirrors
                    // the host mod's 0.22f-per-character pacing so
                    // multiple matches read out rather than dogpile.
                    float delay = 0f;
                    for (int i = 0; i < matches.Count; i++)
                    {
                        if (i > 0)
                        {
                            int prevEnd = matches[i - 1].Index + matches[i - 1].Length;
                            int curStart = matches[i].Index;
                            delay += System.Math.Max(0, curStart - prevEnd) * 0.22f;
                        }
                        var clip = PickRandomClip(entry);
                        if (clip == null) continue;
                        if (delay <= 0f) GameAudio.PlayUi(clip, entry.DefaultVolume);
                        else host.StartCoroutine(PlayAfter(delay, clip, entry.DefaultVolume));
                    }
                }
            }
        }

        private Regex GetOrBuildRegex(string pattern)
        {
            if (_patternRegex.TryGetValue(pattern, out var r)) return r;
            r = new Regex(Regex.Escape(pattern), RegexOptions.IgnoreCase | RegexOptions.Compiled);
            _patternRegex[pattern] = r;
            return r;
        }

        private IEnumerator PlayAfter(float seconds, AudioClip clip, float volume)
        {
            yield return new WaitForSeconds(seconds);
            GameAudio.PlayUi(clip, volume);
        }

        public void Reset()
        {
            _byKey.Clear();
            _patternRegex.Clear();
            Source = null;
        }
    }
}
