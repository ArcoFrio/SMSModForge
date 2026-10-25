using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SMSModForge.Services;

namespace SMSModForge.View.Controls;

/// <summary>
/// A very slight glow under the coloured words of a line - words in brackets,
/// the colours a line asks for - each in its own colour, in dark EX themes only
/// (the author, 1.8.0: "add a very slight glow effect as well ... to the text
/// coloring"). Put behind a <see cref="MarkupTextBox"/> showing
/// <see cref="MarkupTextBox.ReadableColours"/>: it draws those words again,
/// where the box laid them out, and blurs them.
/// <para/>
/// Its own element because a glow is an effect, and an effect is a whole
/// element's: on the box it would light every word, in one colour.
/// </summary>
public sealed class TextGlow : FrameworkElement
{
    /// <summary>How strong the glow is: faint, as asked.</summary>
    private const double Strength = 0.55;

    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(nameof(Target), typeof(MarkupTextBox), typeof(TextGlow),
            new FrameworkPropertyMetadata(null, OnTargetChanged));

    /// <summary>The box whose coloured words glow.</summary>
    public MarkupTextBox? Target
    {
        get => (MarkupTextBox?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public TextGlow()
    {
        IsHitTestVisible = false;
        Focusable = false;
        Effect = new BlurEffect { Radius = 5, KernelType = KernelType.Gaussian };
        Opacity = Strength;
        Loaded += (_, _) => { ThemeManager.Changed -= Again; ThemeManager.Changed += Again; Again(); };
        Unloaded += (_, _) => ThemeManager.Changed -= Again;
    }

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var glow = (TextGlow)d;
        if (e.OldValue is MarkupTextBox old)
        {
            old.Rendered -= glow.Again;
            old.SizeChanged -= glow.OnTargetResized;
        }
        if (e.NewValue is MarkupTextBox box)
        {
            box.Rendered += glow.Again;
            box.SizeChanged += glow.OnTargetResized;
        }
        glow.Again();
    }

    private void OnTargetResized(object sender, SizeChangedEventArgs e) => Again();

    /// <summary>Drawn again once the box has laid its words out anew.</summary>
    private void Again() => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(InvalidateVisual));

    protected override void OnRender(DrawingContext dc)
    {
        var box = Target;
        // Dark EX themes only: on a light background a blurred colour under
        // the words reads as a smudge, not as light (the author, 1.8.0).
        if (box == null || !ThemeManager.Current.IsEx || !ThemeManager.Current.IsDark || !box.IsLoaded) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var run in box.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()))
        {
            if (!ReferenceEquals(run.Tag, MarkupTextBox.Glows) || string.IsNullOrEmpty(run.Text)) continue;
            var typeface = new Typeface(run.FontFamily, run.FontStyle, run.FontWeight, run.FontStretch);

            // Line by line, where the box put each line: words that wrap onto
            // the next line glow there, not on past the edge of this one.
            string text = run.Text;
            int from = 0;
            Rect lineStart = Rect.Empty;
            for (int i = 0; i <= text.Length; i++)
            {
                Rect at = i < text.Length
                    ? run.ContentStart.GetPositionAtOffset(i)?.GetCharacterRect(LogicalDirection.Forward) ?? Rect.Empty
                    : Rect.Empty;
                bool newLine = i == text.Length || (!lineStart.IsEmpty && !at.IsEmpty && Math.Abs(at.Top - lineStart.Top) > 1);
                if (newLine && !lineStart.IsEmpty && i > from)
                {
                    var words = new FormattedText(text.Substring(from, i - from), CultureInfo.CurrentUICulture, box.FlowDirection,
                                                  typeface, run.FontSize, run.Foreground, dpi);
                    dc.DrawText(words, box.TranslatePoint(lineStart.TopLeft, this));
                    from = i;
                    lineStart = Rect.Empty;
                }
                if (lineStart.IsEmpty && !at.IsEmpty) lineStart = at;
            }
        }
    }
}
