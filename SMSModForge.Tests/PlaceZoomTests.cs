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

namespace SMSModForge.Tests;

/// <summary>
/// The place preview zoomed in keeps the art's own detail (1.9.1, the author:
/// "the zoom on the Places preview is not wired correctly, and even some art
/// from the pack itself looks low res when zoomed in"). Until then the game's
/// look was one 960x540 picture of the screen, enlarged with the view - the
/// art's fine lines gone under it - ending at the screen's edge, past which the
/// art showed without it; and a placed NPC's moving picture was at most 384
/// pixels, enlarged. Measured on what the view draws: the level art's one-pixel
/// grid lines stand out from the pixels beside them, inside the screen and past
/// its edge alike (control: the same picture blurred does not pass), and the
/// NPC's picture has the pixels it covers on screen.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceZoomTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-placezoom-" + Guid.NewGuid().ToString("N"));
    public PlaceZoomTests(ITestOutputHelper o) { _out = o; Directory.CreateDirectory(_dir); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static void Write(string path, int w, int h, Func<int, int, Color> at)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = at(x, y);
                int i = (y * w + x) * 4;
                px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = c.A;
            }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static (int w, int h, byte[] px) Drawn(FrameworkElement e)
    {
        int w = (int)Math.Floor(e.ActualWidth), h = (int)Math.Floor(e.ActualHeight);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        // Rendered where it is: an element's own offset in its parent would shift it.
        var shot = new DrawingVisual();
        using (var dc = shot.RenderOpen()) dc.DrawRectangle(new VisualBrush(e) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        rtb.Render(shot);
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        return (w, h, px);
    }

    /// <summary>Pixels brighter than both pixels three to each side by 40 levels: a thin bright line, across.</summary>
    private static int Lines(byte[] px, int w, int h, int x0, int x1)
    {
        int n = 0;
        for (int y = h / 4; y < h * 3 / 4; y++)
            for (int x = Math.Max(3, x0); x < Math.Min(w - 3, x1); x++)
            {
                int L(int xx) { int i = (y * w + xx) * 4; return px[i] + px[i + 1] + px[i + 2]; }
                int c = L(x);
                if (c > L(x - 3) + 120 && c > L(x + 3) + 120) n++;
            }
        return n;
    }

    private static byte[] Blurred(byte[] px, int w, int h)
    {
        var o = (byte[])px.Clone();
        for (int y = 1; y < h - 1; y++)
            for (int x = 2; x < w - 2; x++)
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0;
                    for (int k = -2; k <= 2; k++) sum += px[(y * w + x + k) * 4 + c];
                    o[(y * w + x) * 4 + c] = (byte)(sum / 5);
                }
        return o;
    }

    [Fact]
    public void ZoomedInTheArtKeepsItsDetailUnderTheGamesLook_InsideTheScreenAndPastItsEdge()
    {
        // Level art: one-pixel white lines every 16 over coloured blocks; an NPC of fine stripes.
        Write(Path.Combine(_dir, "Places", "front.png"), 2048, 1136, (x, y) =>
            x % 16 == 0 || y % 16 == 0 ? Color.FromRgb(250, 250, 250) : Color.FromRgb((byte)(x / 128 * 12 + 20), (byte)(y / 128 * 20 + 20), 70));
        Write(Path.Combine(_dir, "Places", "back.png"), 2048, 1136, (_, _) => Color.FromRgb(40, 40, 60));
        Write(Path.Combine(_dir, "Npcs", "guard.png"), 600, 1200, (x, y) => (x / 2 + y / 2) % 2 == 0 ? Color.FromRgb(220, 40, 40) : Color.FromRgb(40, 40, 220));
        Write(Path.Combine(_dir, "Npcs", "mask.png"), 600, 1200, (_, _) => Color.FromArgb(255, 0, 0, 255));
        var pack = PackRepository.CreateEmpty("zoom.pack");
        pack.Npcs.Add(new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Npcs/guard.png", Mask = "Npcs/mask.png" });
        pack.Places.Add(new PlaceDef
        {
            Key = "cave", InternalName = "Cave", DisplayName = "Cave", BaseSprite = "Places/front.png", SecondarySprite = "Places/back.png",
            GameObjects = { new GameObjectDef { Name = "NPCs", Role = GameObjectDef.RoleNpcRoot,
                Npcs = { new NpcPlacementDef { Npc = "guard", Name = "Guard", StartActive = true, SortingOrder = 5 } } } },
        });
        PackRepository.Save(pack, _dir);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        var was = PreviewQualityManager.Current;
        try
        {
            PreviewQualityManager.Apply(PreviewQualityManager.All.First(q => q.Name == "Ultra"));
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(_dir);
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                WindowHarness.Pump();
                vm.SelectedPlace = vm.Places.First();
                WindowHarness.Pump();
                var place = (PlacePreview)window.FindName("PlacePreviewMain");
                if (window.FindName("PlaceShaders") is CheckBox shaders) shaders.IsChecked = true;
                WindowHarness.Wait(TimeSpan.FromSeconds(1));
                var t = typeof(PlacePreview);
                var viewport = (Border)t.GetField("_viewport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(place)!;
                var targets = (System.Collections.IList)t.GetField("_shaderTargets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(place)!;
                int NpcPictureWidth()
                {
                    foreach (var target in targets)
                        if (target.GetType().GetField("Params")!.GetValue(target) != null) return (int)target.GetType().GetField("OutW")!.GetValue(target)!;
                    return 0;
                }
                int fittedNpc = NpcPictureWidth();

                // Zoom 4, the screen's right edge in the middle of the view.
                double zoom = 4.0;
                t.GetField("_zoom", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(place, zoom);
                t.GetMethod("ApplyViewScale", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(place, null);
                double s = (double)t.GetField("_fitScale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(place)! * zoom;
                var pan = (TranslateTransform)t.GetField("_viewPan", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(place)!;
                pan.X = viewport.ActualWidth / 2 - 1920 * s;
                pan.Y = viewport.ActualHeight / 2 - 540 * s;
                int before = place.LookPasses;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                // Up to half a minute: beside other testers on a held machine the picture took more than ten seconds (1.10.0).
                while ((place.LookPasses == before || NpcPictureWidth() == fittedNpc) && clock.Elapsed < TimeSpan.FromSeconds(30)) WindowHarness.Wait(TimeSpan.FromMilliseconds(100));
                WindowHarness.Wait(TimeSpan.FromMilliseconds(500));
                Assert.True(place.LookShown, "the game's look is not drawn over the place");
                var (w, h, px) = Drawn(viewport);
                int inside = Lines(px, w, h, 0, w / 2 - 6), past = Lines(px, w, h, w / 2 + 6, w);
                var blur = Blurred(px, w, h);
                int insideBlurred = Lines(blur, w, h, 0, w / 2 - 6), pastBlurred = Lines(blur, w, h, w / 2 + 6, w);
                int zoomedNpc = NpcPictureWidth();
                _out.WriteLine($"zoom 4, the view {w}x{h}: thin bright lines inside the screen {inside}, past its edge {past}; blurred {insideBlurred} and {pastBlurred}; "
                               + $"the NPC's picture {fittedNpc} pixels across fitted, {zoomedNpc} zoomed in");
                Assert.True(insideBlurred < inside / 3 && pastBlurred < past / 3, "control: the picture blurred keeps its thin lines - the check cannot tell");
                Assert.True(inside > 400, $"inside the screen the art's thin lines are gone under the game's look ({inside})");
                Assert.True(past > 400, $"past the screen's edge the art's thin lines are gone ({past})");
                Assert.True(Math.Abs(inside - past) < Math.Max(inside, past) / 2, $"the art differs across the screen's edge: {inside} lines inside, {past} past it");
                Assert.True(zoomedNpc > fittedNpc * 2 && zoomedNpc > 384, $"the NPC's picture is {zoomedNpc} pixels across zoomed in ({fittedNpc} fitted)");
            });
        }
        finally { PreviewQualityManager.Apply(was); }
    }
}
