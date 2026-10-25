using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SMSModForge.View.Controls;

/// <summary>
/// One of <see cref="Model.ExpressionIcons"/>, drawn: a round face in lines,
/// in the text colour around it, so it follows every theme and stays sharp at
/// any size. The faces are built from a few parts - eyes, brows, a mouth, and
/// a tear, a drop of sweat, a blush or a "z" - so they read as one set.
/// <para/>
/// Drawn on a 24-unit square, scaled to whatever the control is given.
/// </summary>
public sealed class ExpressionIconView : FrameworkElement
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(ExpressionIconView),
        new FrameworkPropertyMetadata("neutral", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(ExpressionIconView),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override Size MeasureOverride(Size available)
    {
        double side = double.IsInfinity(available.Width) && double.IsInfinity(available.Height) ? 24
                    : Math.Min(double.IsInfinity(available.Width) ? available.Height : available.Width,
                               double.IsInfinity(available.Height) ? available.Width : available.Height);
        return new Size(side, side);
    }

    private Pen _pen = null!;
    private Brush _ink = null!;

    protected override void OnRender(DrawingContext dc)
    {
        double side = Math.Min(ActualWidth, ActualHeight);
        if (side <= 0) return;
        _ink = Foreground ?? Brushes.Black;
        _pen = new Pen(_ink, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        _pen.Freeze();

        dc.PushTransform(new TranslateTransform((ActualWidth - side) / 2, (ActualHeight - side) / 2));
        dc.PushTransform(new ScaleTransform(side / 24, side / 24));
        dc.DrawEllipse(null, _pen, new Point(12, 12), 10.5, 10.5);
        Draw(dc, Icon ?? "neutral");
        dc.Pop();
        dc.Pop();
    }

    private const double LX = 8.5, RX = 15.5, EY = 10;

    private void Draw(DrawingContext dc, string icon)
    {
        switch (icon)
        {
            case "happy": Dots(dc); Smile(dc, 4); break;
            case "smile": ClosedUp(dc, LX); ClosedUp(dc, RX); Smile(dc, 3); break;
            case "laugh": ClosedUp(dc, LX); ClosedUp(dc, RX); BigSmile(dc); break;
            case "love": Heart(dc, LX); Heart(dc, RX); Smile(dc, 4); break;
            case "flirty": Dot(dc, LX); ClosedUp(dc, RX); Smirk(dc); break;
            case "smug": Lidded(dc, LX); Lidded(dc, RX); Smirk(dc); break;
            case "talk": Dots(dc); dc.DrawEllipse(_ink, null, new Point(12, 16.3), 2.4, 1.7); break;
            case "sad": Dots(dc); Brows(dc, sad: true); Frown(dc); break;
            case "cry": ClosedDown(dc, LX); ClosedDown(dc, RX); Tear(dc, LX - 0.5, 13.2); Tear(dc, RX + 0.5, 13.2); Frown(dc); break;
            case "angry": Dots(dc); Brows(dc, sad: false); Line(dc, 9, 16.5, 15, 16.5); break;
            case "furious": Dots(dc); Brows(dc, sad: false, steep: true); Teeth(dc); break;
            case "pout": Dots(dc); Brows(dc, sad: false); Pout(dc); break;
            case "disgusted": Line(dc, LX - 2, EY, LX + 2, EY); Line(dc, RX - 2, EY, RX + 2, EY); Brows(dc, sad: false); Wavy(dc); break;
            case "surprised": Big(dc, LX); Big(dc, RX); Line(dc, 6.5, 5.6, 10.5, 5.6); Line(dc, 13.5, 5.6, 17.5, 5.6);
                dc.DrawEllipse(null, _pen, new Point(12, 16.4), 1.8, 2.1); break;
            case "shocked": Big(dc, LX); Big(dc, RX); dc.DrawEllipse(null, _pen, new Point(12, 16.6), 2.5, 2.9); Sweat(dc); break;
            case "scared": Big(dc, LX); Big(dc, RX); Brows(dc, sad: true); Wavy(dc); break;
            case "nervous": Dots(dc); Wavy(dc); Sweat(dc); break;
            case "embarrassed": Dots(dc); Smile(dc, 2.5); Blush(dc); break;
            case "shy": ClosedDown(dc, LX); ClosedDown(dc, RX); Line(dc, 10.8, 16.3, 13.2, 16.3); Blush(dc); break;
            case "confused": Dots(dc); Line(dc, 6.5, 6.4, 10.5, 6.4); Line(dc, 13.5, 6.2, 17.5, 7.4); Line(dc, 9, 16.8, 15, 15.4); break;
            case "thinking": Dot(dc, LX - 0.8, EY - 1); Dot(dc, RX - 0.8, EY - 1); Line(dc, 12, 16.2, 15, 16.2); break;
            case "determined": Dots(dc); Brows(dc, sad: false); Line(dc, 9, 16.2, 15, 16.2); break;
            case "bored": Lidded(dc, LX); Lidded(dc, RX); Line(dc, 9.5, 16.2, 14.5, 16.2); break;
            case "sleepy": Line(dc, LX - 2, EY + 0.5, LX + 2, EY + 0.5); Line(dc, RX - 2, EY + 0.5, RX + 2, EY + 0.5);
                dc.DrawEllipse(null, _pen, new Point(12, 16.3), 1.3, 1.5); Zee(dc); break;
            case "pained": Squint(dc, LX, right: false); Squint(dc, RX, right: true); Zigzag(dc); break;
            default: Dots(dc); Line(dc, 9, 16, 15, 16); break;   // neutral
        }
    }

    // ── Parts ──

    private void Line(DrawingContext dc, double x1, double y1, double x2, double y2)
        => dc.DrawLine(_pen, new Point(x1, y1), new Point(x2, y2));

    private void Path(DrawingContext dc, string data, bool fill = false)
        => dc.DrawGeometry(fill ? _ink : null, fill ? null : _pen, Geometry.Parse(data));

    private void Dot(DrawingContext dc, double x, double y = EY) => dc.DrawEllipse(_ink, null, new Point(x, y), 1.35, 1.35);
    private void Dots(DrawingContext dc) { Dot(dc, LX); Dot(dc, RX); }

    private void Big(DrawingContext dc, double x)
    {
        dc.DrawEllipse(null, _pen, new Point(x, EY), 2.2, 2.2);
        dc.DrawEllipse(_ink, null, new Point(x, EY), 0.8, 0.8);
    }

    private void ClosedUp(DrawingContext dc, double x)
        => Path(dc, $"M {F(x - 2)},{F(EY + 0.8)} Q {F(x)},{F(EY - 1.8)} {F(x + 2)},{F(EY + 0.8)}");

    private void ClosedDown(DrawingContext dc, double x)
        => Path(dc, $"M {F(x - 2)},{F(EY - 0.4)} Q {F(x)},{F(EY + 1.8)} {F(x + 2)},{F(EY - 0.4)}");

    private void Lidded(DrawingContext dc, double x)
    {
        Line(dc, x - 2.1, EY - 0.4, x + 2.1, EY - 0.4);
        Path(dc, $"M {F(x - 1.3)},{F(EY - 0.4)} A 1.3,1.3 0 0 0 {F(x + 1.3)},{F(EY - 0.4)} Z", fill: true);
    }

    private void Squint(DrawingContext dc, double x, bool right)
    {
        double d = right ? -1 : 1;
        Path(dc, $"M {F(x - 1.6 * d)},{F(EY - 1.6)} L {F(x + 1.6 * d)},{F(EY)} L {F(x - 1.6 * d)},{F(EY + 1.6)}");
    }

    private void Heart(DrawingContext dc, double x)
        => Path(dc, $"M {F(x)},{F(EY + 2)} C {F(x - 3.4)},{F(EY - 0.4)} {F(x - 1.6)},{F(EY - 3)} {F(x)},{F(EY - 1.1)} " +
                    $"C {F(x + 1.6)},{F(EY - 3)} {F(x + 3.4)},{F(EY - 0.4)} {F(x)},{F(EY + 2)} Z", fill: true);

    private void Brows(DrawingContext dc, bool sad, bool steep = false)
    {
        double lift = steep ? 2.2 : 1.4;
        if (sad)
        {
            Line(dc, 6.3, 6.8, 10.3, 6.8 - lift);
            Line(dc, 13.7, 6.8 - lift, 17.7, 6.8);
        }
        else
        {
            Line(dc, 6.3, 5.8, 10.3, 5.8 + lift);
            Line(dc, 13.7, 5.8 + lift, 17.7, 5.8);
        }
    }

    private void Smile(DrawingContext dc, double depth)
        => Path(dc, $"M 8,{F(15)} Q 12,{F(15 + depth)} 16,{F(15)}");

    private void BigSmile(DrawingContext dc) => Path(dc, "M 7.5,14.3 Q 12,21.5 16.5,14.3 Z", fill: true);

    private void Frown(DrawingContext dc) => Path(dc, "M 8.5,17.6 Q 12,13.6 15.5,17.6");

    private void Smirk(DrawingContext dc) => Path(dc, "M 9,16.3 Q 13,17.4 15.8,14.6");

    private void Wavy(DrawingContext dc) => Path(dc, "M 8,16.4 Q 9,15.2 10,16.4 T 12,16.4 T 14,16.4 T 16,16.4");

    private void Zigzag(DrawingContext dc) => Path(dc, "M 8,16.6 L 9.6,15.4 L 11.2,16.8 L 12.8,15.4 L 14.4,16.8 L 16,15.4");

    private void Pout(DrawingContext dc) => Path(dc, "M 11.2,15 Q 13.4,15.6 11.2,16.3 Q 13.4,17 11.2,17.6");

    private void Teeth(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(null, _pen, new Rect(8.4, 14.6, 7.2, 3.6), 0.8, 0.8);
        Line(dc, 8.6, 16.4, 15.4, 16.4);
    }

    private void Tear(DrawingContext dc, double x, double y)
        => Path(dc, $"M {F(x)},{F(y)} Q {F(x + 1.4)},{F(y + 2.2)} {F(x)},{F(y + 2.8)} Q {F(x - 1.4)},{F(y + 2.2)} {F(x)},{F(y)} Z", fill: true);

    private void Sweat(DrawingContext dc) => Tear(dc, 19.2, 4.6);

    private void Blush(DrawingContext dc)
    {
        Line(dc, 5.2, 13.6, 6.4, 12.7);
        Line(dc, 6.6, 13.9, 7.8, 13.0);
        Line(dc, 16.2, 13.0, 17.4, 13.9);
        Line(dc, 17.6, 12.7, 18.8, 13.6);
    }

    private void Zee(DrawingContext dc) => Path(dc, "M 17.2,3.4 L 20,3.4 L 17.2,6.2 L 20,6.2");

    private static string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
