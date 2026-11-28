using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// The hierarchy and the settings beside the picture, one size for every place (after 1.10.0, the author: "the left
/// and right menu sizes for the Places previews ... should be a fixed size for all modpack places, the hierarchy one
/// based on the longest name in any of the levels' GOs"), and over the picture when it fills the screen ("For the full
/// screen, the canvas should span the whole screen, while the left and right menus overlay it").
/// <para/>
/// The hierarchy was as wide as the place's own longest row, so it - and with a picture its corner was dragged out, the
/// settings beside it, which give way with it - changed width from one place to the next. It now takes the widest row
/// of every place the preview shows (<see cref="AllPlaces"/>): a row of it, made as the others are, sits in the list
/// unseen and at no height, so the list is measured as wide as it, whatever is shown. The settings panel was already
/// one width (SettingsWidth).
/// </summary>
public sealed partial class PlacePreview
{
    public static readonly DependencyProperty AllPlacesProperty =
        DependencyProperty.Register(nameof(AllPlaces), typeof(IEnumerable), typeof(PlacePreview),
            new PropertyMetadata(null, (d, _) => ((PlacePreview)d).RefreshObjectMenu()));

    /// <summary>Every place this preview shows, one after another (a pack's places, or the game's levels it extends):
    /// the hierarchy is as wide as the widest row of any of them.</summary>
    public IEnumerable? AllPlaces
    {
        get => (IEnumerable?)GetValue(AllPlacesProperty);
        set => SetValue(AllPlacesProperty, value);
    }

    private static readonly Dictionary<string, double> LabelWidths = new(StringComparer.Ordinal);
    private static double _monoWidth;

    /// <summary>The row unseen in the list that holds it as wide as the widest of every place, as last made - for tests.</summary>
    internal string? WidestRowAnywhere { get; private set; }

    /// <summary>The widest row of every place shown - its depth and its words - or null without <see cref="AllPlaces"/>.</summary>
    private (int Depth, string Label, bool Parent)? WidestRow()
    {
        if (AllPlaces is not { } places) return null;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface(System.Windows.Documents.TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double Width(string label)
        {
            if (LabelWidths.TryGetValue(label, out double w)) return w;
            w = new FormattedText(label, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 11, Brushes.White, dip)
                .WidthIncludingTrailingWhitespace;
            if (LabelWidths.Count > 20000) LabelWidths.Clear();
            LabelWidths[label] = w;
            return w;
        }
        if (_monoWidth <= 0)
            _monoWidth = new FormattedText("──", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                           new Typeface(RowMono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 11, Brushes.White, dip)
                .WidthIncludingTrailingWhitespace / 2;
        (int, string, bool)? best = null;
        double bestWidth = -1;
        void Consider(int depth, string label, bool parent)
        {
            // The guides (two of the mono font a level, and the row's own branch), the fold arrow and the eye, then the words.
            double w = _monoWidth * (2 * depth + (depth > 0 ? 2 : 0) + 4) + Width(label);
            if (w > bestWidth) { bestWidth = w; best = (depth, label, parent); }
        }
        void Walk(IEnumerable<GameObjectViewModel> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                string icon = node.IsNpcRoot ? "◈" : !string.IsNullOrWhiteSpace(node.Sprite) ? "▦"
                            : node.HasLight || node.LightRemoved ? "☼" : node.HasEffect ? "✦" : "⌗";
                Consider(depth, icon + "  " + node.Display, node.Children.Count > 0 || node.Npcs.Count > 0);
                foreach (var placed in node.Npcs)
                {
                    Consider(depth + 1, "☺  " + placed.Display, placed.Children.Count > 0);
                    Walk(placed.Children, depth + 2);
                }
                Walk(node.Children, depth + 1);
            }
        }
        foreach (var place in places)
        {
            var tree = place switch
            {
                PlaceViewModel p => p.GameObjects,
                VanillaPlaceExtensionViewModel v => v.GameObjects,
                _ => null,
            };
            if (tree != null) Walk(tree, 0);
        }
        return best;
    }

    /// <summary>The widest row of every place, made as a row of the list is, unseen and at no height: what holds the
    /// list as wide as it.</summary>
    private void AddWidestRow()
    {
        WidestRowAnywhere = null;
        if (WidestRow() is not { } widest) return;
        var entry = new SceneEntry { Depth = widest.Depth, Guides = new bool[widest.Depth], IsLast = true, Path = "" };
        var row = MenuRow(widest.Label, entry, false, () => { }, false, widest.Parent ? () => { } : null, () => { });
        row.IsHitTestVisible = false;
        // In a holder of no height, unseen: no row of the list (its rows are its buttons), nothing to click or walk.
        _objectMenuList.Children.Add(new Border { Child = row, Height = 0, Visibility = Visibility.Hidden, IsHitTestVisible = false });
        WidestRowAnywhere = widest.Label;
    }

    // ── Over the picture, when it fills the screen ─────────────────────

    private Thickness _menuMargin, _settingsMargin;

    /// <summary>
    /// What is shown under the hierarchy while the picture fills the screen (after 1.10.0, the author: "can you also put
    /// these settings in the full screen view, in a dedicated space right below the hierarchy?"): the preview's own tick
    /// boxes and quality, which MainWindow.FullScreen moves here and back. In a panel of the page's own colours, as wide
    /// as the hierarchy - both as wide as the widest of them - the hierarchy above it taking what is left.
    /// </summary>
    public UIElement? OverlayExtras
    {
        get => _extrasHost.Child;
        set
        {
            _extrasHost.Child = value;
            ShowExtras();
            InvalidateMeasure();
        }
    }

    private readonly Border _extrasHost = MakeExtrasHost();

    private static Border MakeExtrasHost()
    {
        var host = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };
        host.SetResourceReference(Border.BackgroundProperty, "Theme.Surface");
        host.SetResourceReference(Border.BorderBrushProperty, "Theme.Border");
        SetColumn(host, 1);
        Panel.SetZIndex(host, 16);
        return host;
    }

