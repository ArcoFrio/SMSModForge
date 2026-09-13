using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMSModForge.View.Controls;

/// <summary>
/// The guides down the left of a node row that say what it is inside.
/// <para/>
/// A conversation is a tree shown as a flat list, and depth was said only by
/// how far in a row started. That reads at one level and stops reading at
/// three: two rows indented the same amount are siblings, two rows indented
/// differently are related SOMEHOW, and a row halfway down a long branch is
/// anyone's guess. A line per ancestor answers it — count the lines to the left
/// of a row and you have its depth, and follow one up the list to find the node
/// it belongs to.
/// <para/>
/// <b>Why this is a control and not a margin.</b> It occupies exactly the
/// indent, so the row after it starts where it always did: the guides are drawn
/// in space that was already being left blank. That also means the row itself
/// needs no margin, which is what lets the game-look panel begin at the indent
/// and step in with the tree.
/// <para/>
/// <b>The last line is drawn differently.</b> The one belonging to the node's
/// own parent carries a short arm into the row, so "this hangs off that" is
/// visible without counting anything. The ones further out are the branches it
/// happens to be under, and they are drawn quieter.
/// </summary>
public sealed class NodeDepthRail : Control
{
    public NodeDepthRail()
    {
        Focusable = false;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    public static readonly DependencyProperty DepthProperty =
        DependencyProperty.Register(nameof(Depth), typeof(int), typeof(NodeDepthRail),
            new FrameworkPropertyMetadata(0,
                FrameworkPropertyMetadataOptions.AffectsMeasure
                | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How many nodes this one is inside. 0 is a root, and draws
    /// nothing at all.</summary>
    public int Depth
    {
        get => (int)GetValue(DepthProperty);
        set => SetValue(DepthProperty, value);
    }

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(NodeDepthRail),
            new FrameworkPropertyMetadata(16.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure
                | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How far one level of nesting moves a row in. Must agree with
    /// <c>DialogueNodeViewModel.IndentMargin</c>, and a test says so — two
    /// numbers meaning the same thing is how the guides end up beside the rows
    /// rather than under them.</summary>
    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public static readonly DependencyProperty RailProperty =
        DependencyProperty.Register(nameof(Rail), typeof(Brush), typeof(NodeDepthRail),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>What the guides are drawn in. Passed from outside because the
    /// two row styles answer it differently: the plain rows follow the editor's
    /// theme, and the game-look rows are a fixed palette that does not.</summary>
    public Brush? Rail
    {
        get => (Brush?)GetValue(RailProperty);
        set => SetValue(RailProperty, value);
    }

    /// <summary>How far the arm reaches into the row from its parent's line.</summary>
    private const double Arm = 5;

    /// <summary>How much quieter an ancestor's line is than the parent's. The
    /// row belongs to ONE of them, and the rest are context.</summary>
    private const double Ancestor = 0.45;

    protected override Size MeasureOverride(Size available)
        => new(Math.Max(0, Depth) * Step, 0);

    protected override void OnRender(DrawingContext dc)
    {
        int depth = Depth;
        if (depth <= 0 || Rail == null) return;

        double height = RenderSize.Height;
        if (height <= 0) return;

        // Half a pixel off the grid, so a one-pixel line lands ON a pixel
        // instead of across two of them and coming back grey.
        double dpi = Dpi();
        double hair = 1.0 / dpi;
        double half = hair / 2;

        var quiet = Faded(Rail, Ancestor);
        var pen = new Pen(Rail, hair);
        var quietPen = new Pen(quiet, hair);
        pen.Freeze();
        quietPen.Freeze();

        for (int level = 0; level < depth; level++)
        {
            // Down the middle of the step it occupies, so a guide sits between
            // two rows rather than against the edge of one.
            double x = Math.Round((level * Step + Step / 2) * dpi) / dpi + half;
            bool parent = level == depth - 1;

            dc.DrawLine(parent ? pen : quietPen,
                        new Point(x, 0), new Point(x, height));

            if (!parent) continue;

            // The arm, at the height of the row's first line rather than its
            // middle: a row that wrapped onto three lines would otherwise point
            // at its own second line.
            double y = Math.Round(Math.Min(height / 2, 9) * dpi) / dpi + half;
            dc.DrawLine(pen, new Point(x, y), new Point(x + Arm, y));
        }
    }

    /// <summary>The same colour, softer. Made here rather than asked for, so
    /// the caller names one brush and not two.</summary>
    private static Brush Faded(Brush brush, double amount)
    {
        if (brush is SolidColorBrush solid)
        {
            var made = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(solid.Color.A * amount),
                solid.Color.R, solid.Color.G, solid.Color.B));
            made.Freeze();
            return made;
        }

        var copy = brush.CloneCurrentValue();
        copy.Opacity = brush.Opacity * amount;
        copy.Freeze();
        return copy;
    }

    /// <summary>How many device pixels there are to a layout unit, so a hairline
    /// can be one of the display's pixels rather than one of WPF's units.</summary>
    private double Dpi()
    {
        try
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return scale > 0.1 && scale < 10 ? scale : 1.0;
        }
        catch (InvalidOperationException) { return 1.0; }
    }
}
