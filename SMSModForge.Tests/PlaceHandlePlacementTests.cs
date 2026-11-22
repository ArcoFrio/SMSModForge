using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;
using Path = System.IO.Path;

namespace SMSModForge.Tests;

/// <summary>
/// The place preview draws an object where its move handle is (1.10.0). A
/// pack's own object stands at its X and Y in the game's world (the plugin
/// sets its position there; InGameSuiteTests.S11 holds the preview to the
/// game for the place's lights, which are such objects): the preview drew it
/// at 0.79 of that from the place's middle - the level root's scale - while
/// its handle was where the game has it; and an NPC's handle was 1/0.79 as
/// far out as the NPC. Measured as drawn: the object's picture's centre (its
/// transform), the handle's pip; then moved one unit, both move together.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceHandlePlacementTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-handles-" + Guid.NewGuid().ToString("N"));

    public PlaceHandlePlacementTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Art"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private void Png(string rel, int w, int h, byte grey)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = grey; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(Path.Combine(_dir, rel));
        enc.Save(fs);
    }

    private static T Field<T>(object o, string name)
        => (T)typeof(PlacePreview).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(o)!;

    [Fact]
    public void A_pack_objects_handle_and_an_NPCs_sit_on_what_they_move()
    {
        Png("Art/base.png", 2048, 1136, 80);
        Png("Art/dot.png", 16, 16, 250);
        Png("Art/npc.png", 40, 60, 200);
        var pack = PackRepository.CreateEmpty("handles.pack");
        pack.Npcs.Add(new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Art/npc.png" });
        var place = new PlaceDef { Key = "p", InternalName = "P", DisplayName = "P", BaseSprite = "Art/base.png",
                                   GameObjects = { new GameObjectDef { Name = "Dot", Sprite = "Art/dot.png", X = 5, Y = 2 } } };
        new PlaceViewModel(place);   // its NPCs node
        place.GameObjects.Single(g => g.IsNpcRoot).Npcs.Add(new NpcPlacementDef { Npc = "guard", Name = "Guard", Body = new NpcTransform { X = -4, Y = 1 } });
        pack.Places.Add(place);
        PackRepository.Save(pack, _dir);

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            vm.SelectedPlace = vm.Places.First();
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");

            Point Drawn(int w, int h)
            {
                var img = Field<Canvas>(preview, "_worldLayer").Children.OfType<Image>()
                    .Single(i => i.Source is BitmapSource b && b.PixelWidth == w && b.PixelHeight == h);
                return ((MatrixTransform)img.RenderTransform).Matrix.Transform(new Point(img.Width / 2, img.Height / 2));
            }
            Point Pip()
            {
                var pip = Field<Canvas>(preview, "_gizmoLayer").Children.OfType<System.Windows.Shapes.Ellipse>().Single(e => e.Width == 12);
                return new Point(Canvas.GetLeft(pip) + 6, Canvas.GetTop(pip) + 6);
            }
            void Pick(string name)
            {
                var row = Field<StackPanel>(preview, "_objectMenuList").Children.OfType<Button>()
                    .First(b => b.Content is Panel sp && sp.Children.OfType<TextBlock>().Any(t => t.Text.EndsWith(name, StringComparison.Ordinal)));
                ((IInvokeProvider)new ButtonAutomationPeer(row).GetPattern(PatternInterface.Invoke)!).Invoke();
                WindowHarness.Pump();
            }

            Pick("Dot");
            Point dot = Drawn(16, 16), dotPip = Pip();
            _out.WriteLine($"the pack's object drawn at {dot}, its handle at {dotPip}");
            Assert.True((dot - dotPip).Length < 1.0, $"the pack's object is drawn at {dot}, its handle at {dotPip}");
            var obj = vm.SelectedPlace!.GameObjects.Single(g => g.Name == "Dot");
            obj.X += 1;
            WindowHarness.Pump();
            Vector moved = Drawn(16, 16) - dot, handleMoved = Pip() - dotPip;
            _out.WriteLine($"one unit right: the object {moved.X:F1} px, its handle {handleMoved.X:F1} px");
            Assert.True(moved.X > 1 && Math.Abs(moved.X - handleMoved.X) < 1.0, "the object and its handle do not move together");

            Pick("Guard");
            Point npc = Drawn(40, 60), npcPip = Pip();
            _out.WriteLine($"the NPC drawn at {npc}, its handle at {npcPip}");
            Assert.True((npc - npcPip).Length < 1.0, $"the NPC is drawn at {npc}, its handle at {npcPip}");
        });
    }
}
