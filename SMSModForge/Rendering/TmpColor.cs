using System;

namespace SMSModForge.Rendering;

/// <summary>
/// What the game makes of the value in a <c>&lt;color=…&gt;</c> tag, read off
/// its own text engine (TextMeshPro's <c>TMP_Text.ValidateHtmlTag</c>,
/// <c>HexCharsToColor</c> and <c>HexToInt</c> in the game's
/// Unity.TextMeshPro.dll) rather than assumed.
/// <para/>
/// ONE reader for both places a line is drawn. The Text box used WPF's colour
/// parser, which knows a hundred and forty names the game does not and reads
/// four hex digits as ARGB where the game reads RGBA; the game-look row used
/// the UI tint parser, which only knows six and eight digits - so the
/// formatting button's own <c>#f66</c> was red in one and white in the other.
/// Neither was the game.
/// <para/>
/// What the game accepts:
/// <list type="bullet">
///   <item><c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c> and <c>#RRGGBBAA</c>. A
///   single digit stands for itself twice, so <c>#f66</c> is <c>#ff6666</c>.
///   Any other length is not a colour.</item>
///   <item>A character that is not a hex digit counts as <c>f</c>. That is the
///   game's <c>HexToInt</c>, which returns 15 for anything it does not know.</item>
///   <item>Ten names, in any case: red, lightblue, blue, grey, black, green,
///   white, orange, purple, yellow - each confirmed by its hash in the IL.</item>
/// </list>
/// </summary>
public static class TmpColor
{
    /// <summary>The colour a value names, or false when the game would not
    /// read it as one.</summary>
    public static bool TryParse(string? value, out UiColor color)
    {
        color = UiColor.White;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string v = value!.Trim();

        if (v[0] == '#')
        {
            switch (v.Length - 1)
            {
                case 3:
                    color = new UiColor(Twice(v[3]), Twice(v[2]), Twice(v[1]), 255);
                    return true;
                case 4:
                    color = new UiColor(Twice(v[3]), Twice(v[2]), Twice(v[1]), Twice(v[4]));
                    return true;
                case 6:
                    color = new UiColor(Pair(v, 5), Pair(v, 3), Pair(v, 1), 255);
                    return true;
                case 8:
                    color = new UiColor(Pair(v, 5), Pair(v, 3), Pair(v, 1), Pair(v, 7));
                    return true;
                default:
                    return false;
            }
        }

        // Colours as the game builds them: Unity's Color constants through
        // Color32, or the bytes written out in the IL.
        switch (v.ToLowerInvariant())
        {
            case "red":       color = Rgb(255, 0, 0); return true;
            case "lightblue": color = Rgb(173, 216, 230); return true;
            case "blue":      color = Rgb(0, 0, 255); return true;
            case "grey":      color = Rgb(128, 128, 128); return true;
            case "black":     color = Rgb(0, 0, 0); return true;
            case "green":     color = Rgb(0, 255, 0); return true;
            case "white":     color = Rgb(255, 255, 255); return true;
            case "orange":    color = Rgb(255, 128, 0); return true;
            case "purple":    color = Rgb(160, 32, 240); return true;
            case "yellow":    color = Rgb(255, 235, 4); return true;
        }
        return false;
    }

    /// <summary>
    /// The value the formatting button writes for a colour: six digits when it
    /// is opaque, which is the form a person reads, and eight when it is not.
    /// Never three: the picker chooses any colour, and only a few of them have
    /// a short form.
    /// </summary>
    public static string ToTagValue(byte r, byte g, byte b, byte a)
        => a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}";

    private static UiColor Rgb(byte r, byte g, byte b) => new(b, g, r, 255);

    private static byte Twice(char c) => (byte)(Digit(c) * 17);

    private static byte Pair(string v, int at) => (byte)(Digit(v[at]) * 16 + Digit(v[at + 1]));

    private static int Digit(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return 15;
    }
}
