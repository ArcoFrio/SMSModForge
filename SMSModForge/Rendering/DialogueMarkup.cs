using System;
using System.Collections.Generic;
using System.Globalization;

namespace SMSModForge.Rendering;

/// <summary>
/// Reads a dialogue line the way the game's text renderer does, so the editor
/// can show what the player will see.
/// <para/>
/// The game speaks through TextMeshPro, which treats <c>&lt;b&gt;</c> and
/// friends as instructions rather than as characters — so a line an author
/// types is part text and part markup, and the markup is invisible in the
/// result. Showing it flat means an author cannot tell a tag that will work
/// from one that will be printed at the player verbatim.
/// <para/>
/// Deliberately NOT a general HTML or TMP parser. It knows the four tags the
/// editor offers, and anything else it does not recognise is left as ordinary
/// text — which is exactly what the game does with it, and the honest thing to
/// show for a tag nobody has confirmed.
/// <para/>
/// Tags STACK, and that is most of the reason this is a parser rather than a
/// regular expression: <c>&lt;b&gt;bold &lt;i&gt;and italic&lt;/i&gt;&lt;/b&gt;</c>
/// is two styles over the middle words, and a closing tag returns to whatever
/// was underneath rather than to nothing.
/// </summary>
public static class DialogueMarkup
{
    /// <summary>What a run of text looks like once the open tags are applied.</summary>
    public readonly struct Style : IEquatable<Style>
    {
        public bool Bold { get; init; }
        public bool Italic { get; init; }

        /// <summary>The colour as written, or null for the ordinary one. Kept as
        /// the author's own text rather than parsed here, so the thing that
        /// paints it decides what it can make of "#f66" or "red".</summary>
        public string? Color { get; init; }

        /// <summary>Size as a multiple of normal: 0.7 for <c>&lt;size=70%&gt;</c>.
        /// 1 when nothing is asking for a change.</summary>
        public double Scale { get; init; }

        public static Style Plain => new() { Scale = 1 };

        public bool IsPlain => !Bold && !Italic && Color == null && Math.Abs(Scale - 1) < 0.0001;

        public bool Equals(Style other)
            => Bold == other.Bold && Italic == other.Italic
               && Color == other.Color && Math.Abs(Scale - other.Scale) < 0.0001;

        public override bool Equals(object? o) => o is Style s && Equals(s);
        public override int GetHashCode() => HashCode.Combine(Bold, Italic, Color, Scale);
    }

    /// <summary>What one stretch of a line is.</summary>
    public enum Kind
    {
        /// <summary>Words. The player reads these.</summary>
        Text,

        /// <summary>Markup. The player never sees it; it styles what it wraps.</summary>
        Tag,

        /// <summary>One of the game's braced name tokens. The player never sees
        /// the braces either, but unlike a tag this leaves something behind —
        /// whatever that player chose to call somebody.</summary>
        Token,
    }

    /// <summary>One stretch of the line: markup, one of the game's tokens, or
    /// text with the style the markup around it puts on it.</summary>
    public readonly record struct Span(int Start, int Length, Kind Kind, Style Style)
    {
        public string Of(string text) => text.Substring(Start, Length);

        public bool IsTag => Kind == Kind.Tag;
        public bool IsToken => Kind == Kind.Token;

        /// <summary>Words, and nothing else: neither an instruction nor a
        /// stand-in for a name.</summary>
        public bool IsWords => Kind == Kind.Text;
    }

    /// <summary>The tags this understands. Anything else is text.</summary>
    private static readonly string[] Known = { "b", "i", "color", "size" };

    /// <summary>
    /// The game's braced name tokens, every one confirmed to resolve in game.
    /// <para/>
    /// A player chooses what they call their family, and the game swaps these
    /// for the words they picked. An author who writes "Mom" instead says it to
    /// the player who chose "Mum" — which from their side is indistinguishable
    /// from the game losing their choice.
    /// <para/>
    /// This is the ONE list. The cheatsheet under the dialogue tree and the
    /// in-app reference are both checked against it, because they have drifted
    /// from each other before.
    /// </summary>
    public static readonly IReadOnlyList<string> Tokens =
        new[] { "{PC}", "{M}", "{D}", "{B}", "{S}", "{DA}", "{F}" };

