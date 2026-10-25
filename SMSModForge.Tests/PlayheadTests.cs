using System;
using NAudio.Wave;
using SMSModForge.Services;
using SMSModForge.Services.Audio;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The line that follows a sound on the strip is where the sound is HEARD
/// (the author, 2026-10-21: it moved in jumps, and on a short sound it could
/// not show where the sound ended).
/// <para/>
/// The output reads ahead of the speakers a buffer at a time, so the reading
/// is the wrong clock: it jumps a buffer at a time, and a short sound is read
/// whole before any of it plays. The device's own count of what it has played
/// is the right one.
/// </summary>
public sealed class PlayheadTests
{
    private readonly ITestOutputHelper _out;
    public PlayheadTests(ITestOutputHelper o) => _out = o;

    private const int Rate = 1000;

    private sealed class Device : IWavePosition
    {
        public long Bytes;
        public WaveFormat OutputWaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);
        public long GetPosition() => Bytes;
    }

    /// <summary>Two seconds of stereo, played from half a second in, with
    /// 0.8 s of it already handed to the output.</summary>
    private static SfxPreviewPlayer.MemorySound ReadAhead()
    {
        var sound = new AudioData(new[] { new float[2 * Rate], new float[2 * Rate] }, Rate);
        var memory = new SfxPreviewPlayer.MemorySound(sound, Rate / 2);
        memory.Read(new float[1600], 0, 1600);   // 800 frames: what the output has buffered
        return memory;
    }

    [Fact]
    public void TheLineIsWhereTheSpeakersHaveGot_NotWhereTheReadingHas()
    {
        var memory = ReadAhead();
        var device = new Device { Bytes = (long)(0.2 * Rate * 2 * 4) };   // 0.2 s played
        double at = SfxPreviewPlayer.Heard(memory, device);
        _out.WriteLine($"read to {memory.Seconds:0.000} s, heard to {at:0.000} s");
        Assert.Equal(1.3, memory.Seconds, 3);    // the old answer, a buffer ahead
        Assert.Equal(0.7, at, 3);
    }

    [Fact]
    public void NeverPastWhatHasBeenRead()
    {
        var memory = ReadAhead();
        var device = new Device { Bytes = 10 * Rate * 2 * 4 };
        Assert.Equal(memory.Seconds, SfxPreviewPlayer.Heard(memory, device), 3);
    }

    [Fact]
    public void WithNoDeviceToAsk_TheReadingIsAllThereIs()
    {
        var memory = ReadAhead();
        Assert.Equal(memory.Seconds, SfxPreviewPlayer.Heard(memory, null), 3);
    }
}
