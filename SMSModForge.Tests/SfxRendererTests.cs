using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Services.Audio;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What the SFX tab's editor does to a sound (the author, 1.7.0: trim, pitch,
/// speed, echo, reverb). Measured on the samples made, never on the settings
/// asked for: a pitch that is "2 semitones" in the edit and the same frequency
/// in the file is the bug these are here to catch.
/// </summary>
public sealed class SfxRendererTests
{
    private readonly ITestOutputHelper _out;
    public SfxRendererTests(ITestOutputHelper o) => _out = o;

    private const int Rate = 44100;

    /// <summary>A sine, in <paramref name="channels"/> channels, the right one
    /// at a different level so a mix-down would show.</summary>
    private static AudioData Tone(double hz, double seconds, int channels = 2, float level = 0.5f)
    {
        int n = (int)(seconds * Rate);
        var ch = new float[channels][];
        for (int c = 0; c < channels; c++)
        {
            ch[c] = new float[n];
            float l = c == 0 ? level : level / 2;
            for (int i = 0; i < n; i++) ch[c][i] = l * (float)Math.Sin(2 * Math.PI * hz * i / Rate);
        }
        return new AudioData(ch, Rate);
    }

    /// <summary>The frequency of a sound's middle, by counting where it crosses zero going up.</summary>
    private static double Frequency(AudioData a, int channel = 0)
    {
        var x = a.Channels[channel];
        int from = x.Length / 4, to = x.Length * 3 / 4;
        int first = -1, last = -1, crossings = 0;
        for (int i = from + 1; i < to; i++)
            if (x[i - 1] < 0 && x[i] >= 0)
            {
                if (first < 0) first = i; else crossings++;
                last = i;
            }
        return crossings * (double)a.SampleRate / (last - first);
    }

    private static double Rms(float[] x, int from, int to)
    {
        double s = 0;
        for (int i = from; i < to; i++) s += x[i] * x[i];
        return Math.Sqrt(s / Math.Max(1, to - from));
    }

    // ── Pieces ───────────────────────────────────────────────────────────

    [Fact]
    public void ThePiecesKeptPlayOneAfterAnotherAndNothingElse()
    {
        var src = Tone(440, 2);
        var pieces = new List<SfxPieceDef> { new() { From = 1.5, To = 2.0 }, new() { From = 0.0, To = 0.25 } };

        var cut = SfxRenderer.Cut(src, pieces);

        _out.WriteLine($"{cut.Seconds:0.000} s from {src.Seconds:0.000} s");
        Assert.Equal(0.75, cut.Seconds, 3);
        Assert.Equal(2, cut.ChannelCount);
        // The order is the pieces' order: the first quarter second of the cut
        // is what was at 1.5 s.
        int at = (int)(0.1 * Rate);
        Assert.Equal(src.Channels[0][(int)(1.6 * Rate)], cut.Channels[0][at], 4);
        Assert.Equal(src.Channels[1][(int)(1.6 * Rate)], cut.Channels[1][at], 4);
    }

    [Fact]
    public void ACutEdgeIsFadedSoItDoesNotClick_AndTheRecordingsOwnEdgesAreNot()
    {
        var src = new AudioData(new[] { Enumerable.Repeat(0.8f, Rate).ToArray() }, Rate);
        var cut = SfxRenderer.Cut(src, new List<SfxPieceDef> { new() { From = 0, To = 0.5 }, new() { From = 0.6, To = 1.0 } });

        int join = (int)(0.5 * Rate);
        _out.WriteLine($"either side of the join: {cut.Channels[0][join - 1]:0.000} | {cut.Channels[0][join]:0.000}");
        Assert.Equal(0.8f, cut.Channels[0][0], 3);                // the recording's start: untouched
        Assert.True(Math.Abs(cut.Channels[0][join - 1]) < 0.05);  // faded out into the cut...
        Assert.True(Math.Abs(cut.Channels[0][join]) < 0.05);      // ...and in out of it
        Assert.Equal(0.8f, cut.Channels[0][cut.Frames - 1], 3);   // the recording's end: untouched
    }

    // ── Speed and pitch ──────────────────────────────────────────────────

