using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The volume rows of the SFX and Music tabs (the author, 1.7.0): a slider
/// from 0 to 5 first, then a box just wide enough for "1.0", both starting at
/// the game's own level and moving together.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class VolumeRowTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-volrow-" + Guid.NewGuid().ToString("N"));

    public VolumeRowTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private static T Bound<T>(MainWindow window, DependencyProperty property, string path) where T : FrameworkElement
        => Descendants<T>(window).Single(e => e.IsVisible && BindingOperations.GetBinding(e, property)?.Path?.Path == path);

    private void Check(MainWindow window, string valuePath, string textPath, Func<double> stored)
    {
        window.UpdateLayout();
        WindowHarness.Pump();
        var slider = Bound<Slider>(window, System.Windows.Controls.Primitives.RangeBase.ValueProperty, valuePath);
        var box = Bound<TextBox>(window, TextBox.TextProperty, textPath);

        double sliderX = slider.TranslatePoint(new Point(0, 0), window).X;
        double boxX = box.TranslatePoint(new Point(0, 0), window).X;
        _out.WriteLine($"{valuePath}: slider at {sliderX:0} ({slider.ActualWidth:0} wide, 0..{slider.Maximum}), box at {boxX:0} ({box.ActualWidth:0} wide) reading '{box.Text}'");

        Assert.True(sliderX < boxX, "the slider comes before the box");
        Assert.Equal(0, slider.Minimum);
        Assert.Equal(5, slider.Maximum);
        Assert.True(slider.ActualWidth > box.ActualWidth * 2, "the slider is the main control");
        // Wide enough for "1.0", and not much more.
        var typed = new FormattedText("5.0", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch), box.FontSize, Brushes.Black, 1.0);
        Assert.InRange(box.ActualWidth, typed.Width + 4, typed.Width + 24);

        Assert.Equal("1.0", box.Text);
        Assert.Equal(1.0, slider.Value);

        slider.Value = 3.0;
        WindowHarness.Pump();
        Assert.Equal("3.0", box.Text);
        _out.WriteLine($"   at 3.0 the pack stores {stored()}");
    }

    [Fact]
    public void TheSoundsVolumeRow()
    {
        var pack = PackRepository.CreateEmpty("volrow.pack");
        pack.Sfx.Add(new SfxDef { Key = "plap", DisplayName = "Plap" });
        PackRepository.Save(pack, _dir);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = 8;   // SFX
            vm.SelectedSfx = vm.Sfx.Single();
            WindowHarness.Pump();
            Check(window, nameof(SfxViewModel.DefaultVolumeValue), nameof(SfxViewModel.DefaultVolumeText),
                  () => vm.SelectedSfx!.Model.DefaultVolume ?? -1);
            Assert.Equal(3f, vm.SelectedSfx!.Model.DefaultVolume);
        });
    }

    [Fact]
    public void TheTracksVolumeRow()
    {
        var pack = PackRepository.CreateEmpty("volrow.pack");
        pack.Music.Add(new MusicDef { Key = "theme", DisplayName = "Theme" });
        PackRepository.Save(pack, _dir);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = 7;   // Music
            vm.SelectedMusic = vm.Music.Single();
            WindowHarness.Pump();
            Check(window, nameof(MusicViewModel.VolumeValue), nameof(MusicViewModel.VolumeText),
                  () => vm.SelectedMusic!.Model.Volume ?? -1);
            // Three times the game's level for a track: its AudioSource at 1.5.
            Assert.Equal(1.5f, vm.SelectedMusic!.Model.Volume);
        });
    }
}
