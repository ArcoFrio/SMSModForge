using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Looks = SMSModForge.Tests.InGameSuitePacks.Looks;

namespace SMSModForge.Tests;

/// <summary>
/// What components do in the game, held to what the place preview plays (after 1.10.0, the author: "for the components
/// that show something visually, would we be able to see what they do in real time?"; PlaceComponentPreviewTests).
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// The anim place (InGameSuitePacks.Looks), read through the bridge as drawn. A fade in starts at nothing, rises
    /// and ends at its target; a fade out ends switched off, its child with it; a blink takes its two alphas and no
    /// other; a RandomChildActivator has one child on; a CinemaSpriteChanger one at a time, changing; a
    /// ConcertLightSwing turns within its two angles over most of them; a CarInteriorShake started moves within its
    /// intensity - control: one not started stays still; a RandomSpriteBlender's children fade about.
    /// </summary>
    [Fact]
    public void S25_Components_play_in_the_game_as_the_preview_plays_them()
    {
        if (Off) return;
        var game = _s.InGame();
        string token = "pack:" + Looks.Id + "." + Looks.AnimPlaceKey;
        Travel(game, token, "_" + Looks.AnimPlaceObject);
        game.Ask("wait 1", 10);
        string level = "5_Levels/" + game.Ask("level").Split(' ').Last().Trim();
        string P(string name) => level + "/" + name;
        static float Alpha(string said) => float.Parse(Regex.Match(said, @"alpha (-?[\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
        static bool On(string said) => Regex.Match(said, @"active (\w+)/(\w+)").Groups[2].Value == "True";
        static float RotZ(string said)
        {
            float z = float.Parse(Regex.Match(said, @"rotz (-?[\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);
            return z > 180 ? z - 360 : z;
        }
        static (float X, float Y) Local(string said)
        {
            var m = Regex.Match(said, @"local \(?(-?[\d.]+),\s*(-?[\d.]+)");
            return (float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        }
        var fadeIn = new List<float>(); var blink = new List<float>(); var blended = new List<float>(); var swing = new List<float>();
        var shake = new List<(float X, float Y)>(); var noShake = new List<(float X, float Y)>();
        var pickerOn = new List<int>(); var cinemaOn = new List<int>(); var cinemaPicks = new List<int>();
        bool fadeOutOff, fadeOutChildOff;
        try
        {
            // The fades start again as their objects are switched on.
            game.Ask("set-active " + P("FadeIn") + " false");
            game.Ask("set-active " + P("FadeOut") + " false");
            game.Ask("set-active " + P("FadeIn") + " true");
            game.Ask("set-active " + P("FadeOut") + " true");
            // Each read in a run of its own, a frame or so apart (the test copy draws about ten a second unfocused):
            // read side by side, a blink of a second was caught at the same moment of it every time.
            for (int i = 0; i < 20; i++) fadeIn.Add(Alpha(Find(game, P("FadeIn"))));
            for (int i = 0; i < 20; i++) blink.Add(Alpha(Find(game, P("Blink"))));
            for (int i = 0; i < 25; i++) swing.Add(RotZ(Find(game, P("Swing"))));
            for (int i = 0; i < 15; i++) { shake.Add(Local(Find(game, P("Shake")))); noShake.Add(Local(Find(game, P("NoShake")))); }
            for (int i = 0; i < 25; i++) blended.Add(Alpha(Find(game, P("Blender/Blended0"))));
            for (int i = 0; i < 12; i++)
            {
                // The children that are on, read in one frame (read one by one, a change between them read as two on).
                List<int> OnNow(string parent, string child)
                {
                    string said = game.Ask("children " + P(parent));
                    string list = said.Substring(said.IndexOf(':') + 1).Trim();
                    return list == "(none)" ? new List<int>()
                         : list.Split(" | ", StringSplitOptions.RemoveEmptyEntries)
                               .Select(path => int.Parse(path.Trim().Substring(path.Trim().LastIndexOf('/') + 1 + child.Length), CultureInfo.InvariantCulture)).ToList();
                }
                pickerOn.Add(OnNow("Picker", "Pick").Count);
                var frames = OnNow("Cinema", "Frame");
                cinemaOn.Add(frames.Count);
                if (frames.Count == 1) cinemaPicks.Add(frames[0]);
                game.Ask("wait 0.2", 10);
            }
            game.Ask("wait 1", 10);
            fadeOutOff = !On(Find(game, P("FadeOut")));
            fadeOutChildOff = !On(Find(game, P("FadeOut/FadeOutChild")));
        }
        finally { Travel(game, "vanilla:5_MyRoom", "5_MyRoom"); }
        string Row(IEnumerable<float> v) => string.Join(" ", v.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture)));
        _out.WriteLine("fade in: " + Row(fadeIn));
        _out.WriteLine("blink: " + Row(blink));
        _out.WriteLine("blended: " + Row(blended));
        _out.WriteLine("swing: " + Row(swing));
        _out.WriteLine($"shake x {shake.Min(p => p.X):0.###}..{shake.Max(p => p.X):0.###}, y {shake.Min(p => p.Y):0.###}..{shake.Max(p => p.Y):0.###}; "
                       + $"not started x {noShake.Min(p => p.X):0.###}..{noShake.Max(p => p.X):0.###}");
        _out.WriteLine($"picker: {string.Join(",", pickerOn)} on; cinema: {string.Join(",", cinemaOn)} on, picks {string.Join(",", cinemaPicks)}; "
                       + $"fade out then off {fadeOutOff}, its child off {fadeOutChildOff}");

        // Fade in: nothing first (its delay), its target at the end, rising between.
        Assert.True(fadeIn[0] < 0.05f, $"the fade in starts at {fadeIn[0]}");
        Assert.InRange(fadeIn[^1], 0.79f, 0.81f);
        Assert.Contains(fadeIn, a => a > 0.1f && a < 0.7f);
        Assert.True(fadeIn.Zip(fadeIn.Skip(1), (a, b) => b >= a - 0.001f).All(x => x), "the fade in goes down somewhere");
        // Fade out, then off - its child with it.
        Assert.True(fadeOutOff && fadeOutChildOff, "the faded-out object or its child is still on");
        // Blink: its two alphas, both, and no other.
        Assert.All(blink, a => Assert.True(Math.Abs(a - 0.2f) < 0.01f || Math.Abs(a - 0.9f) < 0.01f, $"the blink at {a}"));
        Assert.Contains(blink, a => a < 0.5f); Assert.Contains(blink, a => a > 0.5f);
        // One child on, and one at a time, changing.
        Assert.All(pickerOn, n => Assert.Equal(1, n));
        Assert.All(cinemaOn, n => Assert.Equal(1, n));
        Assert.True(cinemaPicks.Distinct().Count() >= 2, "the cinema changer always shows the same child");
        // The swing: within its angles, over most of them.
        Assert.All(swing, z => Assert.InRange(z, -30.5f, 30.5f));
        Assert.True(swing.Max() - swing.Min() > 40, "the swing does not swing");
        // The shake: started, within its intensity of where it stands, moving; control: not started, still.
        float midX = (shake.Max(p => p.X) + shake.Min(p => p.X)) / 2, midY = (shake.Max(p => p.Y) + shake.Min(p => p.Y)) / 2;
        Assert.True(shake.Max(p => p.X) - shake.Min(p => p.X) > 0.05f, "the shake does not move");
        Assert.True(shake.Max(p => p.X) - shake.Min(p => p.X) <= 1.0f && shake.Max(p => p.Y) - shake.Min(p => p.Y) <= 1.0f, "the shake goes past its intensity");
        Assert.True(noShake.Max(p => p.X) - noShake.Min(p => p.X) < 0.0001f, "control: a shake not started moves");
        // The blender: its child fades about, within 0..1.
        Assert.All(blended, a => Assert.InRange(a, 0f, 1f));
        Assert.True(blended.Max() - blended.Min() > 0.2f, "the blender's child does not fade about");
    }

    /// <summary>
    /// The game's OffsetScrolling on a pack's picture (InGameSuitePacks.Looks: stripes alone in a place): whether the
    /// picture moves at all, frame after frame - the place preview plays it only if it does. Printed and held: what the
    /// game does decides; control: two frames of the place with the picture switched off are the same.
    /// </summary>
    [Fact]
    public void S26_What_the_games_OffsetScrolling_does_to_a_picture()
    {
        if (Off) return;
        var game = _s.InGame();
        Travel(game, "pack:" + Looks.Id + "." + Looks.ScrollPlaceKey, "_" + Looks.ScrollPlaceObject);
        game.Ask("wait 1", 10);
        string level = "5_Levels/" + game.Ask("level").Split(' ').Last().Trim();
        var hidden = new List<string> { "9_MainCanvas" };
        string a, b, c, d;
        try
        {
            hidden.AddRange(OnBusts(game));
            foreach (var h in hidden) game.Ask("set-active " + h + " false");
            a = LookFolder(game.Ask("screen-look scroll-a", 15));
            game.Ask("wait 0.4", 10);
            b = LookFolder(game.Ask("screen-look scroll-b", 15));
            game.Ask("set-active " + level + "/Striped false");
            game.Ask("wait 0.2", 10);
            c = LookFolder(game.Ask("screen-look scroll-c", 15));
            game.Ask("wait 0.4", 10);
            d = LookFolder(game.Ask("screen-look scroll-d", 15));
        }
        finally
        {
            game.Ask("set-active " + level + "/Striped true");
            foreach (var h in hidden) game.Ask("set-active " + h + " true");
            Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        }
        int Changed(string x, string y)
        {
            var (w, h, px) = Png(System.IO.Path.Combine(x, "screen.png"));
            var (_, _, py) = Png(System.IO.Path.Combine(y, "screen.png"));
            int n = 0;
            for (int i = 0; i < w * h * 4; i += 4)
                if (Math.Max(Math.Abs(px[i] - py[i]), Math.Max(Math.Abs(px[i + 1] - py[i + 1]), Math.Abs(px[i + 2] - py[i + 2]))) > 8) n++;
            return n;
        }
        int withIt = Changed(a, b), without = Changed(c, d), drawn = Changed(b, c);
        _out.WriteLine($"pixels changed between two frames 0.4 s apart: with the scrolling picture {withIt}, with it off {without}; "
                       + $"the picture itself covers {drawn}");
        // Control: the picture is on screen - else nothing changing would say nothing.
        Assert.True(drawn > 500, "control: the striped picture is not on screen");
        Assert.True(without < 50, "control: the place changes between two frames with nothing moving in it");
        // Recorded: the preview plays OffsetScrolling only if this says the picture moves (PLAN-PREVIEW-NEXT V3).
        Assert.True(withIt < 50, $"the game's OffsetScrolling moves a pack picture ({withIt} pixels changed): the place preview should play it");
    }

    /// <summary>
    /// An object's effect moved away from it and sized in the place preview's Effect part (after 1.10.0,
    /// PlaceObjectPartsTests): in the game its copy stands under the object that far away in the object's own units -
    /// its local position - and is the size asked. InGameSuitePacks.Effects: the kitchen's FxMoved, twice as big, its
    /// campfire 1.5 by 0.75 of its units away at three times the game's size. Control: the hearth's campfire, at its
    /// object, the game's size - the one to measure the size against (each is sized whatever its object's size).
    /// </summary>
    [Fact]
    public void S27_An_objects_effect_moved_and_sized_in_the_preview_is_so_in_the_game()
    {
        if (Off) return;
        var game = _s.InGame();
        string id = InGameSuitePacks.Effects.Id, room = InGameSuitePacks.Effects.HearthLevel;
        Travel(game, "vanilla:" + room, room);
        string Obj(string name) => "5_Levels/" + room + "/" + name;
        string Fx(string name) => Obj(name) + "/Effect " + id + "." + name;
        static (float X, float Y) Two(string said, string what)
        {
            var m = Regex.Match(said, what + @" \(?(-?[\d.]+),\s*(-?[\d.]+)");
            Assert.True(m.Success, $"no {what} in: {said}");
            return (float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        }
        string moved, hearth, movedObject;
        // Back to the bedroom after, as every check that travels goes back: left in the kitchen, the checks after this
        // one that photograph the bedroom (S3, S4, S6) photographed the kitchen (the first whole run with S27).
        try
        {
            moved = Find(game, Fx(InGameSuitePacks.Effects.Moved));
            hearth = Find(game, Fx(InGameSuitePacks.Effects.Hearth));
            movedObject = Find(game, Obj(InGameSuitePacks.Effects.Moved));
        }
        finally { Travel(game, "vanilla:5_MyRoom", "5_MyRoom"); }
        _out.WriteLine("moved: " + moved);
        _out.WriteLine("its object: " + movedObject);
        _out.WriteLine("hearth: " + hearth);
        var (lx, ly) = Two(moved, "local");
        var (hx, hy) = Two(hearth, "local");
        var (sx, _) = Two(moved, "scale");
        var (hs, _) = Two(hearth, "scale");
        var (wx, wy) = Two(moved, "world");
        var (ox, oy) = Two(movedObject, "world");
        _out.WriteLine($"the moved effect at {lx},{ly} under its object ({wx - ox:0.###},{wy - oy:0.###} in the world); scale {sx} against the hearth's {hs}");

        // Control: the hearth's at its object.
        Assert.True(Math.Abs(hx) < 0.001f && Math.Abs(hy) < 0.001f, $"control: the hearth's effect is not at its object ({hx},{hy})");
        // Moved: as far as asked, in its object's units - twice as far in the world, the object being twice as big.
        Assert.InRange(lx, InGameSuitePacks.Effects.MovedX - 0.001f, InGameSuitePacks.Effects.MovedX + 0.001f);
        Assert.InRange(ly, InGameSuitePacks.Effects.MovedY - 0.001f, InGameSuitePacks.Effects.MovedY + 0.001f);
        Assert.True(Math.Abs(wx - ox) > 1f, "the moved effect is drawn at its object");
        Assert.InRange((wy - oy) / (wx - ox), 0.49f, 0.51f);
        // Sized: three times the game's, under an object twice as big (the hearth's is the game's under one as big as
        // the room's): its own scale three halves of the hearth's.
        Assert.InRange(sx / hs, 1.49f, 1.51f);
    }
}
