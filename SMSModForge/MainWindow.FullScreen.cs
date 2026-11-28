using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SMSModForge.View.Controls;

namespace SMSModForge;

/// <summary>
/// A place preview over the whole screen (after 1.10.0, the author: "can we also add a fullscreen button for the
/// Places preview?"). The preview itself - its picture, its hierarchy and the picked object's settings beside it -
/// moves into <c>FullScreenHost</c>, over the rest of the window, and the window covers its monitor, its frame and the
/// taskbar gone. Everything it does there it does as before: it is the same control, on the same place, its bindings
/// kept (its place reached through the page it came from, so picking another place on the way back finds it right).
/// Esc, F11, the host's own button, or another tab put it back where it was and the window as it was.
/// <para/>
/// Its buttons are the window's (a handler of the window's own), like the mask studio's and the pickers': the walk
/// over every control leaves a button that changes the view rather than the pack alone, and
/// PlacePreviewFullScreenTests holds this one.
/// </summary>
public partial class MainWindow
{
    private PlacePreview? _fullScreen;
    private Panel? _fullScreenFrom;
    private int _fullScreenAt;
    private (WindowStyle Style, WindowState State, ResizeMode Resize, bool Hooked) _fullScreenWas;

    /// <summary>The page's tick boxes and quality for the preview over the screen, and where each was on the page: shown
    /// under its hierarchy meanwhile (after 1.10.0, the author: "can you also put these settings in the full screen view,
    /// in a dedicated space right below the hierarchy?"). The same controls, moved - their bindings with them.</summary>
    private readonly List<(UIElement Row, Panel From, int At)> _fullScreenRows = new();

    private UIElement[] OptionRowsOf(PlacePreview preview)
        => ReferenceEquals(preview, PlacePreviewMain)
            ? new UIElement[] { PlaceOptionsRow, PlaceLookRow, PlaceQualityBar }
            : new UIElement[] { ExtOptionsRow, ExtLookRow, ExtQualityBar };

    /// <summary>The place preview shown over the whole screen, or null - for tests.</summary>
    internal PlacePreview? FullScreenPreview => _fullScreen;

    private void PlaceFullScreen_Click(object sender, RoutedEventArgs e) => EnterFullScreen(PlacePreviewMain);

    private void ExtFullScreen_Click(object sender, RoutedEventArgs e) => EnterFullScreen(ExtPreview);

    private void LeaveFullScreen_Click(object sender, RoutedEventArgs e) => LeaveFullScreen();

    /// <summary>The preview over the whole screen. Nothing when one already is, or it is not on a page.</summary>
    internal void EnterFullScreen(PlacePreview preview)
    {
        if (_fullScreen != null || preview.Parent is not Panel from) return;
        // Its place, from the page it came from: in the host it would take the window's instead.
        preview.SetBinding(DataContextProperty, new Binding(nameof(DataContext)) { Source = from });
        _fullScreenFrom = from;
        _fullScreenAt = from.Children.IndexOf(preview);
        from.Children.RemoveAt(_fullScreenAt);
        _fullScreen = preview;
        preview.FillsRoom = true;
        preview.VerticalAlignment = VerticalAlignment.Center;
        // Its tick boxes and quality under its hierarchy, each where it was noted first (they are next to one another).
        _fullScreenRows.Clear();
        foreach (var row in OptionRowsOf(preview))
            if (LogicalTreeHelper.GetParent(row) is Panel panel) _fullScreenRows.Add((row, panel, panel.Children.IndexOf(row)));
        var options = new StackPanel();
        foreach (var (row, panel, _) in _fullScreenRows)
        {
            panel.Children.Remove(row);
            options.Children.Add(row);
        }
        preview.OverlayExtras = options;
        FullScreenSpot.Children.Add(preview);
        FullScreenHost.Visibility = Visibility.Visible;

        // The window over its monitor: no frame (set before the size, or the taskbar stays over it).
        _fullScreenWas = (WindowStyle, WindowState, ResizeMode, true);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        WindowState = WindowState.Maximized;
        MainTabs.SelectionChanged += LeaveOnAnotherTab;
        preview.Focus();
    }

    /// <summary>The preview back where it was and the window as it was. Nothing when none is over the screen.</summary>
    internal void LeaveFullScreen()
    {
        if (_fullScreen is not { } preview) return;
        _fullScreen = null;
        MainTabs.SelectionChanged -= LeaveOnAnotherTab;
        FullScreenSpot.Children.Remove(preview);
        FullScreenHost.Visibility = Visibility.Collapsed;
        // The tick boxes and quality back on the page, each where it was.
        if (preview.OverlayExtras is Panel options) options.Children.Clear();
        preview.OverlayExtras = null;
        foreach (var (row, panel, at) in _fullScreenRows.OrderBy(r => r.At))
            panel.Children.Insert(System.Math.Min(at, panel.Children.Count), row);
        _fullScreenRows.Clear();
        preview.FillsRoom = false;
        preview.ClearValue(VerticalAlignmentProperty);
        var from = _fullScreenFrom!;
        from.Children.Insert(System.Math.Min(_fullScreenAt, from.Children.Count), preview);
        preview.ClearValue(DataContextProperty);
        _fullScreenFrom = null;
        if (_fullScreenWas.Hooked)
        {
            // The frame back before the size, so a maximized window takes the taskbar's room into account again.
            WindowState = WindowState.Normal;
            WindowStyle = _fullScreenWas.Style;
            ResizeMode = _fullScreenWas.Resize;
            WindowState = _fullScreenWas.State;
            _fullScreenWas = default;
        }
        preview.BringIntoView();
    }

    /// <summary>Over the whole screen, Esc and F11 put it back - a key nothing in it took (an open list closes
    /// first). On the Places tab, F11 puts the preview on it over the whole screen.</summary>
    private void OnFullScreenKey(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (_fullScreen != null)
        {
            if (e.Key is not (Key.Escape or Key.F11)) return;
            e.Handled = true;
            LeaveFullScreen();
            return;
        }
        if (e.Key != Key.F11 || MainTabs.SelectedIndex != ViewModel.MainTab.Places) return;
        var shown = PlacePreviewMain.IsVisible ? PlacePreviewMain : ExtPreview.IsVisible ? ExtPreview : null;
        if (shown == null) return;
        e.Handled = true;
        EnterFullScreen(shown);
    }

    private void LeaveOnAnotherTab(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, MainTabs)) LeaveFullScreen();
    }
}