    [Theory]
    [InlineData(2.0)]
    [InlineData(0.5)]
    [InlineData(1.3)]
    public void SpeedChangesTheLengthAndNotThePitch(double speed)
    {
        var src = Tone(440, 2);
        var made = SfxRenderer.Render(src, null, new SfxEditDef { Speed = speed });

        double hz = Frequency(made);
        _out.WriteLine($"speed {speed}: {made.Seconds:0.000} s at {hz:0.0} Hz");
        Assert.InRange(made.Seconds, 2 / speed * 0.98, 2 / speed * 1.02);
        Assert.InRange(hz, 440 * 0.98, 440 * 1.02);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(-12)]
    [InlineData(2)]
    public void PitchChangesTheFrequencyAndNotTheLength(double semitones)
    {
        var src = Tone(440, 2);
        var made = SfxRenderer.Render(src, null, new SfxEditDef { Pitch = semitones });

        double want = 440 * Math.Pow(2, semitones / 12);
        double hz = Frequency(made);
        _out.WriteLine($"{semitones:+0;-0} semitones: {made.Seconds:0.000} s at {hz:0.0} Hz (want {want:0.0})");
        Assert.InRange(made.Seconds, 2 * 0.98, 2 * 1.02);
        Assert.InRange(hz, want * 0.98, want * 1.02);
    }

    [Fact]
    public void AStereoSoundStaysStereo()
    {
        var made = SfxRenderer.Render(Tone(440, 1), null, new SfxEditDef { Pitch = 3, Speed = 1.2 });
        Assert.Equal(2, made.ChannelCount);
        double left = Rms(made.Channels[0], 0, made.Frames), right = Rms(made.Channels[1], 0, made.Frames);
        _out.WriteLine($"left {left:0.000}, right {right:0.000}");
        Assert.InRange(right / left, 0.45, 0.55);   // the right was made at half the level
    }

    // ── Echo and reverb ──────────────────────────────────────────────────

    [Fact]
    public void AnEchoRepeatsTheSoundAfterItsDelay_QuieterEachTime()
    {
        // A click, then silence: whatever is heard later is the echo.
        var x = new float[Rate];
        for (int i = 0; i < 200; i++) x[i] = 0.9f;
        var made = SfxRenderer.Echo(new AudioData(new[] { x }, Rate), 0.25, 0.5);

        int d = (int)(0.25 * Rate);
        var y = made.Channels[0];
        _out.WriteLine($"{made.Seconds:0.000} s; at 0.25 s {y[d + 100]:0.000}, at 0.5 s {y[2 * d + 100]:0.000}");
        Assert.Equal(0f, y[d / 2], 6);                  // silent between
        Assert.Equal(0.45, y[d + 100], 3);               // the first repeat, at half
        Assert.Equal(0.225, y[2 * d + 100], 3);          // the next, at a quarter
        Assert.True(made.Seconds >= 1.0);                // the sound kept its length
    }

    [Fact]
    public void ReverbGivesTheSoundATailAndABiggerRoomALongerOne()
    {
        var x = new float[Rate / 2];
        for (int i = 0; i < 2000; i++) x[i] = 0.8f * (float)Math.Sin(2 * Math.PI * 300 * i / Rate);
        var src = new AudioData(new[] { x }, Rate);

        var small = SfxRenderer.Reverb(src, 0.6, 0.1);
        var big = SfxRenderer.Reverb(src, 0.6, 0.9);

        double tailSmall = Rms(small.Channels[0], Rate / 4, Rate / 2);
        double tailBig = Rms(big.Channels[0], Rate / 4, Rate / 2);
        _out.WriteLine($"small room {small.Seconds:0.00} s, tail rms {tailSmall:0.0000}; hall {big.Seconds:0.00} s, tail rms {tailBig:0.0000}");
        Assert.Equal(0, Rms(x, Rate / 4, Rate / 2));    // the dry sound is silent there
        Assert.True(tailSmall > 0.0005, "no reverb tail");
        Assert.True(tailBig > tailSmall, "a bigger room should ring longer");
        Assert.True(big.Seconds > small.Seconds);
    }

    [Fact]
    public void EffectsThatPushPastFullScaleAreBroughtBackUnder()
    {
        var made = SfxRenderer.Render(Tone(200, 1, 1, 0.95f), null,
            new SfxEditDef { Echo = new SfxEchoDef { Delay = 0.05, Amount = 0.9 } });
        float peak = made.Channels[0].Max(Math.Abs);
        _out.WriteLine($"peak {peak:0.000}");
        Assert.True(peak <= 0.991f);
    }

