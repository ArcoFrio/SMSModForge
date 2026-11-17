using System;
using SMSModForge.Rendering;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The bust fade-in (1.9.1) against the game's CharacterIntroFade: a black
/// silhouette 0 -> 1 over 1 s with the body hidden, then the body under it and
/// the silhouette 1 -> 0 over 3 s; drawn in linear light, as the game (linear
/// colour space) draws it. The control for the blend is the sRGB arithmetic,
/// which must come out different, or the test cannot tell the two apart.
/// </summary>
public sealed class IntroFadeTests
{
    [Theory]
    [InlineData(0.0, false, 0.0)]
    [InlineData(0.25, false, 0.25)]
    [InlineData(0.999, false, 0.999)]
    [InlineData(1.0, true, 1.0)]
    [InlineData(2.5, true, 0.5)]
    [InlineData(3.7, true, 0.1)]
    [InlineData(4.0, true, 0.0)]
    [InlineData(60.0, true, 0.0)]
    public void TheTimelineIsTheGames(double t, bool bodyShows, double silhouette)
    {
        var (shows, s) = IntroFade.At(t);
        Assert.Equal(bodyShows, shows);
        Assert.Equal(silhouette, s, 3);
    }

    private static double Linear(double srgb) => srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    private static double Srgb(double linear) => linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;

    [Fact]
    public void HalfASilhouetteHalvesTheLightBeneath_NotTheNumbers()
    {
        var output = new byte[] { 0x60, 0x80, 0xC0, 0xFF };     // opaque, premultiplied BGRA
        var bodyAlpha = new byte[] { 255 };
        IntroFade.DrawSilhouette(output, bodyAlpha, 0.5f);
        for (int c = 0; c < 3; c++)
        {
            byte was = new byte[] { 0x60, 0x80, 0xC0 }[c];
            int light = (int)Math.Round(Srgb(Linear(was / 255.0) * 0.5) * 255);
            Assert.InRange(output[c], light - 1, light + 1);
            Assert.NotEqual(was / 2, output[c]);                 // the control: not the sRGB halving
        }
        Assert.Equal(255, output[3]);
    }

    [Fact]
    public void AFullSilhouetteIsBlack_NoneLeavesThePixel_AndOverNothingItIsBlackAtItsCover()
    {
        var full = new byte[] { 0x60, 0x80, 0xC0, 0xFF };
        IntroFade.DrawSilhouette(full, new byte[] { 255 }, 1f);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, full);

        var none = new byte[] { 0x60, 0x80, 0xC0, 0xFF };
        IntroFade.DrawSilhouette(none, new byte[] { 255 }, 0f);
        Assert.Equal(new byte[] { 0x60, 0x80, 0xC0, 0xFF }, none);

        // The body hidden (the first second): black, as opaque as the
        // silhouette times the body's own alpha there.
        var empty = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        IntroFade.DrawSilhouette(empty, new byte[] { 255, 102 }, 0.5f);
        Assert.Equal(new byte[] { 0, 0, 0, 128, 0, 0, 0, 51 }, empty);
    }
}
