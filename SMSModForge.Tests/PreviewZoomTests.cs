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
