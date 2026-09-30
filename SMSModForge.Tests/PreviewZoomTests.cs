using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Making the bust preview bigger, and the panel around it with it.
/// <para/>
/// The preview is pinned to its own size — it has to be, or a parent layout
/// negotiates it into whatever is left and the shader output gets resampled by
/// accident. So "bigger" means moving the pin AND the column it sits in
/// together; moving only one of them is how a preview ends up with more
/// backdrop around the same small bust.
/// <para/>
/// Measured on the control and on the column, not on the preference: the
/// preference is the easy half.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PreviewZoomTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly double _was = SMSModForge.Services.EditorPrefs.BustPreviewZoom;

    public PreviewZoomTests(ITestOutputHelper o) => _out = o;

    /// <summary>Somebody is running this with their own size set.</summary>
    public void Dispose() => SMSModForge.Services.EditorPrefs.BustPreviewZoom = _was;

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

    /// <summary>A window with room for the biggest size, so the size shown is
    /// the size picked. A smaller window shows a smaller one on purpose - see
    /// the tests at the end.</summary>
    private static void Roomy(MainWindow window)
    {
        window.Width = 2600;
        window.Height = 1800;
        WindowHarness.Pump();
    }

    private static void ShowTab(MainWindow window, string header)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == header) { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(0.5)]
    public void ThePictureGrows_AndSoDoesTheColumnHoldingIt(double zoom)
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            Roomy(window);
            ShowTab(window, "Characters");
            vm.BustPreviewZoom = zoom;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var preview = Descendants<JigglePreview>(window).FirstOrDefault();
            Assert.True(preview != null, "the bust preview is not in the window");

            double want = JigglePreview.FixedSize * zoom;
            _out.WriteLine($"{zoom}×: preview {preview!.Width:0}, column {vm.BustPreviewColumnWidth.Value:0}");

            // The picture itself, not the panel behind it.
            Assert.Equal(want, preview.Width);
            Assert.Equal(want, preview.Height);
            // Pinned there, so no parent can negotiate it back.
            Assert.Equal(want, preview.MinWidth);
            Assert.Equal(want, preview.MaxWidth);

            // And the column is wide enough to hold it, or it would be clipped
            // rather than shown bigger.
            Assert.True(vm.BustPreviewColumnWidth.Value >= want,
                        "the column is narrower than the preview it holds");
        });
    }

    [Fact]
    public void AWholeStepStaysPixelExact_AndAHalfStepSaysItIsNot()
    {
        // The reason the size was fixed in the first place: one bitmap pixel on
        // one screen pixel. A whole multiple keeps that and can be drawn with
        // nearest neighbour; a half cannot, and drawing it that way doubles
        // some rows and not others. This is the control for the zoom being a
        // real resize rather than a stretch of whatever was there.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            Roomy(window);
            ShowTab(window, "Characters");
            var preview = Descendants<JigglePreview>(window).First();

            vm.BustPreviewZoom = 2;
            WindowHarness.Pump();
            _out.WriteLine("2×: " + RenderOptions.GetBitmapScalingMode(preview) + ", " + preview.Stretch);
            Assert.Equal(BitmapScalingMode.NearestNeighbor, RenderOptions.GetBitmapScalingMode(preview));

            vm.BustPreviewZoom = 1.5;
            WindowHarness.Pump();
            _out.WriteLine("1.5×: " + RenderOptions.GetBitmapScalingMode(preview) + ", " + preview.Stretch);
            Assert.Equal(BitmapScalingMode.HighQuality, RenderOptions.GetBitmapScalingMode(preview));
        });
    }

    // ── A window too small for the size picked (the author, 1.6.3) ──────
    //
    // A 2x preview in a short window pushed the options under it - the size
    // picker first of all - off the bottom, where nothing could be reached to
    // make it smaller again.

    /// <summary>Where <paramref name="element"/> is drawn, in the window's own
    /// coordinates.</summary>
    private static Rect Drawn(MainWindow window, FrameworkElement element)
    {
        var content = (FrameworkElement)window.Content;
        var at = element.TranslatePoint(new Point(0, 0), content);
        return new Rect(at, new Size(element.ActualWidth, element.ActualHeight));
    }

    /// <summary>Every option under the preview that can be seen, with where it is.</summary>
    private static List<(string Name, Rect At)> Options(MainWindow window)
    {
        var options = (FrameworkElement)window.FindName("BustPreviewOptions");
        return Descendants<Control>(options)
            .Where(c => c.IsVisible && (c is ComboBox || c is CheckBox || c is Slider || c is Button))
            .Select(c => (c.Name.Length > 0 ? c.Name : c.GetType().Name, Drawn(window, c)))
            .ToList();
    }

    private static void Settle(MainWindow window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
    }

    [Fact]
    public void InAShortWindow_EveryOptionStaysInside_AndThePreviewSaysItIsSmaller()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            window.Width = 1400;
            window.Height = 700;
            ShowTab(window, "Characters");
            vm.BustPreviewZoom = 2;
            Settle(window);

            var content = (FrameworkElement)window.Content;
            var preview = Descendants<JigglePreview>(window).First();
            var options = Options(window);
            _out.WriteLine($"window content {content.ActualWidth:0}x{content.ActualHeight:0}, preview {preview.ActualWidth:0}, shown {vm.BustPreviewShownZoom}x");
            foreach (var (name, at) in options) _out.WriteLine($"  {name}: {at}");

            Assert.NotEmpty(options);
            Assert.All(options, o => Assert.True(o.At.Bottom <= content.ActualHeight + 0.5 && o.At.Right <= content.ActualWidth + 0.5,
                                                 $"{o.Name} is drawn at {o.At}, outside a window {content.ActualWidth:0}x{content.ActualHeight:0}"));

            // Smaller, but still one of the sizes offered and pinned there.
            Assert.True(preview.ActualWidth < JigglePreview.FixedSize * 2);
            Assert.Contains(preview.ActualWidth / JigglePreview.FixedSize, SMSModForge.Services.EditorPrefs.ZoomSteps);
            Assert.Equal(preview.Width, preview.MinWidth);

            // The picker still says what was picked, and a line says what is shown.
            var picker = (ComboBox)window.FindName("BustPreviewSizePicker");
            Assert.Equal(2.0, picker.SelectedItem);
            var note = (TextBlock)window.FindName("BustPreviewShrunkText");
            _out.WriteLine("note: " + note.Text);
            Assert.True(note.IsVisible, "nothing says the preview is smaller than the size picked");
        });
    }

    [Fact]
    public void GivenRoomAgain_ItGoesBackToTheSizePicked()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            window.Width = 1400;
            window.Height = 700;
            ShowTab(window, "Characters");
            vm.BustPreviewZoom = 2;
            Settle(window);
            var preview = Descendants<JigglePreview>(window).First();
            Assert.True(preview.ActualWidth < JigglePreview.FixedSize * 2);

            Roomy(window);
            Settle(window);
            _out.WriteLine($"roomy: preview {preview.ActualWidth:0}");
            Assert.Equal(JigglePreview.FixedSize * 2, preview.ActualWidth);
            Assert.False(((TextBlock)window.FindName("BustPreviewShrunkText")).IsVisible);
        });
    }

    [Fact]
    public void InANarrowWindow_TheOptionsStayInsideToo()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            window.Width = window.MinWidth;
            window.Height = 1800;
            ShowTab(window, "Characters");
            vm.BustPreviewZoom = 2;
            Settle(window);

            var content = (FrameworkElement)window.Content;
            var options = Options(window);
            _out.WriteLine($"window content {content.ActualWidth:0} wide, shown {vm.BustPreviewShownZoom}x");
            Assert.All(options, o => Assert.True(o.At.Right <= content.ActualWidth + 0.5,
                                                 $"{o.Name} ends at {o.At.Right:0}, past a window {content.ActualWidth:0} wide"));
            var preview = Descendants<JigglePreview>(window).First();
            var drawn = Drawn(window, preview);
            Assert.True(drawn.Right <= content.ActualWidth + 0.5, $"the preview ends at {drawn.Right:0}, past the window");
        });
    }

    [Theory]
    [InlineData(2.0, 5000, 5000, 2.0)]    // room for it: as picked
    [InlineData(2.0, 5000, 800, 1.5)]     // 1024 does not fit 800 tall; 768 does
    [InlineData(2.0, 600, 5000, 1.0)]     // 512 is the widest that fits 600
    [InlineData(1.0, 5000, 5000, 1.0)]    // never bigger than picked
    [InlineData(2.0, 100, 100, 0.5)]      // nothing fits: the smallest
    public void TheSizeShownIsTheLargestOfferedThatFits(double chosen, double wide, double tall, double shown)
    {
        Assert.Equal(shown, MainViewModel.FitBustPreviewZoom(chosen, SMSModForge.Services.EditorPrefs.ZoomSteps, wide, tall));
    }

    [Fact]
    public void TheSizeIsRememberedAndOnlyEverOneThatWasOffered()
    {
        SMSModForge.Services.EditorPrefs.BustPreviewZoom = 2;
        Assert.Equal(2, SMSModForge.Services.EditorPrefs.BustPreviewZoom);

        // A hand-edited file cannot make the preview a size nothing checked.
        SMSModForge.Services.EditorPrefs.BustPreviewZoom = 7.3;
        _out.WriteLine("after 7.3: " + SMSModForge.Services.EditorPrefs.BustPreviewZoom);
        Assert.Equal(1, SMSModForge.Services.EditorPrefs.BustPreviewZoom);
    }
}
