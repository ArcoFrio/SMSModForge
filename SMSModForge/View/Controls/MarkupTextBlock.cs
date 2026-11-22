using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.Rendering;

namespace SMSModForge.View.Controls;

/// <summary>What the glow under coloured words reads: the runs a line is drawn in, and when they are drawn anew.</summary>
public interface ILineRuns
{
    IEnumerable<Run> LineRuns { get; }
    event Action? Rendered;
}

/// <summary>
/// A line of dialogue as a row of the Dialogues tab's list shows it (1.10.0): the tags gone, the formatting
/// applied, names as names, the game's colours made to read on the row, a cue's dashed underline, and the
/// speller's marks - the same pieces and colours the line box draws in its row mode
/// (<see cref="MarkupTextBox.ReadableInlines"/>), in a <see cref="TextBlock"/>.
/// <para/>
/// The rows used the line box itself, display-only: a rich text box each, with an editor, a document laid out
/// as a page, and a speller of its own. Measured (RowCostProbe): 3 ms a row, 5 ms with its speller; this, 0.3 to
/// 0.5 ms. Showing a conversation of a hundred lines - picking it, or an undo that goes back to it - took
/// two-thirds of a second, nearly all of it in those boxes. The speller here is the one shared by every row
/// (<see cref="Services.Speller"/>), asked once per line and remembered, after the row is on screen.
/// </summary>
public sealed class MarkupTextBlock : TextBlock, ILineRuns
{
    public static readonly DependencyProperty LineProperty =
        DependencyProperty.Register(nameof(Line), typeof(string), typeof(MarkupTextBlock),
            new FrameworkPropertyMetadata("", (d, _) => ((MarkupTextBlock)d).Draw()));

    /// <summary>The line as the pack holds it, tags and tokens included.</summary>
    public string Line
    {
        get => (string)GetValue(LineProperty) ?? "";
        set => SetValue(LineProperty, value ?? "");
    }

    public static readonly DependencyProperty ChecksSpellingProperty =
        DependencyProperty.Register(nameof(ChecksSpelling), typeof(bool), typeof(MarkupTextBlock),
            new FrameworkPropertyMetadata(false, (d, _) => ((MarkupTextBlock)d).Draw()));

    /// <summary>Whether misspelled words are marked, as the box below the list marks them.</summary>
    public bool ChecksSpelling
    {
        get => (bool)GetValue(ChecksSpellingProperty);
        set => SetValue(ChecksSpellingProperty, value);
    }

    /// <summary>The line has been drawn anew.</summary>
    public event Action? Rendered;

    /// <summary>How many times a line (not an empty one) has been drawn - for tests: a row draws its line once.</summary>
    internal int Draws;

    public IEnumerable<Run> LineRuns => Inlines.OfType<Run>();

    /// <summary>What it was last drawn from, and against.</summary>
    private (string Line, Color Behind, object? Accent, double Size, bool Spelling, int Marks)? _drawn;

    private bool _drawWhenLoaded;
    private string? _asking;

    public MarkupTextBlock()
    {
        TextWrapping = TextWrapping.Wrap;
        ReadableInk.Watch(this, RedrawForInk);
        Loaded += (_, _) =>
        {
            DialogueMarkup.TokenColorsChanged -= Redraw;
            DialogueMarkup.TokenColorsChanged += Redraw;
            Services.Speller.Changed -= Redraw;
            Services.Speller.Changed += Redraw;
            if (_drawWhenLoaded) { _drawWhenLoaded = false; Draw(); }
        };
        Unloaded += (_, _) =>
        {
            DialogueMarkup.TokenColorsChanged -= Redraw;
            Services.Speller.Changed -= Redraw;
        };
    }

