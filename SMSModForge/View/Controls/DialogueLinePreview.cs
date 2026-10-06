using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Rendering;

namespace SMSModForge.View.Controls;

/// <summary>
/// A line of dialogue drawn the way the game draws it: the game's own font, its
/// black outline and drop shadow, the speaker's name in their colour, on a dark
/// panel.
/// <para/>
/// Not a styled approximation. <see cref="TmpFont"/> holds the game's actual
/// TextMeshPro atlas with its metrics and kerning, and <see cref="UiTextRaster"/>
/// draws from it — the same pair the UI tab uses to show the game rather than an
/// impression of it. Drawing the same typeface through WPF's text stack would
/// land near it and not on it, and the outline and shadow would be absent
/// entirely.
/// <para/>
/// <b>Fixed colours, on purpose.</b> Everything here is the game's palette and
/// none of it follows the editor's theme, because the point is that it looks the
/// same as the game — which does not have a light mode. The row's own chrome
/// (selection, the speaker chip, the changed dot) stays outside the panel and
/// stays themed; the game's bubble holds only the words, and so does this.
/// <para/>
/// <b>What it costs.</b> About 1.1 ms and 36 KB per row to rasterise, measured.
/// So the bitmap is kept until something about the line changes, and a list of
/// two hundred re-rendered on every scroll would be a quarter of a second of
/// work for no reason. WPF's own virtualisation does the rest.
/// </summary>
public sealed class DialogueLinePreview : Control
{
    public DialogueLinePreview()
    {
        // No template: this draws itself. A ControlTemplate would be a second
        // place for the look to live.
        Focusable = false;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;

        // Background is deliberately NOT used: a Control paints it through its
        // ControlTemplate, and this one has none - it draws itself, so there is
        // one place the look lives rather than two.

        // A word added to the dictionary changes the marks; asked again, only
        // while showing, so a row that has gone is not kept alive by it.
        Loaded += (_, _) => { Services.Speller.Changed -= SpellingChanged; Services.Speller.Changed += SpellingChanged; };
        Unloaded += (_, _) => Services.Speller.Changed -= SpellingChanged;

        // So does a character's name colour, which a token is drawn in.
        Loaded += (_, _) => { DialogueMarkup.TokenColorsChanged -= SpellingChanged; DialogueMarkup.TokenColorsChanged += SpellingChanged; };
        Unloaded += (_, _) => DialogueMarkup.TokenColorsChanged -= SpellingChanged;
    }

