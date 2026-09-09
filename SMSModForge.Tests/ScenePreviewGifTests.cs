using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A GIF scene in the preview.
/// <para/>
/// The video layer is deliberately held on its first frame, because a video
/// costs a decode pipeline and a codec Windows may not have. A GIF costs a
/// blit with the decoder already in the box, so it plays — and it has to,
/// because an animation whose preview never moves cannot be judged without
/// exporting the pack and starting the game.
/// <para/>
/// What is checked is that the picture actually CHANGES and that it changes to
/// the right thing: a preview that cycles the wrong frames, or the same frame
/// forever, both look like a working animation from a distance.
/// </summary>
public sealed class ScenePreviewGifTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public ScenePreviewGifTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "scenegif-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // ── A GIF to look at ─────────────────────────────────────────────

    private static readonly Color[] Sequence = { Colors.Red, Colors.Lime, Colors.Blue };

    /// <summary>Write a GIF of flat, distinguishable frames, so "which frame is
    /// showing" is answerable by reading one pixel.</summary>
    private string WriteGif(string name, int size, int frames)
    {
        var encoder = new GifBitmapEncoder();
        for (int i = 0; i < frames; i++)
        {
            var colour = Sequence[i % Sequence.Length];
            var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[size * size * 4];
            for (int p = 0; p < pixels.Length; p += 4)
            {
                pixels[p] = colour.B;
                pixels[p + 1] = colour.G;
                pixels[p + 2] = colour.R;
                pixels[p + 3] = 255;
            }
            bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
        }

        string path = Path.Combine(_dir, name);
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private static void WritePng(string path, int size)
    {
        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>The top-left pixel of whatever the art layer is showing.</summary>
    private static Color PixelOf(Image layer)
    {
        var source = (BitmapSource)layer.Source;
        var one = new CroppedBitmap(source, new Int32Rect(0, 0, 1, 1));
        var bytes = new byte[4];
        one.CopyPixels(bytes, 4, 0);
        return Color.FromRgb(bytes[2], bytes[1], bytes[0]);
    }

    private static Image ArtLayer(ScenePreview preview) => preview.Children.OfType<Image>().First();

    // ── The tests ────────────────────────────────────────────────────

    [Fact]
    public void AGifPreviewMovesOnItsOwn()
    {
        WriteGif("loop.gif", 64, 3);

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview { PackRoot = _dir, SceneSprite = "loop.gif" };
            var host = new Border { Child = preview };   // so IsVisible can be true
            WindowHarness.Pump();

            var art = ArtLayer(preview);
            var seen = new List<Color> { PixelOf(art) };

            // The timer runs at the GIF's own rate, and this one carries no
            // delay field - so a tenth of a second per frame, the number every
            // browser settles on. Three frames is a third of a second.
            for (int i = 0; i < 12; i++)
            {
                WindowHarness.Wait(TimeSpan.FromMilliseconds(60));
                var now = PixelOf(art);
                if (now != seen[^1]) seen.Add(now);
            }

            _out.WriteLine("frames seen: " + string.Join(" -> ", seen.Select(c => c.ToString())));

            // It moved at all, which is the thing the video layer does not do.
            Assert.True(seen.Count > 1, "the preview never changed frame");

            // And it moved to the right pictures, in the right order. A cycle
            // through the wrong frames looks identical to a working one until
            // somebody checks.
            Assert.All(seen, c => Assert.Contains(c, Sequence));
            for (int i = 1; i < seen.Count; i++)
            {
                int was = Array.IndexOf(Sequence, seen[i - 1]);
                Assert.Equal(Sequence[(was + 1) % Sequence.Length], seen[i]);
            }
        });
    }

    [Fact]
    public void AStillIsNotAnimated()
    {
        // The control. Every assertion above would pass just as happily against
        // a preview that swapped pictures at random, so something that must NOT
        // move has to be checked too.
        WritePng(Path.Combine(_dir, "still.png"), 64);

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview { PackRoot = _dir, SceneSprite = "still.png" };
            var host = new Border { Child = preview };
            WindowHarness.Pump();

            var art = ArtLayer(preview);
            var first = PixelOf(art);

            for (int i = 0; i < 6; i++)
            {
                WindowHarness.Wait(TimeSpan.FromMilliseconds(60));
                Assert.Equal(first, PixelOf(art));
            }
        });
    }

    [Fact]
    public void AGifIsFittedTheSameWayAStillIs()
    {
        // A 512 GIF and a 512 PNG have to occupy the same space: the runtime
        // fits both to the scene square, and a preview that drew the animation
        // at twice the size of its neighbours would be worse than none.
        WriteGif("big.gif", 512, 3);
        WritePng(Path.Combine(_dir, "big.png"), 512);

        WindowHarness.Run(_ =>
        {
            double Drawn(string sprite)
            {
                var preview = new ScenePreview { PackRoot = _dir, SceneSprite = sprite };
                var host = new Border { Child = preview };
                WindowHarness.Pump();

                var art = ArtLayer(preview);
                var source = (BitmapSource)art.Source;

                // Measured, not calculated: pixels times scale is not what
                // lands on screen when a file carries a dpi of its own.
                art.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                _out.WriteLine($"{sprite}: {source.PixelWidth}x{source.PixelHeight} px, "
                               + $"{source.DpiX} dpi -> {art.DesiredSize.Width}px drawn");
                return art.DesiredSize.Width;
            }

            Assert.Equal(Drawn("big.png"), Drawn("big.gif"), 3);
        });
    }

    [Fact]
    public void SelectingSomethingElseStopsTheAnimation()
    {
        // Nothing should keep composing frames for a scene the author has
        // moved off. The GIF is the one thing here with a clock, so it is the
        // one thing that can be left running.
        WriteGif("loop.gif", 64, 3);
        WritePng(Path.Combine(_dir, "still.png"), 64);

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview { PackRoot = _dir, SceneSprite = "loop.gif" };
            var host = new Border { Child = preview };
            WindowHarness.Pump();
            WindowHarness.Wait(TimeSpan.FromMilliseconds(120));

            preview.SceneSprite = "still.png";
            WindowHarness.Pump();

            var art = ArtLayer(preview);
            var settled = PixelOf(art);
            for (int i = 0; i < 6; i++)
            {
                WindowHarness.Wait(TimeSpan.FromMilliseconds(60));
                Assert.Equal(settled, PixelOf(art));
            }
        });
    }

    [Fact]
    public void ASingleFrameGifIsJustAPicture()
    {
        WriteGif("one.gif", 64, 1);

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview { PackRoot = _dir, SceneSprite = "one.gif" };
            var host = new Border { Child = preview };
            WindowHarness.Pump();

            var art = ArtLayer(preview);
            Assert.NotNull(art.Source);
            Assert.Equal(Visibility.Visible, art.Visibility);

            var first = PixelOf(art);
            WindowHarness.Wait(TimeSpan.FromMilliseconds(120));
            Assert.Equal(first, PixelOf(art));
        });
    }

    [Fact]
    public void SomethingCallingItselfAGifAndNotBeingOneStillShowsWhatItCan()
    {
        // Authors rename files. Falling back to the still path means a PNG
        // called .gif previews as the picture it is, rather than as an error
        // about a format nobody was thinking about.
        string pretending = Path.Combine(_dir, "notreally.gif");
        WritePng(pretending, 64);

        Assert.Null(GifFrames.Reader.Open(pretending));   // the decoder refuses it

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview { PackRoot = _dir, SceneSprite = "notreally.gif" };
            var host = new Border { Child = preview };
            WindowHarness.Pump();

            Assert.NotNull(ArtLayer(preview).Source);
            Assert.Equal(Visibility.Visible, ArtLayer(preview).Visibility);
        });
    }

    // ── The reader underneath ────────────────────────────────────────

    [Fact]
    public void TheReaderComposesTheSameFramesTheSaveWrites()
    {
        // The preview and the exported pack read the same file through the same
        // code now. If they ever stop agreeing, the author is judging one
        // animation and shipping another.
        string gif = WriteGif("shared.gif", 64, 3);
        string frames = Path.Combine(_dir, "shared.frames");

        var written = GifFrames.Decode(gif, frames);
        Assert.NotNull(written);

        var reader = GifFrames.Reader.Open(gif);
        Assert.NotNull(reader);
        Assert.Equal(written!.Count, reader!.Count);
        Assert.Equal(written.Delays, reader.Delays.ToList());

        for (int i = 0; i < reader.Count; i++)
        {
            var composed = reader.Compose(i);
            var onDisk = new BitmapImage();
            using (var fs = File.OpenRead(Path.Combine(frames, SMSModForge.Shared.MediaKinds.FrameName(i))))
            {
                onDisk.BeginInit();
                onDisk.CacheOption = BitmapCacheOption.OnLoad;
                onDisk.StreamSource = fs;
                onDisk.EndInit();
            }

            var a = new byte[4];
            var b = new byte[4];
            new CroppedBitmap(composed, new Int32Rect(0, 0, 1, 1)).CopyPixels(a, 4, 0);
            new CroppedBitmap(onDisk, new Int32Rect(0, 0, 1, 1)).CopyPixels(b, 4, 0);

            _out.WriteLine($"frame {i}: composed {a[2]},{a[1]},{a[0]} / on disk {b[2]},{b[1]},{b[0]}");
            Assert.Equal(b[2], a[2]);
            Assert.Equal(b[1], a[1]);
            Assert.Equal(b[0], a[0]);
        }
    }

    [Fact]
    public void AskingForAFrameAlreadyPassedStartsTheWalkAgain()
    {
        // The loop does exactly this once per lap: frame 2, then frame 0. A
        // reader that could only go forward would hand back the wrong picture
        // there, and it would be wrong in a way that only shows up after the
        // preview has been open for a few seconds.
        string gif = WriteGif("rewind.gif", 64, 3);
        var reader = GifFrames.Reader.Open(gif);
        Assert.NotNull(reader);

        var forward = new List<byte[]>();
        for (int i = 0; i < reader!.Count; i++)
        {
            var bytes = new byte[4];
            new CroppedBitmap(reader.Compose(i), new Int32Rect(0, 0, 1, 1)).CopyPixels(bytes, 4, 0);
            forward.Add(bytes);
        }

        for (int i = 0; i < reader.Count; i++)
        {
            var bytes = new byte[4];
            new CroppedBitmap(reader.Compose(i), new Int32Rect(0, 0, 1, 1)).CopyPixels(bytes, 4, 0);
            Assert.Equal(forward[i], bytes);
        }
    }
}
