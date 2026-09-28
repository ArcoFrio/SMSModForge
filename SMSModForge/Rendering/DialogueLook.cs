namespace SMSModForge.Rendering;

/// <summary>
/// How the game draws a line of dialogue, in the game's own numbers.
/// <para/>
/// Every value here was read off the live <c>TMP_Text</c> on
/// <c>Dialogue_Default_Speech(Clone)</c> in a running game, not inferred. None
/// of it is in the game's files in a form anything could extract: the speech UI
/// is built from a prefab when a conversation starts, which is why the UI
/// extraction has no speech surface in it at all.
/// <para/>
/// They live in one place because they are a DESCRIPTION of something outside
/// this repository. When the game changes one, exactly one file is wrong, and
/// it is the file whose whole job is to say where the numbers came from.
/// </summary>
public static class DialogueLook
{
    /// <summary>
    /// The font asset the dialogue is typed in.
    /// <para/>
    /// Its name is a good guess and that is not why it is here: the shipped
    /// export of this asset carries <c>OutlineWidth 0.362</c>, <c>OutlineColor
    /// #000000FF</c> and <c>FaceColor #FFFFFFFF</c>, and the live component
    /// reports the same three. The asset and the game agree.
    /// </summary>
    public const string FontName = "Curse Casual Dialogue";   // English on purpose: the game's font name.

    /// <summary>The material that font draws through, for the record. The face
    /// colour, the black outline and the drop shadow all come from it, and the
    /// rasteriser reads all three out of the export.</summary>
    public const string MaterialName = "Curse Casual Atlas Material";   // English on purpose: the game's material name.

    /// <summary>What the line is typed at.</summary>
    public const double BodyPointSize = 38;

    /// <summary>What the speaker's name is typed at — 1.63x the line, which is
    /// the proportion worth keeping when both are scaled to fit a list row.</summary>
    public const double NamePointSize = 62;

    /// <summary>
    /// How far TMP will shrink the line to make it fit, and the reason a long
    /// line in game is smaller than a short one.
    /// <para/>
    /// <c>enableAutoSizing</c> is on, between <see cref="BodyPointSize"/> and
    /// this. The list rows do not shrink by it - a row is a different width, so
    /// shrinking on its own terms would agree with nothing - but
    /// <see cref="DialogueFit"/> does, in the game's own box, to tell the author
    /// when a line is made smaller or runs out of the box.
    /// </summary>
    public const double BodyMinPointSize = 28;

    /// <summary>
    /// The width of the box the game wraps the line inside, in its own units:
    /// 1010, read off the dialogue box's own TextMeshPro rect in a running game
    /// (2026-09-24; see <see cref="DialogueFit"/>).
    /// <para/>
    /// It said 509.12 before, taken from <c>TMP_Text.bounds</c> - which is the
    /// size of the words drawn, not of the box they are drawn in, so it was only
    /// ever as wide as whichever line happened to be showing. Nothing used it.
    /// </summary>
    public const double WrapWidth = DialogueFit.BoxWidth;

    /// <summary>How much taller the name is than the line: 62 against 38. The
    /// one proportion worth keeping when both are shrunk to fit a list row.</summary>
    public const double NameToBody = NamePointSize / BodyPointSize;

    /// <summary>
    /// The tallest ONE LINE of a preview row is allowed to be.
    /// <para/>
    /// The game draws a line in a panel 125 units deep, and at the proportions
    /// above a faithful copy of that comes out around 40px in a list — which
    /// nearly halves how many lines are on screen at once. The list is for
    /// scanning a conversation, so the height is capped and the text scaled to
    /// suit rather than the other way round.
    /// <para/>
    /// A row that wraps is that much taller per line it wraps onto: this is what
    /// the TEXT is sized against, not a ceiling on the row. A line too long for
    /// its column is worth two rows of space — it was worth clipping before, and
    /// clipping is how half a sentence goes unread.
    /// <para/>
    /// The cost, and it is a real one: the point-size-to-width ratio is no
    /// longer the game's, so a line does NOT break between the same two words
    /// the game breaks it between. It breaks where this column runs out.
    /// </summary>
    public const double MaxRowHeight = 28;

    /// <summary>
    /// The gap between the panel's left edge and the first thing drawn on it,
    /// as a share of the text size.
    /// <para/>
    /// Before the FIRST thing, whatever that is — the speaker's name, the line,
    /// or the "(no text)" a node with nothing in it shows. A row whose text
    /// starts hard against the edge reads as clipped even when it is whole.
    /// <para/>
    /// Proportional rather than a pixel count so it holds at any row height:
    /// six pixels of inset beside 20-point text is a margin, and beside 60-point
    /// text it is a mistake.
    /// </summary>
    public const double Inset = 0.35;

