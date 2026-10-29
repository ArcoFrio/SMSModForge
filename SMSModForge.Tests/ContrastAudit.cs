using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SMSModForge.Tests;

/// <summary>
/// Every piece of text a window is showing, measured against what is actually
/// behind it: the theme can define good pairs and still put text on something
/// it never thought of - a system selection colour, a hard-coded grey, a
/// panel at half opacity (the author, 2026-10-21: Midnight's selected dialogue
/// is white on white, and some of its fields read as greyed out).
/// <para/>
/// Measured, not recomputed from the theme: the foreground is the element's own
/// brush, faded by every opacity on the way up, and the background is the
/// first opaque fill found walking up the visual tree, with any translucent
/// fills on the way composited over it. Contrast is WCAG's ratio.
/// <para/>
/// Text over a picture is measured from pixels instead (1.8.0): a picture is
/// drawn by a sibling underneath, not a parent, so walking up finds whatever
/// is behind the picture - black labels on a place's white button art read as
/// black on the dark canvas around it. Such text is drawn twice, with and
/// without it, and its letters are measured against what is really under them.
/// A filled shape drawn under text is measured the same way, and EX's
/// cut-corner fill (ChamferBorder) counts as what is behind its words - both
/// unread until after 1.8.0, when the audit first reached windows other than
/// the main one.
/// </summary>
internal static class ContrastAudit
{
    public sealed record Finding(double Ratio, Color Foreground, Color Background, string Kind,
                                 string Text, string Path, bool Enabled, bool Large);

    /// <summary>WCAG AA: 4.5 for body text, 3 for large (18.66px, or 14px bold).</summary>
    public static double Needed(bool large) => large ? 3.0 : 4.5;

    public static List<Finding> Walk(Visual root)
    {
        var found = new List<Finding>();
        var walk = new Walking(root);
        Visit(root, found, walk);
        return found;
    }

    /// <summary>The root being audited, and its picture once drawn.</summary>
    private sealed class Walking
    {
        public Walking(Visual root)
        {
            Root = root;
            Back = (root as DependencyObject) is { } d && Window.GetWindow(d) is { } w ? w.Background : null;
        }
        public Visual Root { get; }
        /// <summary>The window's own fill: the content is drawn over it, and
        /// without it what is empty would read as black.</summary>
        private Brush? Back { get; }
        private RenderTargetBitmap? _drawn;
        public RenderTargetBitmap Drawn => _drawn ??= Draw();
        public RenderTargetBitmap Draw()
        {
            var size = Root is FrameworkElement fe ? new Size(fe.ActualWidth, fe.ActualHeight) : new Size(1, 1);
            var area = new Rect(0, 0, Math.Max(1, Math.Ceiling(size.Width)), Math.Max(1, Math.Ceiling(size.Height)));
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(Back ?? Brushes.White, null, area);
                dc.DrawRectangle(new VisualBrush(Root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                                                         ViewboxUnits = BrushMappingMode.Absolute, Viewbox = area }, null, area);
            }
            var bmp = new RenderTargetBitmap((int)area.Width, (int)area.Height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(drawing);
            return bmp;
        }
    }

