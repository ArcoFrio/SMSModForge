using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Bringing a reduced UI sprite back to the size the extraction measured.
/// <para/>
/// The shipped PNGs are smaller than the sprites they represent — a screen full
/// of 2048-pixel wallpapers was half the editor download. Everything that reads
/// them, though, is expressed in the ORIGINAL size: a sliced image's border, and
/// the pixels-per-unit the renderer divides by, both recorded in surface files
/// the sprite loader never sees.
/// <para/>
/// So the restore is not a nicety. Skip it and every nine-sliced panel draws its
/// corners at the wrong scale — which looks like a rendering bug in the preview
/// and is impossible to trace back to a file being 25% smaller on disk.
/// </summary>
public sealed class UiSpriteRestoreTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public UiSpriteRestoreTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "uisprite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "Sprites"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>An extraction of one sprite, whose PNG is written at
    /// <paramref name="onDisk"/> while the index claims <paramref name="claimed"/>.</summary>
    private void Extraction(int claimed, int onDisk)
    {
        var pixels = new byte[onDisk * onDisk * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x40; pixels[i + 1] = 0x80; pixels[i + 2] = 0xC0; pixels[i + 3] = 0xFF;
        }
        var bmp = BitmapSource.Create(onDisk, onDisk, 96, 96,
                                      System.Windows.Media.PixelFormats.Bgra32, null,
                                      pixels, onDisk * 4);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using (var file = File.Create(Path.Combine(_dir, "Sprites", "panel.png")))
            png.Save(file);

        File.WriteAllText(Path.Combine(_dir, "Sprites", "index.json"),
            JsonConvert.SerializeObject(new[]
            {
                new
                {
                    key = "panel_1", sprite = "panel", texture = "atlas", file = "panel.png",
                    textureRect = new[] { 0.0, 0.0, claimed, claimed },
                    border = new[] { 8, 8, 8, 8 },
                },
            }));
    }

    [Fact]
    public void AReducedSpriteComesBackAtTheSizeTheIndexRecorded()
    {
        Extraction(claimed: 256, onDisk: 192);          // the x0.75 tier

        var assets = new VanillaUiAssets(_dir);
        var sprite = assets.Sprite("panel_1");

        Assert.NotNull(sprite);
        _out.WriteLine($"file 192x192, index says 256 -> loaded {sprite!.Width}x{sprite.Height}");

        Assert.Equal(256, sprite.Width);
        Assert.Equal(256, sprite.Height);

        // And it is a whole picture, not a buffer sized for one.
        Assert.Equal(256 * 256 * 4, sprite.Pixels.Length);
        Assert.Contains(sprite.Pixels, b => b != 0);
    }

    [Fact]
    public void AHalvedSpriteComesBackToo()
    {
        Extraction(claimed: 2048, onDisk: 1024);        // the x0.5 tier
        var sprite = new VanillaUiAssets(_dir).Sprite("panel_1");

        Assert.NotNull(sprite);
        Assert.Equal(2048, sprite!.Width);
        Assert.Equal(2048, sprite.Height);
    }

    [Fact]
    public void AnUnreducedSpriteIsNotTouched()
    {
        // The control, and it matters twice over: it proves the restore is
        // conditional rather than unconditional, and it is what every sprite
        // below the size floor goes through. Resampling one to the size it
        // already is would soften every icon in the editor for nothing.
        Extraction(claimed: 256, onDisk: 256);

        var assets = new VanillaUiAssets(_dir);
        var sprite = assets.Sprite("panel_1");

        Assert.NotNull(sprite);
        Assert.Equal(256, sprite!.Width);

        // Untouched means exactly the colour that was written, not a bilinear
        // average of it.
        Assert.Equal(0x40, sprite.Pixels[0]);
        Assert.Equal(0x80, sprite.Pixels[1]);
        Assert.Equal(0xC0, sprite.Pixels[2]);
    }

    [Fact]
    public void TheShippedExtractionRestoresEverySpriteItShips()
    {
        // Against the real extraction beside the tests, which is the one that
        // matters: every sprite the editor ships must come back at the size its
        // own index claims, or something downstream is working from a lie.
        string root = Path.Combine(AppContext.BaseDirectory, "VanillaUi");
        if (!File.Exists(Path.Combine(root, "Sprites", "index.json")))
        {
            _out.WriteLine("no extraction beside the tests; skipping");
            return;
        }

        var entries = JsonConvert.DeserializeObject<dynamic[]>(
            File.ReadAllText(Path.Combine(root, "Sprites", "index.json")))!;
        var assets = new VanillaUiAssets(root);

        int checked_ = 0, reduced = 0;
        foreach (var e in entries.Take(400))
        {
            string key = (string)e.key;
            int want = (int)Math.Round((double)e.textureRect[2]);
            int high = (int)Math.Round((double)e.textureRect[3]);
            if (want <= 0 || high <= 0) continue;

            var sprite = assets.Sprite(key);
            if (sprite == null) continue;

            Assert.Equal(want, sprite.Width);
            Assert.Equal(high, sprite.Height);
            checked_++;

            string file = Path.Combine(root, "Sprites", (string)e.file);
            if (File.Exists(file))
            {
                var frame = BitmapFrame.Create(new Uri(file), BitmapCreateOptions.None,
                                               BitmapCacheOption.OnLoad);
                if (frame.PixelWidth != want) reduced++;
            }
        }

        _out.WriteLine($"{checked_} sprites all restored to their recorded size; "
                       + $"{reduced} of them were smaller on disk");
        Assert.True(checked_ > 100, "the extraction produced almost nothing to check");
    }
}
