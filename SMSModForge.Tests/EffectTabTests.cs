using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;
using FX = SMSModForge.Shared.Effects;

namespace SMSModForge.Tests;

/// <summary>
/// The FX tab (1.10.0): each template shows the rows it takes and no others, a change is a step Ctrl+Z puts
/// back, and the preview draws the effect - measured on the picture it draws, with a control that draws none.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class EffectTabTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-fx-" + Guid.NewGuid().ToString("N"));

    public EffectTabTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static IEnumerable<T> Under<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in Under<T>(c)) yield return d;
        }
    }

    private static string? PathOf(FrameworkElement e, DependencyProperty p)
        => BindingOperations.GetBinding(e, p)?.Path?.Path;

    private static DependencyObject Page(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        return (DependencyObject)tabs.Template.FindName("PART_SelectedContentHost", tabs);
    }

    /// <summary>The rows on screen now, by what they are bound to.</summary>
    private static HashSet<string> Shown(MainWindow window)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in Under<FrameworkElement>(Page(window)))
        {
            if (!e.IsVisible) continue;
            var path = e switch
            {
                Slider s => PathOf(s, RangeBase.ValueProperty),
                TextBox t => PathOf(t, TextBox.TextProperty),
                CheckBox c => PathOf(c, ToggleButton.IsCheckedProperty),
                ComboBox b => PathOf(b, Selector.SelectedItemProperty) ?? PathOf(b, ComboBox.TextProperty),
                _ => null,
            };
            if (path != null) shown.Add(path);
        }
        return shown;
    }

    [Fact]
    public void EachTemplateShowsItsOwnRows_AndAChangeIsAStepCtrlZPutsBack()
    {
        PackRepository.Save(UndoCrawlPack.Make(_dir), _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Fx;
            WindowHarness.Pump();
            try
            {
                Assert.True(vm.Effects.Count >= 5, "the walk's pack has its effects");

                // The points: how far they spread, how long they drift and fly - no words.
                vm.SelectedEffect = vm.Effects.First(e => e.Key == "photoHearts");
                WindowHarness.Pump();
                var points = Shown(window);
                _out.WriteLine("points: " + string.Join(", ", points.OrderBy(x => x)));
                Assert.Contains("Spread", points);
                Assert.Contains("FloatSeconds", points);
                Assert.Contains("Under", points);
                Assert.DoesNotContain("Text", points);

                // Floating words: the words, no spread, no amount.
                vm.SelectedEffect = vm.Effects.First(e => e.Key == "words");
                WindowHarness.Pump();
                var words = Shown(window);
                Assert.Contains("Text", words);
                Assert.DoesNotContain("Spread", words);
                Assert.DoesNotContain("Amount", words);

                // The game's own changed: no "where", no "when" - the game decides both.
                vm.SelectedEffect = vm.Effects.First(e => e.Key == "moreFollowers");
                WindowHarness.Pump();
                var changed = Shown(window);
                Assert.Contains("ChangesGame", changed);
                Assert.DoesNotContain("X", changed);
                Assert.DoesNotContain("PlayWhen", changed);

                // A change is a step; Ctrl+Z puts it back.
                vm.SelectedEffect = vm.Effects.First(e => e.Key == "fire");
                WindowHarness.Pump();
                vm.Undo.Checkpoint();
                string before = PackRepository.Serialize(vm.Pack);
                var size = Under<Slider>(Page(window)).First(s => s.IsVisible && PathOf(s, RangeBase.ValueProperty) == "Size");
                size.Value = 2.5;
                WindowHarness.Pump();
                vm.Undo.Checkpoint();
                Assert.Equal(2.5f, vm.Effects.First(e => e.Key == "fire").Model.Size);
                Assert.NotEqual(before, PackRepository.Serialize(vm.Pack));
                RealKeys.Press(window, Key.Z, ModifierKeys.Control);
                WindowHarness.Pump();
                Assert.Equal(before, PackRepository.Serialize(vm.Pack));
                Assert.Equal(MainTab.Fx, ((TabControl)window.FindName("MainTabs")).SelectedIndex);
                Assert.Equal("fire", vm.SelectedEffect?.Key);
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }

    /// <summary>How many pixels of the preview differ from the room it draws over.</summary>
    private static int Drawn(EffectDef? def)
    {
        var bare = Render(null);
        var with = Render(def);
        int n = 0;
        for (int i = 0; i < bare.Length; i += 4)
            if (Math.Abs(bare[i] - with[i]) + Math.Abs(bare[i + 1] - with[i + 1]) + Math.Abs(bare[i + 2] - with[i + 2]) > 24) n++;
        return n;
    }

    private static byte[] Render(EffectDef? def)
    {
        var preview = new EffectPreview { Width = 320, Height = 180, Effect = def };
        preview.Measure(new Size(320, 180));
        preview.Arrange(new Rect(0, 0, 320, 180));
        preview.UpdateLayout();
        var bmp = new RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(preview);
        var px = new byte[320 * 180 * 4];
        bmp.CopyPixels(px, 320 * 4, 0);
        return px;
    }

    [Fact]
    public void ThePreviewDrawsTheEffect_AndNothingWhenThereIsNoneToDraw()
    {
        WindowHarness.Run(_ =>
        {
            foreach (var template in new[] { "vanilla:campfire", "forge:rising", "forge:falling", "vanilla:snow", FX.FollowerPoints, "forge:flash", "forge:popup" })
            {
                // The preview under the harness draws the moment 1.2 s in: a flash of 2 s is still up then.
                int drawn = Drawn(new EffectDef { Template = template, X = 0.5f, Y = 0.5f, Seconds = template == "forge:flash" ? 2 : null });
                _out.WriteLine($"{template}: {drawn} pixels drawn");
                Assert.True(drawn > 20, template + " drew nothing in the preview");
            }
            // Controls: none of them (an effect with no particles), and no effect at all.
            int none = Drawn(new EffectDef { Template = "vanilla:campfire", X = 0.5f, Y = 0.5f, Amount = 0 });
            _out.WriteLine("campfire, none of them: " + none);
            Assert.True(none < 5, "an effect with none of its particles drew " + none + " pixels");
            Assert.Equal(0, Drawn(null));
        });
    }
}