    private void SpellingChanged()
    {
        // Said on whichever thread changed the speller, and every thread has
        // its own; this draws on its own thread, so it is asked there.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(SpellingChanged));
            return;
        }
        _cached = null;
        InvalidateVisual();
    }

    // ── What to draw ──────────────────────────────────────────────────

    public static readonly DependencyProperty LineProperty =
        DependencyProperty.Register(nameof(Line), typeof(string), typeof(DialogueLinePreview),
            new FrameworkPropertyMetadata("", Redraw));

    /// <summary>The line, markup and all. The tags are applied and not drawn —
    /// this is the player's view, and the player never sees them.</summary>
    public string Line
    {
        get => (string)GetValue(LineProperty) ?? "";
        set => SetValue(LineProperty, value ?? "");
    }

    public static readonly DependencyProperty SpeakerProperty =
        DependencyProperty.Register(nameof(Speaker), typeof(string), typeof(DialogueLinePreview),
            new FrameworkPropertyMetadata("", Redraw));

    /// <summary>Who is speaking, with no brackets: the game shows the name
    /// itself, larger than the line and in the character's own colour.</summary>
    public string Speaker
    {
        get => (string)GetValue(SpeakerProperty) ?? "";
        set => SetValue(SpeakerProperty, value ?? "");
    }

    public static readonly DependencyProperty SpeakerColorProperty =
        DependencyProperty.Register(nameof(SpeakerColor), typeof(string),
            typeof(DialogueLinePreview), new FrameworkPropertyMetadata("", Redraw));

    /// <summary>The speaker's name colour as <c>#RRGGBB</c>, or empty for the
    /// ordinary one. The same colour the game paints their name.</summary>
    public string SpeakerColor
    {
        get => (string)GetValue(SpeakerColorProperty) ?? "";
        set => SetValue(SpeakerColorProperty, value ?? "");
    }

    public static readonly DependencyProperty ChecksSpellingProperty =
        DependencyProperty.Register(nameof(ChecksSpelling), typeof(bool),
            typeof(DialogueLinePreview), new FrameworkPropertyMetadata(false, Redraw));

    /// <summary>
    /// Underline the misspelled words, as the editing box below the list does.
    /// <para/>
    /// Follows the same Options switch as the box, because they are one answer
    /// to one question: an author who turned spell checking off did not mean
    /// "off in the box and on in the list".
    /// </summary>
    public bool ChecksSpelling
    {
        get => (bool)GetValue(ChecksSpellingProperty);
        set => SetValue(ChecksSpellingProperty, value);
    }

    private static void Redraw(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var it = (DialogueLinePreview)d;
        it._cached = null;
        it.InvalidateMeasure();
        it.InvalidateVisual();
    }

    // ── Drawing ───────────────────────────────────────────────────────

    private BitmapSource? _cached;
    private double _cachedWidth;

    public static readonly DependencyProperty RowHeightProperty =
        DependencyProperty.Register(nameof(RowHeight), typeof(double),
            typeof(DialogueLinePreview),
            new FrameworkPropertyMetadata(DialogueLook.MaxRowHeight, Redraw));

    /// <summary>The tallest ONE LINE of this row may be. The text is scaled to
    /// suit, which is the whole reason the size is not taken from the width; a
    /// line too long for the column wraps and costs the row another of these.</summary>
    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    /// <summary>
    /// The panel, parsed once. Not from the theme: see the type doc.
    /// <para/>
    /// Public because the LIST behind these rows is painted the same colour, and
    /// two places naming the same grey is how they end up being two greys. XAML
    /// binds this with x:Static.
    /// </summary>
    public static readonly Brush Panel = Frozen(DialogueLook.PanelHex);

    /// <summary>What sits ON the panel: the row's own glyphs and chips, which
    /// are theme-coloured everywhere else and would be invisible here.</summary>
    public static readonly Brush OnPanel = Frozen(DialogueLook.OnPanelHex);

    /// <summary>The nesting guides beside these rows, for the same reason: the
    /// theme's own line colour is a coin toss against a fixed dark panel.</summary>
    public static readonly Brush Rail = Frozen(DialogueLook.RailHex);

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 400 : available.Width;
        var bitmap = Bitmap(width);
        return new Size(width, bitmap?.Height ?? MinHeight);
    }

    /// <summary>
    /// Draw again when the row changes width.
    /// <para/>
    /// WPF keeps the drawing from the last OnRender and does not ask for
    /// another just because the control was arranged differently, so without
    /// this a control that grew kept a panel the width it used to be - measured
    /// as a bare 32-pixel strip down the side of a 420-wide row. The rendered
    /// line is width-dependent too, so both want redoing.
    /// </summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (info.WidthChanged) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // RenderSize, not ActualWidth: this is the size being drawn INTO, and
        // the two are the same only once layout has settled.
        double width = RenderSize.Width;
        if (width <= 1) return;

        // The panel fills the slot, whatever the slot is. Painting it only as
        // tall as the words leaves bare space wherever the row is stretched
        // past its desired height, which a list row is whenever another row in
        // view is taller.
        dc.DrawRectangle(Panel, null, new Rect(0, 0, width, RenderSize.Height));

        var bitmap = Bitmap(width);
        if (bitmap != null)
            dc.DrawImage(bitmap, new Rect(0, 0, bitmap.Width, bitmap.Height));
    }

    /// <summary>
    /// A little room around the glyphs, so a descender is not flush against the
    /// panel edge.
    /// <para/>
    /// Taken OUT of the budget before the text is sized rather than added after
    /// it: added after, every row came out two pixels past the cap, which is a
    /// cap that does not cap anything.
    /// </summary>
    private const int Breathing = 2;

    /// <summary>The rendered line, kept until the line or the width changes.</summary>
    private BitmapSource? Bitmap(double width)
    {
        if (_cached != null && Math.Abs(width - _cachedWidth) < 0.5) return _cached;

        var made = Compose(width);
        _cached = made;
        _cachedWidth = width;
        return made;
    }

    /// <summary>
    /// Lay out the speaker and the line and draw both into one buffer.
    /// <para/>
    /// Sized from the row HEIGHT, not from the width: see
    /// <see cref="DialogueLook.MaxRowHeight"/> for why, and for what that costs.
    /// The width is what the line wraps at, so a row grows a line at a time
    /// rather than cutting the sentence off at the column edge.
    /// </summary>
    private BitmapSource? Compose(double width)
    {
        if (!VanillaUiLibrary.IsAvailable) return null;

        var set = VanillaUiLibrary.Assets.Font(DialogueLook.FontName);
        if (set?.Font == null) return null;

        // The speaker and the words share a line, rather than the name sitting
        // over them the way the game stacks it. Stacking spends 62 of every 100
        // units of a capped row on the name alone, which leaves the words too
        // small to scan - and scanning is what a list is for. So the name is
        // typed at the same size as the line and put in front of it.
        bool named = !string.IsNullOrEmpty(Speaker);
        double size = DialogueLook.BodySizeToFit(
            set.Font.Face.LineHeight * set.Font.ScaleFor(set.Font.Face.PointSize),
            set.Font.Face.PointSize, withName: false, budget: RowHeight - Breathing);

        // The line, split into the pieces the markup makes of it. The tags
        // themselves are not in it: the player never sees those.
        var pieces = Pieces(Line);

        // A margin either side: the left one is what the whole row is offset by,
        // and the right one is only there so a wrapped line does not end flush
        // against the edge it just broke at.
        double inset = size * DialogueLook.Inset;
        double column = width - inset * 2;

        // The name is never wrapped — it is one short thing at the head of the
        // line, and a character whose name does not fit the column has bigger
        // problems than this row.
        var name = named
            ? TmpTextLayout.Measure(set.Font, Speaker, size, 0, DialogueLook.Align)
            : null;

        // The line wraps at the column, and its first line starts after the
        // name. Told to the layout rather than applied afterwards, because a
        // shift after the fact would move the text over the edge it was
        // measured to fit inside.
        //
        // One measure over all the pieces, not one per piece: a <size=70%> in
        // the middle of a sentence has to break the line where the SENTENCE
        // runs out, and kerning has to carry across the join.
        double after = name == null ? 0 : name.Width + size * NameGap;
        var body = TmpTextLayout.MeasureRuns(
            set.Font, Runs(pieces), size, column, DialogueLook.Align,
            firstLineIndent: after);

        // Device pixels, not layout units. The bitmap was built one pixel per
        // unit and then drawn into however many device pixels the display
        // actually has - which on anything but 100% scaling is a resample, and
        // a resampled glyph is a soft one.
        double scale = Dpi();
        int pixels = (int)Math.Ceiling(width * scale);
        double tall = Math.Max(name?.Height ?? 0, body.Height) + Breathing;
        int height = (int)Math.Ceiling(tall * scale);
        if (pixels <= 0 || height <= 0) return null;

        var target = UiCompositor.NewLayer(pixels, height);

        if (name != null)
        {
            UiTextRaster.DrawInto(target, pixels, height, Scaled(Shift(name, inset, 0), scale),
                                  set.Font, set.Alpha, set.Width, set.Height, NameColor());
        }

        var placed = Scaled(Shift(body, inset, 0), scale);
        DrawStyled(target, pixels, height, placed, set, pieces);

        // After the words, so a mark sits over the line rather than under it.
        if (ChecksSpelling && !DrawSpelling(target, pixels, height, placed, pieces, scale, out string words))
            CheckSoon(words);

        // The DPI stamped on the bitmap is what makes one of its pixels land on
        // one of the screen's rather than being stretched to fit.
        var bmp = BitmapSource.Create(pixels, height, 96 * scale, 96 * scale,
                                      PixelFormats.Pbgra32, null, target, pixels * 4);
        bmp.Freeze();
        return bmp;
    }

    // ── The line, in pieces ────────────────────────────────────

    /// <summary>One stretch of the line and how it is to be drawn.</summary>
    private readonly record struct Piece(string Text, DialogueMarkup.Style Style, bool Token);

    /// <summary>
    /// The words the player reads, split where the styling changes.
    /// <para/>
    /// The tags are dropped — the player never sees them — but what they ask
    /// for is kept and applied. A braced token is kept as well, and marked: the
    /// player will not see the braces either, but unlike a tag it leaves
    /// something behind, and a row that showed it as ordinary words would not
    /// say that a name goes there.
    /// </summary>
    private static List<Piece> Pieces(string line)
    {
        var pieces = new List<Piece>();
        foreach (var span in DialogueMarkup.Parse(line))
        {
            if (span.IsTag) continue;
            pieces.Add(new Piece(span.Of(line), span.Style, span.IsToken));
        }
        return pieces;
    }

    /// <summary>The pieces as the layout wants them: text, the size it asks
    /// for, and an index back to the piece it came from.</summary>
    private static StyledRun[] Runs(List<Piece> pieces)
    {
        var runs = new StyledRun[pieces.Count];
        for (int i = 0; i < pieces.Count; i++)
            runs[i] = new StyledRun(pieces[i].Text, pieces[i].Style.Scale, i);
        return runs;
    }

    /// <summary>
    /// Draw the line, one stretch at a time, each in its own colour and weight.
    /// <para/>
    /// Grouped by the piece a glyph came from, because the rasteriser draws one
    /// colour and one weight per pass — and a laid-out line has already been
    /// measured as a whole, so splitting it here costs nothing but the passes.
    /// </summary>
    private static void DrawStyled(byte[] target, int width, int height,
                                   TextLayout body, UiFontSet set,
                                   List<Piece> pieces)
    {
        int at = 0;
        while (at < body.Glyphs.Count)
        {
            int run = body.Glyphs[at].Run;
            int end = at;
            while (end < body.Glyphs.Count && body.Glyphs[end].Run == run) end++;

            var slice = new TextLayout();
            for (int i = at; i < end; i++) slice.Glyphs.Add(body.Glyphs[i]);

            var piece = run >= 0 && run < pieces.Count ? pieces[run] : default;

            // The chip goes down first, so the glyphs land on top of it.
            if (piece.Token) Chip(target, width, height, slice);

            UiTextRaster.DrawInto(
                target, width, height, slice, set.Font,
                set.Alpha, set.Width, set.Height,
                InkFor(piece),
                faceShift: piece.Style.Bold ? set.Font.BoldShift : -1,
                slant: piece.Style.Italic ? DialogueLook.ItalicSlant : 0);

            at = end;
        }
    }

    /// <summary>What one piece is written in: a token in the name colour of
    /// whoever it stands in for, or the token mark; otherwise the colour its
    /// markup asked for, or the ordinary white.</summary>
    private static UiColor InkFor(Piece piece)
    {
        if (piece.Token) return DialogueMarkup.TokenColor?.Invoke(piece.Text) ?? DialogueLook.TokenColor;

        // The game's reading of the value, shared with the Text box so the two
        // cannot disagree - see TmpColor. A fully transparent colour is drawn
        // in the ordinary one, here and there: the game would show nothing, and
        // a line that vanishes from the editor cannot be read or fixed.
        return TmpColor.TryParse(piece.Style.Color, out var asked) && asked.A > 0
            ? asked
            : DialogueLook.BodyColor;
    }

    /// <summary>
    /// A filled rectangle behind a token, one per line it sits on.
    /// <para/>
    /// Around the glyphs rather than around their ink: a token drawn tight to
    /// its letters would have a chip that changed shape with the word inside
    /// it, so the box is the line's own height.
    /// </summary>
    private static void Chip(byte[] target, int width, int height, TextLayout slice)
    {
        if (slice.Glyphs.Count == 0) return;

        var colour = DialogueLook.TokenChipColor;
        if (colour.A == 0) return;

        int line = slice.Glyphs[0].Line;
        double left = double.MaxValue, right = double.MinValue;
        double top = double.MaxValue, bottom = double.MinValue;

        void Flush()
        {
            if (right <= left) return;
            FillRect(target, width, height, left - Pad, top - Pad, right + Pad, bottom + Pad,
                     colour);
            left = top = double.MaxValue;
            right = bottom = double.MinValue;
        }

        foreach (var glyph in slice.Glyphs)
        {
            if (glyph.Line != line) { Flush(); line = glyph.Line; }
            left = Math.Min(left, glyph.X);
            right = Math.Max(right, glyph.Right);
            top = Math.Min(top, glyph.Y);
            bottom = Math.Max(bottom, glyph.Bottom);
        }
        Flush();
    }

    /// <summary>How far the chip stands off the token's ink, in device
    /// pixels.</summary>
    private const double Pad = 1.5;

    /// <summary>A solid rectangle, premultiplied, over whatever is there.</summary>
    // ── Spelling ───────────────────────────────────────────────────

    /// <summary>
    /// Underline the misspelled words, the way the editing box below the list
    /// does.
    /// <para/>
    /// This row is not a TextBox — it is the game's own glyph atlas, blitted —
    /// so WPF cannot squiggle it and the marks have to be drawn. Which words
    /// still comes from Windows' speller and the author's own dictionary
    /// (<see cref="Services.Speller"/>), so a word added there stops being
    /// underlined here as well as in the box.
    /// <para/>
    /// The text checked is what the row SHOWS: the pieces joined, tags already
    /// dropped. Checking the raw line would hand the speller <c>&lt;color=#FF6666&gt;</c>
    /// and get back three misspellings nobody can act on.
    /// </summary>
    /// <returns>False when the words have not been checked yet, and
    /// <paramref name="shown"/> is what to check: nothing is drawn for them
    /// now, and the row is drawn again once they have been.</returns>
    private static bool DrawSpelling(byte[] target, int width, int height,
                                     TextLayout body, List<Piece> pieces, double scale, out string shown)
    {
        var joined = new System.Text.StringBuilder();
        foreach (var piece in pieces) joined.Append(piece.Text);
        shown = joined.ToString();
        if (!Services.Speller.TryKnown(shown, out var bad)) return false;
        if (bad.Count == 0) return true;

        foreach (var word in bad)
        {
            // The glyphs of this word, which is where the index carried
            // through the layout earns its keep: wrapping drops the space it
            // broke at and the atlas may have no glyph for a character, so
            // counting glyphs would drift a word to the left of itself.
            int line = int.MinValue;
            double left = double.MaxValue, right = double.MinValue, bottom = double.MinValue;

            void Flush()
            {
                if (right > left) Squiggle(target, width, height, left, right, bottom, scale);
                left = double.MaxValue;
                right = bottom = double.MinValue;
            }

            foreach (var glyph in body.Glyphs)
            {
                if (glyph.Index < word.At || glyph.Index >= word.At + word.Length) continue;

                // A word split across a wrap gets a mark on each line.
                if (line != int.MinValue && glyph.Line != line) Flush();
                line = glyph.Line;

                left = Math.Min(left, glyph.X);
                right = Math.Max(right, glyph.Right);
                bottom = Math.Max(bottom, glyph.Bottom);
            }
            Flush();
        }
        return true;
    }

    /// <summary>The words waiting to be checked for this row, if any.</summary>
    private string? _checking;

    /// <summary>
    /// Check <paramref name="shown"/> once everything waiting to be drawn has
    /// been, and draw the row again with its marks.
    /// <para/>
    /// Not now: asking Windows takes about forty milliseconds a line, and a
    /// conversation of a hundred lines asked in the middle of drawing held the
    /// whole window for four seconds - after every undo, since an undo draws
    /// the list again. Each row now shows at once and its marks follow, one row
    /// at a time behind whatever else the window is doing; a line checked
    /// before is not asked again at all (<see cref="Services.Speller"/>).
    /// </summary>
    private void CheckSoon(string shown)
    {
        if (_checking == shown) return;
        _checking = shown;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (_checking != shown) return;   // the row moved on to other words
            _checking = null;
            Services.Speller.Check(shown);
            _cached = null;
            InvalidateVisual();
        }));
    }

    /// <summary>
    /// A wavy line under one stretch of a row.
    /// <para/>
    /// Two pixels of period at 100% scaling, which is what Windows draws and
    /// what makes it read as a spelling mark rather than an underline. Scaled
    /// with the display so it does not disappear on a high-DPI screen.
    /// </summary>
    private static void Squiggle(byte[] target, int width, int height,
                                 double left, double right, double bottom, double scale)
    {
        var colour = DialogueLook.SpellingColor;
        if (colour.A == 0) return;

        int thickness = Math.Max(1, (int)Math.Round(scale));
        int period = Math.Max(2, (int)Math.Round(2 * scale));
        double top = bottom + thickness;

        for (int x = (int)Math.Floor(left); x < (int)Math.Ceiling(right); x++)
        {
            // Up on one half of the period, down on the other: the cheapest
            // zigzag there is, and at this size a smoother curve is invisible.
            int step = ((x - (int)Math.Floor(left)) / period) % 2;
            double y = top + (step == 0 ? 0 : thickness);
            FillRect(target, width, height, x, y, x + 1, y + thickness, colour);
        }
    }

    private static void FillRect(byte[] target, int width, int height,
                                 double x0, double y0, double x1, double y1, UiColor colour)
    {
        int px0 = Math.Max(0, (int)Math.Floor(x0));
        int py0 = Math.Max(0, (int)Math.Floor(y0));
        int px1 = Math.Min(width, (int)Math.Ceiling(x1));
        int py1 = Math.Min(height, (int)Math.Ceiling(y1));

        double a = colour.A / 255.0;
        byte b = (byte)Math.Round(colour.B * a);
        byte g = (byte)Math.Round(colour.G * a);
        byte r = (byte)Math.Round(colour.R * a);
        byte alpha = colour.A;

        for (int y = py0; y < py1; y++)
        for (int x = px0; x < px1; x++)
        {
            int i = (y * width + x) * 4;
            target[i] = b;
            target[i + 1] = g;
            target[i + 2] = r;
            target[i + 3] = alpha;
        }
    }

    /// <summary>The speaker's own colour, or the ordinary one.</summary>
    private UiColor NameColor()
        => string.IsNullOrWhiteSpace(SpeakerColor)
            ? DialogueLook.NameFallbackColor
            : UiColor.Parse(SpeakerColor);

    /// <summary>The gap between the speaker and the words, as a share of the
    /// text size, so it stays proportional at any row height.</summary>
    private const double NameGap = 0.55;

    /// <summary>
    /// How many device pixels there are to a layout unit.
    /// <para/>
    /// 1 at 100% display scaling and 1.5 at 150%, where a bitmap built one
    /// pixel per unit is stretched by half again on its way to the screen.
    /// That stretch is what made the letters look soft.
    /// </summary>
    private double Dpi()
    {
        try
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return scale > 0.1 && scale < 10 ? scale : 1.0;
        }
        catch (InvalidOperationException) { return 1.0; }   // no visual tree yet
    }

    /// <summary>The same glyphs at a different scale, for drawing into a buffer
    /// measured in device pixels while the layout is in layout units.</summary>
    private static TextLayout Scaled(TextLayout layout, double scale)
    {
        if (Math.Abs(scale - 1) < 0.001) return layout;

        var made = new TextLayout { Height = layout.Height * scale };
        foreach (double w in layout.LineWidths) made.LineWidths.Add(w * scale);
        foreach (var g in layout.Glyphs)
            made.Glyphs.Add(g with
            {
                X = g.X * scale,
                Y = g.Y * scale,
                Width = g.Width * scale,
                Height = g.Height * scale,
            });
        foreach (int m in layout.Missing) made.Missing.Add(m);
        return made;
    }

    /// <summary>The same glyphs, moved. The layout puts everything relative to
    /// its own box, and two of them share one buffer here.</summary>
    private static TextLayout Shift(TextLayout layout, double dx, double dy)
    {
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001) return layout;

        var moved = new TextLayout { Height = layout.Height };
        foreach (double w in layout.LineWidths) moved.LineWidths.Add(w);
        foreach (var g in layout.Glyphs)
            moved.Glyphs.Add(g with { X = g.X + dx, Y = g.Y + dy });
        foreach (int m in layout.Missing) moved.Missing.Add(m);
        return moved;
    }
}
