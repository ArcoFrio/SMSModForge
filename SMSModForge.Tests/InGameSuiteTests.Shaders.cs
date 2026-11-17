using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// S areas (1.9.1, the author: "a full shader check ... to make sure our editor
/// implementation matches exactly", "now that we're able to confirm in-game"):
/// the editor's bust shader port against the game's own drawing of a pack bust,
/// in the test save's room.
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>What bust-look wrote: its info.txt as key = value, and the screen.</summary>
    private sealed record Look(Dictionary<string, string> Info, int W, int H, byte[] Screen, string Folder)
    {
        public float F(string key) => float.Parse(Info[key].Split(' ')[0], CultureInfo.InvariantCulture);

        /// <summary>The art's corners on screen (uv 00, 10, 01, 11), pixels from the bottom
        /// left: "corners" where its transform puts them, "drawnCorners" where the
        /// game's pixel snapping moved them to draw.</summary>
        public (float x, float y)[] Corners(string key = "corners") => Info[key].Split(' ')[0].Split(';')
            .Select(c => c.Split(',')).Select(p => (float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture))).ToArray();

        /// <summary>The colour drawn at (x, y), y from the bottom: B, G, R.</summary>
        public (byte b, byte g, byte r) At(int x, int y)
        {
            int i = ((H - 1 - y) * W + x) * 4;
            return (Screen[i], Screen[i + 1], Screen[i + 2]);
        }
    }

    /// <summary>A bust shown, waited on past its fade-in, frozen and read. With
    /// <paramref name="plain"/>, drawn plain: the 2D lights neutral (point lights
    /// off, the global ones white at 1), the fake GI off and the camera's
    /// post-processing off, so the colours are the art's own; put back after.</summary>
    private Look LookAt(GameRun game, string bust, string name, bool plain)
    {
        string root = "2_Bust_Manager/" + bust;
        game.Ask("set-active " + root + " true");      // an error throws
        game.Ask("wait 5", 15);                 // past the bust's 4 s fade-in
        if (plain)
        {
            _out.WriteLine("   " + game.Ask("lights neutral"));
            game.Ask("fake-gi off");
            game.Ask("postfx off");
        }
        game.Ask("freeze on");
        game.Ask("wait 0.3", 10);
        string said;
        try { said = game.Ask("bust-look " + root + "/MBase1 " + name, 30); }
        finally
        {
            game.Ask("freeze off");
            if (plain)
            {
                game.Ask("postfx on");
                game.Ask("fake-gi on");
                game.Ask("lights restore");
            }
            game.Ask("set-active " + root + " false");
        }
        // "<name> <folder>": what follows "ok bust-look".
        string folder = said.Substring(name.Length).Trim();
        var info = File.ReadAllLines(Path.Combine(folder, "info.txt"))
            .Where(l => l.Contains('='))
            .GroupBy(l => l.Substring(0, l.IndexOf('=')))
            .ToDictionary(g => g.Key, g => g.Last().Substring(g.Key.Length + 1));
        var frame = BitmapFrame.Create(new Uri(Path.Combine(folder, "screen.png")), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var px = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
        foreach (var line in info.Where(kv => kv.Key is "time" or "corners" or "drawnCorners" or "pixelSnapSpacing" or "material" or "renderer" or "screen" or "background"
                                              || kv.Key.StartsWith("mat_") || kv.Key.StartsWith("_Global")))
            _out.WriteLine($"   {name} {line.Key} = {line.Value}");
        foreach (var light in File.ReadAllLines(Path.Combine(folder, "info.txt")).Where(l => l.StartsWith("light=") && l.Contains("lightsBody=True")))
            _out.WriteLine($"   {name} {light}");
        return new Look(info, bgra.PixelWidth, bgra.PixelHeight, px, folder);
    }

    /// <summary>
    /// Every interior pixel of the drawn bust names the texel it read (by the
    /// art's colour code - see InGameSuitePacks.Looks); the editor's
    /// port, at the game's frozen _Time and with the material's own values, must
    /// name the same one. Interior: far enough from the empty border that no
    /// displacement reaches it and the fake GI adds nothing - which the colours
    /// decoding cleanly proves, or lights and rim would have changed them.
    /// Where the bust is drawn: the main camera's Pixel Perfect Camera has the
    /// engine move every sprite to the nearest whole pixel as it draws
    /// (drawnCorners). Measured 2026-11-16: from the transform's own corners the
    /// noise looked a quarter texel off, 75% agreeing; from the snapped ones, 99%.
    /// Two allowances, each counted and printed. A tie: the port's sample within
    /// <see cref="Tie"/> texels of the edge of the texel the game read - the GPU's
    /// own float rounding decides those (measured: exactly on the edge, once the
    /// noise's hash took its dot product fused as the GPU does; with it rounded
    /// term by term, whole cells of the noise read another texel). The
    /// interface: a pixel the still outfit - no displacement, so every pixel reads
    /// its own texel - decodes more than two texels from its own, which is the
    /// game's buttons and bars over the bust, not the bust.
    /// The controls: the still outfit tests the screen mapping alone; on the noise
    /// and all outfits the unsnapped corners must agree visibly less, and on the
    /// noise outfit 1.9.0's noise must miss.
    /// </summary>
    [Fact]
    public void S1_The_bust_preview_moves_a_pack_bust_as_the_game_draws_it()
    {
        if (Off) return;
        var game = _s.InGame();
        int size = InGameSuitePacks.Looks.Size;
        // What the room draws through, for the record: its post-processing.
        _out.WriteLine(game.Ask("volume-settings"));
        foreach (var line in File.ReadAllLines(Path.Combine(game.BridgeFolder, "volumes.txt"))) _out.WriteLine("   " + line);
        var failures = new List<string>();
        var covered = new HashSet<(int x, int y)>();
        // Still first: with no mask, only the screen mapping is tested, and it
        // finds where the interface covers the bust.
        foreach (var (key, bust, mask) in InGameSuitePacks.Looks.Outfits.OrderBy(o => o.Key != "still"))
        {
            var look = LookAt(game, bust, key, plain: true);
            var c = look.Corners("drawnCorners");
            var raw = look.Corners();
            float x0 = c[0].x, x1 = c[1].x, y0 = c[0].y, y1 = c[2].y;
            Assert.True(Math.Abs(c[0].y - c[1].y) < 0.01 && Math.Abs(c[0].x - c[2].x) < 0.01, "the bust is not upright on screen: " + look.Info["drawnCorners"]);
            var p = new JiggleParams
            {
                Speed = look.F("mat_JiggleSpeed"), Strength = look.F("mat_JiggleStrength"), Frequency = look.F("mat_JiggleFrequency"),
                NoiseScale = look.F("mat_NoiseScale"), NoiseSpeed = look.F("mat_NoiseSpeed"), NoiseStrength = look.F("mat_NoiseStrength"),
            };
            float time = look.F("time");
            var frame = new JiggleShader.Frame(p, time);
            float r = mask.R / 255f, g = mask.G / 255f, b = mask.B / 255f, a = mask.A / 255f;

            if (key == "still")
            {
                // Where the interface covers the bust: over all of the still
                // outfit's art, not its interior only - the other outfits are
                // drawn a few pixels higher or lower (the bust's own idle motion).
                float art = (float)InGameSuitePacks.Looks.Border / size;
                for (int y = (int)Math.Ceiling(Math.Min(y0, y1)); y < (int)Math.Floor(Math.Max(y0, y1)); y++)
                    for (int x = (int)Math.Ceiling(Math.Min(x0, x1)); x < (int)Math.Floor(Math.Max(x0, x1)); x++)
                    {
                        float u = (x + 0.5f - x0) / (x1 - x0), v = (y + 0.5f - y0) / (y1 - y0);
                        if (u < art || u > 1 - art || v < art || v > 1 - art) continue;
                        var drawn = look.At(x, y);
                        var own = InGameSuitePacks.Looks.TexelOf(drawn.r, drawn.g, drawn.b);
                        int tx = Math.Clamp((int)MathF.Floor(u * size), 0, size - 1), ty = size - 1 - Math.Clamp((int)MathF.Floor(v * size), 0, size - 1);
                        if (own == null || Math.Abs(own.Value.x - tx) > 2 || Math.Abs(own.Value.y - ty) > 2) covered.Add((x, y));
                    }
            }

            int inside = 0, compared = 0, same = 0, ties = 0, rawSame = 0, oldSame = 0, underInterface = 0;
            float widestTie = 0f;
            var misses = new Dictionary<(int, int), int>();
            float margin = 32f / size;
            for (int y = (int)Math.Ceiling(Math.Min(y0, y1)); y < (int)Math.Floor(Math.Max(y0, y1)); y++)
                for (int x = (int)Math.Ceiling(Math.Min(x0, x1)); x < (int)Math.Floor(Math.Max(x0, x1)); x++)
                {
                    float u = (x + 0.5f - x0) / (x1 - x0), v = (y + 0.5f - y0) / (y1 - y0);
                    if (u < margin || u > 1 - margin || v < margin || v > 1 - margin) continue;
                    inside++;
                    if (covered.Contains((x, y))) { underInterface++; continue; }
                    // The bust's own pixels: colours the art's code names. The
                    // game's buttons and bars over it are not compared.
                    var drawn = look.At(x, y);
                    var texel = InGameSuitePacks.Looks.TexelOf(drawn.r, drawn.g, drawn.b);
                    if (texel == null) continue;
                    var (drawnX, drawnY) = texel.Value;
                    var (du, dv) = frame.Offset(r, g, b, a, u, v);
                    float sx = (u + du) * size, sy = (v + dv) * size;      // the port's sample, in texels; sy from the bottom
                    int tx = Math.Clamp((int)MathF.Floor(sx), 0, size - 1);
                    int ty = size - 1 - Math.Clamp((int)MathF.Floor(sy), 0, size - 1);
                    compared++;
                    if (drawnX == tx && drawnY == ty) same++;
                    else
                    {
                        // How far the port's sample lies outside the texel the game read.
                        float rowBottom = size - 1 - drawnY;
                        float outside = Math.Max(Math.Max(Math.Max(drawnX - sx, sx - (drawnX + 1)), Math.Max(rowBottom - sy, sy - (rowBottom + 1))), 0f);
                        if (outside <= Tie) { ties++; widestTie = Math.Max(widestTie, outside); }
                        else
                        {
                            var d = (drawnX - tx, drawnY - ty);
                            misses[d] = misses.GetValueOrDefault(d) + 1;
                        }
                    }
                    // The control: where the transform alone puts the bust.
                    float ru = (x + 0.5f - raw[0].x) / (raw[1].x - raw[0].x), rv = (y + 0.5f - raw[0].y) / (raw[2].y - raw[0].y);
                    var (rdu, rdv) = frame.Offset(r, g, b, a, ru, rv);
                    if (drawnX == Math.Clamp((int)MathF.Floor((ru + rdu) * size), 0, size - 1)
                        && drawnY == size - 1 - Math.Clamp((int)MathF.Floor((rv + rdv) * size), 0, size - 1)) rawSame++;
                    if (key == "noise")
                    {
                        // 1.9.0's noise: the level shader's gradient noise, timed by Noise Speed alone.
                        float amp = p.NoiseStrength * b * a;
                        float ncx = u * p.NoiseScale + time * p.NoiseSpeed, ncy = v * p.NoiseScale + time * p.NoiseSpeed;
                        int ox = Math.Clamp((int)MathF.Floor((u + OldNoise(ncx, ncy) * amp) * size), 0, size - 1);
                        int oy = size - 1 - Math.Clamp((int)MathF.Floor((v + OldNoise(ncx + 100f, ncy + 100f) * amp) * size), 0, size - 1);
                        if (drawnX == ox && drawnY == oy) oldSame++;
                    }
                }
            double Share(int n) => compared == 0 ? 0 : (double)n / compared;
            double agree = Share(same + ties);
            _out.WriteLine($"{key}: {same + ties} of {compared} pixels read the texel the port names ({agree:P2}): {same} exactly, {ties} ties within {widestTie:0.0000} texel of an edge;"
                           + $" {inside - compared} of {inside} covered by the game's interface ({underInterface} where the still outfit found it);"
                           + $" from the unsnapped corners {Share(rawSame):P2}"
                           + (key == "noise" ? $"; 1.9.0's noise names {oldSame} ({Share(oldSame):P2})" : ""));
            _out.WriteLine("   misses by (drawn - port) texels: " + string.Join(", ", misses.OrderByDescending(m => m.Value).Take(8).Select(m => $"{m.Key}x{m.Value}")));
            if (compared <= inside / 2) failures.Add($"{key}: only {compared} of {inside} pixels are the bust's own colours - lights or the rim still change them, or the bust is not where the check looked");
            if (agree < 0.999) failures.Add($"{key}: the game read another texel than the port at {compared - same - ties} of {compared} pixels");
            if (key is "noise" or "all" && Share(rawSame) > 0.95)
                failures.Add($"{key}: the unsnapped corners agree as well ({Share(rawSame):P2}) - the check no longer shows where the bust is drawn");
            if (key == "noise" && Share(oldSame) >= 0.5) failures.Add("1.9.0's noise lands on the game's - the check cannot tell them apart");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>Texels: how far outside the texel the game read the port's sample
    /// may lie and still count as the same read (the GPU's float rounding).</summary>
    private const float Tie = 0.005f;

    /// <summary>
    /// A pack's bust art - body and blink - and a pack NPC's are sampled at
    /// their edges as the game's own busts are: clamped, so a read the jiggle
    /// moves past an edge takes the edge's own texel, which is what the
    /// editor's previews draw. Until 1.9.1 they loaded with Unity's default,
    /// Repeat: a bust bouncing up read its TOP row - nothing - along its bottom
    /// edge, where the torso is cut off. The control: the game's own bust
    /// (Anna_YellowSexy) is not Repeat, so the check can tell a texture loaded
    /// with the default from one loaded as the game's are.
    /// </summary>
    [Fact]
    public void S2_A_pack_bust_and_NPC_are_clamped_at_their_edges_like_the_games_busts()
    {
        if (Off) return;
        var game = _s.InGame();
        var vanilla = SpriteTextures(game, "2_Bust_Manager/Anna_YellowSexy");
        var bust = SpriteTextures(game, "2_Bust_Manager/" + InGameSuitePacks.Looks.PartsBust);
        var npc = SpriteTextures(game, RoomLevel + "/NPCs/Walker");
        string wrap = vanilla.First(t => t.Path.EndsWith("/MBase1", StringComparison.Ordinal)).Wrap;
        _out.WriteLine("the game's bust: " + wrap + " (all its parts: " + string.Join(", ", vanilla.Select(t => t.Wrap).Distinct()) + ")");
        Assert.NotEqual("Repeat", wrap);
        // The pack's own art: textures the plugin made, which have no name.
        var own = bust.Concat(npc).Where(t => t.Name.Length == 0).ToList();
        foreach (var t in own) _out.WriteLine($"   {t.Path}: {t.Wrap}, {t.Filter}");
        Assert.True(bust.Count(t => t.Name.Length == 0) >= 2, "the pack bust's body and blink were not found: " + string.Join(" | ", bust));
        Assert.True(npc.Count(t => t.Name.Length == 0) >= 2, "the pack NPC's sprite and eyes were not found: " + string.Join(" | ", npc));
        var wrong = own.Where(t => t.Wrap != wrap).Select(t => t.Path + " " + t.Wrap).ToList();
        Assert.True(wrong.Count == 0, "wrapped unlike the game's busts (" + wrap + "): " + string.Join(", ", wrong));
    }

    /// <summary>
    /// A pack bust throws the shadow of its own art (1.9.1). The room's lamps
    /// cast a bust's shadow on the wall behind it, from a ShadowCaster2D that
    /// takes its shape from the bust's sprite. A pack bust is a clone of a game
    /// bust whose sprite is replaced while the clone is switched off, which the
    /// caster never noticed: every pack bust threw the cloned bust's shadow
    /// (Anna's). Measured: the shape the caster casts, on screen, against the
    /// pack art's sprite - the suite's art is opaque but for a thin border, so
    /// its shadow spans nearly the whole sprite. Control: the shape it cast
    /// before - the game bust's own, moved to where the pack bust stands -
    /// fails the same measure. And the game's bust keeps its own shadow: the
    /// clone shares its shape's mesh with the bust it came from, and the
    /// first fix rebuilt that mesh in place - Anna then threw the pack art's
    /// shadow, far wider than her own sprite, which this catches (the pack
    /// bust is read first, so its rebuild has happened).
    /// <para/>
    /// The shape is ShadowOutline's, exactly - the outline the editor's
    /// preview casts too - for the suite's square art and for the figure, a
    /// person's shape. Checked point for point in the caster's own units;
    /// control: the outline of Unity's own mesh of the sprite (what URP would
    /// have cast) is not it. The first version of the fix read the picture
    /// from Unity's crop of it and put every point 6 texels off, which the
    /// boxes above let through and this does not.
    /// <para/>
    /// And the pack bust's sprite is drawn over its whole picture: its mesh is
    /// its rectangle, where Unity's own (Tight) cut the art off wherever the
    /// jiggle moved it out past a coarse outline (S3's noise outfit). Control:
    /// the game's own bust keeps Unity's outline.
    /// </summary>
    [Fact]
    public void S5_A_pack_bust_throws_the_shadow_of_its_own_art()
    {
        if (Off) return;
        var game = _s.InGame();
        var pack = ShadowOf(game, "2_Bust_Manager/" + InGameSuitePacks.Looks.Outfits[4].Bust);
        var anna = ShadowOf(game, "2_Bust_Manager/Anna_YellowSexy");
        _out.WriteLine("pack bust: " + pack);
        _out.WriteLine("the game's bust: " + anna);
        Assert.True(pack.Casters.Length > 0, "the pack bust has no shadow caster");
        var before = anna.Caster.Shift(pack.Origin.x - anna.Origin.x, pack.Origin.y - anna.Origin.y);
        Assert.False(FollowsArt(before, pack.Sprite, out var control), "control: the game bust's shape passes as the pack art's: " + control);
        Assert.True(FollowsArt(pack.Caster, pack.Sprite, out var why), why);
        Assert.True(Inside(anna.Caster, anna.Sprite), "the game's bust no longer throws its own shadow: caster " + anna.Caster + " against its sprite " + anna.Sprite);
        // Control for the whole-picture check below: the game's own bust keeps
        // Unity's tight mesh of its art (measured 2026-11-16: 31 points).
        Assert.True(anna.SpriteOutline.Count > 4, $"control: the game's bust's sprite mesh has {anna.SpriteOutline.Count} points - the check cannot tell a whole picture from an outline");

        int size = InGameSuitePacks.Looks.Size;
        foreach (var (bust, art) in new (string, Func<int, int, Color>)[]
                 { (InGameSuitePacks.Looks.Outfits[4].Bust, InGameSuitePacks.Looks.ArtAt), (InGameSuitePacks.Looks.FigureBust, InGameSuitePacks.Looks.FigureAt) })
        {
            var cast = ShadowOf(game, "2_Bust_Manager/" + bust);
            var alpha = new byte[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    alpha[(size - 1 - y) * size + x] = art(x, y).A;
            var corners = SMSModForge.Shared.ShadowOutline.Of(alpha, size, size, 100f);
            var expected = Enumerable.Range(0, corners.Length / 2).Select(i => ((corners[2 * i] - 128) / 100f, (corners[2 * i + 1] - 128) / 100f)).ToList();
            _out.WriteLine($"{bust}: outline of {expected.Count} points, the caster's {cast.Outline.Count}, the sprite's mesh {cast.SpriteOutline.Count}");
            Assert.False(SameOutline(cast.SpriteOutline, expected), "control: the mesh of " + bust + "'s sprite is the outline");
            // Drawn over its whole picture (FittedSprite.MovingMesh): the sprite's
            // mesh is its rectangle, not Unity's tight outline of the art.
            bool whole = cast.SpriteOutline.Count == 4
                         && cast.SpriteOutline.All(q => Math.Abs(Math.Abs(q.x) - 1.28f) < 1e-3f && Math.Abs(Math.Abs(q.y) - 1.28f) < 1e-3f);
            Assert.True(whole, bust + " is drawn only inside an outline of its art: " + string.Join(" ", cast.SpriteOutline.Take(8)));
            Assert.True(SameOutline(cast.Outline, expected), bust + "'s caster does not cast ShadowOutline's outline: " + string.Join(" ", cast.Outline.Take(6)) + " ... against " + string.Join(" ", expected.Take(6)));
        }
    }

    /// <summary>The same points in the same order, to a ten-thousandth of a unit.</summary>
    private static bool SameOutline(List<(float x, float y)> a, List<(float x, float y)> b)
        => a.Count == b.Count && a.Zip(b).All(p => Math.Abs(p.First.x - p.Second.x) < 1e-4f && Math.Abs(p.First.y - p.Second.y) < 1e-4f);

    /// <summary>A rectangle on screen, in pixels from the bottom left.</summary>
    private sealed record ScreenBox(float X0, float Y0, float X1, float Y1)
    {
        public float W => X1 - X0;
        public float H => Y1 - Y0;
        public ScreenBox Shift(float dx, float dy) => new(X0 + dx, Y0 + dy, X1 + dx, Y1 + dy);
        public static ScreenBox Parse(string s)
        {
            var v = s.Split(',').Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            return new ScreenBox(v[0], v[1], v[2], v[3]);
        }
        public override string ToString() => FormattableString.Invariant($"{X0:0.0},{Y0:0.0}..{X1:0.0},{Y1:0.0} ({W:0.0}x{H:0.0})");
    }

    /// <summary>What a bust's sprite root casts: the caster's shape and the
    /// sprite's mesh on screen, the root's origin, and which of the bust's
    /// objects carry a caster.</summary>
    private sealed record Shadow(ScreenBox Caster, ScreenBox Sprite, (float x, float y) Origin, string[] Casters,
                                 List<(float x, float y)> Outline, List<(float x, float y)> SpriteOutline)
    {
        public override string ToString() => "caster " + Caster + ", sprite " + Sprite + ", casters on " + string.Join(", ", Casters);
    }

    /// <summary>The bust shown for a few frames - the caster rebuilds in its
    /// own Update - then read.</summary>
    private Shadow ShadowOf(GameRun game, string root)
    {
        game.Ask("set-active " + root + " true");
        try
        {
            game.Ask("wait 3", 10);
            game.Ask("frames");
            string reply = game.Ask("shadow-mesh " + root + "/MBase1", 20);
            var parts = reply.Split("; ").Skip(1).Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
            var o = parts["origin"].Split(',').Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            // The caster's outline: its mesh's unprojected points (tangent x -1),
            // in its own units, in the path's order.
            var mesh = File.ReadAllLines(Path.Combine(game.BridgeFolder, "shadow-mesh.txt"));
            static (float, float) Xy(string s) { var v = s.Split(','); return (float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture)); }
            var outline = mesh.Where(l => l.StartsWith("cl ") && l.Split(' ')[3].StartsWith("-1,")).Select(l => Xy(l.Split(' ')[1])).ToList();
            var sprite = mesh.Where(l => l.StartsWith("sl ")).Select(l => Xy(l.Split(' ')[1])).ToList();
            return new Shadow(ScreenBox.Parse(parts["casterBounds"]), ScreenBox.Parse(parts["spriteBounds"]), (o[0], o[1]),
                              parts["casters"].Split('|', StringSplitOptions.RemoveEmptyEntries), outline, sprite);
        }
        finally { game.Ask("set-active " + root + " false"); }
    }

    /// <summary>A caster's shape is the art's when it lies within the sprite
    /// and spans most of it both ways (the suite's art fills its sprite but for
    /// the border; URP trims a little off the outline).</summary>
    private static bool Inside(ScreenBox caster, ScreenBox sprite, float within = 1f)
        => caster.X0 >= sprite.X0 - within && caster.Y0 >= sprite.Y0 - within && caster.X1 <= sprite.X1 + within && caster.Y1 <= sprite.Y1 + within;

    private static bool FollowsArt(ScreenBox caster, ScreenBox sprite, out string why)
    {
        const float Spans = 0.85f;
        why = "caster " + caster + " against the sprite " + sprite;
        if (!Inside(caster, sprite))
        {
            why += ": reaches outside the sprite";
            return false;
        }
        if (caster.W < Spans * sprite.W || caster.H < Spans * sprite.H)
        {
            why += FormattableString.Invariant($": spans {caster.W / sprite.W:P0} x {caster.H / sprite.H:P0} of it");
            return false;
        }
        return true;
    }

    /// <summary>
    /// The bust preview over the player's room draws what the game draws there
    /// (1.9.1, the author: "the sprites in game look differently based on the
    /// lighting, with some sprite edge lighting in place too"; "Ultra should be
    /// the 1:1 comparison to what we see in the game"): the coded bust shown in
    /// the room - its fake GI, the room's lamp, its shadow and glow, the
    /// post-processing all on; the interface and the room's buttons off - and
    /// the preview's own frame for the same outfit, at the game's frozen time,
    /// the height the game drew the bust at and the place its shadow was cast
    /// from, compared pixel by pixel over the frame. The figure is a person's
    /// shape, so its edge glow and its shadow fall on the room; the square art
    /// covers nearly all the frame. The room's moving parts (its plant) are left
    /// out. Control: the preview without its fake GI misses the bust's edge,
    /// where the game lights it.
    /// </summary>
    [Fact]
    public void S3_The_bust_preview_over_the_room_draws_what_the_game_draws_there()
    {
        if (Off) return;
        var game = _s.InGame();
        const string Buttons = "5_Levels/5_MyRoom/PlayerRoom_ButtonCanvas";
        var backdrop = PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom"));
        var scene = new BackdropScene(backdrop);
        var gi = scene.Prepare();
        var lamp = scene.Lamp ?? throw new InvalidOperationException("the shipped backdrop has no lamp");
        int size = InGameSuitePacks.Looks.Size;
        byte[] ArtOf(Func<int, int, System.Windows.Media.Color> at)
        {
            var art = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var c = at(x, y);
                    int i = (y * size + x) * 4;
                    art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
                }
            return art;
        }
        var failures = new List<string>();
        foreach (var key in new[] { "figure", "still", "noise" })
        {
            Func<int, int, System.Windows.Media.Color> artAt = key == "figure" ? InGameSuitePacks.Looks.FigureAt : InGameSuitePacks.Looks.ArtAt;
            string bust = key == "figure" ? InGameSuitePacks.Looks.FigureBust : InGameSuitePacks.Looks.Outfits.First(o => o.Key == key).Bust;
            var maskColour = key == "figure" ? System.Windows.Media.Color.FromArgb(0, 0, 0, 0) : InGameSuitePacks.Looks.Outfits.First(o => o.Key == key).Mask;
            var art = ArtOf(artAt);
            var giArt = new FakeGi.Art(art, size, size, gi.Reach);
            string root = "2_Bust_Manager/" + bust;
            game.Ask("set-active " + root + " true");
            game.Ask("wait 5", 15);
            game.Ask("set-active 9_MainCanvas false");
            game.Ask("set-active " + Buttons + " false");
            // the post-processing on: what the player sees
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            string said, meshReply, still, later;
            try
            {
                said = game.Ask("bust-look " + root + "/MBase1 room-" + key, 30);
                meshReply = game.Ask("shadow-mesh " + root + "/MBase1", 20);
                // The room alone at that moment and a moment later: what moves by
                // itself is left out (the bust's shadow does not: it is compared).
                game.Ask("set-active " + root + " false");
                game.Ask("wait 0.3", 10);
                still = LookFolder(game.Ask("screen-look room-" + key + "-still", 15));
                game.Ask("freeze off");
                game.Ask("wait 1.3", 10);
                game.Ask("freeze on");
                game.Ask("wait 0.3", 10);
                later = LookFolder(game.Ask("screen-look room-" + key + "-later", 15));
            }
            finally
            {
                game.Ask("freeze off");
                // (the post-processing was left on)
                game.Ask("set-active " + Buttons + " true");
                game.Ask("set-active 9_MainCanvas true");
                game.Ask("set-active " + root + " false");
            }
            var look = LookOf(said.Substring(("room-" + key).Length).Trim());
            var (_, _, stillPx) = Png(Path.Combine(still, "screen.png"));
            var (_, _, laterPx) = Png(Path.Combine(later, "screen.png"));
            var origin = meshReply.Split("; ").First(q => q.StartsWith("origin=", StringComparison.Ordinal)).Substring(7).Split(',')
                                  .Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            var corners = look.Corners("drawnCorners");
            int bustBottom = (int)Math.Round(corners[0].y);
            float time = look.F("time");
            var p = new JiggleParams
            {
                Speed = look.F("mat_JiggleSpeed"), Strength = look.F("mat_JiggleStrength"), Frequency = look.F("mat_JiggleFrequency"),
                NoiseScale = look.F("mat_NoiseScale"), NoiseSpeed = look.F("mat_NoiseSpeed"), NoiseStrength = look.F("mat_NoiseStrength"),
            };
            var mask = new byte[size * size * 4];
            for (int i = 0; i < mask.Length; i += 4) { mask[i] = maskColour.B; mask[i + 1] = maskColour.G; mask[i + 2] = maskColour.R; mask[i + 3] = maskColour.A; }
            var caster = new BustShadow.Caster(art, size, size, 100f);
            var shadow = new float[lamp.Shadow.W * lamp.Shadow.H];
            lamp.Shadow.Draw(caster, origin[0], origin[1], 2f, 1f, shadow);

            // The preview's frame, as JigglePreview makes it over the backdrop.
            byte[] Frame(FakeGi? withGi)
            {
                const int N = BackdropRender.FrameSize;
                var target = new GamePost.Target(backdrop.ScreenWidth, backdrop.ScreenHeight);
                var background = new float[backdrop.BackgroundWidth * backdrop.BackgroundHeight * 4];
                lamp.Room(shadow, target, background, backdrop.BackgroundWidth, backdrop.BackgroundHeight);
                withGi?.UseBackground(background);
                var body = new float[N * N * 4];
                BackdropRender.Body(art, mask, p, (1f, 1f, 1f, 1f), time, withGi, giArt, scene.Left, scene.FrameBottom, bustBottom, body, true, null, 0f, true, scene.Decode);
                var coverage = new float[N * N];
                BackdropRender.Layer(target, scene, body, coverage);
                lamp.Glow(target, shadow, BackdropRender.Covered(scene, coverage));
                var output = new byte[N * N * 4];
                BackdropRender.Present(target, scene, BackdropRender.PostParts.All, output);
                return output;
            }
            var preview = Frame(gi);
            var bare = Frame(null);
            SaveFrame(preview, Path.Combine(look.Folder, "preview.png"));
            SaveFrame(bare, Path.Combine(look.Folder, "preview-without-gi.png"));
            // The same frame drawn on the graphics card (Rendering.Gpu.GpuFrame,
            // Options > Draw previews on), at the test copy's zoom: held to the
            // game by the same measure.
            byte[] onCard = new byte[BackdropRender.FrameSize * BackdropRender.FrameSize * 4];
            using (var card = SMSModForge.Rendering.Gpu.GpuFrame.TryCreate() ?? throw new InvalidOperationException("no graphics card device"))
                card.Draw(lamp, shadow, scene, art, mask, p, (1f, 1f, 1f, 1f), time, true, bustBottom, true, 0f, null, null, null,
                          BackdropRender.PostParts.All, 1, onCard);
            SaveFrame(onCard, Path.Combine(look.Folder, "preview-card.png"));

            const int F = BackdropRender.FrameSize;
            int compared = 0, within1 = 0, within3 = 0, moving = 0, edgeCompared = 0, edgeWithin3 = 0, bareEdgeWithin3 = 0, worst = 0;
            int cardWithin1 = 0, cardWithin3 = 0, cardEdgeWithin3 = 0;
            for (int y = 0; y < F; y++)
                for (int x = 0; x < F; x++)
                {
                    int sx = scene.Left + x, sy = scene.FrameBottom + (F - 1 - y);
                    var g = look.At(sx, sy);
                    int li = ((look.H - 1 - sy) * look.W + sx) * 4;
                    if (Math.Abs(stillPx[li] - laterPx[li]) > 1 || Math.Abs(stillPx[li + 1] - laterPx[li + 1]) > 1 || Math.Abs(stillPx[li + 2] - laterPx[li + 2]) > 1)
                    {
                        // Moved by itself: the plant. Where the bust covers it, the
                        // bust is still compared.
                        float uv = (sy + 0.5f - bustBottom) / F;
                        int tx = x / 2, trow = size - 1 - (int)MathF.Floor(uv * size);
                        bool onArt = uv >= 0 && uv < 1 && trow >= 0 && trow < size && artAt(tx, trow).A > 0;
                        if (!onArt) { moving++; continue; }
                    }
                    int o = (y * F + x) * 4;
                    int d = Math.Max(Math.Abs(preview[o] - g.b), Math.Max(Math.Abs(preview[o + 1] - g.g), Math.Abs(preview[o + 2] - g.r)));
                    int db = Math.Max(Math.Abs(bare[o] - g.b), Math.Max(Math.Abs(bare[o + 1] - g.g), Math.Abs(bare[o + 2] - g.r)));
                    int dc = Math.Max(Math.Abs(onCard[o] - g.b), Math.Max(Math.Abs(onCard[o + 1] - g.g), Math.Abs(onCard[o + 2] - g.r)));
                    compared++;
                    worst = Math.Max(worst, d);
                    if (d <= 1) within1++;
                    if (d <= 3) within3++;
                    if (dc <= 1) cardWithin1++;
                    if (dc <= 3) cardWithin3++;
                    // The bust's edge: where the fake GI changes the preview by more
                    // than the closeness asked for (four levels or more).
                    if (Math.Max(Math.Abs(preview[o] - bare[o]), Math.Max(Math.Abs(preview[o + 1] - bare[o + 1]), Math.Abs(preview[o + 2] - bare[o + 2]))) >= 4)
                    {
                        edgeCompared++;
                        if (d <= 3) edgeWithin3++;
                        if (db <= 3) bareEdgeWithin3++;
                        if (dc <= 3) cardEdgeWithin3++;
                    }
                }
            double Share(int n, int of) => of == 0 ? 0 : (double)n / of;
            _out.WriteLine($"{key}: {compared} frame pixels ({moving} moving in the room left out): within one level {Share(within1, compared):P2}, within three {Share(within3, compared):P2}, worst {worst}; "
                           + $"the bust's lit edge ({edgeCompared} pixels): within three {Share(edgeWithin3, edgeCompared):P2}, without the fake GI {Share(bareEdgeWithin3, edgeCompared):P2}");
            _out.WriteLine($"{key} on the graphics card: within one level {Share(cardWithin1, compared):P2}, within three {Share(cardWithin3, compared):P2}; lit edge within three {Share(cardEdgeWithin3, edgeCompared):P2}");
            if (Share(cardWithin3, compared) < 0.99) failures.Add($"{key}: the card's preview is more than three levels from the game at {compared - cardWithin3} of {compared} pixels");
            if (Share(cardEdgeWithin3, edgeCompared) < 0.9) failures.Add($"{key}: the card's lit edge is more than three levels off at {edgeCompared - cardEdgeWithin3} of {edgeCompared}");
            if (Share(within3, compared) < 0.99) failures.Add($"{key}: the preview is more than three levels from the game at {compared - within3} of {compared} pixels");
            if (Share(edgeWithin3, edgeCompared) < 0.9) failures.Add($"{key}: the bust's lit edge is more than three levels off at {edgeCompared - edgeWithin3} of {edgeCompared}");
            if (edgeCompared < 300 || Share(bareEdgeWithin3, edgeCompared) > Share(edgeWithin3, edgeCompared) - 0.3)
                failures.Add($"{key}: without its fake GI the preview's edge is nearly as close - the check does not see the edge lighting");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// What the bust preview's backdrop is taken from (1.9.1, the author: "The
    /// default should be the player's room level, which is where we load the
    /// game into"): the room as the bust shader's fake GI reads it - the
    /// Default layer as lit, without the interface, the room's own buttons
    /// (drawn over busts) or the post-processing - and what the room's lights
    /// do to a bust drawn over it, from two flat grey busts: per screen pixel,
    /// drawn = grey x multiply + add, in linear light. The multiply comes out 1
    /// (measured 2026-11-16: 0.99-1.005, the 8-bit steps), so the lights only
    /// add. Written to the looks folder; Tools/Game/backdrop-from-game.py makes
    /// the editor's copy from it (Resources/Backdrops/MyRoom), and this holds
    /// the shipped copy to the game. The control: the room with its lights off
    /// must not pass for it.
    /// </summary>
    [Fact]
    public void S4_The_shipped_room_backdrop_is_the_room_the_game_draws_behind_a_bust()
    {
        if (Off) return;
        var game = _s.InGame();
        game.Ask("wait 8", 20);                  // past a fresh load's fade in from black
        const string Buttons = "5_Levels/5_MyRoom/PlayerRoom_ButtonCanvas";
        string room, later, dark = "", light = "", unlit;
        var laters = new List<string>();
        var lampOnly = new List<string>();
        game.Ask("set-active 9_MainCanvas false");
        game.Ask("set-active " + Buttons + " false");
        // What the post-processing runs with, while it is on: URP's grading table
        // and both passes' settings.
        game.Ask("wait 0.3", 10);
        game.Ask("rt-save _InternalGradingLut post-lut.bin", 20);
        game.Ask("material-dump Hidden/Kronnect/Beautify", 20);
        File.Copy(Path.Combine(game.BridgeFolder, "material-dump.txt"), Path.Combine(game.BridgeFolder, "post-beautify.txt"), true);
        game.Ask("material-dump Hidden/Universal Render Pipeline/UberPost", 20);
        File.Copy(Path.Combine(game.BridgeFolder, "material-dump.txt"), Path.Combine(game.BridgeFolder, "post-uberpost.txt"), true);
        game.Ask("postfx off");
        // A look's HDR frame (the camera's target) and the background the bust
        // shader's fake GI reads (_CameraSortingLayerTexture), beside it.
        void Hdr(string folder)
        {
            string at = Path.GetRelativePath(game.BridgeFolder, folder);
            game.Ask("rt-save _CameraTarget " + Path.Combine(at, "frame.bin"), 20);
            game.Ask("rt-save _CameraSortingLayerTexture " + Path.Combine(at, "background.bin"), 20);
        }
        try
        {
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            room = LookFolder(game.Ask("screen-look backdrop-room", 15));
            Hdr(room);
            foreach (var f in new[] { "post-lut.bin", "post-beautify.txt", "post-uberpost.txt" })
                File.Copy(Path.Combine(game.BridgeFolder, f), Path.Combine(room, f), true);
            // The room's own moving parts (its plant sways, about 3% of the
            // screen, left of the bust): what differs a moment later is not compared.
            // Three looks over five seconds, as the level backdrops' (S7): with one,
            // pixels of the plant that happened to match it were held to the shipped
            // room and failed it one run in two (1.9.1 release, 0.2-0.45%).
            foreach (var (wait, n) in new[] { (1.3, ""), (1.3, "-2"), (2.6, "-3") })
            {
                game.Ask("freeze off");
                game.Ask("wait " + wait.ToString(CultureInfo.InvariantCulture), 10);
                game.Ask("freeze on");
                game.Ask("wait 0.3", 10);
                laters.Add(LookFolder(game.Ask("screen-look backdrop-room-later" + n, 15)));
            }
            // The game stays still on the last of them for the looks that follow.
            later = laters[^1];
            _out.WriteLine("   " + game.Ask("lights neutral"));
            game.Ask("wait 0.3", 10);
            unlit = LookFolder(game.Ask("screen-look backdrop-unlit", 15));
            Hdr(unlit);
            game.Ask("lights restore");
            // Each lamp alone: what it adds to the room, which a bust's shadow
            // takes from (at the same moment as the unlit room).
            for (int k = 0; k < Lamps.Length; k++)
            {
                foreach (var other in Lamps.Where((_, j) => j != k)) game.Ask("set-active " + other + " false");
                game.Ask("wait 0.3", 10);
                lampOnly.Add(LookFolder(game.Ask("screen-look backdrop-lamp-" + k, 15)));
                Hdr(lampOnly[k]);
                foreach (var other in Lamps.Where((_, j) => j != k)) game.Ask("set-active " + other + " true");
                File.WriteAllText(Path.Combine(lampOnly[k], "lamp.txt"), game.Ask("fields Light2D " + Lamps[k]));
            }
            // The room before the lamp's glow is added (its light volume off):
            // with the room unlit, what pins down the room's own colour, which
            // the lamp's light multiplies (GameLamp). And the numbers the lamp's
            // light and its shadow are drawn with.
            game.Ask("set-field Light2D m_LightVolumeEnabled false " + Lamps[0]);
            game.Ask("wait 0.3", 10);
            string noGlow = LookFolder(game.Ask("screen-look backdrop-noglow", 15));
            Hdr(noGlow);
            game.Ask("set-field Light2D m_LightVolumeEnabled true " + Lamps[0]);
            File.WriteAllText(Path.Combine(room, "shadow-light.txt"), game.Ask("shadow-light " + Lamps[0]));
            File.WriteAllText(Path.Combine(room, "lights-list.txt"), game.Ask("lights-list"));
            File.WriteAllText(Path.Combine(room, "lamp-fields.txt"), game.Ask("fields Light2D " + Lamps[0]));
            game.Ask("texture-save _FalloffLookup falloff.bin", 20);
            File.Copy(Path.Combine(game.BridgeFolder, "falloff.bin"), Path.Combine(room, "falloff.bin"), true);
            game.Ask("asset-fields Renderer2DData", 20);
            File.Copy(Path.Combine(game.BridgeFolder, "asset-fields.txt"), Path.Combine(room, "renderer2d.txt"), true);
            game.Ask("freeze off");
            game.Ask("fake-gi off");
            foreach (var (key, bust, _) in InGameSuitePacks.Looks.Greys)
            {
                string root = "2_Bust_Manager/" + bust;
                game.Ask("set-active " + root + " true");
                game.Ask("wait 5", 15);
                game.Ask("freeze on");
                game.Ask("wait 0.3", 10);
                try
                {
                    string said = game.Ask("bust-look " + root + "/MBase1 backdrop-" + key, 30);
                    string folder = said.Substring(("backdrop-" + key).Length).Trim();
                    if (key == "grey-dark") dark = folder; else light = folder;
                }
                finally
                {
                    game.Ask("freeze off");
                    game.Ask("set-active " + root + " false");
                }
            }
        }
        finally
        {
            game.Ask("fake-gi on");
            game.Ask("postfx on");
            game.Ask("set-active " + Buttons + " true");
            game.Ask("set-active 9_MainCanvas true");
        }
        _out.WriteLine("room " + room);
        foreach (var line in File.ReadAllLines(Path.Combine(room, "info.txt")).Where(l => l.StartsWith("light=") || l.StartsWith("layer="))) _out.WriteLine("   " + line);
        // Where the game puts a bust: SpriteManager's layout, breathing and parallax.
        string manager = game.Ask("fields SpriteManager 2_Bust_Manager");
        string anchor = game.Ask("find Screen_Center");
        File.WriteAllText(Path.Combine(room, "bust-manager.txt"), manager + Environment.NewLine + anchor);
        _out.WriteLine("   anchor: " + anchor);

        // The lights on a bust, per screen pixel, from the two greys.
        var lookDark = LookOf(dark);
        var lookLight = LookOf(light);
        float g1 = Srgb.ToLinear(InGameSuitePacks.Looks.Greys[0].Grey), g2 = Srgb.ToLinear(InGameSuitePacks.Looks.Greys[1].Grey);
        var cd = lookDark.Corners("drawnCorners"); var cl = lookLight.Corners("drawnCorners");
        // Pixels whose centres both greys cover.
        int xa = (int)Math.Ceiling(Math.Max(cd[0].x, cl[0].x) - 0.5f), xb = (int)Math.Floor(Math.Min(cd[1].x, cl[1].x) - 0.5f) + 1;
        int ya = Math.Max(0, (int)Math.Ceiling(Math.Max(cd[0].y, cl[0].y) - 0.5f)), yb = Math.Min(lookDark.H, (int)Math.Floor(Math.Min(cd[2].y, cl[2].y) - 0.5f) + 1);
        int mw = xb - xa, mh = yb - ya;
        var map = new float[mw * mh * 6];
        int far = 0;
        for (int y = ya; y < yb; y++)
            for (int x = xa; x < xb; x++)
            {
                var d1 = lookDark.At(x, y); var d2 = lookLight.At(x, y);
                int i = ((y - ya) * mw + (x - xa)) * 6;
                bool off = false;
                foreach (var (c, v1, v2) in new[] { (0, d1.r, d2.r), (1, d1.g, d2.g), (2, d1.b, d2.b) })
                {
                    float l1 = Srgb.ToLinear(v1), l2 = Srgb.ToLinear(v2);
                    float m = (l2 - l1) / (g2 - g1);
                    map[i + c] = m; map[i + 3 + c] = l1 - m * g1;
                    if (Math.Abs(m - 1) > 0.05f) off = true;
                }
                if (off) far++;
            }
        File.WriteAllBytes(Path.Combine(room, "lights.bin"), map.SelectMany(BitConverter.GetBytes).ToArray());
        File.WriteAllLines(Path.Combine(room, "lights.txt"), new[] { $"region={xa},{ya},{xb},{yb}  (screen pixels from the bottom left, both greys over it)" });
        _out.WriteLine($"lights over {mw}x{mh} px: {far} pixels multiplied by something other than 1 (of {mw * mh})");
        Assert.True(far < mw * mh / 100, $"the room's lights multiply a bust at {far} of {mw * mh} pixels - the backdrop takes them only as an add");

        // The shipped copy, held to what the game just drew.
        string shipped = Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom");
        string refresh = "python Tools/Game/backdrop-from-game.py \"" + room + "\"";
        Assert.True(File.Exists(Path.Combine(shipped, "backdrop.json")), "no backdrop shipped - make it: " + refresh);
        var (sw, sh, shippedRoom) = Png(Path.Combine(shipped, "room.png"));
        var (rw, rh, drawnRoom) = Png(Path.Combine(room, "screen.png"));
        var (_, _, unlitRoom) = Png(Path.Combine(unlit, "screen.png"));
        var (_, _, laterRoom) = Png(Path.Combine(later, "screen.png"));
        var laterRooms = laters.Select(l => Png(Path.Combine(l, "screen.png")).Item3).ToList();
        Assert.True(sw == rw && sh == rh, $"the shipped room is {sw}x{sh}, the game's screen {rw}x{rh}");
        static bool Near(byte[] a, byte[] b, int i) => Math.Abs(a[i] - b[i]) <= 1 && Math.Abs(a[i + 1] - b[i + 1]) <= 1 && Math.Abs(a[i + 2] - b[i + 2]) <= 1;
        bool Still(int i) => laterRooms.All(l => Near(drawnRoom, l, i));
        int moving = 0;
        for (int i = 0; i < drawnRoom.Length; i += 4) if (!Still(i)) moving++;
        double Same(byte[] a, byte[] b)
        {
            int n = 0, of = 0;
            for (int i = 0; i < a.Length; i += 4)
            {
                if (!Still(i)) continue;
                of++;
                if (Near(a, b, i)) n++;
            }
            return n / (double)of;
        }
        double room1 = Same(shippedRoom, drawnRoom), control = Same(shippedRoom, unlitRoom);
        _out.WriteLine($"{moving} of {drawnRoom.Length / 4} pixels move in the room (left out)");
        Assert.True(moving < drawnRoom.Length / 4 / 20, $"{moving} pixels of the room move - more than its plant (about 3% of the screen)");
        _out.WriteLine($"the shipped room: {room1:P2} of the still pixels within one level of the game's; the room unlit: {control:P2}");
        Assert.True(control < 0.9, "the room with its lights off passes for the shipped one - the check cannot tell");
        Assert.True(room1 >= 0.999, "the shipped room is not the game's any more - refresh it: " + refresh);

        // The lamps add in linear light: the unlit room and what each adds alone
        // make the lit room (the model a bust's shadow is taken from).
        var lamps = lampOnly.Select(f => Png(Path.Combine(f, "screen.png")).Item3).ToList();
        int adds = 0, addOf = 0;
        for (int i = 0; i < laterRoom.Length; i += 4)
        {
            if (!Still(i)) continue;
            addOf++;
            bool ok = true;
            for (int c = 0; c < 3; c++)
            {
                float sum = Srgb.ToLinear(unlitRoom[i + c]);
                foreach (var l in lamps) sum += Srgb.ToLinear(l[i + c]) - Srgb.ToLinear(unlitRoom[i + c]);
                if (Math.Abs(BackdropRender.ToSrgbByte(sum) - laterRoom[i + c]) > 1) ok = false;
            }
            if (ok) adds++;
        }
        _out.WriteLine($"the unlit room and each lamp alone add up to the lit room at {adds} of {addOf} still pixels");
        Assert.True(adds >= addOf * 0.999, "the lamps do not add up to the lit room - a shadow cannot be taken from them one by one");

        var backdrop = PreviewBackdrop.Load(shipped);
        int near = 0, all = 0;
        double worst = 0;
        for (int y = ya; y < yb; y++)
            for (int x = xa; x < xb; x++)
            {
                int i = ((y - ya) * mw + (x - xa)) * 6;
                if (Math.Abs(map[i] - 1) > 0.05f || Math.Abs(map[i + 1] - 1) > 0.05f || Math.Abs(map[i + 2] - 1) > 0.05f) continue;
                var (ar, ag, ab) = backdrop.LightAt(x + 0.5f, y + 0.5f);
                double d = Math.Max(Math.Abs(ar - map[i + 3]), Math.Max(Math.Abs(ag - map[i + 4]), Math.Abs(ab - map[i + 5])));
                worst = Math.Max(worst, d);
                all++;
                if (d <= 0.004) near++;
            }
        _out.WriteLine($"the shipped lights: {near} of {all} pixels within 0.004 of the game's add (worst {worst:0.0000})");
        Assert.True(near >= all * 0.99, "the shipped lights are not the game's any more - refresh them: " + refresh);
    }

    /// <summary>A preview frame (BGRA, rows top-down) as a PNG, beside the
    /// game's capture, for a person or a script to compare.</summary>
    private static void SaveFrame(byte[] bgra, string path)
    {
        int n = BackdropRender.FrameSize;
        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, bgra, n * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>The player's room's own lights (5_MyRoom's two Light 2D).</summary>
    private static readonly string[] Lamps = { "5_Levels/5_MyRoom/Light 2D", "5_Levels/5_MyRoom/Light 2D (1)" };

    private static string LookFolder(string said) => said.Substring(said.IndexOf(' ') + 1).Trim();

    private Look LookOf(string folder)
    {
        var info = File.ReadAllLines(Path.Combine(folder, "info.txt")).Where(l => l.Contains('='))
            .GroupBy(l => l.Substring(0, l.IndexOf('='))).ToDictionary(g => g.Key, g => g.Last().Substring(g.Key.Length + 1));
        var (w, h, px) = Png(Path.Combine(folder, "screen.png"));
        return new Look(info, w, h, px, folder);
    }

    private static (int w, int h, byte[] bgra) Png(string path)
    {
        var frame = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var px = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
        return (bgra.PixelWidth, bgra.PixelHeight, px);
    }

    private static class Srgb
    {
        public static float ToLinear(byte b)
        {
            double c = b / 255.0;
            return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
    }

    private sealed record SpriteTexture(string Path, string Name, string Filter, string Wrap);

    /// <summary>What sprite-textures says: "n: path = 'name' WxH filter=F wrap=W | ...".</summary>
    private static List<SpriteTexture> SpriteTextures(GameRun game, string path)
    {
        string said = game.Ask("sprite-textures " + path);
        return said.Substring(said.IndexOf(':') + 1).Split(" | ", StringSplitOptions.RemoveEmptyEntries).Select(e =>
        {
            int eq = e.IndexOf(" = '", StringComparison.Ordinal);
            int close = e.IndexOf('\'', eq + 4);
            string tail = e.Substring(close + 1);
            string Of(string key) => tail.Split(' ').First(w => w.StartsWith(key + "=", StringComparison.Ordinal)).Substring(key.Length + 1);
            return new SpriteTexture(e.Substring(0, eq).Trim(), e.Substring(eq + 4, close - eq - 4), Of("filter"), Of("wrap"));
        }).ToList();
    }

    private static float OldNoise(float px, float py)
    {
        static (float, float) Hash(float ix, float iy)
        {
            float g1 = MathF.Sin(ix * 127.1f + iy * 311.7f) * 43758.5453f, g2 = MathF.Sin(ix * 269.5f + iy * 183.3f) * 43758.5453f;
            g1 -= MathF.Floor(g1); g2 -= MathF.Floor(g2);
            return (g1 * 2f - 1f, g2 * 2f - 1f);
        }
        float ix = MathF.Floor(px), iy = MathF.Floor(py), fx = px - ix, fy = py - iy;
        float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
        var (a0, a1) = Hash(ix, iy); var (b0, b1) = Hash(ix + 1f, iy);
        var (c0, c1) = Hash(ix, iy + 1f); var (d0, d1) = Hash(ix + 1f, iy + 1f);
        float n00 = a0 * fx + a1 * fy, n10 = b0 * (fx - 1f) + b1 * fy, n01 = c0 * fx + c1 * (fy - 1f), n11 = d0 * (fx - 1f) + d1 * (fy - 1f);
        float bottom = n00 + ux * (n10 - n00), top = n01 + ux * (n11 - n01);
        return bottom + uy * (top - bottom);
    }
}
