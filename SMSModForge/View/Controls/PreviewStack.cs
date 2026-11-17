using System;
using System.Windows;
using System.Windows.Controls;

namespace SMSModForge.View.Controls;

/// <summary>
/// A preview with what goes with it directly under it, top-down with no gap
/// (1.9.1, the author: only the backdrop buttons belong at the bottom of the
/// pane, "the rest of the layout should have stayed like before"). The first
/// child is the preview: it gets the height the others leave, so a preview
/// that fits its room (a <see cref="PreviewFrame"/>) never pushes them out of
/// the pane. The others are as tall as they ask, under it in order.
/// </summary>
public sealed class PreviewStack : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double width = 0, below = 0;
        var room = new Size(available.Width, double.PositiveInfinity);
        for (int i = 1; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(room);
            width = Math.Max(width, child.DesiredSize.Width);
            below += child.DesiredSize.Height;
        }
        double above = 0;
        if (InternalChildren.Count > 0)
        {
            var preview = InternalChildren[0];
            double height = double.IsInfinity(available.Height) ? double.PositiveInfinity : Math.Max(0, available.Height - below);
            preview.Measure(new Size(available.Width, height));
            width = Math.Max(width, preview.DesiredSize.Width);
            above = preview.DesiredSize.Height;
        }
        return new Size(width, above + below);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0;
        foreach (UIElement child in InternalChildren)
        {
            double h = child.DesiredSize.Height;
            child.Arrange(new Rect(0, y, final.Width, h));
            y += h;
        }
        return final;
    }
}
