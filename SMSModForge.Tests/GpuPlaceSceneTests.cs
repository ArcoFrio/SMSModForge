using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;
using Path = System.IO.Path;

namespace SMSModForge.Tests;

/// <summary>
/// A place drawn by the graphics card from its scene (GpuPlaceScene, 1.10.0)
/// is the place as the processor draws it from the preview's own captures
/// (PlacePreview.RenderStill - what the in-game suite holds to the game): the
/// level's art and its backdrop, objects turned, sized, faded and tinted, an
/// NPC with its shadow, its reflection and its spray, round lamps, a cone, a
/// free-shape and a whole-place light, the art drawn unlit - with the game's
/// look over it and without. Compared on the screen's bytes at 1920x1080.
/// Control: the same place with one object moved is visibly not it.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class GpuPlaceSceneTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-cardscene-" + Guid.NewGuid().ToString("N"));

    public GpuPlaceSceneTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Art"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>A soft picture (slow waves of colour, so a sub-pixel's difference in how it is read stays small)
    /// with a few hard blocks (so a picture out of place shows): opaque, or a disc with a soft edge on nothing.</summary>
    private void Png(string rel, int w, int h, int seed, bool disc = false)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                double fx = (double)x / w, fy = (double)y / h;
                double r = 0.5 + 0.35 * Math.Sin(fx * 6.0 + seed), g = 0.5 + 0.35 * Math.Sin(fy * 5.0 + seed * 1.7), b = 0.5 + 0.35 * Math.Sin((fx + fy) * 4.0 + seed * 0.6);
                if (((int)(fx * 8) + (int)(fy * 6)) % 5 == 0) { r *= 0.55; g *= 0.6; b *= 0.5; }
                double a = 1.0;
                if (disc)
                {
                    double dx = (fx - 0.5) * 2, dy = (fy - 0.5) * 2, d = Math.Sqrt(dx * dx + dy * dy);
                    a = Math.Clamp((0.92 - d) * 12.0, 0.0, 1.0);
                }
                // Premultiplied, as Pbgra32 keeps it.
                px[i] = (byte)Math.Round(b * a * 255); px[i + 1] = (byte)Math.Round(g * a * 255); px[i + 2] = (byte)Math.Round(r * a * 255); px[i + 3] = (byte)Math.Round(a * 255);
            }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4)));
        using var fs = File.Create(Path.Combine(_dir, rel));
        enc.Save(fs);
    }

    private void MakePack()
    {
        Png("Art/base.png", 2048, 1136, 1);
        Png("Art/back.png", 2048, 1136, 2);
        Png("Art/npc.png", 220, 420, 4, disc: true);
        for (int k = 0; k < 3; k++) Png($"Art/obj{k}.png", 280, 220, 6 + k, disc: true);
        var pack = PackRepository.CreateEmpty("cardscene.pack");
        var guard = new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Art/npc.png" };
        guard.Reflection.Enabled = true;
        guard.Reflection.Tint = "#80A0FF";
        guard.Wet.Enabled = true;
        pack.Npcs.Add(guard);
        var place = new PlaceDef { Key = "p", InternalName = "P", DisplayName = "P", BaseSprite = "Art/base.png", SecondarySprite = "Art/back.png" };
        place.GameObjects.Add(new GameObjectDef { Name = "Plain", Sprite = "Art/obj0.png", X = -6f, Y = 2.5f, SortingOrder = 1 });
        place.GameObjects.Add(new GameObjectDef { Name = "Turned", Sprite = "Art/obj1.png", X = 0.5f, Y = 2f, RotationZ = 25f, ScaleX = 1.4f, ScaleY = 0.8f, SortingOrder = 2 });
        place.GameObjects.Add(new GameObjectDef { Name = "Faded", Sprite = "Art/obj2.png", X = 6f, Y = 1.5f, StartAlpha = 0.5f, SortingOrder = 3 });
        place.GameObjects.Add(new GameObjectDef { Name = "Tinted", Sprite = "Art/obj0.png", X = -1.5f, Y = -2.5f, Tint = "#FF8040", SortingOrder = 4 });
        place.GameObjects.Add(new GameObjectDef { Name = "LampA", X = -5, Y = 1, Light = new LightDef { Shape = "round", OuterRadius = 4f, Strength = 0.9f, Glow = 0.5f, Color = "#FFD0A0" } });
        place.GameObjects.Add(new GameObjectDef
        {
            Name = "Cone", X = 5, Y = 3, RotationZ = 160f,
            Light = new LightDef { Shape = "round", OuterRadius = 5f, InnerRadius = 1f, OuterAngle = 70f, InnerAngle = 30f, Strength = 1.2f, Glow = 0.3f, Color = "#A0C0FF" },
        });
        place.GameObjects.Add(new GameObjectDef
        {
            Name = "Window", X = 1, Y = -1,
            Light = new LightDef { Shape = "free", Points = { new[] { -2f, -1f }, new[] { 2f, -1.2f }, new[] { 1.5f, 1.3f }, new[] { -1.8f, 1f } }, Edge = 0.8f, Strength = 0.7f, Glow = 0.4f, Color = "#FFFFC0" },
        });
        place.GameObjects.Add(new GameObjectDef { Name = "Dusk", Light = new LightDef { Shape = "ambient", Strength = 0.6f, Color = "#B0B8E0" } });
        new PlaceViewModel(place);   // its NPCs node, its template light
        place.GameObjects.Single(g => g.IsNpcRoot).Npcs.Add(new NpcPlacementDef { Npc = "guard", Name = "Guard", Body = new NpcTransform { X = 3, Y = -1.5f } });
        pack.Places.Add(place);
        PackRepository.Save(pack, _dir);
    }

    private static (double within1, double within3, int max) Compare(byte[] a, byte[] b)
    {
        int one = 0, three = 0, max = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if ((i & 3) == 3) continue;
            int d = Math.Abs(a[i] - b[i]);
            if (d <= 1) one++;
            if (d <= 3) three++;
            if (d > max) max = d;
        }
        double n = a.Length / 4 * 3;
        return (one / n, three / n, max);
    }

    private static void Save(string? dir, string name, byte[] bgra, int w, int h)
    {
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4)));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    [Fact]
    public void ThePlaceTheCardDrawsIsTheProcessors()
    {
        MakePack();
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        string? shots = Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR");
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            vm.SelectedPlace = vm.Places.First();
            WindowHarness.Pump();
            var place = (PlacePreview)window.FindName("PlacePreviewMain");
            Assert.True(WindowHarness.Until(() => PreviewBackdropManager.GameLook != null, TimeSpan.FromSeconds(20)), "the game's look did not load");
            // The spray's drops move: stopped where they are, so both pictures are of the same moment.
            typeof(PlacePreview).GetMethod("StopWetTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(place, null);
            WindowHarness.Pump();
            const int W = 1920, H = 1080;

            void Hold(string what, bool look, double least1, double least3)
            {
                var processor = place.RenderStill(look, lights: true);
                var card = place.RenderStillOnCard(look, lights: true);
                Assert.True(processor != null, what + ": the processor drew nothing");
                Assert.True(card != null, what + ": the card drew nothing (" + SMSModForge.Rendering.Gpu.GpuDevice.SharedFailure + ")");
                var (within1, within3, max) = Compare(processor!, card!);
                _out.WriteLine($"{what}: {within1:P2} of the bytes within a level, {within3:P2} within three, the furthest {max}");
                Save(shots, what.Replace(' ', '-') + "-processor.png", processor!, W, H);
                Save(shots, what.Replace(' ', '-') + "-card.png", card!, W, H);
                Assert.True(within1 >= least1, $"{what}: only {within1:P2} of the card's bytes are within a level of the processor's");
                Assert.True(within3 >= least3, $"{what}: only {within3:P2} of the card's bytes are within three levels of the processor's");
            }

            Hold("lit, no look", look: false, 0.97, 0.995);
            Hold("lit, the look", look: true, 0.97, 0.995);

            // Something drawn with the unlit material: the lit mask is read.
            place.LevelArtUnlit = true;
            WindowHarness.Pump();
            typeof(PlacePreview).GetMethod("StopWetTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(place, null);
            Hold("the art unlit, the look", look: true, 0.97, 0.995);
            place.LevelArtUnlit = false;
            WindowHarness.Pump();
            typeof(PlacePreview).GetMethod("StopWetTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(place, null);

            // Control: one object a unit to the side is not the same place.
            var before = place.RenderStill(true, true)!;
            vm.SelectedPlace!.GameObjects.Single(g => g.Name == "Turned").X += 1;
            WindowHarness.Pump();
            typeof(PlacePreview).GetMethod("StopWetTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(place, null);
            var moved = place.RenderStillOnCard(true, true)!;
            var (c1, c3, cmax) = Compare(before, moved);
            _out.WriteLine($"control, an object moved: {c1:P2} within a level, {c3:P2} within three, the furthest {cmax}");
            Assert.True(c3 < 0.99, $"control: the place with an object moved passes for the place ({c3:P2} within three levels)");
        });
    }

    private static string LevelArt => Path.Combine(TranslationCoverageTests.EditorDir, "Resources", "VanillaLevelArt");

#if DEBUG   // builds each level the way the place pictures export does, and that is compiled only in Debug (see PlaceExport)
    /// <summary>
    /// Every one of the game's levels, as the place preview shows it on arrival - its art and its video's still,
    /// its objects and figures, their tints, what it draws with the unlit material, its lamps - is on the card what
    /// it is on the processor: each can be put as a list for the card (none falls back to the captures), and the
    /// two pictures of it are the same. Without the game's look, so what is compared is the scene and its lights.
    /// Control: a level with lamps drawn by the card without them is not the processor's with them.
    /// </summary>
    [Fact]
    public void EveryOneOfTheGamesLevelsIsOnTheCardWhatItIsOnTheProcessor()
    {
        Assert.True(VanillaLevelCatalog.IsAvailable, "the game's levels are not there to draw");
        var lines = new System.Collections.Generic.List<string>();
        var failures = new System.Collections.Generic.List<string>();
        int levels = 0, lit = 0;
        double controlWithin = 1, least1 = 1, least3 = 1;
        string leastName1 = "", leastName3 = "";
        WindowHarness.Run(_ =>
        {
            Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
            Assert.True(WindowHarness.Until(() => PreviewBackdropManager.GameLook != null, TimeSpan.FromSeconds(20)), "the game's look did not load");
            foreach (var level in VanillaLevelCatalog.All.OrderBy(l => l.GoName, StringComparer.Ordinal))
            {
                var preview = PlaceExport.VanillaPreview(PlaceExport.VanillaLevel("vanilla:" + level.GoName, null, LevelArt), null, null, LevelArt);
                preview.Width = PlaceExport.Width; preview.Height = PlaceExport.Height;
                preview.Measure(new Size(PlaceExport.Width, PlaceExport.Height));
                preview.Arrange(new Rect(0, 0, PlaceExport.Width, PlaceExport.Height));
                preview.UpdateLayout();
                WindowHarness.Pump();
                var processor = preview.RenderStill(look: false, lights: true);
                if (processor == null) continue;          // nothing of it to draw (a level with no art of its own shown)
                levels++;
                int lamps = preview.PlaceLightsNow?.Lights.Count ?? 0;
                if (lamps > 0) lit++;
                var card = preview.RenderStillOnCard(look: false, lights: true);
                if (card == null) { failures.Add(level.GoName + ": the card cannot draw it"); continue; }
                var (within1, within3, max) = Compare(processor, card);
                lines.Add($"{level.GoName} ({lamps} lamps): {within1:P2} within a level, {within3:P2} within three, the furthest {max}");
                if (within3 < 0.99 || within1 < 0.95) failures.Add($"{level.GoName}: {within1:P2} within a level, {within3:P2} within three");
                if (within1 < least1) { least1 = within1; leastName1 = level.GoName; }
                if (within3 < least3) { least3 = within3; leastName3 = level.GoName; }
                if (lamps > 0 && controlWithin == 1)
                {
                    var unlit = preview.RenderStillOnCard(look: false, lights: false)!;
                    controlWithin = Compare(processor, unlit).within3;
                    lines.Add($"control, {level.GoName} drawn by the card without its lamps: {controlWithin:P2} within three levels of the processor's with them");
                }
            }
        });
        foreach (var line in lines) _out.WriteLine(line);
        _out.WriteLine($"{levels} levels drawn, {lit} with lamps; {failures.Count} not the processor's; the least alike {least1:P2} within a level ({leastName1}), {least3:P2} within three ({leastName3})");
        Assert.True(levels >= 100, $"only {levels} of the game's levels were drawn");
        Assert.True(lit >= 20, $"only {lit} levels with lamps were drawn");
        Assert.True(failures.Count == 0, "on the card these are not what they are on the processor: " + string.Join("; ", failures));
        Assert.True(controlWithin < 0.9, $"control: a level without its lamps passes for the level with them ({controlWithin:P2})");
    }
#endif
}