    /// <summary>
    /// A dropdown on the preview's own dark panels, set on the theme's surface: a theme whose dropdowns draw no box of
    /// their own (Solar) put its dark words straight on the dark panel - the Stand-in's "None" read 1.29 against the
    /// 4.5 it needs once the panels took the preview's colour (after 1.10.0).
    /// </summary>
    private static Border OnThemeSurface(Control box, Thickness margin = default)
    {
        var under = new Border { CornerRadius = new CornerRadius(2), Child = box, Margin = margin };
        under.SetResourceReference(Border.BackgroundProperty, "Theme.Surface");
        return under;
    }

    private void ShowExtras()
    {
        bool shown = FillsRoom && _extrasHost.Child != null;
        if (shown && !Children.Contains(_extrasHost)) Children.Add(_extrasHost);
        _extrasHost.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The narrowest the panel under the hierarchy can be: its widest single piece - a tick box, the quality
    /// row - with its padding and border.</summary>
    private double ExtrasLeast()
    {
        double widest = 0;
        void Piece(UIElement e)
        {
            if (e is WrapPanel wrap)
            {
                foreach (UIElement c in wrap.Children)
                {
                    c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    widest = Math.Max(widest, c.DesiredSize.Width);
                }
                return;
            }
            // The list of rows itself; any other row (the quality's chips) is one piece, as wide as all of it.
            if (e is StackPanel { Orientation: Orientation.Vertical } stack)
            {
                foreach (UIElement c in stack.Children) Piece(c);
                return;
            }
            e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, e.DesiredSize.Width);
        }
        if (_extrasHost.Child is { } child) Piece(child);
        return widest + _extrasHost.Padding.Left + _extrasHost.Padding.Right + 2;
    }

    /// <summary>Where the panel under the hierarchy is drawn, in this control's own pixels (empty when none) - for tests.</summary>
    internal Rect ExtrasDrawnAt => Bounds(_extrasHost);

    /// <summary>The hierarchy and the settings over the picture's sides (filling the screen), or beside it again.</summary>
    private void PanelsOver(bool over)
    {
        if (over)
        {
            _menuMargin = _objectMenu.Margin;
            _settingsMargin = _gizmoToolbar.Margin;
            SetColumn(_objectMenu, 1);
            SetColumn(_gizmoToolbar, 1);
            _objectMenu.HorizontalAlignment = HorizontalAlignment.Left;
            _gizmoToolbar.HorizontalAlignment = HorizontalAlignment.Right;
            _objectMenu.Margin = new Thickness(8, 8, 0, 8);
            _gizmoToolbar.Margin = new Thickness(0, 8, 8, 8);
            Panel.SetZIndex(_objectMenu, 15);
            Panel.SetZIndex(_gizmoToolbar, 15);
            ShowExtras();
        }
        else
        {
            SetColumn(_objectMenu, 0);
            SetColumn(_gizmoToolbar, 2);
            _objectMenu.ClearValue(HorizontalAlignmentProperty);
            _gizmoToolbar.ClearValue(HorizontalAlignmentProperty);
            _objectMenu.Margin = _menuMargin;
            _gizmoToolbar.Margin = _settingsMargin;
            _objectMenu.ClearValue(WidthProperty);
            _gizmoToolbar.ClearValue(WidthProperty);
            Panel.SetZIndex(_objectMenu, 0);
            Panel.SetZIndex(_gizmoToolbar, 0);
            ShowExtras();
        }
        InvalidateMeasure();
    }

    /// <summary>Where the hierarchy and the settings are drawn, and the picture, in this control's own pixels - for tests.</summary>
    internal (Rect Hierarchy, Rect Settings, Rect Picture) PanelsDrawnAt
        => (Bounds(_objectMenu), Bounds(_gizmoToolbar), Bounds(_viewport));

    private Rect Bounds(FrameworkElement e)
        => e.IsVisible ? e.TransformToAncestor(this).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)) : Rect.Empty;
}
