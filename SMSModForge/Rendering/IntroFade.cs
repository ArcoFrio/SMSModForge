using System;

namespace SMSModForge.Rendering;

/// <summary>
/// How a bust appears in the game (1.9.1, asked by ArcoFrio for the preview):
/// the game's <c>CharacterIntroFade</c>, on every bust body, each time it is
/// switched on.
/// <list type="number">
///   <item>The body goes invisible, and its Blink and Expressions are switched
///   off.</item>
///   <item>A copy of the body in black - the same sprite and material, so it
///   jiggles with it - drawn above everything of the bust (its order is the
///   body's + 8 to 16; expressions are 3, the mouth 4, blink 5) fades in over
///   1 s.</item>
///   <item>Then Blink and Expressions come back on, the body shows in full under
///   the silhouette, and the silhouette fades out over 3 s.</item>
/// </list>
/// The mouth is not switched off. The game draws in linear colour, so the
/// silhouette darkens linear light, not the sRGB numbers.
/// </summary>
public static class IntroFade
{
    /// <summary>The silhouette's fade-in, then the character's, in seconds.</summary>
    public const double SilhouetteSeconds = 1.0, RevealSeconds = 3.0;

    public static double TotalSeconds => SilhouetteSeconds + RevealSeconds;

    /// <summary>What the bust looks like <paramref name="t"/> seconds into its
    /// fade-in: whether the body (and its blink and expression) shows, and how
    /// opaque the black silhouette over it is.</summary>
    public static (bool BodyShows, float Silhouette) At(double t)
    {
        if (t < 0) return (false, 0f);
        if (t < SilhouetteSeconds) return (false, (float)(t / SilhouetteSeconds));
        if (t < TotalSeconds) return (true, (float)(1.0 - (t - SilhouetteSeconds) / RevealSeconds));
        return (true, 0f);
    }

    private static readonly float[] ToLinear = BuildToLinear();

    private static float[] BuildToLinear()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            t[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
        return t;
    }

    private static float ToSrgb(float linear)
    {
        double c = Math.Clamp(linear, 0f, 1f);
        return (float)(c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055);
    }

    /// <summary>
    /// Draws the black silhouette over <paramref name="output"/> (premultiplied
    /// BGRA): at each pixel it covers <paramref name="silhouette"/> times the
    /// body's own alpha there (<paramref name="bodyAlpha"/>, one byte a pixel,
    /// taken from the displaced body before anything else was drawn). Black
    /// over, in linear light: the colour beneath keeps (1 - cover) of its light.
    /// </summary>
    public static void DrawSilhouette(byte[] output, byte[] bodyAlpha, float silhouette)
    {
        if (silhouette <= 0f) return;
        for (int p = 0, i = 0; p < bodyAlpha.Length; p++, i += 4)
        {
            float cover = silhouette * bodyAlpha[p] / 255f;
            if (cover <= 0f) continue;
            float a = output[i + 3] / 255f;
            float aOut = cover + a * (1f - cover);
            if (a > 0f)
            {
                // Straight colour beneath, in linear light; what is left of it is
                // a*(1-cover) of the new pixel's aOut.
                float keep = a * (1f - cover) / aOut;
                for (int c = 0; c < 3; c++)
                {
                    float straight = Math.Min(1f, output[i + c] / 255f / a);
                    float lit = ToLinear[(int)(straight * 255f + 0.5f)] * keep;
                    output[i + c] = (byte)(ToSrgb(lit) * aOut * 255f + 0.5f);
                }
            }
            else
            {
                output[i] = output[i + 1] = output[i + 2] = 0;
            }
            output[i + 3] = (byte)(aOut * 255f + 0.5f);
        }
    }
}
