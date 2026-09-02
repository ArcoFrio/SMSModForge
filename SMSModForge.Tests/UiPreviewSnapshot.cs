using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Renders real vanilla screens to PNGs beside the test output, so a person can
/// look at them.
/// <para/>
/// Not an assertion about how they should look - nothing here can check that,
/// and pretending otherwise would be the exact self-deception this preview is
/// built to avoid. It is a way to put the thing in front of eyes that can
/// judge it, which is the only check that counts for a picture.
/// </summary>
public class UiPreviewSnapshot
{
    private readonly ITestOutputHelper _out;
    public UiPreviewSnapshot(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData("9_MainCanvas.json", "Quitagme")]
    [InlineData("9_MainCanvas.json", "Payout")]
    [InlineData("9_MainCanvas.json", "Navigator")]
    [InlineData("9_MainCanvas.json", "Tooltip_Finances")]
    [InlineData("9_QuestJournal.json", null)]
    public void Draw(string surfaceFile, string? baseName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? root = null;
        while (dir != null && root == null)
        {
            string c = Path.Combine(dir.FullName, "SMSModForge", "Resources", "VanillaOverlays");
            if (File.Exists(Path.Combine(c, "index.json"))) root = c;
            dir = dir.Parent;
        }
        if (root == null) { _out.WriteLine("no extraction - skipping"); return; }

        var surface = VanillaUiSurface.Load(Path.Combine(root, "Surfaces", surfaceFile));
        if (surface == null || !surface.IsDrawable) { _out.WriteLine("not drawable"); return; }
        var assets = new VanillaUiAssets(root);
        if (!assets.IsAvailable) { _out.WriteLine("no sprites"); return; }

        var report = new UiRenderReport();
        byte[] pixels;
        if (baseName == null)
        {
            pixels = UiSceneRenderer.Render(surface, assets, report);
        }
        else
        {
            var node = surface.Base(baseName);
            if (node == null) { _out.WriteLine("no base " + baseName); return; }
            pixels = UiSceneRenderer.RenderBase(surface, node, assets, report);
        }
        if (pixels.Length == 0) { _out.WriteLine("nothing rendered"); return; }

        int w = (int)Math.Round(surface.Width), h = (int)Math.Round(surface.Height);
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));

        string outDir = Path.Combine(AppContext.BaseDirectory, "PreviewSnapshots");
        Directory.CreateDirectory(outDir);
        string name = (baseName ?? Path.GetFileNameWithoutExtension(surfaceFile)) + ".png";
        using (var file = File.Create(Path.Combine(outDir, name))) png.Save(file);

        int lit = 0;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] > 0) lit++;
        _out.WriteLine($"{name}: {report.Drawn} graphics, {lit} lit px " +
                       $"({100.0 * lit / (w * h):0.#}%), " +
                       $"{report.MissingSprites.Count} missing sprites, " +
                       $"{report.MissingFonts.Count} missing fonts, " +
                       $"{report.LegacyText.Count} legacy text, " +
                       $"{report.Untrustworthy.Count} untrustworthy rects");
        _out.WriteLine("written to " + Path.Combine(outDir, name));
    }
}
