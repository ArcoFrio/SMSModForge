using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using SMSModForge.Localization;

namespace SMSModForge.View.Controls;

/// <summary>How a <see cref="PreviewFrame"/> makes its preview bigger.</summary>
public enum PreviewScaling
{
    /// <summary>Scales the preview as drawn: for a preview pinned to a size of
    /// its own (a scene, a wallpaper, a bust).</summary>
    Transform,

    /// <summary>Only says the size: for a preview that sizes itself from
    /// <see cref="PreviewFrame.Scale"/>, bound (the character's bust). The
    /// place preview sizes itself too, with a corner of its own: its lists
    /// made bigger show more rows, not bigger words.</summary>
    Bound,
}

/// <summary>
/// A tab's preview, made bigger or smaller by dragging the mark at its corner,
/// in proportion (the author, 1.8.0: "the user should be able to change the
/// size of the preview by stretching them with the mouse uniformly ... with
/// some indication of where the user needs to drag"). The size is remembered
/// per preview, and shown no larger than the room there is: a window made
/// narrower shows it smaller, and wider again, as large as it was made.
/// <para/>
/// Drawn crisp: the game is pixel art, so made bigger the picture is drawn
/// pixel by pixel rather than smoothed (nearest neighbour), and smoothed only
/// when made smaller than life, where point sampling would drop whole rows.
/// A preview that decides that for one of its pictures itself - a wallpaper,
/// a photo shrunk to fit - still does.
/// </summary>
[ContentProperty(nameof(Preview))]
public sealed class PreviewFrame : Grid
{
    private readonly Thumb _grip;
    private ScaleTransform? _transform;

