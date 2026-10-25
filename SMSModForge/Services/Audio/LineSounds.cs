using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;

namespace SMSModForge.Services.Audio;

/// <summary>
/// A line's sounds as the game will play them, for the node editor's "Play
/// the line" (1.8.0): each pattern's sound at its volume, starting when
/// <see cref="SoundCues.Plan"/> says - the same plan the game follows - and
/// each sound's variants taken in turn, as the SFX tab's Play takes them.
/// Mixed into one recording, so it plays as one and a test can measure where
/// each sound starts.
/// </summary>
public static class LineSounds
{
    /// <summary>One sound placed in the line.</summary>
    public sealed record Piece(SoundCues.Match Match, string File, double Start, double Seconds, float Gain);

    /// <summary>The rate everything is mixed at.</summary>
    public const int Rate = 44100;

    /// <summary>
    /// The pieces of <paramref name="text"/>'s sounds.
    /// <paramref name="charactersPerSecond"/> is the speaker's typing speed,
    /// for a sound that waits for the typewriter.
    /// </summary>
    public static List<Piece> Plan(ModPack pack, string packRoot, string text, IList<SoundCueDef>? cues,
                                   double charactersPerSecond, Func<string, AudioData?> load)
    {
        var patterns = Patterns(pack);
        var matches = SoundCues.Find(text, patterns);
        var settings = (cues ?? new List<SoundCueDef>()).Select(c => c.ToSetting()).ToList();
        var turn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new string?[matches.Count];
        var sounds = new AudioData?[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            var sfx = pack.Sfx.FirstOrDefault(s => string.Equals(s.Key, matches[i].Sound, StringComparison.OrdinalIgnoreCase));
            if (sfx == null || string.IsNullOrWhiteSpace(sfx.AudioPath)) continue;
            var variants = SfxEdits.Files(packRoot, sfx.AudioPath).Select(r => Path.Combine(packRoot, r)).Where(File.Exists).ToList();
            if (variants.Count == 0) continue;
            turn.TryGetValue(sfx.Key, out int n);
            turn[sfx.Key] = n + 1;
            files[i] = variants[n % variants.Count];
            sounds[i] = load(files[i]!);
        }

        double speed = Math.Max(1, charactersPerSecond);
        var starts = SoundCues.Plan(matches, settings,
            m => sounds[m.Order - 1]?.Seconds ?? 0,
            m => SoundCues.VisibleIndex(text, m.Index) / speed);

        var pieces = new List<Piece>();
        for (int i = 0; i < matches.Count; i++)
        {
            if (sounds[i] == null) continue;
            var sfx = pack.Sfx.First(s => string.Equals(s.Key, matches[i].Sound, StringComparison.OrdinalIgnoreCase));
            float volume = (sfx.DefaultVolume ?? 1f) * SoundCues.VolumeOf(SoundCues.For(matches[i], settings));
            pieces.Add(new Piece(matches[i], files[i]!, starts[i], sounds[i]!.Seconds, GameLoudness.Sfx(volume)));
        }
        return pieces;
    }

    /// <summary>Every sound's patterns, with the sound each plays.</summary>
    public static List<KeyValuePair<string, string>> Patterns(ModPack pack)
        => pack.Sfx.SelectMany(s => (s.TextPatterns ?? new List<string>())
                                     .Where(p => !string.IsNullOrWhiteSpace(p))
                                     .Select(p => new KeyValuePair<string, string>(p, s.Key)))
                   .ToList();

    /// <summary>The pieces in one stereo recording, each at its start and gain.</summary>
    public static AudioData Mix(IEnumerable<(AudioData Sound, double Start, float Gain)> pieces)
    {
        var list = pieces.Where(p => p.Sound != null && p.Sound.Frames > 0).ToList();
        double end = list.Count == 0 ? 0 : list.Max(p => p.Start + p.Sound.Seconds);
        int frames = Math.Max(1, (int)Math.Ceiling(end * Rate));
        var left = new float[frames];
        var right = new float[frames];
        foreach (var (sound, start, gain) in list)
        {
            int at = (int)Math.Round(start * Rate);
            double step = sound.SampleRate / (double)Rate;
            var l = sound.Channels[0];
            var r = sound.ChannelCount > 1 ? sound.Channels[1] : l;
            for (int f = 0; at + f < frames; f++)
            {
                double pos = f * step;
                int i = (int)pos;
                if (i >= l.Length) break;
                double t = pos - i;
                int j = Math.Min(i + 1, l.Length - 1);
                left[at + f] += (float)((l[i] + (l[j] - l[i]) * t) * gain);
                right[at + f] += (float)((r[i] + (r[j] - r[i]) * t) * gain);
            }
        }
        return new AudioData(new[] { left, right }, Rate);
    }
}
