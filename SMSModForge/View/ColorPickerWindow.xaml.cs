using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SMSModForge.Services;

namespace SMSModForge.View;

/// <summary>
/// Choosing a colour.
/// <para/>
/// Reported: with the Windows colour dialog, clicking in the gradient did
/// nothing - only the ready-made swatches took effect. That dialog also has no
/// alpha at all, so every pick through it silently kept whatever transparency
/// was already stored and offered no way to change it, while the pack's own
/// format is "#RRGGBBAA" and the game's UI leans on that eighth digit for
/// faded and disabled states.
/// <para/>
/// So this is ours: a saturation/brightness field, a hue bar, an alpha bar, and
/// a hex box, each of which writes to the same place and reads back from it.
/// Press and drag both work on all three bars, because a colour is chosen by
/// sliding around until it looks right, not by landing a single click.
/// </summary>
public partial class ColorPickerWindow : Window
{
    private double _h;             // 0-360
    private double _s, _v;         // 0-1
    private byte _a = 255;

    /// <summary>Set while the boxes are being written from the wheel, so their
    /// TextChanged does not turn round and re-parse what it just printed -
    /// which rounds the hue away as soon as the colour is a grey.</summary>
    private bool _echoing;

    internal ColorPickerWindow() => InitializeComponent();

    /// <summary>The chosen colour as "#RRGGBBAA", or null if cancelled.</summary>
    public string? Chosen { get; private set; }

    /// <summary>
    /// Open on <paramref name="current"/> and return what was chosen, or null
    /// when cancelled - so a cancel leaves the object exactly as it was rather
    /// than writing back the colour it opened on.
    /// </summary>
    public static string? Pick(Window owner, string? current)
    {
        var w = new ColorPickerWindow { Owner = owner };
        w.Seed(current);
        return w.ShowDialog() == true ? w.Chosen : null;
    }

    /// <summary>Open on a colour. Anything unreadable starts from white rather
    /// than from transparent black, which is what an empty field parses as and
    /// is a colour nobody chose.</summary>
    internal void Seed(string? current)
    {
        if (!ColorMath.TryParse(current, out byte r, out byte g, out byte b, out byte a))
        { r = g = b = 255; a = 255; }

        (_h, _s, _v) = ColorMath.ToHsv(r, g, b);
        _a = a;

        Chequer.Fill = Chequerboard();
        AlphaChequer.Fill = Chequerboard();
        WasSwatch.Fill = new SolidColorBrush(Color.FromArgb(a, r, g, b));

        Loaded += (_, _) => Redraw();
    }

    /// <summary>What the wheel says right now, as the pack would store it.</summary>
    internal string Current => Hex();

    // ── What the wheel currently says ────────────────────────────────

    private (byte R, byte G, byte B) Rgb() => ColorMath.FromHsv(_h, _s, _v);

    private string Hex()
    {
        var (r, g, b) = Rgb();
        return ColorMath.ToHex(r, g, b, _a);
    }

    /// <summary>Put every part of the window in step with h/s/v/a. One place,
    /// called after every change, so no control can be left showing something
    /// the others disagree with.</summary>
    private void Redraw()
    {
        var (r, g, b) = Rgb();
        var (pr, pg, pb) = ColorMath.FromHsv(_h, 1, 1);      // the hue at full strength

        FieldHue.Fill = new SolidColorBrush(Color.FromRgb(pr, pg, pb));
        NowSwatch.Fill = new SolidColorBrush(Color.FromArgb(_a, r, g, b));

        AlphaRamp.Fill = new LinearGradientBrush(
            Color.FromArgb(0, r, g, b), Color.FromArgb(255, r, g, b),
            new Point(0, 0), new Point(0, 1));

        Place(Marker, Field, _s, 1 - _v);
        Place(MarkerInner, Field, _s, 1 - _v);
        PlaceBar(HueMarker, HueBar, _h / 360.0);
        PlaceBar(AlphaMarker, AlphaBar, 1 - _a / 255.0);

        _echoing = true;
        HexBox.Text = Hex();
        AlphaBox.Text = _a.ToString();
        _echoing = false;
    }

