using System;
using System.IO;
using System.Linq;
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
/// The NPCs tab's preview over a backdrop (1.9.1, the author: "the NPC preview
/// should get the same treatment as the Character one"): with a backdrop it
/// draws the game's frame around the NPC, the place behind it; with None the
/// NPC alone - the same size, in the same place of the same frame (the author:
/// "the no background and the actual backgrounds had different scales, please
/// unify those"). Measured on what the preview draws: its corners, far from the
/// NPC, are the room's colours over a backdrop and the preview's own grey with
/// none, and the NPC (magenta, still) covers the same box in both.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NpcPreviewBackdropTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-npcbackdrop-" + Guid.NewGuid().ToString("N"));

    public NpcPreviewBackdropTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    /// <summary>The preview as drawn: its pixels (BGRA), at its own size.</summary>
    private static (int w, int h, byte[] px) Drawn(NpcPreview preview)
    {
        int w = (int)Math.Round(preview.ActualWidth), h = (int)Math.Round(preview.ActualHeight);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(preview);
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        return (w, h, px);
    }

    [Fact]
    public void OverABackdropTheNpcStandsInThePlaceAndWithNoneItIsAlone()
    {
        // Magenta, which neither the room nor the grey is; a mask that moves nothing.
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Npcs", "guard.png"), 200, 400, Color.FromRgb(0xFF, 0x00, 0xFF));
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Npcs", "mask.png"), 200, 400, Color.FromRgb(0, 0, 0));
        var pack = PackRepository.CreateEmpty("npcbackdrop.pack");
        pack.Npcs.Add(new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Npcs/guard.png", Mask = "Npcs/mask.png" });
        PackRepository.Save(pack, _dir);

        var was = PreviewBackdropManager.Current;
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(_dir);
                var tabs = (TabControl)window.FindName("MainTabs");
                tabs.SelectedIndex = MainTab.Npcs;
                WindowHarness.Pump();
                vm.SelectedNpc = vm.Npcs.First();
                WindowHarness.Pump();
                window.UpdateLayout();
                var npc = Descendants<NpcPreview>(window).Single(n => n.IsVisible);

                // Over the PC's room, once its backdrop has loaded.
                var room = PreviewBackdropManager.All.First(d => d.Name == "MyRoom");
                PreviewBackdropManager.Apply(room);
                room.SceneWaited();
                // Its first frame over the room has its preparing to do, slower
                // beside other testers: drawing is counted from there.
                int applied = npc.FramesShown;
                var first = System.Diagnostics.Stopwatch.StartNew();
                // A minute: drawn on the card (SMSMODFORGE_TEST_RENDERER, 1.10.0) its first frame compiles the
                // game's own program for this run - ten seconds and more beside other tests.
                while (npc.FramesShown == applied && first.Elapsed < TimeSpan.FromSeconds(60)) WindowHarness.Wait(TimeSpan.FromMilliseconds(50));
                int before = npc.FramesShown;
                WindowHarness.Wait(TimeSpan.FromSeconds(1.5));
                Assert.True(npc.OverBackdrop, "the NPC preview does not draw over the backdrop");
                Assert.True(npc.FramesShown > before + 5, $"the NPC preview drew {npc.FramesShown - before} frames over the backdrop in 1.5 s ({npc.LastFault})");
                var (w, h, px) = Drawn(npc);
                int Grey(int x, int y)
                {
                    int i = (y * w + x) * 4;
                    return Math.Abs(px[i] - 0x2A) + Math.Abs(px[i + 1] - 0x2A) + Math.Abs(px[i + 2] - 0x2A);
                }
                int[] corner = { Grey(4, 4), Grey(w - 5, 4), Grey(4, h - 5), Grey(w - 5, h - 5) };
                _out.WriteLine($"over the PC's room: {w}x{h}, the corners {string.Join(", ", corner)} from the preview's grey");
                Assert.True(w == SMSModForge.Rendering.BackdropRender.FrameSize && h == SMSModForge.Rendering.BackdropRender.FrameSize,
                            $"the frame is {w}x{h}, not the backdrop's {SMSModForge.Rendering.BackdropRender.FrameSize}");
                Assert.True(corner.Count(c => c > 12) >= 3, "the corners over the PC's room are the preview's own grey - no room is drawn");
                // Where the NPC is drawn: its magenta (red and blue strong, little
                // green), in the frame's middle half - the room has purples of its own.
                (int x0, int y0, int x1, int y1) Box(byte[] p, int pw, int ph)
                {
                    int bx0 = pw, by0 = ph, bx1 = -1, by1 = -1;
                    for (int y = ph / 4; y < ph * 3 / 4; y++)
                        for (int x = pw / 4; x < pw * 3 / 4; x++)
                        {
                            int i = (y * pw + x) * 4;
                            int b = p[i], g = p[i + 1], r = p[i + 2];
                            if (r > 200 && b > 150 && g < 0.3 * Math.Min(r, b))
                            { bx0 = Math.Min(bx0, x); by0 = Math.Min(by0, y); bx1 = Math.Max(bx1, x); by1 = Math.Max(by1, y); }
                        }
                    return (bx0, by0, bx1, by1);
                }
                var overRoom = Box(px, w, h);

                // None: the pose alone, the preview's grey around it.
                PreviewBackdropManager.Apply(PreviewBackdropManager.All.First(d => d.IsNone));
                applied = npc.FramesShown;
                first.Restart();
                while (npc.FramesShown == applied && first.Elapsed < TimeSpan.FromSeconds(10)) WindowHarness.Wait(TimeSpan.FromMilliseconds(50));
                before = npc.FramesShown;
                WindowHarness.Wait(TimeSpan.FromSeconds(1));
                Assert.False(npc.OverBackdrop, "with None the NPC preview still draws over a backdrop");
                Assert.True(npc.FramesShown > before + 5, $"with None the NPC preview drew {npc.FramesShown - before} frames in 1 s");
                (w, h, px) = Drawn(npc);
                corner = new[] { Grey(4, 4), Grey(w - 5, 4), Grey(4, h - 5), Grey(w - 5, h - 5) };
                _out.WriteLine($"with none: {w}x{h}, the corners {string.Join(", ", corner)} from the preview's grey");
                Assert.True(corner.All(c => c <= 6), "with None the corners are not the preview's grey");
                var alone = Box(px, w, h);
                double expected = 200 * 0.79 / 2;   // its art's 200 pixels across at the place's scale (0.79)
                _out.WriteLine($"the NPC drawn over the PC's room {overRoom}, alone {alone}; {expected:0.0} pixels across in the game");
                Assert.True(w == SMSModForge.Rendering.BackdropRender.FrameSize && h == SMSModForge.Rendering.BackdropRender.FrameSize,
                            $"with None the frame is {w}x{h}, not the backdrop's");
                Assert.True(overRoom.x1 > overRoom.x0 && alone.x1 > alone.x0, "no NPC found in one of the frames - the check proves nothing");
                Assert.True(Math.Abs(overRoom.x0 - alone.x0) <= 2 && Math.Abs(overRoom.x1 - alone.x1) <= 2
                            && Math.Abs(overRoom.y0 - alone.y0) <= 2 && Math.Abs(overRoom.y1 - alone.y1) <= 2,
                            $"the NPC is drawn at {alone} alone and at {overRoom} over the PC's room - not the same size and place");
                Assert.True(Math.Abs(alone.x1 - alone.x0 + 1 - expected) <= 2, $"alone the NPC is {alone.x1 - alone.x0 + 1} pixels across, the game's {expected:0.0}");
            });
        }
        finally { PreviewBackdropManager.Apply(was); }
    }
}