    /// <summary>The player's name.</summary>
    public const string PlayerToken = "{PC}";

    /// <summary>
    /// Whom each family word is said of, so the token can be drawn in that
    /// character's name colour (the author, 1.7.0): what the player calls Anna,
    /// Josef and Adrian, the word for a son - Adrian - and for a daughter,
    /// Emma. <c>{F}</c>, the family as a whole, is nobody's, and keeps the
    /// token mark; so does <see cref="PlayerToken"/>, which is the player's own.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> TokenCharacters =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // English on purpose: the game's characters, by the names its cast is filed under.
            ["{M}"] = "Anna",
            ["{D}"] = "Josef",
            ["{B}"] = "Adrian",
            ["{S}"] = "Adrian",
            ["{DA}"] = "Emma",
        };

    /// <summary>
    /// The colour a token is drawn in: the name colour of whoever it stands in
    /// for, or null for the token mark. Set by the editor, which knows the
    /// pack's cast - and so each pack's own colour for them.
    /// </summary>
    public static Func<string, UiColor?>? TokenColor { get; set; }

    /// <summary>Said when one of those colours may have changed, so a line on
    /// screen draws its tokens again.</summary>
    public static event Action? TokenColorsChanged;

    public static void RaiseTokenColorsChanged() => TokenColorsChanged?.Invoke();

    /// <summary>
    /// Split a line into tags and the styled text between them.
    /// <para/>
    /// Every character of the input lands in exactly one span and the spans are
    /// in order, so a caller can rebuild the original by concatenating them —
    /// which is what makes this safe to drive an editable control from.
    /// </summary>
    public static IReadOnlyList<Span> Parse(string? text)
    {
        var spans = new List<Span>();
        if (string.IsNullOrEmpty(text)) return spans;

        var open = new List<(string Tag, string? Value)>();
        int runFrom = 0;
        int at = 0;

        while (at < text!.Length)
        {
            if (text[at] != '<') { at++; continue; }

            int close = text.IndexOf('>', at + 1);
            if (close < 0) break;                       // an unfinished tag is just text

            string inside = text.Substring(at + 1, close - at - 1);
            if (!Recognised(inside, out string name, out string? value, out bool closing))
            { at++; continue; }

            // The text before this tag, under whatever was open over it.
            if (at > runFrom)
                AddText(spans, text, runFrom, at - runFrom, StyleOf(open));

            spans.Add(new Span(at, close - at + 1, Kind.Tag, StyleOf(open)));

            if (closing)
            {
                // Back to what was underneath. TMP forgives a close with no
                // open, and so does this: it changes nothing.
                for (int i = open.Count - 1; i >= 0; i--)
                    if (open[i].Tag == name) { open.RemoveAt(i); break; }
            }
            else open.Add((name, value));

            at = close + 1;
            runFrom = at;
        }

        if (runFrom < text.Length)
            AddText(spans, text, runFrom, text.Length - runFrom, StyleOf(open));

        return spans;
    }

    /// <summary>
    /// A stretch of non-markup, split again around any of the game's tokens
    /// inside it.
    /// <para/>
    /// Only the tokens on the confirmed list. A brace pair this does not know
    /// is left as words, for the same reason an unknown tag is: the editor
    /// cannot promise the game will do anything with it, and dressing up
    /// <c>{PCC}</c> as a working token is how a typo survives to the player.
    /// <para/>
    /// A token never spans a tag, because this only ever sees one side of one.
    /// </summary>
    private static void AddText(List<Span> spans, string text, int start, int length, Style style)
    {
        int at = start;
        int end = start + length;

        while (at < end)
        {
            int open = text.IndexOf('{', at);
            if (open < 0 || open >= end) break;

            int close = text.IndexOf('}', open + 1);
            if (close < 0 || close >= end) break;

            string braced = text.Substring(open, close - open + 1);
            if (!IsToken(braced)) { at = open + 1; continue; }

            if (open > start) spans.Add(new Span(start, open - start, Kind.Text, style));
            spans.Add(new Span(open, braced.Length, Kind.Token, style));

            start = at = close + 1;
        }

        if (start < end) spans.Add(new Span(start, end - start, Kind.Text, style));
    }

    /// <summary>Whether a braced word is one the game resolves. Case-sensitive:
    /// the game's own lines write them in capitals, and nothing here has seen
    /// <c>{pc}</c> work.</summary>
    private static bool IsToken(string braced)
    {
        foreach (string known in Tokens)
            if (string.Equals(braced, known, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// Whether this is one of the tags the editor knows, and which.
    /// <para/>
    /// A tag needing a value without one — a bare <c>&lt;color&gt;</c> — is not
    /// recognised, because the game would not act on it either.
    /// </summary>
    private static bool Recognised(string inside, out string name, out string? value, out bool closing)
    {
        name = "";
        value = null;
        closing = inside.StartsWith("/", StringComparison.Ordinal);
        if (closing) inside = inside.Substring(1);
        if (inside.Length == 0) return false;

        int eq = inside.IndexOf('=');
        if (eq >= 0)
        {
            name = inside.Substring(0, eq).Trim().ToLowerInvariant();
            value = inside.Substring(eq + 1).Trim().Trim('"');
            if (value.Length == 0) return false;
        }
        else name = inside.Trim().ToLowerInvariant();

        if (System.Array.IndexOf(Known, name) < 0) return false;

        // color and size say WHAT; opening one without a value does nothing.
        if (!closing && (name == "color" || name == "size") && value == null) return false;
        return true;
    }

    /// <summary>The style every open tag adds up to. Later tags win where two
    /// of the same kind are open, which is what nesting one colour inside
    /// another does.</summary>
    private static Style StyleOf(List<(string Tag, string? Value)> open)
    {
        bool bold = false, italic = false;
        string? color = null;
        double scale = 1;

        foreach (var (tag, value) in open)
        {
            switch (tag)
            {
                case "b": bold = true; break;
                case "i": italic = true; break;
                case "color": color = value; break;
                case "size": scale = ScaleOf(value, scale); break;
            }
        }
        return new Style { Bold = bold, Italic = italic, Color = color, Scale = scale };
    }

    /// <summary>
    /// What a size value multiplies the text by.
    /// <para/>
    /// A percentage is a multiple of normal. A plain number is a point size in
    /// TMP, so it is shown against the size the game actually draws dialogue at
    /// — see <see cref="NominalPointSize"/>. An unreadable value leaves the size
    /// alone rather than guessing.
    /// </summary>
    private static double ScaleOf(string? value, double fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        string v = value!.Trim();

        bool percent = v.EndsWith("%", StringComparison.Ordinal);
        if (percent) v = v.Substring(0, v.Length - 1);

        // Leading + and - are relative in TMP; treated as absolute here, since
        // the nominal size is already an approximation.
        if (!double.TryParse(v.TrimStart('+'), NumberStyles.Float,
                             CultureInfo.InvariantCulture, out double n))
            return fallback;

        double scale = percent ? n / 100.0 : n / NominalPointSize;
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) return fallback;

        // Bounded so a typo cannot make the editor's text box unusable. An
        // author writing <size=900> has a problem, but a 25x line in their
        // editor is not the way to be told about it.
        return Math.Clamp(scale, 0.3, 3.0);
    }

    /// <summary>
    /// What a plain <c>&lt;size=40&gt;</c> is measured against: the size the game
    /// sets on its dialogue text, read off the live component
    /// (<c>TMP_Text.fontSize</c> = 38).
    /// <para/>
    /// It was a guessed 36 until somebody looked. Worth having exact, because a
    /// point size is the one form of this tag the game never uses in its own
    /// lines — so there is no example to check the result against, and a wrong
    /// constant here would go unnoticed.
    /// <para/>
    /// The same component carries <c>fontSizeMin</c> 28 and <c>fontSizeMax</c>
    /// 38, which is TMP auto-sizing: a long line is shrunk to fit, as far as
    /// 28. That is NOT reproduced here. The editor's box is a different width
    /// in a different font, so shrinking on its own terms would be a number
    /// that looks precise and agrees with nothing.
    /// </summary>
    private const double NominalPointSize = 38.0;

    /// <summary>Whether the line has any markup worth showing differently.</summary>
    public static bool HasMarkup(string? text)
    {
        foreach (var span in Parse(text))
            if (span.IsTag) return true;
        return false;
    }
}
