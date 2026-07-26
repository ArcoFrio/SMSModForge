using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// Drop-preview overlay for the dialogue node list. Renders on the adorner
/// layer above the row currently under the cursor:
/// <list type="bullet">
///   <item><see cref="NodeDropMode.Before"/> / <see cref="NodeDropMode.After"/> —
///   a 2px insertion line at the row's top/bottom edge, inset to the depth the
///   node would land at, so the line visually starts where the moved row will.</item>
///   <item><see cref="NodeDropMode.Into"/> — a rounded outline around the whole
///   row, reading as "goes inside this one".</item>
/// </list>
/// Colours come from the theme's Accent resource rather than a literal so the
/// preview stays visible on all ten themes, light and dark.
/// </summary>
internal sealed class NodeDropAdorner : Adorner
{
    private readonly NodeDropMode _mode;
    private readonly double _indent;

    /// <param name="adorned">The row (ListBoxItem) under the cursor.</param>
    /// <param name="mode">Which of the three drop bands the cursor is in.</param>
    /// <param name="indent">Left inset for the insertion line, in pixels —
    /// the indent of the level the dragged node would end up at.</param>
    public NodeDropAdorner(UIElement adorned, NodeDropMode mode, double indent) : base(adorned)
    {
        _mode = mode;
        _indent = indent;
        IsHitTestVisible = false;   // never eat the drag events we're previewing
    }

    private static Brush AccentBrush()
    {
        // DynamicResource isn't available to a raw OnRender, so resolve the
        // live value at paint time; the adorner is short-lived (one drag) so
        // it can't go stale.
        if (Application.Current?.TryFindResource("Theme.Accent") is Brush b) return b;
        return Brushes.DodgerBlue;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = AdornedElement.RenderSize.Width;
        var h = AdornedElement.RenderSize.Height;
        var brush = AccentBrush();

        if (_mode == NodeDropMode.Into)
        {
            var pen = new Pen(brush, 2);
            pen.Freeze();
            dc.DrawRoundedRectangle(null, pen, new Rect(1, 1, w - 2, h - 2), 3, 3);
            return;
        }

        double y = _mode == NodeDropMode.Before ? 1 : h - 1;
        var linePen = new Pen(brush, 2);
        linePen.Freeze();
        dc.DrawLine(linePen, new Point(_indent, y), new Point(w - 1, y));
        // Leading tick, so a line at depth N is distinguishable from one at N+1
        // even when the rows above and below happen to align.
        dc.DrawEllipse(brush, null, new Point(_indent + 3, y), 3, 3);
    }
}
