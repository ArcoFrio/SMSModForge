using System;
using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;

namespace SMSModForge.Services.Audio;

/// <summary>
/// A sound in memory: one array of samples per channel, -1 to 1, at its own
/// rate. What the SFX tab's editor cuts and the renderer works on.
/// <para/>
/// Channels are kept apart rather than mixed down: the strip draws one
/// waveform for simplicity, but a stereo sound edited stays stereo (the
/// author, 1.7.0: "only showing a single channel for simplicity, while not
/// forcing the audio to become mono").
/// </summary>
public sealed class AudioData
{
    public float[][] Channels { get; }
    public int SampleRate { get; }

    public AudioData(float[][] channels, int sampleRate)
    {
        if (channels.Length == 0) throw new ArgumentException("A sound needs at least one channel.");
        Channels = channels;
        SampleRate = sampleRate;
    }

    public int ChannelCount => Channels.Length;
    public int Frames => Channels[0].Length;
    public double Seconds => (double)Frames / SampleRate;

    /// <summary>An empty sound shaped like this one.</summary>
    public AudioData Empty(int frames)
    {
        var ch = new float[ChannelCount][];
        for (int c = 0; c < ch.Length; c++) ch[c] = new float[frames];
        return new AudioData(ch, SampleRate);
    }

    /// <summary>The frame at <paramref name="seconds"/>, kept inside the sound.</summary>
    public int FrameAt(double seconds)
        => (int)Math.Clamp(Math.Round(seconds * SampleRate), 0, Frames);

    // ── Reading ──────────────────────────────────────────────────────────

    /// <summary>
    /// Read a sound file: OGG Vorbis through NAudio.Vorbis (WPF's own player
    /// cannot), WAV, MP3 and the rest through NAudio's reader - the same
    /// readers the preview button has always used.
    /// </summary>
    public static AudioData Load(string path)
    {
        using WaveStream reader = Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            ? new VorbisWaveReader(path)
            : new AudioFileReader(path);
        var provider = reader.ToSampleProvider();
        int channels = provider.WaveFormat.Channels;
        int rate = provider.WaveFormat.SampleRate;

        var all = new System.Collections.Generic.List<float>(
            (int)Math.Min(int.MaxValue / 2, Math.Max(0, reader.Length / 2)));
        var buffer = new float[rate * channels];
        int got;
        while ((got = provider.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < got; i++) all.Add(buffer[i]);

        int frames = all.Count / channels;
        var data = new float[channels][];
        for (int c = 0; c < channels; c++) data[c] = new float[frames];
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < channels; c++)
                data[c][f] = all[f * channels + c];
        return new AudioData(data, rate);
    }

    // ── Writing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Write the sound as 16-bit PCM WAV, the form every player reads the same
    /// way. Not OGG: the managed Vorbis encoders tried lose the first 23 ms on
    /// decoding, and whether the game's decoder does too could not be checked
    /// from here - a cut that moves by 23 ms is not the cut the author made.
    /// </summary>
    public void WriteWav(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var format = new WaveFormat(SampleRate, 16, ChannelCount);
        using var writer = new WaveFileWriter(path, format);
        var frame = new byte[2 * ChannelCount];
        var block = new byte[frame.Length * 4096];
        int at = 0;
        for (int f = 0; f < Frames; f++)
        {
            for (int c = 0; c < ChannelCount; c++)
            {
                float v = Math.Clamp(Channels[c][f], -1f, 1f);
                short s = (short)Math.Round(v * 32767f);
                block[at++] = (byte)(s & 0xFF);
                block[at++] = (byte)((s >> 8) & 0xFF);
            }
            if (at == block.Length) { writer.Write(block, 0, at); at = 0; }
        }
        if (at > 0) writer.Write(block, 0, at);
    }

