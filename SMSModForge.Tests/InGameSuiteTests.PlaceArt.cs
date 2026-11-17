using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// How the game moves a pack place's art (1.9.1, the author's "full shader
/// check": every shader the previews port, held to the game - LevelShaderTests
/// holds the level shader's port to the game's compiled program; this, to the
/// game itself).
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// The place preview's level shader (MilkingShader) draws a pack place's art
    /// where the game does, moved as the game moves it: the look place (coded
    /// ramps, its mask's left half moving, its right half still) in the game, the
    /// interface off, the place's lights neutral and the post-processing off, its
    /// time frozen - every screen pixel over the art against the shader's colour
    /// at that point of the art (MilkingShader.At, the shader's own path), with
    /// the material's numbers read from the game. Controls: the art not moved is
    /// not the game's where the mask moves it, and is where it does not.
    /// </summary>
    [Fact]
    public void S10_The_place_preview_moves_a_pack_places_art_as_the_game_does()
    {
        if (Off) return;
        var game = _s.InGame();
        string token = "pack:" + InGameSuitePacks.Looks.Id + "." + InGameSuitePacks.Looks.PlaceKey;
        string ending = "_" + InGameSuitePacks.Looks.PlaceObject;
        var hidden = new List<string> { "9_MainCanvas" };
        string said, later;
        string level;
        try
        {
            Travel(game, token, ending);
            game.Ask("wait 2", 10);
            level = game.Ask("level").Split(' ').Last().Trim();
            string canvases = game.Ask("find-all *Canvas*");
            hidden.AddRange(canvases.Substring(canvases.IndexOf(':') + 1).Split(" | ", StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim()).Where(p => p.Contains("5_Levels/" + level + "/")));
            hidden.AddRange(OnBusts(game));
            foreach (var h in hidden) game.Ask("set-active " + h + " false");
            _out.WriteLine("   " + game.Ask("lights neutral"));
            game.Ask("postfx off");
            game.Ask("wait 0.3", 10);
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            said = game.Ask("bust-look 5_Levels/" + level + " place-art", 30);
            game.Ask("freeze off");
            game.Ask("wait 1.3", 10);
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            later = LookFolder(game.Ask("screen-look place-art-later", 15));
        }
        finally
        {
            game.Ask("freeze off");
            game.Ask("lights restore");
            game.Ask("postfx on");
            foreach (var h in hidden) game.Ask("set-active " + h + " true");
            Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        }
        var look = LookOf(said.Substring("place-art".Length).Trim());
        var info = File.ReadAllLines(Path.Combine(look.Folder, "info.txt"));
        foreach (var l in info.Where(l => l.StartsWith("material=") || l.StartsWith("mat_") || l.StartsWith("drawnCorners=") || l.StartsWith("sprite=") || l.StartsWith("renderer=")))
            _out.WriteLine("   " + l);
        var (_, _, laterPx) = Png(Path.Combine(later, "screen.png"));
        var corners = look.Corners("drawnCorners");
        float left = corners[0].x, bottom = corners[0].y, width = corners[1].x - corners[0].x, height = corners[2].y - corners[0].y;
        float time = look.F("time");
        float[] Vec(string name) => info.First(l => l.StartsWith(name + "=")).Substring(name.Length + 1).Split(',')
                                        .Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var dir = Vec("mat_MilkingDirection");
        var settings = new MilkingShader.Settings(look.F("mat_JiggleSpeed"), look.F("mat_JiggleStrength"), look.F("mat_JigglePhaseOffset"),
                                                  look.F("mat_JiggleAmplitudeVariation"), look.F("mat_MilkingWaveCount"), look.F("mat_MilkingCompressionRatio"),
                                                  dir[0], dir[1]);
        var still = new MilkingShader.Settings(0f, 0f, 0f, 0f, settings.WaveCount, settings.CompressionRatio, dir[0], dir[1]);
        int aw = InGameSuitePacks.Looks.PlaceW, ah = InGameSuitePacks.Looks.PlaceH, mw = InGameSuitePacks.Looks.PlaceMaskW, mh = InGameSuitePacks.Looks.PlaceMaskH;
        var art = new byte[aw * ah * 4];
        for (int y = 0; y < ah; y++)
            for (int x = 0; x < aw; x++)
            {
                var c = InGameSuitePacks.Looks.PlaceArtAt(x, y);
                int i = (y * aw + x) * 4;
                art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = 255;
            }
        var mask = new byte[mw * mh * 4];
        for (int y = 0; y < mh; y++)
            for (int x = 0; x < mw / 2; x++) { int i = (y * mw + x) * 4; mask[i] = mask[i + 1] = mask[i + 2] = mask[i + 3] = 255; }

        // Every screen pixel over the art (a pixel in from its edges).
        int moving = 0, movingNear = 0, movingStillNear = 0, stillSide = 0, stillNear = 0, stillChanged = 0, gameMoved = 0;
        var px = new byte[4];
        int x0 = (int)MathF.Ceiling(left) + 1, x1 = (int)MathF.Floor(left + width) - 2, y0 = (int)MathF.Ceiling(bottom) + 1, y1 = (int)MathF.Floor(bottom + height) - 2;
        for (int sy = Math.Max(0, y0); sy <= Math.Min(look.H - 1, y1); sy++)
            for (int sx = Math.Max(0, x0); sx <= Math.Min(look.W - 1, x1); sx++)
            {
                float u = (sx + 0.5f - left) / width, v = (sy + 0.5f - bottom) / height;
                var g = look.At(sx, sy);
                int D()
                    => Math.Max(Math.Abs(px[0] - g.b), Math.Max(Math.Abs(px[1] - g.g), Math.Abs(px[2] - g.r)));
                MilkingShader.At(art, aw, ah, mask, mw, mh, settings, time, u, v, px, 0);
                int d = D();
                MilkingShader.At(art, aw, ah, mask, mw, mh, still, time, u, v, px, 0);
                int ds = D();
                int li = ((look.H - 1 - sy) * look.W + sx) * 4;
                bool changed = Math.Abs(laterPx[li] - g.b) > 1 || Math.Abs(laterPx[li + 1] - g.g) > 1 || Math.Abs(laterPx[li + 2] - g.r) > 1;
                if (u < 0.49f)            // the mask moves its left half; the seam left out
                {
                    moving++;
                    if (d <= 3) movingNear++;
                    if (ds <= 3) movingStillNear++;
                    if (changed) gameMoved++;
                }
                else if (u > 0.51f)
                {
                    stillSide++;
                    if (d <= 3) stillNear++;
                    if (changed) stillChanged++;
                }
            }
        double Share(int n, int of) => of == 0 ? 0 : (double)n / of;
        _out.WriteLine($"the art at {left},{bottom} {width}x{height}, time {time}: where the mask moves it ({moving} pixels) within three {Share(movingNear, moving):P2}, "
                       + $"not moved {Share(movingStillNear, moving):P2}, moved by the game a moment later {Share(gameMoved, moving):P2}; "
                       + $"where it does not ({stillSide}) within three {Share(stillNear, stillSide):P2}, changed a moment later {Share(stillChanged, stillSide):P2}");
        Assert.True(moving > 10000 && stillSide > 10000, "the art covers too little of the screen to tell");
        Assert.True(Share(gameMoved, moving) > 0.3, "control: the game did not move the art where the mask moves it - the check cannot tell");
        Assert.True(Share(stillChanged, stillSide) < 0.01, "control: the game moved the art where the mask holds it still");
        Assert.True(Share(movingStillNear, moving) < Share(movingNear, moving) - 0.2, "control: the art not moved passes for the game's where the mask moves it");
        Assert.True(Share(movingNear, moving) >= 0.99, $"where the mask moves the art the preview's shader is more than three levels off at {moving - movingNear} of {moving}");
        Assert.True(Share(stillNear, stillSide) >= 0.99, $"where the art is still the preview's shader is more than three levels off at {stillSide - stillNear} of {stillSide}");
    }
}