    /// <summary>
    /// The point size to type the line at so the row fits
    /// <see cref="MaxRowHeight"/>, given the font's own line height.
    /// <para/>
    /// Solved rather than guessed: one line of body, plus one of name when
    /// there is a speaker, has to come to the budget. A font states its line
    /// height in the units it was baked at, so the whole thing is one ratio.
    /// </summary>
    public static double BodySizeToFit(double lineHeightAtPointSize, double pointSize,
                                       bool withName, double budget = MaxRowHeight)
    {
        if (lineHeightAtPointSize <= 0 || pointSize <= 0) return BodyPointSize;

        double linesWorth = withName ? 1 + NameToBody : 1;
        double perPoint = lineHeightAtPointSize / pointSize;   // pixels of line per point
        double size = budget / (perPoint * linesWorth);
        return size > 0 ? size : BodyPointSize;
    }

    /// <summary>
    /// The line's own colour, which multiplies the material's face colour.
    /// <para/>
    /// <c>TMP_Text.color</c> is 1 1 1 1 and <c>faceColor</c> is 255 255 255 255,
    /// so the words are white and the material's black outline and drop shadow
    /// are what makes them readable over art.
    /// </summary>
    public static UiColor BodyColor => UiColor.White;

    /// <summary>What the speaker's name is coloured when the character has no
    /// colour of their own. The name takes the actor's colour where there is
    /// one; this is the ordinary case.</summary>
    public static UiColor NameFallbackColor => UiColor.White;

    /// <summary>
    /// The panel behind it.
    /// <para/>
    /// A flat dark grey rather than the game's actual speech-skin art, which is
    /// per-character and is a sprite rather than a colour. The point of the
    /// preview is reading the line, and a busy panel behind three words in a
    /// list row would be harder to read than the game is, not truer to it.
    /// </summary>
    public const string PanelHex = "#2B2B2B";

    /// <summary>
    /// What the row's own marks are drawn in once they sit on that panel.
    /// <para/>
    /// The kind glyph, the tag chip and the rest are theme brushes everywhere
    /// else, and a theme brush on a fixed dark panel is a coin toss: on the
    /// light theme the glyph is near-black on near-black. Fixed, like the panel,
    /// because the panel is fixed.
    /// </summary>
    public const string OnPanelHex = "#F0F0F0";

    /// <summary>Dialogue is laid out from the top left, not centred.</summary>
    public const TextAlign Align = TextAlign.Left;

    /// <summary>
    /// How far the top of an italic glyph leans right of its baseline, as a
    /// share of the glyph's height.
    /// <para/>
    /// <b>This one number is not the game's.</b> Everything else in this file
    /// was read off a live component; TextMeshPro keeps its italic angle on the
    /// FONT ASSET rather than on the material, and the asset export does not
    /// carry it. So this is a slant chosen to read as italic at list-row sizes,
    /// and it is the one thing in the preview that is a likeness rather than a
    /// copy. If the real angle ever gets read off the asset, it belongs here.
    /// </summary>
    public const double ItalicSlant = 0.22;

    /// <summary>
    /// What one of the game's braced tokens is drawn in, and the chip behind it.
    /// <para/>
    /// <b>The editor's mark, not the game's.</b> The player never sees
    /// <c>{PC}</c> — they see whatever they called themselves, in the ordinary
    /// colour. This says "something goes here", which is the one thing a row
    /// cannot show by drawing the line as the player will read it.
    /// <para/>
    /// Fixed, like the rest of the panel, and picked against it rather than
    /// against a theme: the light blue is the same family as the markup colour
    /// the Text box uses, so a token means the same thing in both places.
    /// </summary>
    public const string TokenHex = "#8FBEE8";

    /// <summary>The chip behind it. Enough of a lift off the panel to bound the
    /// token, nowhere near enough to outshine the words.</summary>
    public const string TokenChipHex = "#3D454D";

    /// <summary>
    /// The guides down the left of a row that say what it is inside.
    /// <para/>
    /// Light enough to read on the panel, dark enough to stay behind the words:
    /// they are structure, and the line is the content.
    /// </summary>
    public const string RailHex = "#6A7178";

    /// <summary>
    /// The wavy line under a misspelled word.
    /// <para/>
    /// Windows' own red, because that is what the box below the list draws and
    /// a second red would read as a second kind of problem. Fully opaque: a
    /// mark this thin loses its colour long before it loses its shape.
    /// </summary>
    public const string SpellingHex = "#FF3B30";

    public static UiColor TokenColor => UiColor.Parse(TokenHex);
    public static UiColor TokenChipColor => UiColor.Parse(TokenChipHex);
    public static UiColor SpellingColor => UiColor.Parse(SpellingHex);
}
