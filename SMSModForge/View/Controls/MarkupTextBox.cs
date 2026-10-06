using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.Rendering;

namespace SMSModForge.View.Controls;

/// <summary>
/// The dialogue line, shown the way the player will read it while it is being
/// written: the markup set apart in its own colour, and the words it wraps
/// actually bold, italic, coloured or resized.
/// <para/>
/// A line was shown flat, so <c>&lt;b&gt;</c> looked exactly like
/// <c>&lt;bold&gt;</c> — one of which the game acts on and the other of which it
/// prints at the player. The only way to tell them apart was to build the pack
/// and read the line in game.
/// <para/>
/// <b>Why this is a RichTextBox wearing a TextBox's clothes.</b> Showing style
/// inside the text being edited needs a flow document; everything AROUND this
/// control — the two-way binding, the spelling menu built in code-behind, the
/// trick that re-runs the checker after a word is added to the dictionary —
/// was written against a TextBox. So the TextBox surface those call sites use
/// is reproduced here (<see cref="Text"/>, <see cref="CaretIndex"/>,
/// <see cref="GetCharacterIndexFromPoint"/>, <see cref="GetSpellingError"/>)
/// over a RichTextBox underneath, and they carry on unchanged.
/// <para/>
/// <b>What the document is.</b> Exactly one <see cref="Paragraph"/>, holding
/// only <see cref="Run"/>s and <see cref="LineBreak"/>s. Nothing else is ever
/// put in it, which is what makes the mapping between a character offset and a
/// position in the document something this can compute rather than something
/// WPF has to be asked about — and <c>TextRange.Text</c> has its own ideas
/// about newlines that are not worth fighting.
/// <para/>
/// <b>The rule the whole thing rests on.</b> The document is only ever a
/// rendering OF <see cref="Text"/>: what comes back out of it must be exactly
/// what went in. <c>DialogueMarkup.Parse</c> guarantees the spans rebuild the
/// input, and the tests hold both halves — because this is the field an author
/// spends their time in, and a control that eats a character while somebody
/// types is worse than one that shows no formatting at all.
/// </summary>
public sealed class MarkupTextBox : RichTextBox
{
    /// <summary>Guards the two directions from driving each other: setting the
    /// document raises TextChanged, which would write back to Text, which would
    /// rebuild the document.</summary>
    private bool _syncing;

    /// <summary>
    /// What the document was last built from, so an edit that does not change
    /// the styling does not rebuild it.
    /// <para/>
    /// Null means "nothing drawn yet, or what is drawn was drawn under the
    /// other setting of <see cref="HidesTags"/>" — an explicit sentinel rather
    /// than a string no line would be, because there is no such string.
    /// </summary>
    private string? _rendered;

