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
        try
        {
            _reader = OpenReader(absolutePath);
            var sample = new VolumeSampleProvider(_reader.ToSampleProvider())
            {
                Volume = Math.Clamp(volume, 0f, 1f),
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
    }

    public void Dispose() => Stop();
}