    public PreviewFrame()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        _grip = new Thumb
        {
            Width = GripSize, Height = GripSize,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Template = GripTemplate(),
            Focusable = false,
        };
        LocText.Bind(_grip, ToolTipProperty, "preview.resize.tip");
        Panel.SetZIndex(_grip, 10);
        _grip.DragDelta += OnDrag;
        _grip.DragCompleted += (_, _) => Remember();
        _grip.MouseDoubleClick += (_, e) => { Scale = 1; Remember(); e.Handled = true; };
        Children.Add(_grip);
        Loaded += (_, _) => Recall();
    }

    /// <summary>Its size in the corner: big enough to find, and to grab.</summary>
    public const double GripSize = 16;

    // ── Properties ────────────────────────────────────────────────────────

    public static readonly DependencyProperty PreviewProperty =
        DependencyProperty.Register(nameof(Preview), typeof(FrameworkElement), typeof(PreviewFrame),
            new PropertyMetadata(null, (d, e) => ((PreviewFrame)d).Adopt((FrameworkElement?)e.OldValue, (FrameworkElement?)e.NewValue)));

    public FrameworkElement? Preview
    {
        get => (FrameworkElement?)GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    /// <summary>The name its size is remembered by; not remembered without one.</summary>
    public string Key { get; set; } = "";

    public PreviewScaling Scaling { get; set; } = PreviewScaling.Transform;

    /// <summary>Sizes it may be dragged to, to snap to; any size between
    /// <see cref="MinScale"/> and <see cref="MaxScale"/> without.</summary>
    public double[]? Steps { get; set; }

    public double MinScale { get; set; } = 0.5;
    public double MaxScale { get; set; } = 4;

    /// <summary>The size chosen, 1 being the preview's own.</summary>
    public static readonly DependencyProperty ScaleProperty =
        DependencyProperty.Register(nameof(Scale), typeof(double), typeof(PreviewFrame),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsMeasure,
                null, (d, v) => Math.Clamp((double)v, ((PreviewFrame)d).MinScale, ((PreviewFrame)d).MaxScale)));

    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    /// <summary>The size it is drawn at: <see cref="Scale"/>, or smaller when
    /// the room is narrower than that.</summary>
    public double ShownScale { get; private set; } = 1;

    // ── Layout ────────────────────────────────────────────────────────────

    private void Adopt(FrameworkElement? old, FrameworkElement? preview)
    {
        if (old != null) Children.Remove(old);
        if (preview == null) return;
        Children.Insert(0, preview);
        if (Scaling == PreviewScaling.Transform)
            preview.LayoutTransform = _transform = new ScaleTransform(1, 1);
    }

    protected override Size MeasureOverride(Size available)
    {
        var preview = Preview;
        if (preview != null)
        {
            double chosen = Math.Clamp(Scale, MinScale, MaxScale);
            double room = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width;
            double shown = chosen;
            switch (Scaling)
            {
                case PreviewScaling.Transform when _transform != null:
                    // Its own size, measured unscaled.
                    double own = Natural(preview).Width;
                    if (own > 0 && !double.IsInfinity(room)) shown = Math.Min(chosen, room / own);
                    shown = Math.Max(shown, Math.Min(MinScale, chosen));
                    if (Math.Abs(_transform.ScaleX - shown) > 1e-6) { _transform.ScaleX = shown; _transform.ScaleY = shown; }
                    break;
            }
            ShownScale = shown;
            // Pixel art made bigger is drawn pixel by pixel; made smaller, smoothed.
            RenderOptions.SetBitmapScalingMode(this, shown >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        }
        return base.MeasureOverride(available);
    }

    private static Size Natural(FrameworkElement preview)
    {
        var transform = preview.LayoutTransform;
        if (!double.IsNaN(preview.Width) && !double.IsNaN(preview.Height)) return new Size(preview.Width, preview.Height);
        preview.LayoutTransform = Transform.Identity;
        preview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = preview.DesiredSize;
        preview.LayoutTransform = transform;
        return size;
    }

    // ── Dragging ──────────────────────────────────────────────────────────

    /// <summary>In proportion: the corner follows the pointer along the
    /// preview's diagonal, so the picture keeps its shape.</summary>
    private void OnDrag(object sender, DragDeltaEventArgs e)
    {
        var preview = Preview;
        if (preview == null) return;
        double width = Math.Max(1, preview.ActualWidth * (Scaling == PreviewScaling.Transform ? ShownScale : 1));
        double height = Math.Max(1, preview.ActualHeight * (Scaling == PreviewScaling.Transform ? ShownScale : 1));
        // The pointer's move along the diagonal, as a share of it.
        double along = (e.HorizontalChange * width + e.VerticalChange * height) / (width * width + height * height);
        double wanted = Math.Clamp(ShownScale * (1 + along), MinScale, MaxScale);
        Scale = Snap(wanted);
    }

    private double Snap(double wanted)
    {
        if (Steps is not { Length: > 0 } steps) return Math.Round(wanted, 2);
        double best = steps[0];
        foreach (double s in steps) if (Math.Abs(s - wanted) < Math.Abs(best - wanted)) best = s;
        return best;
    }

    private void Remember()
    {
        if (Key.Length > 0 && Scaling != PreviewScaling.Bound) Services.EditorPrefs.SetPreviewScale(Key, Scale);
    }

    private void Recall()
    {
        if (Key.Length > 0 && Scaling != PreviewScaling.Bound) Scale = Services.EditorPrefs.GetPreviewScale(Key);
    }

    /// <summary>Three diagonal ticks in the accent colour, over a faint
    /// corner: the mark a window has where it is resized.</summary>
    internal static ControlTemplate GripTemplate()
    {
        var canvas = new FrameworkElementFactory(typeof(Canvas));
        canvas.SetValue(Canvas.BackgroundProperty, Brushes.Transparent);
        var corner = new FrameworkElementFactory(typeof(System.Windows.Shapes.Polygon));
        corner.SetValue(System.Windows.Shapes.Polygon.PointsProperty,
            new PointCollection { new(GripSize, 0), new(GripSize, GripSize), new(0, GripSize) });
        corner.SetValue(System.Windows.Shapes.Shape.FillProperty, new SolidColorBrush(Color.FromArgb(0x99, 0x10, 0x10, 0x10)));
        canvas.AppendChild(corner);
        for (int i = 0; i < 3; i++)
        {
            double o = 4 + i * 4;
            var line = new FrameworkElementFactory(typeof(System.Windows.Shapes.Line));
            line.SetValue(System.Windows.Shapes.Line.X1Property, GripSize - 1);
            line.SetValue(System.Windows.Shapes.Line.Y1Property, o);
            line.SetValue(System.Windows.Shapes.Line.X2Property, o);
            line.SetValue(System.Windows.Shapes.Line.Y2Property, GripSize - 1);
            line.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, Services.ThemeManager.KeyAccent);
            line.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.5);
            canvas.AppendChild(line);
        }
        return new ControlTemplate(typeof(Thumb)) { VisualTree = canvas };
    }
}
