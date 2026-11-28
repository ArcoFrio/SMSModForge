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
/// One width for the place preview's hierarchy and settings, whatever place is shown (after 1.10.0, the author: "the
/// left and right menu sizes for the Places previews ... should be a fixed size for all modpack places, the hierarchy
/// one based on the longest name in any of the levels' GOs"). Two places, one with only a short name, one with a long
/// name three deep: the panels are drawn the same width on both, as wide as the long row. Control: without the other
/// places to go by, the short place's hierarchy is narrower - the width comes from them.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlacePanelWidthTests : IDisposable
{
    private const string LongName = "A rather long name for a lamp in the far corner of the room";
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-panelwidth-" + Guid.NewGuid().ToString("N"));

    public PlacePanelWidthTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Locations"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static void Png(string path, Color c)
    {
        var px = new byte[64 * 36 * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(64, 36, 96, 96, PixelFormats.Bgra32, null, px, 64 * 4)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static void Settle(Window window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
    }

    [Fact]
    public void The_hierarchy_and_settings_are_one_width_for_every_place()
    {
        Png(Path.Combine(_dir, "Locations", "Short.png"), Colors.SaddleBrown);
        Png(Path.Combine(_dir, "Locations", "Long.png"), Colors.SteelBlue);
        var pack = PackRepository.CreateEmpty("panelwidth");
        var shortPlace = new PlaceDef { Key = "short", InternalName = "Short", BaseSprite = "Locations/Short.png" };
        shortPlace.GameObjects.Add(new GameObjectDef { Name = "Cup" });
        var longPlace = new PlaceDef { Key = "long", InternalName = "Long", BaseSprite = "Locations/Long.png" };
        longPlace.GameObjects.Add(new GameObjectDef
        {
            Name = "Room",
            Children = { new GameObjectDef { Name = "Corner", Children = { new GameObjectDef { Name = LongName } } } },
        });
        pack.Places.Add(shortPlace);
        pack.Places.Add(longPlace);
        PackRepository.Save(pack, _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            try
            {
                vm.OpenPackFromPath(_dir);
                Settle(window);
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                Settle(window);
                var preview = (PlacePreview)window.FindName("PlacePreviewMain");
                (double Menu, double Settings, double Picture) Drawn(string key)
                {
                    vm.SelectedPlace = vm.Places.Single(p => p.Key == key);
                    Settle(window);
                    var (menu, settings, picture) = preview.PanelsDrawnAt;
                    return (menu.Width, settings.Width, picture.Width);
                }
                var a = Drawn("short");
                var b = Drawn("long");
                var a2 = Drawn("short");
                _out.WriteLine($"short place: hierarchy {a.Menu:0.#}, settings {a.Settings:0.#}, picture {a.Picture:0.#}; long place: {b.Menu:0.#}, {b.Settings:0.#}, {b.Picture:0.#}; "
                               + $"the widest row anywhere: {preview.WidestRowAnywhere}");
                Assert.Contains(LongName, preview.WidestRowAnywhere);
                Assert.Equal(b.Menu, a.Menu, 1);
                Assert.Equal(b.Settings, a.Settings, 1);
                Assert.Equal(b.Picture, a.Picture, 1);
                Assert.Equal(a.Menu, a2.Menu, 1);

                // Control: the short place on its own - nothing else to go by - has a narrower hierarchy.
                var all = preview.AllPlaces;
                preview.AllPlaces = null;
                Settle(window);
                var alone = Drawn("short");
                preview.AllPlaces = all;
                Settle(window);
                _out.WriteLine($"control: the short place alone - hierarchy {alone.Menu:0.#}");
                Assert.True(alone.Menu < a.Menu - 20, "control: the short place's hierarchy is as wide alone - the width does not come from the other places");
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }
}
