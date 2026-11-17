using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Characters tab's bust preview scales as the mask painter's does (1.9.1,
/// the author: "remove the 'Preview size' field on the Characters tab and
/// instead make it scale just like the preview on the mask editor"): no bigger
/// than the size its corner is dragged to, and no bigger than the room above
/// its options - so in a short or narrow window every option stays inside, and
/// given room again it grows back. The share of the tab it has is its
/// splitter's, remembered. Measured where the picture is drawn, not on a
/// preference.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PreviewZoomTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly double _scaleWas = EditorPrefs.GetPreviewScale("characters.bust");
    private readonly double _splitWas = EditorPrefs.CharactersPreviewSplit;

    public PreviewZoomTests(ITestOutputHelper o) => _out = o;

    public void Dispose()
    {
        EditorPrefs.SetPreviewScale("characters.bust", _scaleWas);
        EditorPrefs.CharactersPreviewSplit = _splitWas;
    }

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

    private static void ShowTab(MainWindow window, string header)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == header) { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();
    }

    /// <summary>Where <paramref name="element"/> is drawn, in the window's own
    /// coordinates - its transforms included.</summary>
    private static Rect Drawn(MainWindow window, FrameworkElement element)
    {
        var content = (FrameworkElement)window.Content;
        return element.TransformToVisual(content).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
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
        for (int i = 0; i < 3; i++) { WindowHarness.Pump(); window.UpdateLayout(); }
        WindowHarness.Pump();
    }

    private static (PreviewFrame frame, JigglePreview preview) Bust(MainWindow window)
        => ((PreviewFrame)window.FindName("BustPreviewResizer"), (JigglePreview)window.FindName("PreviewControl"));

    [Fact]
    public void InAShortWindow_EveryOptionStaysInside_AndTheBustShrinksToFit()
    {
        WindowHarness.Run(window =>
        {
            window.Width = 1400;
            window.Height = 700;
            ShowTab(window, "Characters");
            var (frame, preview) = Bust(window);
            frame.Scale = 2;
            Settle(window);

            var content = (FrameworkElement)window.Content;
            var drawn = Drawn(window, preview);
            var options = Options(window);
            _out.WriteLine($"window content {content.ActualWidth:0}x{content.ActualHeight:0}; the bust drawn at {drawn} (shown {frame.ShownScale:0.00}x of 2x)");
            foreach (var (name, at) in options) _out.WriteLine($"  {name}: {at}");
            foreach (var n in new[] { "BustPreviewPane", "BustPreviewHeader", "BustPreviewOptions", "BustQualityBar", "BustBackdropBar", "BustPreviewResizer", "BustPreviewFrame" })
            {
                var el = (FrameworkElement)window.FindName(n);
                _out.WriteLine($"  [{n}] {Drawn(window, el)} visible {el.IsVisible} desired {el.DesiredSize}");
            }
            Assert.NotEmpty(options);
            Assert.All(options, o => Assert.True(o.At.Bottom <= content.ActualHeight + 0.5 && o.At.Right <= content.ActualWidth + 0.5,
                                                 $"{o.Name} is drawn at {o.At}, outside a window {content.ActualWidth:0}x{content.ActualHeight:0}"));
            Assert.True(drawn.Width < JigglePreview.FixedSize * 2 - 1, "the bust is drawn at the size dragged to, in a window too short for it");
            var below = Options(window).Min(o => o.At.Top);
            Assert.True(drawn.Bottom <= below + 0.5, $"the bust ({drawn.Bottom:0}) is drawn over the options ({below:0})");
        });
    }

    [Fact]
    public void GivenRoomAgain_ItGrowsBackToTheSizeDragged()
    {
        WindowHarness.Run(window =>
        {
            window.Width = 1400;
            window.Height = 700;
            ShowTab(window, "Characters");
            var (frame, preview) = Bust(window);
            frame.Scale = 0.8;
            Settle(window);
            double small = Drawn(window, preview).Width;

            window.Width = 3000;
            window.Height = 1900;
            Settle(window);
            double big = Drawn(window, preview).Width;
            _out.WriteLine($"short window: {small:0} wide; roomy: {big:0} wide (the bust's own {JigglePreview.FixedSize})");
            Assert.True(small < JigglePreview.FixedSize * 0.8 - 1, "control: in the short window the bust was not smaller");
            Assert.InRange(big, JigglePreview.FixedSize * 0.8 - 1, JigglePreview.FixedSize * 0.8 + 1);
        });
    }

    [Fact]
    public void InANarrowWindow_TheOptionsAndTheBustStayInsideToo()
    {
        WindowHarness.Run(window =>
        {
            window.Width = window.MinWidth;
            window.Height = 1800;
            ShowTab(window, "Characters");
            Bust(window).frame.Scale = 2;
            Settle(window);

            var content = (FrameworkElement)window.Content;
            var options = Options(window);
            _out.WriteLine($"window content {content.ActualWidth:0} wide");
            Assert.All(options, o => Assert.True(o.At.Right <= content.ActualWidth + 0.5,
                                                 $"{o.Name} ends at {o.At.Right:0}, past a window {content.ActualWidth:0} wide"));
            var drawn = Drawn(window, Bust(window).preview);
            Assert.True(drawn.Right <= content.ActualWidth + 0.5, $"the bust ends at {drawn.Right:0}, past the window");
        });
    }

    [Fact]
    public void EnlargedTheBustIsDrawnPixelByPixel_AndShrunkItIsSmoothed()
    {
        // Pixel by pixel whenever it is made bigger; made smaller, nearest
        // neighbour would drop whole rows, so smoothed - by the frame, which the
        // bust at its own size no longer overrules (it pinned nearest neighbour
        // on itself until 1.9.1).
        WindowHarness.Run(window =>
        {
            window.Width = 3000;
            window.Height = 1900;
            ShowTab(window, "Characters");
            var (frame, preview) = Bust(window);
            frame.Scale = 2;
            Settle(window);
            _out.WriteLine($"2x: frame {RenderOptions.GetBitmapScalingMode(frame)}, the bust's own {preview.ReadLocalValue(RenderOptions.BitmapScalingModeProperty)}");
            Assert.Equal(BitmapScalingMode.NearestNeighbor, RenderOptions.GetBitmapScalingMode(frame));
            Assert.Equal(DependencyProperty.UnsetValue, preview.ReadLocalValue(RenderOptions.BitmapScalingModeProperty));
            frame.Scale = 0.5;
            Settle(window);
            Assert.Equal(BitmapScalingMode.HighQuality, RenderOptions.GetBitmapScalingMode(frame));
        });
    }

    [Fact]
    public void TheSplitterSharesTheTab_AndTheBustFollowsIt()
    {
        // A larger share for the preview: its column wider, the bust drawn bigger
        // up to the size dragged to. Control: a smaller share draws it smaller.
        double DrawnAt(double share)
        {
            EditorPrefs.CharactersPreviewSplit = share;
            double width = 0;
            WindowHarness.Run(window =>
            {
                window.Width = 1600;
                window.Height = 1900;
                ShowTab(window, "Characters");
                Bust(window).frame.Scale = 2;
                Settle(window);
                width = Drawn(window, Bust(window).preview).Width;
                _out.WriteLine($"share {share}: the bust drawn {width:0} wide");
            });
            return width;
        }
        double wide = DrawnAt(0.7), narrow = DrawnAt(0.3);
        Assert.True(wide > narrow + 50, $"a larger share for the preview did not draw the bust larger ({wide:0} against {narrow:0})");
    }

    [Fact]
    public void TheSizeDraggedToIsRemembered_StartingFromTheOldPreviewSize()
    {
        // A preference file from before 1.9.1: the picker's size, nothing dragged.
        EditorPrefs.SetPreviewScale("characters.bust", double.NaN);
        var raw = typeof(EditorPrefs).GetMethod("SetDouble", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        raw.Invoke(null, new object[] { "bustPreviewZoom", 1.5 });
        Assert.Equal(1.5, EditorPrefs.GetPreviewScale("characters.bust"));
        EditorPrefs.SetPreviewScale("characters.bust", 2);
        Assert.Equal(2, EditorPrefs.GetPreviewScale("characters.bust"));
        // Control: another preview does not take the old picker's size.
        Assert.Equal(1, EditorPrefs.GetPreviewScale("maskStudio.npc-never-dragged"));
    }
}
