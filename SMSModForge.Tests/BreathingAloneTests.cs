using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// With no backdrop the bust breathes inside its own picture, a whole pixel at
/// a time, as it does over the room (1.9.1, the author: "if I put None as the
/// backdrop I get no breathing"). Until then it was a WPF transform sliding the
/// whole image. Measured on the preview's own bitmap: the first row of the
/// bust (its art starts below a clear band) goes up and down by about the
/// breathing depth. Control: with breathing off it stays put.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class BreathingAloneTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-breathing-" + Guid.NewGuid().ToString("N"));

    public BreathingAloneTests(ITestOutputHelper o) { _out = o; Directory.CreateDirectory(_dir); }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private void Pack()
    {
        // Opaque from row 40 down: a clear band above the bust.
        const int S = 256;
        var px = new byte[S * S * 4];
        for (int y = 40; y < S; y++)
            for (int x = 0; x < S; x++) { int i = (y * S + x) * 4; px[i] = 0x60; px[i + 1] = 0x80; px[i + 2] = 0xC0; px[i + 3] = 255; }
        Directory.CreateDirectory(Path.Combine(_dir, "Busts"));
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(S, S, 96, 96, PixelFormats.Bgra32, null, px, S * 4)));
        using (var fs = File.Create(Path.Combine(_dir, "Busts", "anna.png"))) enc.Save(fs);
        var pack = PackRepository.CreateEmpty("breathing.pack");
        var anna = new CharacterDef { Key = "anna", Name = "Anna", DisplayName = "Anna" };
        anna.Outfits.Add(new OutfitDef { Key = "casual", GameObjectName = "Anna_Casual", BaseSprite = "Busts/anna.png" });
        pack.Characters.Add(anna);
        PackRepository.Save(pack, _dir);
    }

    [Fact]
    public void AloneTheBustBreathesInsideItsPicture_AWholePixelAtATime() => BustFadeInTests.WithBackdrop("None", () =>
    {
        Pack();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Characters;
            WindowHarness.Pump();
            vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
            vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
            var preview = (JigglePreview)window.FindName("PreviewControl");
            preview.FadesIn = false;
            WindowHarness.Wait(TimeSpan.FromMilliseconds(500));

            List<int> Tops()
            {
                var tops = new List<int>();
                for (int k = 0; k < 25; k++)
                {
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(60));
                    var column = new byte[JiggleShader.RenderSize * 4];
                    ((WriteableBitmap)preview.Source).CopyPixels(new Int32Rect(256, 0, 1, JiggleShader.RenderSize), column, 4, 0);
                    int top = -1;
                    for (int y = 0; y < JiggleShader.RenderSize && top < 0; y++) if (column[y * 4 + 3] > 0) top = y;
                    tops.Add(top);
                }
                return tops;
            }
            var breathing = Tops();
            preview.Breathing = false;
            WindowHarness.Wait(TimeSpan.FromMilliseconds(300));
            var still = Tops();
            _out.WriteLine($"depth {preview.BreathingAmplitude}: the bust's first row while breathing {string.Join(",", breathing)}; still {string.Join(",", still)}");
            Assert.All(breathing, t => Assert.True(t >= 0, "the bust is not drawn"));
            Assert.True(breathing.Max() - breathing.Min() >= 2 * (int)preview.BreathingAmplitude - 2,
                        $"alone the bust moves {breathing.Max() - breathing.Min()} rows, not its breathing depth");
            Assert.True(still.Distinct().Count() == 1 && still[0] == 80, $"control: without breathing the bust's first row moves or is not row 80 ({string.Join(",", still.Distinct())})");
        });
    });
}