    private void Redraw()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Redraw)); return; }
        _drawn = null;
        Draw();
    }

    /// <summary>When what is behind the row may have changed: drawn again only when a colour it is drawn from did.</summary>
    private void RedrawForInk()
    {
        if (_drawn is { } was && was.Line == Line && was.Behind == ReadableInk.Behind(this)
            && Equals(was.Accent, TryFindResource(Services.ThemeManager.KeyAccent)) && was.Size == FontSize)
            return;
        Redraw();
    }

    private void Draw()
    {
        // A row is drawn once it is loaded, with all its settings in place - they arrive one at a time while WPF
        // builds the row, and each would draw it again.
        if (!IsLoaded && TemplatedParent != null) { _drawWhenLoaded = true; return; }
        string line = Line;
        var behind = ReadableInk.Behind(this);
        var pieces = DialogueLinePreview.Pieces(line);
        string shown = string.Concat(pieces.Select(p => p.Text));

        IReadOnlyList<Services.Speller.Bad> bad = Array.Empty<Services.Speller.Bad>();
        if (ChecksSpelling && shown.Length > 0 && !Services.Speller.TryKnown(shown, out bad))
        {
            bad = Array.Empty<Services.Speller.Bad>();
            AskSoon(shown);
        }

        var key = (line, behind, TryFindResource(Services.ThemeManager.KeyAccent), FontSize, ChecksSpelling, bad.Count);
        if (_drawn == key) return;
        _drawn = key;

        Inlines.Clear();
        if (line.Length > 0) Draws++;
        var built = new List<Inline>();
        foreach (var piece in pieces)
            built.AddRange(MarkupTextBox.ReadableInlines(this, FontSize, piece, behind));
        foreach (var inline in bad.Count == 0 ? built : Marked(built, bad))
            Inlines.Add(inline);
        Rendered?.Invoke();
    }

    /// <summary>The words Windows has not been asked about yet, asked about once the row is on screen: forty
    /// milliseconds a line, which in the middle of showing a conversation held the window for seconds.</summary>
    private void AskSoon(string shown)
    {
        if (_asking == shown) return;
        _asking = shown;
        // Asked on the speller's own thread (1.10.0): the row is drawn at once, its marks follow.
        Services.Speller.Ask(shown, bad =>
        {
            if (_asking != shown) return;
            _asking = null;
            // Drawn again only when there is something to mark: a line spelled right is the row it already is.
            if (bad.Count > 0) Redraw();
        });
    }

    /// <summary>The runs, cut where a misspelled word begins and ends, the word under the speller's wavy mark.</summary>
    private static IEnumerable<Inline> Marked(List<Inline> inlines, IReadOnlyList<Services.Speller.Bad> bad)
    {
        int at = 0;
        foreach (var inline in inlines)
        {
            if (inline is not Run run)
            {
                at += inline is LineBreak ? 1 : 0;
                yield return inline;
                continue;
            }
            string text = run.Text;
            int from = 0;
            while (from < text.Length)
            {
                int here = at + from;
                var word = bad.FirstOrDefault(b => b.At + b.Length > here && b.At < at + text.Length);
                bool inside = word.Length > 0 && word.At <= here;
                int to = word.Length == 0 ? text.Length
                       : inside ? Math.Min(text.Length, word.At + word.Length - at)
                       : Math.Min(text.Length, word.At - at);
                if (to <= from) to = text.Length;
                var piece = from == 0 && to == text.Length ? run : CopyOf(run, text.Substring(from, to - from));
                if (inside) piece.TextDecorations = Squiggle;
                yield return piece;
                from = to;
            }
            at += text.Length;
        }
    }

    private static Run CopyOf(Run run, string text) => new(text)
    {
        Foreground = run.Foreground, Background = run.Background, FontWeight = run.FontWeight, FontStyle = run.FontStyle,
        FontSize = run.FontSize, TextDecorations = run.TextDecorations, Tag = run.Tag,
    };

    /// <summary>The speller's wavy red mark, as Windows draws it under a misspelled word.</summary>
    internal static readonly TextDecorationCollection Squiggle = MakeSquiggle();

    private static TextDecorationCollection MakeSquiggle()
    {
        var wave = new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x20, 0x20)), 0.8),
                                       Geometry.Parse("M0,2 L1.5,0.5 L3,2"));
        var tile = new DrawingBrush(wave)
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 3, 2.5), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 3, 2.5), ViewboxUnits = BrushMappingMode.Absolute,
        };
        tile.Freeze();
        var pen = new Pen(tile, 2.5);
        pen.Freeze();
        var squiggle = new TextDecorationCollection
        {
            new TextDecoration(TextDecorationLocation.Underline, pen, 1, TextDecorationUnit.FontRecommended, TextDecorationUnit.Pixel),
        };
        squiggle.Freeze();
        return squiggle;
    }
}