    private static void Visit(DependencyObject node, List<Finding> found, Walking walk)
    {
        if (node is UIElement ui && !ui.IsVisible) return;

        switch (node)
        {
            case TextBlock tb when !string.IsNullOrWhiteSpace(tb.Text) && tb.ActualWidth > 0:
                Measure(tb, tb.Foreground, tb.Text, tb.FontSize, tb.FontWeight, "TextBlock", found, walk);
                break;
            case TextBox box when !string.IsNullOrWhiteSpace(box.Text) && box.ActualWidth > 0:
                Measure(box, box.Foreground, box.Text, box.FontSize, box.FontWeight, "TextBox", found, walk);
                break;
        }

        int n = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++) Visit(VisualTreeHelper.GetChild(node, i), found, walk);
    }

    private static void Measure(FrameworkElement element, Brush? foreground, string text, double size,
                                FontWeight weight, string kind, List<Finding> found, Walking walk)
    {
        if (foreground is not SolidColorBrush fgBrush) return;
        if (OverPicture(element))
        {
            MeasureDrawn(element, text, size, weight, kind, found, walk);
            return;
        }
        var background = BackgroundOf(element);
        if (background == null) return;

        // Every opacity between the text and the window fades it toward what
        // is behind - which is exactly how a field comes to look disabled.
        double opacity = fgBrush.Opacity * (fgBrush.Color.A / 255.0);
        for (DependencyObject? up = element; up != null; up = VisualTreeHelper.GetParent(up))
            if (up is UIElement u) opacity *= u.Opacity;
        // Faded out altogether - a notice between showings: not drawn, so not read.
        if (opacity < 0.05) return;
        var fg = Over(fgBrush.Color, background.Value, opacity);

        bool large = size >= 18.66 || (size >= 14 && weight >= FontWeights.Bold);
        found.Add(new Finding(Ratio(fg, background.Value), fg, background.Value, kind,
                              text.Length > 50 ? text[..50] + "…" : text, PathOf(element),
                              element.IsEnabled, large));
    }

    /// <summary>
    /// Whether a picture is drawn under <paramref name="element"/>: an image,
    /// or a fill painted from one, in something that comes before it at any
    /// level up to the first opaque fill behind it and overlaps it on screen.
    /// </summary>
    private static bool OverPicture(FrameworkElement element)
    {
        var bounds = BoundsOf(element);
        if (bounds == null) return false;
        DependencyObject child = element;
        for (var up = VisualTreeHelper.GetParent(element); up != null; child = up, up = VisualTreeHelper.GetParent(up))
        {
            int n = VisualTreeHelper.GetChildrenCount(up);
            for (int i = 0; i < n; i++)
            {
                var under = VisualTreeHelper.GetChild(up, i);
                if (ReferenceEquals(under, child)) break;   // what comes after is drawn over, not under
                if (HasPicture(under, bounds.Value)) return true;
            }
            if (up is Panel { Background: SolidColorBrush { Color.A: 255 } } or Border { Background: SolidColorBrush { Color.A: 255 } }
                   or SMSModForge.View.Controls.ChamferBorder { Background: SolidColorBrush { Color.A: 255 } }) return false;
        }
        return false;
    }

    private static bool HasPicture(DependencyObject node, Rect over)
    {
        if (node is UIElement { IsVisible: false }) return false;
        bool picture = node switch
        {
            Image => true,
            System.Windows.Shapes.Shape { Fill: ImageBrush or VisualBrush or DrawingBrush } => true,
            // A filled shape drawn under text beside it - a colour picker's
            // swatch under its "was" and "now": walking up never meets it, so
            // the words were measured against the panel round the swatch.
            System.Windows.Shapes.Shape { Fill: SolidColorBrush { Color.A: > 0 } or GradientBrush } => true,
            System.Windows.Shapes.Shape { OpacityMask: ImageBrush } => true,
            Border { Background: ImageBrush or VisualBrush } => true,
            Panel { Background: ImageBrush or VisualBrush } => true,
            _ => false,
        };
        if (picture && node is FrameworkElement fe && ShownBoundsOf(fe) is { } b && b.IntersectsWith(over)) return true;
        int n = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++)
            if (HasPicture(VisualTreeHelper.GetChild(node, i), over)) return true;
        return false;
    }

    private static Rect? BoundsOf(FrameworkElement element)
    {
        var root = Root(element);
        if (root == null || element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
        try { return element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Where an element is seen: its bounds cut to every container
    /// on the way up that clips - a scrolled picture is not under what its
    /// scroll viewer no longer shows.</summary>
    private static Rect? ShownBoundsOf(FrameworkElement element)
    {
        var shown = BoundsOf(element);
        if (shown == null) return null;
        var rect = shown.Value;
        for (var up = VisualTreeHelper.GetParent(element); up != null; up = VisualTreeHelper.GetParent(up))
        {
            if (up is not FrameworkElement f) continue;
            bool clips = f.ClipToBounds || f is ScrollContentPresenter || f is ScrollViewer || f.Clip != null
                         || f is Window || f is System.Windows.Controls.Primitives.Popup;
            if (!clips || BoundsOf(f) is not { } b) continue;
            rect.Intersect(b);
            if (rect.IsEmpty) return null;
        }
        return rect;
    }

    private static Visual? Root(DependencyObject element)
    {
        Visual? top = null;
        for (var up = element; up != null; up = VisualTreeHelper.GetParent(up))
            if (up is Visual v) top = v;
        return top;
    }

    /// <summary>
    /// The text as drawn over its picture: the root drawn with and without
    /// it, its letters the pixels that differ, each measured against what is
    /// under it there. The tenth percentile is what is reported - the letters
    /// at their worst, not one stray anti-aliased edge.
    /// </summary>
    private static void MeasureDrawn(FrameworkElement element, string text, double size, FontWeight weight,
                                     string kind, List<Finding> found, Walking walk)
    {
        Rect at;
        try { at = element.TransformToAncestor(walk.Root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
        catch (InvalidOperationException) { return; }
        var with = walk.Drawn;
        int x0 = Math.Max(0, (int)Math.Floor(at.X)), y0 = Math.Max(0, (int)Math.Floor(at.Y));
        int w = Math.Min(with.PixelWidth - x0, (int)Math.Ceiling(at.Width)), h = Math.Min(with.PixelHeight - y0, (int)Math.Ceiling(at.Height));
        if (w <= 0 || h <= 0) return;

        double was = element.Opacity;
        element.Opacity = 0;
        RenderTargetBitmap without;
        try { without = walk.Draw(); }
        finally { element.Opacity = was; }

        var a = new byte[w * h * 4];
        var b = new byte[w * h * 4];
        var rect = new Int32Rect(x0, y0, w, h);
        with.CopyPixels(rect, a, w * 4, 0);
        without.CopyPixels(rect, b, w * 4, 0);
        // The letters' own colour, faded by every opacity on the way up, laid
        // over what is under each letter - not the drawn pixel, which at a
        // letter's edge is half background and would read as unreadable.
        if ((element is TextBlock t ? t.Foreground : ((TextBox)element).Foreground) is not SolidColorBrush ink) return;
        double opacity = ink.Opacity * (ink.Color.A / 255.0);
        for (DependencyObject? up = element; up != null; up = VisualTreeHelper.GetParent(up))
            if (up is UIElement u) opacity *= up == element ? was : u.Opacity;
        var ratios = new List<(double Ratio, Color Fg, Color Bg)>();
        for (int i = 0; i < a.Length; i += 4)
        {
            if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) < 60) continue;   // not a letter
            var bg = Color.FromRgb(b[i + 2], b[i + 1], b[i]);
            var fg = Over(ink.Color, bg, opacity);
            ratios.Add((Ratio(fg, bg), fg, bg));
        }
        if (ratios.Count < 4) return;   // nothing drawn to speak of
        var worst = ratios.OrderBy(r => r.Ratio).ElementAt(ratios.Count / 10);
        bool large = size >= 18.66 || (size >= 14 && weight >= FontWeights.Bold);
        found.Add(new Finding(worst.Ratio, worst.Fg, worst.Bg, kind + " over a picture",
                              text.Length > 50 ? text[..50] + "…" : text, PathOf(element), element.IsEnabled, large));
    }

    /// <summary>The colour behind an element: translucent fills composited
    /// down onto the first opaque one.</summary>
    internal static Color? BackgroundOf(DependencyObject element)
    {
        var layers = new List<(Color Color, double Alpha)>();
        // From the element itself: a TextBlock can carry its own background.
        for (DependencyObject? up = element; up != null; up = VisualTreeHelper.GetParent(up))
        {
            Brush? fill = up switch
            {
                TextBlock t => t.Background,
                Border b => b.Background,
                Panel p => p.Background,
                // EX's cut-corner fill. Unread until after 1.8.0, so every
                // word on an EX button was measured against the panel round
                // it - right only while the two were alike.
                SMSModForge.View.Controls.ChamferBorder c => c.Background,
                Control c when up is Window => c.Background,
                _ => null,
            };
            if (fill is not SolidColorBrush s) continue;
            double alpha = s.Opacity * (s.Color.A / 255.0);
            if (up is UIElement u) alpha *= u.Opacity;
            if (alpha <= 0.001) continue;
            layers.Add((Color.FromRgb(s.Color.R, s.Color.G, s.Color.B), alpha));
            if (alpha >= 0.999) break;
        }
        if (layers.Count == 0) return null;

        // From the opaque bottom layer up.
        var colour = layers[^1].Alpha >= 0.999 ? layers[^1].Color : Colors.White;
        for (int i = layers.Count - (layers[^1].Alpha >= 0.999 ? 2 : 1); i >= 0; i--)
            colour = Over(layers[i].Color, colour, layers[i].Alpha);
        return colour;
    }

    private static Color Over(Color top, Color bottom, double alpha)
    {
        byte Mix(byte t, byte b) => (byte)Math.Round(b + (t - b) * alpha);
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    public static double Ratio(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    /// <summary>The named ancestors of an element, outermost first - enough to
    /// find it in MainWindow.xaml.</summary>
    private static string PathOf(DependencyObject element)
    {
        var names = new List<string>();
        for (DependencyObject? up = element; up != null; up = VisualTreeHelper.GetParent(up))
            if (up is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name) && !fe.Name.StartsWith("PART_"))
                names.Add(fe.Name);
            else if (up is ListBoxItem lbi) names.Add(lbi.IsSelected ? "ListBoxItem(selected)" : "ListBoxItem");
            else if (up is TreeViewItem tvi) names.Add(tvi.IsSelected ? "TreeViewItem(selected)" : "TreeViewItem");
            else if (up is TabItem tab) names.Add(tab.IsSelected ? "TabItem(selected)" : "TabItem");
            else if (up is ComboBoxItem) names.Add("ComboBoxItem");
        names.Reverse();
        return string.Join("/", names);
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
