using System;
using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SMSModForge.Services;

/// <summary>
/// Plays a single SFX clip for the editor's preview button. The pack's SFX are
/// OGG Vorbis, which WPF's <see cref="System.Windows.Media.MediaPlayer"/> can't
/// decode, so this uses NAudio.Vorbis to decode and WaveOut to play — through a
/// <see cref="VolumeSampleProvider"/> so the preview honours the SFX's authored
/// default volume. WAV / MP3 also work (via <see cref="AudioFileReader"/>).
/// <para/>
/// One clip at a time: starting a new preview stops the previous one. Editor-only;
/// the runtime plugin still plays SFX through Unity's audio.
/// </summary>
public sealed class SfxPreviewPlayer : IDisposable
{
    private IWavePlayer? _output;
    private WaveStream? _reader;

    /// <summary>Play <paramref name="absolutePath"/> at <paramref name="volume"/>
    /// (0..1). No-op when the file is missing or can't be decoded.</summary>
    public void Play(string absolutePath, float volume)
    {
        Stop();
        if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath)) return;
        LastPlayedFile = absolutePath;
        LastVolume = volume;
        // Nothing out loud under the test harness, as for a sound in memory.
        if (TestMode.Active) return;
        try
        {
            _reader = OpenReader(absolutePath);
            // Not stopped at 1: a sound may be louder than full scale, as it
            // may in the game (see Model.GameLoudness).
            var sample = new VolumeSampleProvider(_reader.ToSampleProvider())
            {
                Volume = Math.Max(0f, volume),
            };
            _output = new WaveOutEvent();
            _output.Init(sample);
            _output.Play();
        }
        catch
        {
            Stop();   // decode / output failure — leave nothing dangling
        }
    }

    /// <summary>
    /// Play a sound already in memory - an edited one, made by
    /// <see cref="Audio.SfxRenderer"/> - from <paramref name="fromSeconds"/>
    /// on. The SFX tab's editor plays from where its cursor is, and draws a
    /// line where the playing has got to (<see cref="Position"/>).
    /// </summary>
    public void Play(Audio.AudioData sound, float volume, double fromSeconds = 0)
    {
        Stop();
        if (sound == null || sound.Frames == 0) return;
        LastPlayed = (sound, fromSeconds);
        LastVolume = volume;
        // Under the test harness nothing is played out loud on the machine of
        // whoever runs the suite; what would have been is kept for the tests.
        if (TestMode.Active) return;
        try
        {
            _output = new WaveOutEvent();
            _output.Init(Provider(sound, volume, fromSeconds, out _memory));
            _output.Play();
        }
        catch
        {
            Stop();
        }
    }

    private MemorySound? _memory;

    /// <summary>The gain the last sound was asked to play at.</summary>
    public float? LastVolume { get; private set; }

    /// <summary>The last file asked to play as it is.</summary>
    public string? LastPlayedFile { get; private set; }

    /// <summary>The last sound in memory asked to play, and from where.</summary>
    public (Audio.AudioData Sound, double From)? LastPlayed { get; private set; }

    /// <summary>How far into the sound in memory the playing has got, in
    /// seconds from its start; null when it is not playing one.</summary>
    public double? Position
        => _memory != null && _output?.PlaybackState == PlaybackState.Playing ? _memory.Seconds : null;

    /// <summary>What the output is given to play: the sound in memory, from
    /// <paramref name="fromSeconds"/>, at <paramref name="volume"/>. Apart from
    /// <see cref="Play(Audio.AudioData, float, double)"/> so a test can read it
    /// the way the output does, with no sound card involved.</summary>
    internal static ISampleProvider Provider(Audio.AudioData sound, float volume, double fromSeconds,
                                             out MemorySound memory)
    {
        memory = new MemorySound(sound, sound.FrameAt(fromSeconds));
        return new VolumeSampleProvider(memory) { Volume = Math.Max(0f, volume) };
    }

    /// <summary>A sound in memory as NAudio reads it.</summary>
    internal sealed class MemorySound : ISampleProvider
    {
        private readonly float[] _samples;
        private readonly int _channels;
        private readonly int _rate;
        private readonly int _startFrame;
        private int _at;

        public MemorySound(Audio.AudioData sound, int fromFrame)
        {
            _samples = sound.Interleaved(fromFrame);
            _channels = sound.ChannelCount;
            _rate = sound.SampleRate;
            _startFrame = fromFrame;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sound.SampleRate, sound.ChannelCount);
        }

        public WaveFormat WaveFormat { get; }

        /// <summary>Where the reading has got to, from the sound's start. A
        /// little ahead of what is heard by the output's buffer, which at a
        /// line on a strip is not to be seen.</summary>
        public double Seconds => (_startFrame + _at / (double)_channels) / _rate;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _samples.Length - _at);
            if (n <= 0) return 0;
            // One at a time, never Array.Copy: the buffer NAudio hands over on
            // its way to the sound card is a byte array wearing a float array's
            // type (its WaveBuffer), which Array.Copy sees through and refuses -
            // and the first read failing stopped every play before a sound.
            for (int i = 0; i < n; i++) buffer[offset + i] = _samples[_at + i];
            _at += n;
            return n;
        }
    }

    private static WaveStream OpenReader(string path)
        => Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            ? new VorbisWaveReader(path)
            : new AudioFileReader(path);   // wav / mp3 / aiff / …

    public void Stop()
    {
        try { _output?.Stop(); } catch { /* device already gone */ }
        _output?.Dispose();
        _output = null;
        _reader?.Dispose();
        _reader = null;
        _memory = null;
    }

    public void Dispose() => Stop();
}
