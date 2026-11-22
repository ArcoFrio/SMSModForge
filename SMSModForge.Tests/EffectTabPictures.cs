using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Pictures of the FX tab (1.10.0), to look at in each look: nothing is asserted. Only when
/// SMSMODFORGE_SHOT_DIR names a folder; the theme is SMSMODFORGE_TEST_THEME's.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class EffectTabPictures
{
    private static void Save(Visual what, double w, double h, string file)
    {
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(what);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(file);
        png.Save(f);
    }

    private static T? FindVisible<T>(DependencyObject root) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t && t.IsVisible) return t;
            if (FindVisible<T>(c) is { } found) return found;
        }
        return null;
    }

    [Fact]
    public void TakeThem()
    {
        string? dir = Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        string theme = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light";
        string pack = Path.Combine(Path.GetTempPath(), "smsmodforge-fxshots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pack);
        PackRepository.Save(UndoCrawlPack.Make(pack), pack);
        WindowHarness.Run(window =>
        {
            window.Width = 1500; window.Height = 1100;
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(pack);
            WindowHarness.Pump();
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Fx;
            foreach (var key in new[] { "fire", "photoHearts", "words" })
            {
                vm.SelectedEffect = vm.Effects.First(e => e.Key == key);
                window.UpdateLayout();
                WindowHarness.Pump();
                var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
                Save(root, root.ActualWidth, root.ActualHeight, Path.Combine(dir, $"fx-{key}-{theme}.png"));
            }
            // A place with an effect object (effects in places).
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            vm.SelectedPlace = vm.Places.First();
            window.UpdateLayout();
            WindowHarness.Pump();
            var preview = FindVisible<SMSModForge.View.Controls.PlacePreview>(window);
            preview?.AddEffectHere();
            window.UpdateLayout();
            WindowHarness.Pump();
            var proot = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
            Save(proot, proot.ActualWidth, proot.ActualHeight, Path.Combine(dir, $"fx-place-{theme}.png"));
            typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        });
    }
}
