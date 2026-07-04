using BepInEx.Logging;
using System.Collections;
using System.Collections.Generic;
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