    [Fact]
    public void NoEditIsTheRecordingAsItIs()
    {
        var src = Tone(440, 0.5);
        var made = SfxRenderer.Render(src, null, new SfxEditDef());
        Assert.Equal(src.Frames, made.Frames);
        Assert.Equal(src.Channels[0][1234], made.Channels[0][1234]);
    }

    // ── Files ────────────────────────────────────────────────────────────

    [Fact]
    public void AWrittenSoundReadsBackAsItWas()
    {
        string path = Path.Combine(Path.GetTempPath(), "smsmodforge-sfx-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var src = Tone(440, 0.5);
            src.WriteWav(path);
            var back = AudioData.Load(path);
            _out.WriteLine($"{back.ChannelCount} ch, {back.SampleRate} Hz, {back.Frames} frames");
            Assert.Equal(src.ChannelCount, back.ChannelCount);
            Assert.Equal(src.SampleRate, back.SampleRate);
            Assert.Equal(src.Frames, back.Frames);
            // Not one frame lost or gained at the start: the cut is where it was made.
            Assert.Equal(src.Channels[0][1000], back.Channels[0][1000], 3);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WhatThePlayerIsGivenReadsThroughToTheSoundCard()
    {
        // The output does not read floats: it reads BYTES, through NAudio's
        // conversion, whose buffer is a byte array wearing a float array's
        // type. A reader that used Array.Copy failed on its first read - and
        // the editor's Play button never made a sound. Read here the same way,
        // with no sound card needed.
        var tone = Tone(440, 0.5);
        var provider = Services.SfxPreviewPlayer.Provider(tone, 0.5f, 0.25, out _);
        var bytes = new byte[4 * 2 * 1000];
        int got = new NAudio.Wave.SampleProviders.SampleToWaveProvider(provider).Read(bytes, 0, bytes.Length);

        Assert.Equal(bytes.Length, got);
        float first = BitConverter.ToSingle(bytes, 0);
        // From a quarter second in, at half the volume.
        Assert.Equal(tone.Channels[0][(int)(0.25 * Rate)] * 0.5f, first, 5);
        float rightTen = BitConverter.ToSingle(bytes, (10 * 2 + 1) * 4);
        Assert.Equal(tone.Channels[1][(int)(0.25 * Rate) + 10] * 0.5f, rightTen, 5);
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(22050)]
    public void AnOggWrittenComesBackFrameForFrame_AtAnyRate(int rate)
    {
        // The encoder's first block only primes a decoder, and every standard
        // decoder drops it: without the silence written in front, the first
        // 23 ms were lost and a cut landed 23 ms early.
        string path = Path.Combine(Path.GetTempPath(), "smsmodforge-ogg-" + Guid.NewGuid().ToString("N") + ".ogg");
        try
        {
            // A sweep: it lines up with itself in one place only, where a
            // steady tone would line up every cycle.
            var x = new float[rate];
            for (int i = 0; i < x.Length; i++)
            {
                double t = (double)i / rate;
                x[i] = 0.5f * (float)Math.Sin(2 * Math.PI * (200 * t + 900 * t * t));
            }
            var src = new AudioData(new[] { x, (float[])x.Clone() }, rate);
            src.WriteOgg(path);
            var back = AudioData.Load(path);
            _out.WriteLine($"{back.Frames} frames back of {src.Frames}, {new FileInfo(path).Length} bytes");
            Assert.Equal(src.Frames, back.Frames);
            Assert.Equal(src.ChannelCount, back.ChannelCount);

            // Lined up: the best match is at no offset at all.
            double Err(int off)
            {
                double e = 0;
                for (int i = rate / 8; i < rate * 3 / 4; i += 3) e += Math.Abs(back.Channels[0][i + off] - src.Channels[0][i]);
                return e;
            }
            int best = Enumerable.Range(-1200, 2401).Where(o => rate / 8 + o >= 0).OrderBy(Err).First();
            _out.WriteLine($"best alignment offset {best}");
            Assert.Equal(0, best);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ThePeaksDrawnAreTheSoundsLoudestAndQuietest()
    {
        var src = Tone(5, 1, 1, 0.5f);   // one slow cycle per fifth of a second
        var peaks = new WavePeaks(src);
        var (lo, hi) = peaks.Range(0, 1);
        Assert.Equal(-0.5f, lo, 2);
        Assert.Equal(0.5f, hi, 2);
        var (lo2, hi2) = peaks.Range(0, 0.04);   // the first fifth of a rising quarter
        Assert.True(lo2 >= -0.001 && hi2 < 0.5);
    }
}
