using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;
using Looks = SMSModForge.Tests.InGameSuitePacks.Looks;

namespace SMSModForge.Tests;

/// <summary>
/// A floating picture (after 1.10.0, the author: "I have a few monsters in vats in the Mountain Lab now in Androids,
/// and I wanted the full sprites to move up and down in a smooth way. Please add it with editable parameters").
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// The float place (InGameSuitePacks.Looks): a picture with a FloatingSprite of one unit over two seconds, a child on
    /// it, and one beside it without. In the game, read as drawn every tenth of a second for two and a half seconds:
    /// the floater's local position rises and falls a unit from its lowest to its highest, smoothly (no step more than
    /// a sine's at that rate), its X still; its child is carried (its world position moves with it, its own stays);
    /// control: the one without it does not move.
    /// </summary>
    [Fact]
    public void S24_A_floating_picture_rises_and_falls_as_set()
    {
        if (Off) return;
        var game = _s.InGame();
        string token = "pack:" + Looks.Id + "." + Looks.FloatPlaceKey;
        Travel(game, token, "_" + Looks.FloatPlaceObject);
        game.Ask("wait 1", 10);
        string level = "5_Levels/" + game.Ask("level").Split(' ').Last().Trim();
        string floater = level + "/" + Looks.Floater, rider = floater + "/" + Looks.FloaterRider, still = level + "/" + Looks.NotFloating;
        var ys = new List<float>(); var xs = new List<float>(); var riderLocal = new List<float>(); var riderWorld = new List<float>(); var stillYs = new List<float>();
        var frames = new List<float>();
        static float[] Vec(string said, string field)
        {
            var m = System.Text.RegularExpressions.Regex.Match(said, field + @" \(?(-?[\d.]+),\s*(-?[\d.]+)");
            Assert.True(m.Success, field + " not in: " + said);
            return new[] { float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) };
        }
        try
        {
            // Frame after frame (each reading waits for the next frame drawn): how far it moves from one to the next.
            for (int i = 0; i < 40; i++) frames.Add(Vec(Find(game, floater), "local")[1]);
            for (int i = 0; i < 25; i++)
            {
                var f = Vec(Find(game, floater), "local");
                ys.Add(f[1]); xs.Add(f[0]);
                riderLocal.Add(Vec(Find(game, rider), "local")[1]);
                riderWorld.Add(Vec(Find(game, rider), "world")[1]);
                stillYs.Add(Vec(Find(game, still), "local")[1]);
                game.Ask("wait 0.1", 10);
            }
        }
        finally { Travel(game, "vanilla:5_MyRoom", "5_MyRoom"); }
        float range = ys.Max() - ys.Min(), worldRange = riderWorld.Max() - riderWorld.Min();
        var steps = frames.Zip(frames.Skip(1), (a, b) => b - a).ToList();
        float biggestStep = steps.Max(Math.Abs);
        // How sharply it turns from one reading to the next: a smooth rise and fall bends a little each time, a jump or
        // a sudden turn bends at once.
        float sharpestTurn = steps.Zip(steps.Skip(1), (a, b) => Math.Abs(b - a)).Max();
        _out.WriteLine("floater y: " + string.Join(" ", ys.Select(y => y.ToString("0.##", CultureInfo.InvariantCulture))));
        _out.WriteLine($"floater: from {ys.Min():0.###} to {ys.Max():0.###} ({range:0.###}), x {xs.Min():0.###}..{xs.Max():0.###}, biggest step {biggestStep:0.###}; "
                       + $"its child's own y {riderLocal.Min():0.###}..{riderLocal.Max():0.###}, world {worldRange:0.###}; the still one {stillYs.Min():0.###}..{stillYs.Max():0.###}");
        // A unit from its lowest to its highest - read a tenth of a second apart, within the step it takes there.
        Assert.InRange(range, 0.9f, 1.02f);
        Assert.True(xs.Max() - xs.Min() < 0.001f, "the floater sways though its sideways is 0");
        // Smooth, whatever the test copy's frame rate (unfocused, about ten a second): a sine of a unit over two seconds
        // read a tenth of a second apart turns by under a tenth from one reading to the next (pi squared x 0.5 x 0.01).
        _out.WriteLine("frame after frame: " + string.Join(" ", frames.Select(y => y.ToString("0.###", CultureInfo.InvariantCulture)))
                       + $" - the biggest step {biggestStep:0.###}, the sharpest turn {sharpestTurn:0.###}");
        Assert.True(sharpestTurn < 0.1f, $"the floater turns {sharpestTurn:0.###} at once between readings - not a smooth rise and fall");
        Assert.True(frames.Max() - frames.Min() > 0.05f, "control: frame after frame it does not move at all - the readings are not of a moving thing");
        Assert.True(riderLocal.Max() - riderLocal.Min() < 0.001f, "the child's own position moves: it should be carried, not float itself");
        Assert.True(worldRange > 0.5f, "the child is not carried by the floater");
        Assert.True(stillYs.Max() - stillYs.Min() < 0.001f, "control: the picture without it moves too");
    }
}