    private static void Place(Shape dot, Canvas on, double fx, double fy)
    {
        Canvas.SetLeft(dot, fx * on.ActualWidth - dot.Width / 2);
        Canvas.SetTop(dot, fy * on.ActualHeight - dot.Height / 2);
    }

    private static void PlaceBar(Shape bar, Canvas on, double fy)
        => Canvas.SetTop(bar, fy * on.ActualHeight - bar.Height / 2);

    /// <summary>Grey squares, so a partly transparent colour reads as
    /// transparent rather than as a paler shade of itself.</summary>
    private static Brush Chequerboard()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White,
            null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        var grey = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        group.Children.Add(new GeometryDrawing(grey, null,
            new RectangleGeometry(new Rect(0, 0, 6, 6))));
        group.Children.Add(new GeometryDrawing(grey, null,
            new RectangleGeometry(new Rect(6, 6, 6, 6))));

        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 12, 12),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
    }

    // ── Dragging ─────────────────────────────────────────────────────
    //
    // Press, move and release on each of the three. Capturing the mouse means a
    // drag that wanders off the control keeps working and clamps at the edge,
    // which is how you reach pure white or full opacity without landing exactly
    // on the last pixel.

    private static double Fraction(double at, double over)
        => over <= 0 ? 0 : Math.Max(0, Math.Min(1, at / over));

    private void Field_Down(object sender, MouseButtonEventArgs e)
    {
        Field.CaptureMouse();
        FieldAt(e.GetPosition(Field));
    }

    private void Field_Move(object sender, MouseEventArgs e)
    {
        if (Field.IsMouseCaptured) FieldAt(e.GetPosition(Field));
    }

    private void Field_Up(object sender, MouseButtonEventArgs e) => Field.ReleaseMouseCapture();

    internal void FieldAt(Point p)
    {
        _s = Fraction(p.X, Field.ActualWidth);
        _v = 1 - Fraction(p.Y, Field.ActualHeight);
        Redraw();
    }

    private void Hue_Down(object sender, MouseButtonEventArgs e)
    {
        HueBar.CaptureMouse();
        HueAt(e.GetPosition(HueBar));
    }

    private void Hue_Move(object sender, MouseEventArgs e)
    {
        if (HueBar.IsMouseCaptured) HueAt(e.GetPosition(HueBar));
    }

    private void Hue_Up(object sender, MouseButtonEventArgs e) => HueBar.ReleaseMouseCapture();

    internal void HueAt(Point p)
    {
        _h = Fraction(p.Y, HueBar.ActualHeight) * 360;
        Redraw();
    }

    private void Alpha_Down(object sender, MouseButtonEventArgs e)
    {
        AlphaBar.CaptureMouse();
        AlphaAt(e.GetPosition(AlphaBar));
    }

    private void Alpha_Move(object sender, MouseEventArgs e)
    {
        if (AlphaBar.IsMouseCaptured) AlphaAt(e.GetPosition(AlphaBar));
    }

    private void Alpha_Up(object sender, MouseButtonEventArgs e) => AlphaBar.ReleaseMouseCapture();

    internal void AlphaAt(Point p)
    {
        _a = (byte)Math.Round((1 - Fraction(p.Y, AlphaBar.ActualHeight)) * 255);
        Redraw();
    }

    // ── Typing ───────────────────────────────────────────────────────

    private void Hex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_echoing) return;
        if (!ColorMath.TryParse(HexBox.Text, out byte r, out byte g, out byte b, out byte a)) return;

        (_h, _s, _v) = ColorMath.ToHsv(r, g, b);
        _a = a;

        // Not through Redraw: rewriting the box under the caret while it is
        // being typed in moves the caret to the end after every character.
        var text = HexBox.Text;
        Redraw();
        _echoing = true;
        HexBox.Text = text;
        HexBox.CaretIndex = text.Length;
        _echoing = false;
    }

    private void Alpha_Changed(object sender, TextChangedEventArgs e)
    {
        if (_echoing) return;
        if (!byte.TryParse(AlphaBox.Text, out byte a)) return;

        _a = a;
        var text = AlphaBox.Text;
        Redraw();
        _echoing = true;
        AlphaBox.Text = text;
        AlphaBox.CaretIndex = text.Length;
        _echoing = false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Chosen = Hex();
        DialogResult = true;
    }
}
