using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// S6 (1.9.1, the author: "Add it" - the bust's shadow on the room): the
/// preview's room, lit by its lamp through a bust's shadow, against the
/// game's own frame of the same bust in the same room.
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// The room's lamp throws the figure bust's shadow on the room, and the
    /// preview draws the room as the game does with it there: the frame the
    /// camera's target holds (post-processing off), pixel for pixel away from
    /// the bust, and the background the bust's edge glow reads. The game's
    /// numbers are 11-bit floats, so "the same" is the same value, or one step
    /// of them. Control: the room without the shadow - the shipped room as it
    /// is - is far from the game's wherever the shadow falls.
    /// </summary>
    [Fact]
    public void S6_The_bust_preview_casts_the_shadow_the_game_does_on_the_room()
    {
        if (Off) return;
        var game = _s.InGame();
        const string Buttons = "5_Levels/5_MyRoom/PlayerRoom_ButtonCanvas";
        string root = "2_Bust_Manager/" + InGameSuitePacks.Looks.FigureBust;
        game.Ask("wait 8", 20);
        game.Ask("set-active 9_MainCanvas false");
        game.Ask("set-active " + Buttons + " false");
        game.Ask("postfx off");
        game.Ask("set-active " + root + " true");
        string folder, meshReply, stillRoom, laterRoom;
        try
        {
            game.Ask("wait 5", 15);
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            meshReply = game.Ask("shadow-mesh " + root + "/MBase1", 20);
            folder = LookFolder(game.Ask("screen-look shadow-room", 15));
            string at = Path.GetRelativePath(game.BridgeFolder, folder);
            game.Ask("rt-save _CameraTarget " + Path.Combine(at, "frame.bin"), 20);
            game.Ask("rt-save _CameraSortingLayerTexture " + Path.Combine(at, "background.bin"), 20);
            // The room alone at that moment and a moment later: what moves by
            // itself (its plant sways) is left out.
            game.Ask("set-active " + root + " false");
            game.Ask("wait 0.3", 10);
            stillRoom = LookFolder(game.Ask("screen-look shadow-room-still", 15));
            game.Ask("freeze off");
            game.Ask("wait 1.3", 10);
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            laterRoom = LookFolder(game.Ask("screen-look shadow-room-later", 15));
        }
        finally
        {
            game.Ask("freeze off");
            game.Ask("set-active " + root + " false");
            game.Ask("postfx on");
            game.Ask("set-active " + Buttons + " true");
            game.Ask("set-active 9_MainCanvas true");
        }
        var origin = meshReply.Split("; ").First(p => p.StartsWith("origin=", StringComparison.Ordinal)).Substring(7).Split(',')
                              .Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var (fw, fh, frame) = Floats(Path.Combine(folder, "frame.bin"));
        var (bw, bh, background) = Floats(Path.Combine(folder, "background.bin"));

        // The preview's room for the same bust, in the same place.
        var backdrop = PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom"));
        var lamp = new GameLamp(backdrop);
        int size = InGameSuitePacks.Looks.Size;
        var art = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var c = InGameSuitePacks.Looks.FigureAt(x, y);
                int i = (y * size + x) * 4;
                art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
            }
        var caster = new BustShadow.Caster(art, size, size, 100f);
        var shadow = new float[lamp.Shadow.W * lamp.Shadow.H];
        lamp.Shadow.Draw(caster, origin[0], origin[1], 2f, 1f, shadow);
        // Where the bust covers, give or take its jiggle and the pixel snap: left out.
        float left = origin[0] - size, bottom = origin[1] - size;
        bool Bust(int x, int y)
        {
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int tx = (int)MathF.Floor((x + dx + 0.5f - left) / 2f), ty = (int)MathF.Floor((y + dy + 0.5f - bottom) / 2f);
                    if (tx >= 0 && tx < size && ty >= 0 && ty < size && InGameSuitePacks.Looks.FigureAt(tx, size - 1 - ty).A > 0) return true;
                }
            return false;
        }
        float[] previewBackground = Array.Empty<float>();
        GamePost.Target Preview(float[]? s)
        {
            var target = new GamePost.Target(backdrop.ScreenWidth, backdrop.ScreenHeight);
            var bg = new float[backdrop.BackgroundWidth * backdrop.BackgroundHeight * 4];
            lamp.Room(s, target, bg, backdrop.BackgroundWidth, backdrop.BackgroundHeight);
            lamp.Glow(target, s, Bust);
            previewBackground = bg;
            return target;
        }
        var withShadow = Preview(shadow);
        var withShadowBackground = previewBackground;
        var without = Preview(null);
        var withoutBackground = previewBackground;

        // The room's own moving parts (S4's two moments) are left out too.
        var (_, _, still) = Png(Path.Combine(stillRoom, "screen.png"));
        var (_, _, later) = Png(Path.Combine(laterRoom, "screen.png"));
        bool Moving(int x, int y)
        {
            int i = ((fh - 1 - y) * fw + x) * 4;
            return Math.Abs(still[i] - later[i]) > 1 || Math.Abs(still[i + 1] - later[i + 1]) > 1 || Math.Abs(still[i + 2] - later[i + 2]) > 1;
        }
        (double same, double step, int n, int shaded) Compare(GamePost.Target t)
        {
            int n = 0, same = 0, step = 0, shaded = 0;
            for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++)
                {
                    if (Bust(x, y) || Moving(x, y)) continue;
                    n++;
                    if (lamp.Shadow.At(shadow, x, y) > 0f) shaded++;
                    int i = (y * fw + x) * 4;
                    bool eq = true, near = true;
                    for (int c = 0; c < 3; c++)
                    {
                        float g = frame[i + c], p = t.Px[i + c];
                        if (g != p) eq = false;
                        if (Math.Abs(g - p) > GamePost.Step(Math.Max(g, p), c == 2 ? 5 : 6) * 1.001f) near = false;
                    }
                    if (eq) same++;
                    if (near) step++;
                }
            return (same / (double)n, step / (double)n, n, shaded);
        }
        var (same, step, pixels, shaded) = Compare(withShadow);
        var (sameWithout, stepWithout, _, _) = Compare(without);
        // The background is one pixel of every 4x4, the one at (1, 1).
        (double same, double step, int n) CompareBackground(float[] preview)
        {
            int n = 0, same = 0, step = 0;
            for (int i = 0; i < bw * bh; i++)
            {
                if (Moving(i % bw * 4 + 1, i / bw * 4 + 1)) continue;
                n++;
                bool eq = true, near = true;
                for (int c = 0; c < 3; c++)
                {
                    float g = background[i * 4 + c], p = preview[i * 4 + c];
                    if (g != p) eq = false;
                    if (Math.Abs(g - p) > GamePost.Step(Math.Max(g, p), c == 2 ? 5 : 6) * 1.001f) near = false;
                }
                if (eq) same++;
                if (near) step++;
            }
            return (same / (double)n, step / (double)n, n);
        }
        var (bgSame, bgStep, bgN) = CompareBackground(withShadowBackground);
        var (_, bgStepWithout, _) = CompareBackground(withoutBackground);
        // The preview's side beside the game's, for a look at where they differ.
        File.WriteAllBytes(Path.Combine(folder, "preview-frame.bin"), withShadow.Px.SelectMany(BitConverter.GetBytes).ToArray());
        File.WriteAllBytes(Path.Combine(folder, "preview-background.bin"), withShadowBackground.SelectMany(BitConverter.GetBytes).ToArray());
        File.WriteAllBytes(Path.Combine(folder, "preview-shadow.bin"), shadow.SelectMany(BitConverter.GetBytes).ToArray());
        _out.WriteLine($"origin {origin[0]},{origin[1]}; {pixels} room pixels compared, {shaded} of them in the shadow");
        _out.WriteLine($"with the shadow: {same:P2} the same, {step:P2} within a step; without (control): {sameWithout:P2}, {stepWithout:P2}");
        _out.WriteLine($"background: {bgSame:P2} the same, {bgStep:P2} within a step ({bgN} texels); without the shadow (control) {bgStepWithout:P2}");
        Assert.True(shaded > pixels / 20, $"the figure's shadow covers only {shaded} of {pixels} room pixels - not the lamp's shadow being measured");
        Assert.True(stepWithout < 0.95, $"control: the room without the shadow is within a step of the game's at {stepWithout:P2} - the shadow is not being measured");
        // Measured 2026-11-16: 99.0% within a step (95.2% the same). What is left
        // is the lamp's own falloff a step off along an arc - its glow alone is
        // the same at 90% of pixels - and the shadow's soft edge here and there.
        Assert.True(step >= 0.985, $"the preview's room is within a step of the game's at {step:P2} of the pixels (the same at {same:P2})");
        // The background: 97.1% within a step (89.7% the same), the rest along
        // the same arc and edge, four screen pixels to a texel.
        Assert.True(bgStepWithout < 0.9, $"control: the background without the shadow is within a step of the game's at {bgStepWithout:P2}");
        Assert.True(bgStep >= 0.965, $"the edge glow's background is within a step of the game's at {bgStep:P2}");
    }

    /// <summary>What rt-save wrote: width, height, RGBA floats, rows from the bottom.</summary>
    private static (int w, int h, float[] px) Floats(string path)
    {
        var b = File.ReadAllBytes(path);
        int w = BitConverter.ToInt32(b, 0), h = BitConverter.ToInt32(b, 4);
        var px = new float[w * h * 4];
        Buffer.BlockCopy(b, 8, px, 0, px.Length * 4);
        return (w, h, px);
    }
}
