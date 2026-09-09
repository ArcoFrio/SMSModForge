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
/// Renders a pack's own screens to PNGs, so a person can look at them beside a
/// screenshot of the game and see whether the preview is telling the truth.
/// <para/>
/// The sibling of <see cref="UiPreviewSnapshot"/>, which does the same for the
/// game's screens. Neither asserts anything about how a picture SHOULD look -
/// nothing here could - and both are skipped unless a pack is named, so an
/// ordinary test pass never goes looking for one.
/// </summary>
public sealed class UiPackSnapshot
{
    private readonly ITestOutputHelper _out;
    public UiPackSnapshot(ITestOutputHelper o) => _out = o;

    /// <summary>Set to a modpack.json to draw its screens. Unset, this does
    /// nothing.</summary>
    private static string? Target =>
        Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOT_PACK");

    [Fact]
    public void Draw()
    {
        string? path = Target;
        if (string.IsNullOrEmpty(path)) { _out.WriteLine("SMSMODFORGE_SNAPSHOT_PACK not set."); return; }
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction"); return; }

        var pack = PackRepository.Deserialize(File.ReadAllText(path));
        if (pack == null) { _out.WriteLine("could not read the pack"); return; }

        // So pictures the pack ships are drawn, not just the game's.
        VanillaUiLibrary.Assets.PackRoot = Path.GetDirectoryName(path) ?? "";

        string outDir = Path.Combine(AppContext.BaseDirectory, "PackSnapshots");
        Directory.CreateDirectory(outDir);

        foreach (var ui in pack.Uis)
        {
            if (ui.Nodes.Count == 0) continue;

            const int w = 1920, h = 1080;   // the canvas every screen is authored against

            var report = new UiRenderReport();
            var pixels = UiAuthoredRenderer.Render(ui.Nodes, w, h,
                                                   VanillaUiLibrary.Assets, report);
            if (pixels.Length == 0) { _out.WriteLine($"{ui.Id}: nothing rendered"); continue; }

            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            string file = Path.Combine(outDir, ui.Id + ".png");
            using (var stream = File.Create(file)) png.Save(stream);

            _out.WriteLine($"{ui.Id}: {report.Drawn} graphics, " +
                           $"{report.MissingSprites.Count} missing sprites, " +
                           $"{report.MissingFonts.Count} missing fonts -> {file}");
        }
    }
}
