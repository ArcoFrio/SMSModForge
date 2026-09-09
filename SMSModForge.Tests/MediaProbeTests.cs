using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Reading what a picked file is, and whether a video carries sound.
/// <para/>
/// The point of doing it from the container rather than from a media pipeline
/// is that it answers instantly, offline, with no codecs installed — so a
/// volume slider can appear the moment a path is typed rather than after
/// something spins up. These check it answers CORRECTLY, which is the part
/// that makes that worth doing.
/// </summary>
public sealed class MediaProbeTests
{
    private readonly ITestOutputHelper _out;
    public MediaProbeTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void TheKindComesFromTheExtension()
    {
        Assert.Equal(MediaProbe.MediaKind.Still, MediaProbe.KindOf("a/b/c.png"));
        Assert.Equal(MediaProbe.MediaKind.Still, MediaProbe.KindOf("SHOUTED.PNG"));
        Assert.Equal(MediaProbe.MediaKind.Gif, MediaProbe.KindOf("loop.gif"));
        Assert.Equal(MediaProbe.MediaKind.Video, MediaProbe.KindOf("clip.mp4"));
        Assert.Equal(MediaProbe.MediaKind.Video, MediaProbe.KindOf("clip.webm"));

        // Not everything that decodes: a format that loads on the author's
        // machine and not in the game is the worst outcome available.
        Assert.Equal(MediaProbe.MediaKind.Unknown, MediaProbe.KindOf("clip.avi"));
        Assert.Equal(MediaProbe.MediaKind.Unknown, MediaProbe.KindOf("clip.mkv"));
        Assert.Equal(MediaProbe.MediaKind.Unknown, MediaProbe.KindOf(""));
        Assert.Equal(MediaProbe.MediaKind.Unknown, MediaProbe.KindOf(null));

        Assert.True(MediaProbe.IsAnimated("x.gif"));
        Assert.True(MediaProbe.IsAnimated("x.mp4"));
        Assert.False(MediaProbe.IsAnimated("x.png"));
    }

    [Fact]
    public void AnMp4IsAskedWhetherItHasASoundTrack()
    {
        using var dir = new Scratch();

        // Two files identical but for the handler on the second track. If the
        // probe cannot tell these apart it is not reading anything.
        string withSound = dir.Write("sound.mp4", Mp4(withAudio: true));
        string silent = dir.Write("silent.mp4", Mp4(withAudio: false));

        _out.WriteLine($"sound.mp4  -> {MediaProbe.HasAudio(withSound)}");
        _out.WriteLine($"silent.mp4 -> {MediaProbe.HasAudio(silent)}");

        Assert.True(MediaProbe.HasAudio(withSound));
        Assert.False(MediaProbe.HasAudio(silent));
    }

    [Fact]
    public void AWebmIsAskedTheSameQuestion()
    {
        using var dir = new Scratch();
        string withSound = dir.Write("sound.webm", Webm(withAudio: true));
        string silent = dir.Write("silent.webm", Webm(withAudio: false));

        Assert.True(MediaProbe.HasAudio(withSound));
        Assert.False(MediaProbe.HasAudio(silent));
    }

    [Fact]
    public void SomethingUnreadableSaysSoRatherThanSayingNo()
    {
        // The distinction that matters. Hiding a volume slider because a file
        // could not be parsed leaves an author unable to turn down a video
        // that is, in fact, loud.
        using var dir = new Scratch();

        Assert.Null(MediaProbe.HasAudio(dir.Write("junk.mp4", new byte[] { 1, 2, 3, 4, 5 })));
        Assert.Null(MediaProbe.HasAudio(dir.Write("junk.webm", new byte[] { 9, 9, 9, 9 })));
        Assert.Null(MediaProbe.HasAudio(Path.Combine(dir.Path, "missing.mp4")));

        // A still is confidently silent - there is nothing to be unsure about.
        Assert.False(MediaProbe.HasAudio("picture.png"));
    }

    [Fact]
    public void AVideoWithOnlyPictureIsNotMistakenForOneWithSound()
    {
        // The control for the MP4 walk: 'vide' and 'soun' differ in four
        // bytes, and a probe that matched the box rather than the handler
        // would call every video audible.
        using var dir = new Scratch();
        string path = dir.Write("videoonly.mp4", Mp4(withAudio: false));

        Assert.False(MediaProbe.HasAudio(path));

        // And the bytes really do contain a track - so "false" is a reading,
        // not a parse that gave up early.
        Assert.Contains("vide", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path)));
    }

    // ── Building the files the probe reads ───────────────────────────

    /// <summary>An ISO-BMFF skeleton: ftyp, then moov holding a video track
    /// and optionally an audio one. Only the boxes the probe walks.</summary>
    private static byte[] Mp4(bool withAudio)
    {
        var tracks = new List<byte[]> { Box("trak", Box("mdia", Hdlr("vide"))) };
        if (withAudio) tracks.Add(Box("trak", Box("mdia", Hdlr("soun"))));

        return Cat(Box("ftyp", new byte[] { (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 2, 0 }),
                   Box("moov", Cat(tracks.ToArray())));
    }

    /// <summary>version+flags(4), predefined(4), then the handler's four
    /// characters — the layout the probe reads.</summary>
    private static byte[] Hdlr(string handler)
        => Box("hdlr", Cat(new byte[8], System.Text.Encoding.ASCII.GetBytes(handler),
                           new byte[12]));

    private static byte[] Box(string type, byte[] payload)
    {
        int size = 8 + payload.Length;
        var head = new byte[8];
        head[0] = (byte)(size >> 24); head[1] = (byte)(size >> 16);
        head[2] = (byte)(size >> 8);  head[3] = (byte)size;
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(head, 4);
        return Cat(head, payload);
    }

    /// <summary>An EBML header, then a TrackType element saying audio or
    /// video — id 0x83, size 0x81, value 2 or 1.</summary>
    private static byte[] Webm(bool withAudio)
        => Cat(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 },
               new byte[64],
               new byte[] { 0x83, 0x81, 0x01 },                       // a video track
               withAudio ? new byte[] { 0x83, 0x81, 0x02 } : new byte[3]);

    private static byte[] Cat(params byte[][] parts)
    {
        var all = new List<byte>();
        foreach (var p in parts) all.AddRange(p);
        return all.ToArray();
    }

    private sealed class Scratch : IDisposable
    {
        public string Path { get; }
        public Scratch()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          "smsmodforge-media-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }
        public string Write(string name, byte[] bytes)
        {
            string p = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(p, bytes);
            return p;
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
