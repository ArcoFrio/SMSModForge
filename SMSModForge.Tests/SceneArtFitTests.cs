using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Validation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Scene art of any size occupies the space a 256x256 scene would.
/// <para/>
/// It did not: scene art went through a bare <c>Sprite.Create</c> with no
/// pixels-per-unit, so it took Unity's default of 100 whatever the file's
/// dimensions were — and a 512x512 scene drew at twice the size of the one
/// beside it. Busts and levels were fixed for exactly this; scenes were simply
/// never included.
/// </summary>
public sealed class SceneArtFitTests
{
    private readonly ITestOutputHelper _out;
    public SceneArtFitTests(ITestOutputHelper o) => _out = o;

    /// <summary>What the runtime divides by: world size is pixels/ppu, and the
    /// ppu is multiplied by this, so the two cancel.</summary>
    private static double Drawn(int w, int h)
    {
        double scale = SMSModForge.Rendering.ArtFit.SceneScale(w, h);
        return w / (100.0 * scale);            // world width, at the scene ppu
    }

    [Fact]
    public void ArtOfAnySizeDrawsAtTheSizeOfA256Scene()
    {
        double reference = Drawn(256, 256);
        _out.WriteLine($"256x256 draws {reference} units wide");

        // The whole point: bigger art is not a bigger picture.
        Assert.Equal(reference, Drawn(512, 512), 6);
        Assert.Equal(reference, Drawn(1024, 1024), 6);
        Assert.Equal(reference, Drawn(128, 128), 6);

        // And 256 is untouched, which is why no existing pack moves: the
        // scale is exactly 1, so the sprite is the one it always made.
        Assert.Equal(1.0, SMSModForge.Rendering.ArtFit.SceneScale(256, 256), 6);
    }

    [Fact]
    public void UnevenArtFitsInsideTheFrameRatherThanSpillingOut()
    {
        // The larger ratio wins, so the long side reaches the frame's edge and
        // the short side falls inside it. Overflow is the worse failure - a
        // scene that spills past its frame draws over the art around it.
        Assert.Equal(2.0, SMSModForge.Rendering.ArtFit.SceneScale(512, 256), 6);
        Assert.Equal(2.0, SMSModForge.Rendering.ArtFit.SceneScale(256, 512), 6);

        // 512x256 at scale 2 is 2.56 x 1.28 units: the width matches a 256
        // square exactly, and the height is half of it. Inside, not over.
        double reference = Drawn(256, 256);
        Assert.Equal(reference, Drawn(512, 256), 6);
        Assert.True(256 / (100.0 * SMSModForge.Rendering.ArtFit.SceneScale(256, 512))
                    < reference);
    }

    [Fact]
    public void NothingIsEverStretched()
    {
        // One factor for both axes, so the aspect a scene was drawn at is the
        // aspect it plays at.
        foreach (var (w, h) in new[] { (512, 256), (300, 700), (256, 256), (1000, 999) })
        {
            double scale = SMSModForge.Rendering.ArtFit.SceneScale(w, h);
            Assert.Equal((double)w / h, (w / scale) / (h / scale), 6);
        }
    }

    [Fact]
    public void TheEditorAndTheRuntimeAgreeOnTheFrame()
    {
        // Two assemblies that share no code, holding the same number. The
        // preview is only worth having if it shows what the game will draw.
        Assert.Equal(256, SMSModForge.Rendering.ArtFit.ScenePixels);
        Assert.Equal(SMSModForge.Rendering.ArtFit.ScenePixels, ArtDimensions.ScenePixels);
    }

    [Fact]
    public void AnOddlySizedSceneIsMentionedButNotRefused()
    {
        string root = Path.Combine(Path.GetTempPath(), "smsmodforge-scenefit-" + System.Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            WritePng(Path.Combine(root, "right.png"), 256, 256);
            WritePng(Path.Combine(root, "big.png"), 512, 512);
            WritePng(Path.Combine(root, "wrongshape.png"), 512, 256);

            var pack = PackRepository.CreateEmpty("scenefit.pack");
            pack.Scenes.Add(new SceneDef { Key = "a", SceneSprite = "right.png" });
            pack.Scenes.Add(new SceneDef { Key = "b", SceneSprite = "big.png" });
            pack.Scenes.Add(new SceneDef { Key = "c", SceneSprite = "wrongshape.png" });

            var issues = PackValidator.Validate(pack, root)
                .Where(i => i.Code != null && i.Code.StartsWith("art.scene"))
                .ToList();
            foreach (var i in issues) _out.WriteLine($"{i.Severity} {i.Code} @ {i.Where}");

            // The right size says nothing at all.
            Assert.DoesNotContain(issues, i => i.Where!.Contains("[a]"));

            // A bigger square is fine and works - worth a note, not a warning.
            var big = Assert.Single(issues.Where(i => i.Where!.Contains("[b]")));
            Assert.Equal(ArtDimensions.CodeSceneSize, big.Code);
            Assert.Equal(Severity.Info, big.Severity);

            // The wrong SHAPE is the one that is nearly always a mistake.
            var shape = Assert.Single(issues.Where(i => i.Where!.Contains("[c]")));
            Assert.Equal(ArtDimensions.CodeSceneAspect, shape.Code);
            Assert.Equal(Severity.Warning, shape.Severity);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    /// <summary>A real PNG of the given size — the checker reads the header,
    /// so it has to be one.</summary>
    private static void WritePng(string path, int w, int h)
    {
        var bmp = new System.Windows.Media.Imaging.WriteableBitmap(
            w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