    public MarkupTextBox()
    {
        // Markup is only legible in a document that does not re-space itself
        // between paragraphs.
        Document = new FlowDocument(new Paragraph()) { PagePadding = new Thickness(0) };
        AcceptsReturn = true;
        TextChanged += OnDocumentChanged;

        // The box keeps its own history of the LINE - see "Undo" below - so the
        // RichTextBox's history of the document is switched off. That one
        // recorded every redraw as an edit of its own.
        IsUndoEnabled = false;

        // A token is drawn in its character's name colour, which can change
        // while the line is on screen. Asked only while showing.
        Loaded += (_, _) => { DialogueMarkup.TokenColorsChanged -= TokenColorsChanged; DialogueMarkup.TokenColorsChanged += TokenColorsChanged; };
        Unloaded += (_, _) => DialogueMarkup.TokenColorsChanged -= TokenColorsChanged;

        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            System.Windows.Input.ApplicationCommands.Undo,
            (_, e) => { e.Handled = Undo(); },
            (_, e) => { e.CanExecute = CanUndoLine; e.Handled = CanUndoLine; }));
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            System.Windows.Input.ApplicationCommands.Redo,
            (_, e) => { e.Handled = Redo(); },
            (_, e) => { e.CanExecute = CanRedoLine; e.Handled = CanRedoLine; }));
    }

    // ── The TextBox surface ───────────────────────────────────────────

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarkupTextBox),
            new FrameworkPropertyMetadata("",
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    /// <summary>The line, as plain text. This is what the pack stores; the
    /// styling on screen is derived from it and never the other way round.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty) ?? "";
        set => SetValue(TextProperty, value ?? "");
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (MarkupTextBox)d;
        string text = (string)e.NewValue ?? "";

        // A line arriving from outside - another node selected, the editor's
        // own undo putting the pack back - is a different line as far as this
        // box's history goes. Stepping back into the last one's edits from
        // here would write them onto this one.
        if (!box._syncing && !box._writingOwnEdit) box.ForgetHistory();
        box._last = text;

        box.Render(text);
    }

    public static readonly DependencyProperty HidesTagsProperty =
        DependencyProperty.Register(nameof(HidesTags), typeof(bool), typeof(MarkupTextBox),
            new FrameworkPropertyMetadata(false, OnHidesTagsChanged));

    /// <summary>
    /// Drop the markup from the display entirely, rather than setting it apart
    /// in place — the line as the player will read it, with the formatting
    /// applied and the instructions gone.
    /// <para/>
    /// For the node LIST, where the point is to scan what a conversation says.
    /// A tag shown in place still takes room there, and in a row narrow enough
    /// to clip, four characters of <c>&lt;b&gt;</c> cost real words.
    /// <para/>
    /// This makes the control DISPLAY-ONLY, and that is not a separate setting
    /// because it cannot be one: the document no longer holds the whole line, so
    /// reading it back would truncate the author's text. The write-back is
    /// switched off with it.
    /// </summary>
    public bool HidesTags
    {
        get => (bool)GetValue(HidesTagsProperty);
        set => SetValue(HidesTagsProperty, value);
    }

    private static void OnHidesTagsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (MarkupTextBox)d;
        box._rendered = null;          // whatever is drawn was drawn the other way
        box.Render(box.Text);
    }

    /// <summary>Where the caret is, counted in characters of <see cref="Text"/>.</summary>
    public int CaretIndex
    {
        get => OffsetOf(CaretPosition);
        set { var at = PointerAt(value); if (at != null) CaretPosition = at; }
    }

    /// <summary>
    /// The character under a point, or -1. Reproduces the TextBox method the
    /// spelling menu calls to find the word the pointer is over.
    /// </summary>
    public int GetCharacterIndexFromPoint(Point point, bool snapToText)
    {
        var at = GetPositionFromPoint(point, snapToText);
        return at == null ? -1 : OffsetOf(at);
    }

    /// <summary>The misspelling at a character offset, or null.</summary>
    public SpellingError? GetSpellingError(int characterIndex)
    {
        var at = PointerAt(characterIndex);
        return at == null ? null : GetSpellingError(at);
    }

    /// <summary>Where the misspelling at a character offset begins, or -1. The
    /// pointer usually lands in the MIDDLE of a word, so the caller needs the
    /// error's real extent rather than a slice from where it was clicked.</summary>
    public int GetSpellingErrorStart(int characterIndex)
    {
        var range = ErrorRange(characterIndex);
        return range == null ? -1 : OffsetOf(range.Start);
    }

    /// <summary>How long that misspelling is, or 0.</summary>
    public int GetSpellingErrorLength(int characterIndex)
    {
        var range = ErrorRange(characterIndex);
        return range == null ? 0 : OffsetOf(range.End) - OffsetOf(range.Start);
    }

    /// <summary>The misspelled word's extent. SpellingError itself only offers
    /// the suggestions and the corrections; where the word STARTS and ends is a
    /// separate question, and RichTextBox answers it as a range.</summary>
    private TextRange? ErrorRange(int characterIndex)
    {
        var at = PointerAt(characterIndex);
        return at == null ? null : GetSpellingErrorRange(at);
    }

    /// <summary>Where the selection starts, in characters of <see cref="Text"/>,
    /// and how long it is. The TextBox names, because the call sites are
    /// TextBox-shaped.</summary>
    public int SelectionStart => Math.Min(OffsetOf(Selection.Start), OffsetOf(Selection.End));

    public int SelectionLength => Math.Abs(OffsetOf(Selection.End) - OffsetOf(Selection.Start));

    /// <summary>Select a stretch of <see cref="Text"/>.</summary>
    public void Select(int start, int length)
    {
        var from = PointerAt(start);
        var to = PointerAt(start + length);
        if (from != null && to != null) Selection.Select(from, to);
    }

    // ── Writing markup into the line ────────────────────────

    /// <summary>
    /// Put <paramref name="open"/> and <paramref name="close"/> round the
    /// selected words, or round the caret when nothing is selected.
    /// <para/>
    /// <b>Where "the caret" is when there isn't one.</b> A toolbar button is
    /// pressed with the mouse, and a button that took focus would take the
    /// selection with it — so the buttons are not focusable and the box keeps
    /// both. When the box has never been in focus at all there is no caret to
    /// speak of: WPF reports one at the very start of the document, which is
    /// not where anybody meant, so the tags go at the END of the line instead.
    /// Somewhere predictable beats somewhere arbitrary.
    /// <para/>
    /// Afterwards the same words are selected again — so a second tag can be
    /// put round the same phrase — or the caret sits between the tags, ready
    /// for what goes inside them.
    /// </summary>
    public void Surround(string open, string close)
    {
        if (IsReadOnly) return;

        string text = Text;
        int from, to;

        if (IsKeyboardFocusWithin)
        {
            from = SelectionStart;
            to = from + SelectionLength;
        }
        else from = to = text.Length;

        from = Math.Clamp(from, 0, text.Length);
        to = Math.Clamp(to, from, text.Length);

        string inside = text.Substring(from, to - from);

        // One step of its own, whatever was typed just before it: undoing a
        // tag takes the tag back off, not the tag and the sentence under it.
        Remember(new Step(text, from, to - from));
        _typing = false;
        WriteOwnEdit(text.Substring(0, from) + open + inside + close + text.Substring(to));

        Focus();
        if (inside.Length > 0) Select(from + open.Length, inside.Length);
        else CaretIndex = from + open.Length;
    }

    /// <summary>
    /// Where words a toolbar button puts in go: just after whatever is
    /// selected - never over it - or at the caret, or at the end of the line
    /// when the box has never had the keyboard (see <see cref="Surround"/>).
    /// Read it BEFORE anything takes the keyboard away, a menu included.
    /// </summary>
    public int InsertionPoint => IsKeyboardFocusWithin ? SelectionStart + SelectionLength : Text.Length;

    /// <summary>
    /// Put <paramref name="words"/> into the line at <paramref name="at"/>,
    /// with a space between them and a word either side so they never run
    /// into one. One undo step of its own, and the caret ends up after them.
    /// </summary>
    public void Insert(string words, int at)
    {
        if (IsReadOnly || string.IsNullOrEmpty(words)) return;

        string text = Text;
        at = Math.Clamp(at, 0, text.Length);
        string before = at > 0 && !char.IsWhiteSpace(text[at - 1]) ? " " : "";
        string after = at < text.Length && !char.IsWhiteSpace(text[at]) ? " " : "";

        Remember(new Step(text, SelectionStart, SelectionLength));
        _typing = false;
        WriteOwnEdit(text.Substring(0, at) + before + words + after + text.Substring(at));

        Focus();
        CaretIndex = at + before.Length + words.Length;
    }

    /// <summary>
    /// Asks for a colour and hands it back as a tag value, or null when the
    /// author changed their mind. Set by the window, which owns the picker; a
    /// box with nothing to ask writes <see cref="Markup.DefaultColor"/>.
    /// </summary>
    public Func<string?, string?>? PickColor { get; set; }

    /// <summary>The colour last written, so the picker opens on it and a run
    /// of lines in one colour is one click each.</summary>
    public string LastColor { get; private set; } = Markup.DefaultColor;

    /// <summary>
    /// Put a colour round the selected words: asked for with the picker when
    /// there is one, and nothing at all written if it is cancelled.
    /// </summary>
    public void SurroundWithColor()
    {
        if (IsReadOnly) return;

        // Read BEFORE the picker opens: a dialog takes the keyboard, and the
        // box's own idea of what was selected is only trusted while it has it.
        bool focused = IsKeyboardFocusWithin;
        int start = SelectionStart, length = SelectionLength;

        string? value = PickColor == null ? LastColor : PickColor(LastColor);
        if (string.IsNullOrWhiteSpace(value)) return;
        LastColor = value!;

        if (focused)
        {
            Focus();
            Select(start, length);
        }
        Surround(Markup.OpenColorFor(value!), Markup.CloseColor);
    }

    // ── Undo ──────────────────────────────────────────────────────────
    //
    // The line's history, kept by this box rather than by the RichTextBox.
    //
    // WPF's own undo records changes to the DOCUMENT, and this box redraws the
    // document whenever the styling moves - which WPF recorded as an edit of
    // its own. So every other Ctrl+Z undid a redraw and appeared to do nothing,
    // and undoing anything redrew the line, which counted as a new edit and
    // threw the redo away. Kept here, a step is a state of the LINE, which is
    // the only thing an author ever changed.
    //
    // Typing collapses into one step until the caret is moved by hand or a
    // formatting button is used, the way a TextBox groups it; each tag is a
    // step of its own. With nothing left to undo here, Ctrl+Z is left for the
    // editor's own undo, which is what it does in every other field.

    private readonly record struct Step(string Text, int SelectionStart, int SelectionLength);

    private readonly List<Step> _undo = new();
    private readonly List<Step> _redo = new();

    /// <summary>Whether the last step taken was typing, which the next
    /// keystroke joins rather than starting one of its own.</summary>
    private bool _typing;

    /// <summary>The line as it was before whatever is happening now.</summary>
    private string _last = "";

    /// <summary>Set while this box writes <see cref="Text"/> itself, so the
    /// write is not mistaken for a line arriving from outside.</summary>
    private bool _writingOwnEdit;

    public bool CanUndoLine => _undo.Count > 0;
    public bool CanRedoLine => _redo.Count > 0;

    /// <summary>Step back once. False when there is nothing to step back to,
    /// so the caller can hand the key on.</summary>
    public bool Undo() => Move(_undo, _redo);

    /// <summary>Step forward again. False when there is nothing to redo.</summary>
    public bool Redo() => Move(_redo, _undo);

    private bool Move(List<Step> from, List<Step> to)
    {
        if (IsReadOnly || from.Count == 0) return false;

        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(new Step(Text, SelectionStart, SelectionLength));
        _typing = false;

        WriteOwnEdit(step.Text);
        Select(Math.Clamp(step.SelectionStart, 0, step.Text.Length),
               Math.Clamp(step.SelectionLength, 0, step.Text.Length - Math.Clamp(step.SelectionStart, 0, step.Text.Length)));
        return true;
    }

    /// <summary>Where the caret goes when a run of typing is undone: the end of
    /// the part of the old line the run replaced, which is where a TextBox puts
    /// it too.</summary>
    private static int CaretFor(string before, string after)
    {
        int prefix = 0;
        int shorter = Math.Min(before.Length, after.Length);
        while (prefix < shorter && before[prefix] == after[prefix]) prefix++;

        int suffix = 0;
        while (suffix < shorter - prefix
               && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix]) suffix++;

        return before.Length - suffix;
    }

    private void Remember(Step before)
    {
        _undo.Add(before);
        _redo.Clear();
    }

    private void ForgetHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _typing = false;
    }

    private void WriteOwnEdit(string text)
    {
        _writingOwnEdit = true;
        try { SetCurrentValue(TextProperty, text); }
        finally { _writingOwnEdit = false; }
    }

    /// <summary>A click moves the caret somewhere new, so whatever is typed
    /// next is a new step.</summary>
    protected override void OnPreviewMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        _typing = false;
        base.OnPreviewMouseDown(e);
    }

    /// <summary>
    /// The shortcuts, handled here rather than as input bindings.
    /// <para/>
    /// A RichTextBox already answers Ctrl+B and Ctrl+I, and what it does is
    /// apply WPF's OWN bold to the document — which is not markup, is not in
    /// <see cref="Text"/>, and vanishes the next time the line is drawn. So the
    /// key has to be taken before the editor sees it, which a preview handler
    /// does and an input binding does not reliably do.
    /// </summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (ApplyShortcut(e.Key, System.Windows.Input.Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        // Moving the caret by hand ends a run of typing, and so does anything
        // done with Ctrl held - a paste or a cut is a step of its own.
        if (IsNavigation(e.Key) || (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            _typing = false;

        base.OnPreviewKeyDown(e);
    }

    private static bool IsNavigation(System.Windows.Input.Key key)
        => key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right
               or System.Windows.Input.Key.Up or System.Windows.Input.Key.Down
               or System.Windows.Input.Key.Home or System.Windows.Input.Key.End
               or System.Windows.Input.Key.PageUp or System.Windows.Input.Key.PageDown;

    /// <summary>
    /// Write the markup a chord asks for, and say whether it was one of ours.
    /// <para/>
    /// Separate from the key handler because the handler reads the live
    /// keyboard, which a test cannot hold down. This half is the whole of what
    /// the chords mean.
    /// </summary>
    public bool ApplyShortcut(System.Windows.Input.Key key,
                              System.Windows.Input.ModifierKeys mods)
    {
        bool ctrl = (mods & System.Windows.Input.ModifierKeys.Control) != 0;
        bool shift = (mods & System.Windows.Input.ModifierKeys.Shift) != 0;
        bool alt = (mods & System.Windows.Input.ModifierKeys.Alt) != 0;
        if (!ctrl || alt) return false;

        if (!shift)
        {
            switch (key)
            {
                case System.Windows.Input.Key.B:
                    Surround(Markup.OpenBold, Markup.CloseBold); return true;
                case System.Windows.Input.Key.I:
                    Surround(Markup.OpenItalic, Markup.CloseItalic); return true;

                // Not one of the four the game is known to act on, and WPF
                // would underline the document for real. Swallowed rather than
                // left to do something that only looks like it worked.
                case System.Windows.Input.Key.U:
                    return true;

                // Only while there is something of this line's to undo or redo.
                // Otherwise the key is not ours, and goes on to the editor's
                // undo like it does from any other field.
                case System.Windows.Input.Key.Z:
                    return Undo();
                case System.Windows.Input.Key.Y:
                    return Redo();
            }
            return false;
        }

        switch (key)
        {
            case System.Windows.Input.Key.C:
                SurroundWithColor(); return true;
            case System.Windows.Input.Key.S:
                Surround(Markup.OpenSize, Markup.CloseSize); return true;
            case System.Windows.Input.Key.Z:
                return Redo();
        }
        return false;
    }

    /// <summary>
    /// What the buttons and the shortcuts write.
    /// <para/>
    /// Colour is asked for with the picker. Size starts at a sensible value the
    /// author can edit in place, shown styled the moment it is written, which is
    /// how they find out it took.
    /// </summary>
    public static class Markup
    {
        public const string OpenBold = "<b>";
        public const string CloseBold = "</b>";
        public const string OpenItalic = "<i>";
        public const string CloseItalic = "</i>";

        /// <summary>The colour the picker first opens on. Six digits, like
        /// everything the button writes - see TmpColor.ToTagValue.</summary>
        public const string DefaultColor = "#FF6666";

        public const string OpenColor = "<color=" + DefaultColor + ">";
        public const string CloseColor = "</color>";
        public const string OpenSize = "<size=70%>";
        public const string CloseSize = "</size>";

        public static string OpenColorFor(string value) => "<color=" + value + ">";
    }

        // ── Document to text ──────────────────────────────────────────────

    private Paragraph Body => (Paragraph)Document.Blocks.FirstBlock;

    /// <summary>What the document says, as plain text.</summary>
    private string PlainText()
    {
        var built = new System.Text.StringBuilder();
        foreach (var inline in Body.Inlines)
        {
            if (inline is Run run) built.Append(run.Text);
            else if (inline is LineBreak) built.Append('\n');
        }
        return built.ToString();
    }

    /// <summary>The character offset a position in the document sits at.</summary>
    private int OffsetOf(TextPointer at)
    {
        int offset = 0;
        foreach (var inline in Body.Inlines)
        {
            if (inline is Run run)
            {
                if (at.CompareTo(run.ContentStart) >= 0 && at.CompareTo(run.ContentEnd) <= 0)
                    return offset + new TextRange(run.ContentStart, at).Text.Length;
                offset += run.Text.Length;
            }
            else if (inline is LineBreak)
            {
                if (at.CompareTo(inline.ContentStart) <= 0) return offset;
                offset++;
            }
        }
        return offset;
    }

    /// <summary>Where a character offset sits in the document, or null when it
    /// is past the end.</summary>
    private TextPointer? PointerAt(int offset)
    {
        if (offset < 0) return null;
        int seen = 0;
        foreach (var inline in Body.Inlines)
        {
            if (inline is Run run)
            {
                if (offset <= seen + run.Text.Length)
                    return run.ContentStart.GetPositionAtOffset(offset - seen);
                seen += run.Text.Length;
            }
            else if (inline is LineBreak)
            {
                if (offset == seen) return inline.ContentStart;
                seen++;
            }
        }
        return Body.ContentEnd;
    }

    // ── Keeping the two in step ───────────────────────────────────────

    private void OnDocumentChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncing) return;

        // Display-only: the document is a shortened rendering of the line, so
        // taking it as the line would throw the markup away.
        if (HidesTags) return;

        string typed = PlainText();

        // Typed, deleted or pasted: one step for the run, recorded from what
        // the line was before the run began. A document change that leaves the
        // line as it was - WPF's own bold, say - is no step at all, and is
        // still redrawn away below.
        if (typed != _last)
        {
            if (!_typing)
            {
                Remember(new Step(_last, CaretFor(_last, typed), 0));
                _typing = true;
            }
            else _redo.Clear();
        }

        _syncing = true;
        try { SetCurrentValue(TextProperty, typed); }
        finally { _syncing = false; }

        Render(typed);
    }

    /// <summary>
    /// Draw the line: the markup in its own colour, the text between it styled by
    /// whatever is open over it.
    /// <para/>
    /// Skipped entirely when the document already shows this exact string,
    /// which is what keeps an ordinary keystroke in an ordinary line from
    /// rebuilding anything.
    /// </summary>
    private void Render(string text)
    {
        if (_syncing) return;
        if (text == _rendered && (HidesTags || PlainText() == text)) return;

        int caret = CaretIndex;
        int anchor = OffsetOf(Selection.Start);
        int head = OffsetOf(Selection.End);
        bool hadSelection = head > anchor;

        _syncing = true;
        try
        {
            // One change block, so the redraw lands as one document change.
            // Nothing is recorded for undo: the box keeps the line's history
            // itself, and a redraw is not an edit.
            BeginChange();
            try
            {
                var body = Body;
                body.Inlines.Clear();
                foreach (var span in DialogueMarkup.Parse(text))
                {
                    if (span.IsTag && HidesTags) continue;
                    foreach (var inline in Build(text, span))
                        body.Inlines.Add(inline);
                }
            }
            finally { EndChange(); }

            _rendered = text;

            if (hadSelection)
            {
                var from = PointerAt(anchor);
                var to = PointerAt(head);
                if (from != null && to != null) Selection.Select(from, to);
            }
            else
            {
                var at = PointerAt(caret);
                if (at != null) CaretPosition = at;
            }
        }
        finally { _syncing = false; }
    }

    /// <summary>
    /// One span as inlines. More than one when the span holds newlines, since a
    /// Run cannot carry a line break.
    /// </summary>
    private IEnumerable<Inline> Build(string text, DialogueMarkup.Span span)
    {
        string content = span.Of(text);
        string[] lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) yield return new LineBreak();
            if (lines[i].Length == 0) continue;

            var run = new Run(lines[i]);
            if (span.IsTag)
            {
                // Its own colour on its own chip, and not styled by itself: a
                // tag is an instruction, and showing <b> in bold would suggest
                // it is part of the sentence.
                run.Foreground = TagBrush;
                run.Background = TagBackBrush;
                run.FontStyle = FontStyles.Normal;
                run.FontWeight = FontWeights.Normal;
            }
            else if (span.IsToken)
            {
                // The same chip, because it is the same KIND of thing: braces
                // the player will never see. But the style around it still
                // applies, because what the game puts here in its place WILL be
                // bold or coloured or half-size along with the rest.
                Apply(run, span.Style);
                var person = DialogueMarkup.TokenColor?.Invoke(lines[i]);
                if (person is { } c)
                {
                    // In the name colour of whoever it stands in for, on the
                    // dark chip the game-look row puts every token on: the
                    // name colours are made for the game's dark panel, and on
                    // a light box half of them would not read.
                    run.Foreground = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                    run.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(DialogueLook.TokenChipHex)!);
                }
                else
                {
                    run.Foreground = TagBrush;
                    run.Background = TagBackBrush;
                }
            }
            else Apply(run, span.Style);

            yield return run;
        }
    }

    private void TokenColorsChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(TokenColorsChanged)); return; }
        _rendered = null;
        Render(Text);
    }

    /// <summary>
    /// What a tag is written in: a hue of its own, so it reads as an instruction
    /// rather than as words somebody faded out.
    /// <para/>
    /// It was a flat grey, which is the one colour that fails on BOTH ends —
    /// washed out on a white box and half-gone on a dark one. It is now mixed
    /// from the theme's own text colour, so it keeps that colour's contrast with
    /// the box; see <see cref="Services.ThemeManager.KeyMarkup"/>.
    /// <para/>
    /// The fallbacks are the Light theme's own values, for a control built
    /// outside the app's resources — which is what a test does.
    /// </summary>
    private Brush TagBrush
        => TryFindResource(Services.ThemeManager.KeyMarkup) as Brush
           ?? new SolidColorBrush(Color.FromRgb(0x2F, 0x5A, 0x82));

    /// <summary>The chip behind it, faint enough not to stripe the line and
    /// solid enough to say where the tag begins and ends.</summary>
    private Brush TagBackBrush
        => TryFindResource(Services.ThemeManager.KeyMarkupBack) as Brush
           ?? new SolidColorBrush(Color.FromRgb(0xDE, 0xE4, 0xEB));

    private void Apply(Run run, DialogueMarkup.Style style)
    {
        if (style.Bold) run.FontWeight = FontWeights.Bold;
        if (style.Italic) run.FontStyle = FontStyles.Italic;
        if (Math.Abs(style.Scale - 1) > 0.0001) run.FontSize = FontSize * style.Scale;

        var brush = BrushFor(style.Color);
        if (brush != null) run.Foreground = brush;
    }

    /// <summary>
    /// The colour a <c>&lt;color=…&gt;</c> asks for, or null when the game would
    /// not read it as one - in which case the text keeps the ordinary colour
    /// rather than being painted a guess.
    /// <para/>
    /// The game's reading, through <see cref="TmpColor"/>, the same one the
    /// game-look row uses. This used WPF's colour parser, which is not the
    /// game's: it coloured a hundred and forty names the game prints as
    /// nothing, and read four hex digits as ARGB where the game reads RGBA.
    /// A fully transparent colour keeps the ordinary one, as it does on the row:
    /// a line that vanished from the box it is typed in could not be fixed.
    /// </summary>
    internal static Brush? BrushFor(string? value)
    {
        if (!TmpColor.TryParse(value, out var c) || c.A == 0) return null;
        return new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
    }
}
