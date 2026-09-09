using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SMSModForge.View.Controls;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Previewing a scene whose art is a video.
/// <para/>
/// Kept deliberately cheap: the player is opened, paused on its first frame,
/// and left there. A loop running behind every selection would cost a decode
/// for as long as the tab is open, which is not what a preview is for.
/// <para/>
/// It cannot always work, and that is a fact about Windows rather than about
/// the pack: Unity ships its own decoders, so a format the game plays happily —
/// VP8 in a .webm — can be one Windows has never heard of. The preview says so
/// instead of looking broken.
/// </summary>
public sealed class ScenePreviewVideoTests
{
    private readonly ITestOutputHelper _out;
    public ScenePreviewVideoTests(ITestOutputHelper o) => _out = o;

    private static MediaElement Video(ScenePreview preview)
        => preview.Children.OfType<MediaElement>().Single();

    private static Image Art(ScenePreview preview)
        => preview.Children.OfType<Image>().First();

    [Fact]
    public void AVideoSceneUsesTheMediaLayerRatherThanTheArtLayer()
    {
        using var dir = new Scratch();
        File.WriteAllBytes(Path.Combine(dir.Path, "clip.mp4"), new byte[64]);

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview { PackRoot = dir.Path, SceneSprite = "clip.mp4" };
            WindowHarness.Pump();

            Assert.Equal(Visibility.Visible, Video(preview).Visibility);
            Assert.Equal(Visibility.Collapsed, Art(preview).Visibility);
            Assert.NotNull(Video(preview).Source);
        });
    }

    [Fact]
    public void AStillSceneLeavesTheMediaLayerAlone()
    {
        // The control, and the thing that keeps this cheap: selecting a still
        // must not leave a video player holding a file open.
        using var dir = new Scratch();
        WritePng(Path.Combine(dir.Path, "still.png"), 256, 256);
        File.WriteAllBytes(Path.Combine(dir.Path, "clip.mp4"), new byte[64]);

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview { PackRoot = dir.Path, SceneSprite = "clip.mp4" };
            WindowHarness.Pump();
            Assert.NotNull(Video(preview).Source);

            preview.SceneSprite = "still.png";
            WindowHarness.Pump();

            Assert.Equal(Visibility.Collapsed, Video(preview).Visibility);
            Assert.Null(Video(preview).Source);
            Assert.Equal(Visibility.Visible, Art(preview).Visibility);
        });
    }

    [Fact]
    public void ThePlayerIsSilentAndDoesNotRunOnItsOwn()
    {
        // A preview is not an audition, and a scene merely being selected
        // should not start playing anything.
        using var dir = new Scratch();
        File.WriteAllBytes(Path.Combine(dir.Path, "clip.mp4"), new byte[64]);

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview { PackRoot = dir.Path, SceneSprite = "clip.mp4" };
            WindowHarness.Pump();

            var player = Video(preview);
            Assert.Equal(0, player.Volume);
            Assert.True(player.ScrubbingEnabled, "a paused player needs scrubbing to show its frame");
            Assert.Equal(MediaState.Manual, player.LoadedBehavior);
        });
    }

    [Fact]
    public void TheVideoOccupiesTheSpaceAStillWould()
    {
        // The runtime fits a video to the same 256 square it fits art to, so
        // the preview has to agree or it is showing a size the game will not.
        using var dir = new Scratch();
        File.WriteAllBytes(Path.Combine(dir.Path, "clip.mp4"), new byte[64]);
        WritePng(Path.Combine(dir.Path, "still.png"), 256, 256);

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview { PackRoot = dir.Path, SceneSprite = "still.png" };
            WindowHarness.Pump();
            var art = Art(preview);
            art.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double stillWidth = art.DesiredSize.Width;

            preview.SceneSprite = "clip.mp4";
            WindowHarness.Pump();

            _out.WriteLine($"still {stillWidth}px, video {Video(preview).Width}px");
            Assert.Equal(stillWidth, Video(preview).Width, 3);
        });
    }

    /// <summary>The caption under the art, as opposed to the placeholder that
    /// replaces it. Added after it, so it is the second of the two.</summary>
    private static TextBlock Note(ScenePreview preview)
        => preview.Children.OfType<TextBlock>().ElementAt(1);

    [Fact]
    public void AFormatWindowsCannotPlayFallsBackToItsFirstFrame()
    {
        // A real VP8 .webm, which cannot be committed - so the check runs when
        // SMSMODFORGE_TEST_VIDEO points at one. See VideoStillTests for the
        // extraction itself, which is tested without needing a real file.
        string? real = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_VIDEO");
        if (string.IsNullOrWhiteSpace(real) || !File.Exists(real))
        {
            _out.WriteLine("SMSMODFORGE_TEST_VIDEO not set; skipping.");
            return;
        }

        using var dir = new Scratch();
        string name = Path.GetFileName(real);
        File.Copy(real, Path.Combine(dir.Path, name));

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview { PackRoot = dir.Path, SceneSprite = name };
            WindowHarness.Pump();

            // The player fails asynchronously, so this waits for it rather than
            // assuming how long it takes.
            for (int i = 0; i < 40 && Art(preview).Source == null
                            && Video(preview).Visibility == Visibility.Visible; i++)
                WindowHarness.Wait(TimeSpan.FromMilliseconds(100));

            if (Video(preview).Visibility == Visibility.Visible)
            {
                // This machine HAS a decoder for it, so the media layer is
                // playing it properly and there is nothing to fall back from.
                _out.WriteLine("the media player opened it; no fallback needed");
                return;
            }

            var art = Art(preview);
            var still = (System.Windows.Media.Imaging.BitmapSource?)art.Source;
            _out.WriteLine($"fell back to a still: {still?.PixelWidth}x{still?.PixelHeight}");

            Assert.NotNull(still);
            Assert.Equal(Visibility.Visible, art.Visibility);

            // Fitted like any other scene art, not left at its own size - and
            // MEASURED rather than calculated from the pixel count. An image
            // codec that reports 72 dpi for a video frame makes those two
            // numbers differ by a third, which is the whole of this bug: the
            // arithmetic said 384 while 512 was on screen, spilling out of the
            // frame and past the edge of the preview.
            art.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _out.WriteLine($"{still.PixelWidth}px at {still.DpiX} dpi"
                           + $" -> {art.DesiredSize.Width}px drawn");
            Assert.Equal(SMSModForge.Rendering.ArtFit.ScenePixels * 1.5,
                         art.DesiredSize.Width, 3);
            Assert.True(art.DesiredSize.Width <= ScenePreview.FixedSize,
                        "the art is wider than the preview it sits in");

            // And it says what it is. A still shown silently would read as an
            // animation that does not animate.
            Assert.Equal(Visibility.Visible, Note(preview).Visibility);
            Assert.Contains("First frame", Note(preview).Text);

            // Selecting anything else takes the caption with it.
            WritePng(Path.Combine(dir.Path, "still.png"), 256, 256);
            preview.SceneSprite = "still.png";
            WindowHarness.Pump();
            Assert.Equal(Visibility.Collapsed, Note(preview).Visibility);
        });
    }

    [Fact]
    public void AVideoNothingCanReadStillSaysWhyRatherThanShowingNothing()
    {
        // 64 zero bytes is not a video in any format, so no still can be lifted
        // out of it. The message has to survive the fallback being added, since
        // this is what most unplayable files will do.
        using var dir = new Scratch();
        File.WriteAllBytes(Path.Combine(dir.Path, "broken.webm"), new byte[64]);

        Assert.Null(SMSModForge.Model.VideoStill.FirstFrame(
            Path.Combine(dir.Path, "broken.webm")));
    }

    [Fact]
    public void ArtThatCarriesItsOwnDpiIsStillDrawnAtTheRightSize()
    {
        // Not a video-only problem. The runtime fits PIXELS to the scene
        // square and knows nothing about dpi, so a scene authored at 72 dpi -
        // which is what half the art tools on the planet write - has to come
        // out the same size as the same picture at 96.
        using var dir = new Scratch();
        WritePng(Path.Combine(dir.Path, "at96.png"), 512, 512);
        WritePngAt(Path.Combine(dir.Path, "at72.png"), 512, 512, 72);

        WindowHarness.Run(_ =>
        {
            double Drawn(string sprite)
            {
                var preview = new ScenePreview { PackRoot = dir.Path, SceneSprite = sprite };
                WindowHarness.Pump();

                var art = Art(preview);
                art.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                _out.WriteLine($"{sprite}: {((System.Windows.Media.Imaging.BitmapSource)art.Source).DpiX}"
                               + $" dpi -> {art.DesiredSize.Width}px");
                return art.DesiredSize.Width;
            }

            Assert.Equal(SMSModForge.Rendering.ArtFit.ScenePixels * 1.5, Drawn("at96.png"), 3);
            Assert.Equal(Drawn("at96.png"), Drawn("at72.png"), 3);
        });
    }

    private static void WritePngAt(string path, int w, int h, double dpi)
    {
        var pixels = new byte[w * h * 4];
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
            w, h, dpi, dpi, System.Windows.Media.PixelFormats.Bgra32, null, pixels, w * 4);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static void WritePng(string path, int w, int h)
    {
        var bmp = new System.Windows.Media.Imaging.WriteableBitmap(
            w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private sealed class Scratch : IDisposable
    {
        public string Path { get; }
        public Scratch()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          "smsmodforge-vprev-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
