using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Decoding a GIF into the frames the game plays.
/// <para/>
/// The reason this is done in the editor rather than in the game is quality: a
/// GIF frame is a PATCH at an offset with a disposal rule, not a picture, and
/// the common case is a frame that redraws only what moved. These check the
/// composition actually happens — a decoder that just wrote each frame out
/// would pass a "did it produce files" test and produce a flickering mess.
/// </summary>
public sealed class GifFramesTests
{
    private readonly ITestOutputHelper _out;
    public GifFramesTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void EveryFrameComesOutAsAPngWithItsDelay()
    {
        using var dir = new Scratch();
        string gif = dir.Path + "/anim.gif";
        WriteGif(gif, 5, 32);

        var made = GifFrames.Decode(gif, dir.Path + "/anim.frames");
        Assert.NotNull(made);

        _out.WriteLine($"{made!.Count} frames at {made.Width}x{made.Height}");
        Assert.Equal(5, made.Count);
        Assert.Equal(32, made.Width);

        // Numbered so a plain sort is the play order, whatever lists them.
        for (int i = 0; i < 5; i++)
            Assert.True(File.Exists(Path.Combine(dir.Path, "anim.frames",
                                                 MediaKinds.FrameName(i))));
        Assert.Equal("0000.png", MediaKinds.FrameName(0));
        Assert.Equal("0012.png", MediaKinds.FrameName(12));

        // And the manifest reads back, which is what the game will do.
        var reread = GifFrames.Read(dir.Path + "/anim.frames");
        Assert.NotNull(reread);
        Assert.Equal(made.Delays, reread!.Delays);
    }

    [Fact]
    public void ReplacingALongerGifDoesNotLeaveStrayFramesBehind()
    {
        // The bug this guards is silent and awful: swap a 9-frame animation
        // for a 3-frame one and the game plays three frames followed by six
        // frames of the previous animation.
        using var dir = new Scratch();
        string frames = dir.Path + "/a.frames";

        WriteGif(dir.Path + "/a.gif", 9, 16);
        Assert.Equal(9, GifFrames.Decode(dir.Path + "/a.gif", frames)!.Count);
        Assert.Equal(9, Directory.GetFiles(frames, "*.png").Length);

        WriteGif(dir.Path + "/a.gif", 3, 16);
        Assert.Equal(3, GifFrames.Decode(dir.Path + "/a.gif", frames)!.Count);
        Assert.Equal(3, Directory.GetFiles(frames, "*.png").Length);
    }

    [Fact]
    public void FramesAreComposedOntoWhatCameBeforeThem()
    {
        // The heart of it. This GIF's second frame is a small patch in the
        // corner; everything else should still be the first frame's colour.
        // A decoder that wrote frames out as-is leaves the rest transparent,
        // and the animation flickers.
        using var dir = new Scratch();
        string gif = dir.Path + "/patch.gif";
        WritePatchGif(gif);

        var made = GifFrames.Decode(gif, dir.Path + "/patch.frames");
        Assert.NotNull(made);
        Assert.Equal(2, made!.Count);

        var second = Load(Path.Combine(dir.Path, "patch.frames", MediaKinds.FrameName(1)));

        // The patch landed...
        Assert.True(Opaque(second, 2, 2), "the second frame's own patch is missing");

        // ...and the rest of the picture survived from frame one, rather than
        // being left as a hole.
        Assert.True(Opaque(second, 20, 20),
                    "frame 2 did not compose onto frame 1 — the rest is transparent");
    }

    [Fact]
    public void SomethingThatIsNotAGifIsRefusedRatherThanHalfWritten()
    {
        using var dir = new Scratch();
        File.WriteAllBytes(dir.Path + "/not.gif", new byte[] { 1, 2, 3, 4, 5, 6 });

        Assert.Null(GifFrames.Decode(dir.Path + "/not.gif", dir.Path + "/not.frames"));
        Assert.Null(GifFrames.Decode(dir.Path + "/missing.gif", dir.Path + "/x.frames"));
        Assert.Null(GifFrames.Read(dir.Path + "/never-decoded"));
    }

    [Fact]
    public void TheFramesFolderIsDerivedFromTheFileTheAuthorPicked()
    {
        // Derived, not stored: the manifest keeps naming the file the author
        // chose, and nothing has to be kept in step with anything.
        Assert.Equal("Scenes/dance.frames", MediaKinds.FramesFolderFor("Scenes/dance.gif"));
        Assert.Equal("a.b/c.frames", MediaKinds.FramesFolderFor("a.b/c.gif"));
        Assert.Equal("", MediaKinds.FramesFolderFor(""));
    }

    [Fact]
    public void ADecodedGifPlaysAtItsOwnRate()
    {
        // The join between the two halves: the delays the decoder read are the
        // delays the clock plays. Written as one test because a mismatch in
        // units - hundredths against seconds - would pass both alone.
        using var dir = new Scratch();
        WriteGif(dir.Path + "/timed.gif", 4, 16);

        var made = GifFrames.Decode(dir.Path + "/timed.gif", dir.Path + "/timed.frames")!;
        var clock = new AnimationClock(made.Delays.ToArray(), GifFrames.DefaultDelaySeconds);

        _out.WriteLine($"delays: {string.Join(", ", made.Delays)} -> {clock.Duration}s");

        Assert.Equal(4, clock.FrameCount);
        Assert.Equal(made.Delays.Sum(), clock.Duration, 6);
        Assert.Equal(0, clock.FrameAt(0.0, true));
        Assert.Equal(0, clock.FrameAt(clock.Duration, true));      // loops cleanly
    }

    // ── Building GIFs to decode ──────────────────────────────────────

    private static void WriteGif(string path, int frames, int size)
    {
        var enc = new GifBitmapEncoder();
        for (int i = 0; i < frames; i++)
            enc.Frames.Add(BitmapFrame.Create(Filled(size, size, Colors.White)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>A full first frame, then a small patch — the shape the
    /// composition test needs.</summary>
    private static void WritePatchGif(string path)
    {
        var enc = new GifBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(Filled(32, 32, Colors.Red)));
        enc.Frames.Add(BitmapFrame.Create(Filled(32, 32, Colors.Blue)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static BitmapSource Filled(int w, int h, Color colour)
    {
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        var row = new byte[w * h * 4];
        for (int i = 0; i < row.Length; i += 4)
        {
            row[i] = colour.B; row[i + 1] = colour.G; row[i + 2] = colour.R; row[i + 3] = 255;
        }
        bmp.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), row, w * 4, 0);
        bmp.Freeze();
        return bmp;
    }

    private static BitmapSource Load(string path)
    {
        using var fs = File.OpenRead(path);
        var dec = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat,
                                       BitmapCacheOption.OnLoad);
        return dec.Frames[0];
    }

    private static bool Opaque(BitmapSource image, int x, int y)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3] > 0;
    }

    private sealed class Scratch : IDisposable
    {
        public string Path { get; }
        public Scratch()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          "smsmodforge-gif-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
