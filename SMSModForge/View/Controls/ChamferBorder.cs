using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMSModForge.View.Controls;

/// <summary>Which corners a <see cref="ChamferBorder"/> cuts.</summary>
[Flags]
public enum Corners
{
    None = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomRight = 4,
    BottomLeft = 8,
    All = TopLeft | TopRight | BottomRight | BottomLeft,
}

/// <summary>
/// A border whose corners are cut off at an angle rather than rounded: the
/// shape the EX themes are drawn in (1.8.0), where a Normal theme has its
/// rounded corners. Draws a fill and an outline along the same cut outline,
/// and clips what it holds to it, so a row's highlight does not spill past a
/// cut corner.
/// </summary>
public sealed class ChamferBorder : Decorator
{
    public static readonly DependencyProperty BackgroundProperty =
        Panel.BackgroundProperty.AddOwner(typeof(ChamferBorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner(typeof(ChamferBorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(double), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty CutProperty =
        DependencyProperty.Register(nameof(Cut), typeof(double), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty CornersProperty =
        DependencyProperty.Register(nameof(Corners), typeof(Corners), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(Corners.TopLeft | Corners.BottomRight,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty PaddingProperty =
        Border.PaddingProperty.AddOwner(typeof(ChamferBorder),
            new FrameworkPropertyMetadata(new Thickness(), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public Brush? Background { get => (Brush?)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public Brush? BorderBrush { get => (Brush?)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    /// <summary>The outline's width.</summary>
    public double Stroke { get => (double)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    /// <summary>How far along each edge a cut corner goes.</summary>
    public double Cut { get => (double)GetValue(CutProperty); set => SetValue(CutProperty, value); }
    public Corners Corners { get => (Corners)GetValue(CornersProperty); set => SetValue(CornersProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    protected override Size MeasureOverride(Size constraint)
    {
        var pad = Padding;
        double sw = Stroke * 2;
        var inner = new Size(Math.Max(0, constraint.Width - pad.Left - pad.Right - sw),
                             Math.Max(0, constraint.Height - pad.Top - pad.Bottom - sw));
        Child?.Measure(inner);
        var want = Child?.DesiredSize ?? default;
        return new Size(want.Width + pad.Left + pad.Right + sw, want.Height + pad.Top + pad.Bottom + sw);
    }

    protected override Size ArrangeOverride(Size size)
    {
        var pad = Padding;
        Child?.Arrange(new Rect(pad.Left + Stroke, pad.Top + Stroke,
                                Math.Max(0, size.Width - pad.Left - pad.Right - Stroke * 2),
                                Math.Max(0, size.Height - pad.Top - pad.Bottom - Stroke * 2)));
        Clip = Outline(new Rect(size), 0);
        return size;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Rect(RenderSize);
        if (size.Width <= 0 || size.Height <= 0) return;
        double half = Stroke / 2;
        var pen = BorderBrush != null && Stroke > 0 ? new Pen(BorderBrush, Stroke) { LineJoin = PenLineJoin.Miter } : null;
        dc.DrawGeometry(Background, pen, Outline(size, half));
    }

    /// <summary>The cut outline of <paramref name="r"/>, inset by
    /// <paramref name="inset"/> so an outline of that half-width lies inside.</summary>
    private Geometry Outline(Rect r, double inset)
    {
        double l = r.Left + inset, t = r.Top + inset, rt = r.Right - inset, b = r.Bottom - inset;
        double c = Math.Max(0, Math.Min(Cut, Math.Min(rt - l, b - t) / 2));
        var corners = Corners;
        double Tl = corners.HasFlag(Corners.TopLeft) ? c : 0, Tr = corners.HasFlag(Corners.TopRight) ? c : 0,
               Br = corners.HasFlag(Corners.BottomRight) ? c : 0, Bl = corners.HasFlag(Corners.BottomLeft) ? c : 0;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(l + Tl, t), true, true);
            ctx.LineTo(new Point(rt - Tr, t), true, false);
            ctx.LineTo(new Point(rt, t + Tr), true, false);
            ctx.LineTo(new Point(rt, b - Br), true, false);
            ctx.LineTo(new Point(rt - Br, b), true, false);
            ctx.LineTo(new Point(l + Bl, b), true, false);
            ctx.LineTo(new Point(l, b - Bl), true, false);
            ctx.LineTo(new Point(l, t + Tl), true, false);
        }
        g.Freeze();
        return g;
    }
}