    /// <summary>
    /// Write the sound as OGG Vorbis, what the game reads its own sounds as and
    /// a tenth the size of a WAV - which matters for music.
    /// <para/>
    /// With silence in front. The encoder's first block only primes the
    /// decoder - every standard decoder drops it, the reference libvorbis as
    /// much as NAudio's - so without it the start of the sound was lost and
    /// every cut landed early (23 ms at 44.1 kHz). How long that block is
    /// depends on the sample rate, so it is measured rather than assumed
    /// (<see cref="Preroll"/>): what comes out is the sound frame for frame,
    /// from the first sample to the last (1.7.0, and a test).
    /// </summary>
    public void WriteOgg(string path, float quality = 0.6f)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        Encode(file, Channels, Frames, ChannelCount, SampleRate, quality, Preroll(ChannelCount, SampleRate, quality));
    }

    private static void Encode(Stream file, float[][] channels, int frames, int channelCount, int rate,
                               float quality, int pad)
    {
        var info = OggVorbisEncoder.VorbisInfo.InitVariableBitRate(channelCount, rate, quality);
        var stream = new OggVorbisEncoder.OggStream(new Random().Next());
        stream.PacketIn(OggVorbisEncoder.HeaderPacketBuilder.BuildInfoPacket(info));
        stream.PacketIn(OggVorbisEncoder.HeaderPacketBuilder.BuildCommentsPacket(new OggVorbisEncoder.Comments()));
        stream.PacketIn(OggVorbisEncoder.HeaderPacketBuilder.BuildBooksPacket(info));

        void Flush(bool force)
        {
            while (stream.PageOut(out var page, force))
            {
                file.Write(page.Header, 0, page.Header.Length);
                file.Write(page.Body, 0, page.Body.Length);
            }
        }
        Flush(true);

        var state = OggVorbisEncoder.ProcessingState.Create(info);
        const int block = 4096;
        int total = pad + frames;
        for (int at = 0; at < total; at += block)
        {
            int len = Math.Min(block, total - at);
            var buffer = new float[channelCount][];
            for (int c = 0; c < channelCount; c++)
            {
                buffer[c] = new float[len];
                for (int i = 0; i < len; i++)
                {
                    int f = at + i - pad;
                    buffer[c][i] = f < 0 ? 0f : Math.Clamp(channels[c][f], -1f, 1f);
                }
            }
            state.WriteData(buffer, len);
            while (!stream.Finished && state.PacketOut(out var packet))
            {
                stream.PacketIn(packet);
                Flush(false);
            }
        }
        state.WriteEndOfStream();
        while (!stream.Finished && state.PacketOut(out var last))
        {
            stream.PacketIn(last);
            Flush(false);
        }
        Flush(true);
    }

    private static readonly System.Collections.Generic.Dictionary<(int, int, float), int> _preroll = new();

    /// <summary>
    /// How many frames a decoder drops from the front of what this encoder
    /// writes, at this rate: a click is written a known distance in, read back,
    /// and where it lands says. Measured once per rate and kept.
    /// </summary>
    internal static int Preroll(int channelCount, int rate, float quality)
    {
        lock (_preroll)
            if (_preroll.TryGetValue((channelCount, rate, quality), out int known)) return known;

        const int at = 8192, length = 16384;
        var probe = new float[channelCount][];
        for (int c = 0; c < channelCount; c++)
        {
            probe[c] = new float[length];
            for (int i = 0; i < 16; i++) probe[c][at + i] = 0.9f;
        }
        int dropped = 0;
        try
        {
            using var ms = new MemoryStream();
            Encode(ms, probe, length, channelCount, rate, quality, 0);
            ms.Position = 0;
            using var reader = new NAudio.Vorbis.VorbisWaveReader(ms);
            var provider = reader.ToSampleProvider();
            var buffer = new float[length * channelCount];
            int got = 0, n;
            while (got < buffer.Length && (n = provider.Read(buffer, got, buffer.Length - got)) > 0) got += n;
            int peak = 0;
            float best = 0;
            for (int f = 0; f < got / channelCount; f++)
            {
                float v = Math.Abs(buffer[f * channelCount]);
                if (v > best) { best = v; peak = f; }
            }
            // The click's own start, a few frames before its loudest point.
            int start = peak;
            while (start > 0 && Math.Abs(buffer[(start - 1) * channelCount]) > best * 0.5f) start--;
            dropped = Math.Max(0, at - start);
        }
        catch (Exception) { dropped = 0; }

        lock (_preroll) _preroll[(channelCount, rate, quality)] = dropped;
        return dropped;
    }

    // ── Playing ──────────────────────────────────────────────────────────

    /// <summary>The samples interleaved, as a player takes them, from
    /// <paramref name="fromFrame"/> on.</summary>
    public float[] Interleaved(int fromFrame = 0)
    {
        fromFrame = Math.Clamp(fromFrame, 0, Frames);
        int frames = Frames - fromFrame;
        var interleaved = new float[frames * ChannelCount];
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < ChannelCount; c++)
                interleaved[f * ChannelCount + c] = Channels[c][fromFrame + f];
        return interleaved;
    }
}
