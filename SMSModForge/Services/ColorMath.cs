using System;
using System.Globalization;

namespace SMSModForge.Services;

/// <summary>
/// Turning a colour between the three forms the editor needs: the pack's
/// "#RRGGBBAA" text, plain bytes, and the hue/saturation/value a picker is
/// actually navigated in.
/// <para/>
/// Deliberately free of WPF types. The maths is the part that can be wrong in a
/// way nobody sees - a hue that drifts a degree per round trip, a grey whose
/// hue is undefined and comes back as red - and keeping it here means it can be
/// tested without a window.
/// </summary>
public static class ColorMath
{
    /// <param name="h">Hue in degrees, 0-360. Undefined for greys, where it is
    /// reported as 0 - see <see cref="FromHsv"/>.</param>
    /// <param name="s">Saturation, 0-1.</param>
    /// <param name="v">Value, 0-1.</param>
    public static (double H, double S, double V) ToHsv(byte r, byte g, byte b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double span = max - min;

        double h = 0;
        if (span > 0)
        {
            if (max == rd) h = 60 * (((gd - bd) / span) % 6);
            else if (max == gd) h = 60 * ((bd - rd) / span + 2);
            else h = 60 * ((rd - gd) / span + 4);
        }
        if (h < 0) h += 360;

        return (h, max <= 0 ? 0 : span / max, max);
    }

    public static (byte R, byte G, byte B) FromHsv(double h, double s, double v)
    {
        h = h % 360; if (h < 0) h += 360;
        s = Clamp01(s);
        v = Clamp01(v);

        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return (Byte(r + m), Byte(g + m), Byte(b + m));
    }

    /// <summary>The pack's form. Always eight digits: a colour that drops its
    /// alpha on the way through an editor is a colour that quietly became
    /// opaque.</summary>
    public static string ToHex(byte r, byte g, byte b, byte a)
        => $"#{r:X2}{g:X2}{b:X2}{a:X2}";

    /// <summary>
    /// Read "#RRGGBB" or "#RRGGBBAA", with or without the hash. Six digits mean
    /// opaque, which is what the game's own serialisation means by them.
    /// </summary>
    public static bool TryParse(string? hex, out byte r, out byte g, out byte b, out byte a)
    {
        r = g = b = 0; a = 255;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        string s = hex.Trim().TrimStart('#');
        if (s.Length != 6 && s.Length != 8) return false;

        try
        {
            r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (s.Length == 8)
                a = byte.Parse(s.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return true;
        }
        catch { return false; }
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private static byte Byte(double v)
    {
        double scaled = Math.Round(v * 255);
        return scaled < 0 ? (byte)0 : scaled > 255 ? (byte)255 : (byte)scaled;
    }
}
