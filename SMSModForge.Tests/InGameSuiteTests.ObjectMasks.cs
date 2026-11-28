using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A place's objects moved by masks of their own in the game (after 1.10.0, the author: "do you think we could
/// implement a way to add masks to other GOs in the place? We should be able to choose between the jiggle and level
/// masks"; asked, "no mask" means still).
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// The mask place (InGameSuitePacks.Looks): four checkered objects over a back layer with a mask - still, with the
    /// back layer's mask, by a level mask of its own, by a jiggle mask of its own. In the game, the interface and the
    /// busts off, time running: each object's material (the plain lit one for the still object; the back layer's; the
    /// level's own pass, as the place's front picture has it, with its own mask; the characters' jiggle with its own
    /// mask), and two frames 0.4 s apart - the three that move change where they are drawn, the still one does not
    /// (the control: under the same back layer, it would have taken its mask before the rules).
    /// </summary>
    [Fact]
    public void S19_A_places_objects_move_by_the_masks_they_are_given_in_the_game()
    {
        if (Off) return;
        var game = _s.InGame();
        string token = "pack:" + InGameSuitePacks.Looks.Id + "." + InGameSuitePacks.Looks.MaskPlaceKey;
        var hidden = new List<string> { "9_MainCanvas" };
        var said = new Dictionary<string, string>();
        string first, second, front;
        try
        {
            Travel(game, token, "_" + InGameSuitePacks.Looks.MaskPlaceObject);
            string levelPath = "5_Levels/" + game.Ask("level").Split(' ').Last().Trim();
            string canvases = game.Ask("find-all *Canvas*");
            hidden.AddRange(canvases.Substring(canvases.IndexOf(':') + 1).Split(" | ", StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim()).Where(p => p.Contains(levelPath + "/")));
            hidden.AddRange(OnBusts(game));
            foreach (var off in hidden) game.Ask("set-active " + off + " false");
            game.Ask("wait 1", 10);
            front = game.Ask("material " + levelPath);
            foreach (var name in new[] { InGameSuitePacks.Looks.MaskStill, InGameSuitePacks.Looks.MaskBack,
                                         InGameSuitePacks.Looks.MaskLevel, InGameSuitePacks.Looks.MaskJiggle })
                said[name] = game.Ask("material " + levelPath + "/" + name);
            first = LookFolder(game.Ask("screen-look masks-a", 15));
            game.Ask("wait 0.4", 10);
            second = LookFolder(game.Ask("screen-look masks-b", 15));
        }
        finally
        {
            foreach (var on in hidden) game.Ask("set-active " + on + " true");
            Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        }
        _out.WriteLine("   front: " + front);
        foreach (var kv in said) _out.WriteLine("   " + kv.Key + ": " + kv.Value);
        static string Of(string said, string field) => Regex.Match(said, @"\b" + field + @"=(.*?)(?= mask=| screen |$)").Groups[1].Value.Trim();
        string frontShader = Of(front, "shader");

        var still = said[InGameSuitePacks.Looks.MaskStill];
        var back = said[InGameSuitePacks.Looks.MaskBack];
        var level = said[InGameSuitePacks.Looks.MaskLevel];
        var jiggle = said[InGameSuitePacks.Looks.MaskJiggle];
        // The plain lit material (whose _MaskTex is the 2D lights' sprite mask, none bound), not the back layer's pass.
        Assert.Equal("Universal Render Pipeline/2D/Sprite-Lit-Default", Of(still, "shader"));
        Assert.Equal("(none bound)", Of(still, "mask"));
        Assert.DoesNotContain("(", Of(back, "mask"));
        Assert.Equal(frontShader, Of(level, "shader"));
        Assert.DoesNotContain("(", Of(level, "mask"));
        Assert.Contains("Jiggle", Of(jiggle, "shader"));
        Assert.DoesNotContain("(", Of(jiggle, "mask"));

        // What moves: the pixels each object covers, between the two frames.
        var (w, h, a) = Png(Path.Combine(first, "screen.png"));
        var (_, _, b) = Png(Path.Combine(second, "screen.png"));
        double Changed(string s)
        {
            var m = Regex.Match(s, @"screen (-?\d+),(-?\d+) to (-?\d+),(-?\d+)");
            int x0 = Math.Max(0, int.Parse(m.Groups[1].Value)), y0 = Math.Max(0, int.Parse(m.Groups[2].Value));
            int x1 = Math.Min(w, int.Parse(m.Groups[3].Value)), y1 = Math.Min(h, int.Parse(m.Groups[4].Value));
            int n = 0, changed = 0;
            for (int y = y0 + 2; y < y1 - 2; y++)
                for (int x = x0 + 2; x < x1 - 2; x++)
                {
                    int i = (y * w + x) * 4;
                    n++;
                    if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 24) changed++;
                }
            return n == 0 ? 0 : (double)changed / n;
        }
        double cStill = Changed(still), cBack = Changed(back), cLevel = Changed(level), cJiggle = Changed(jiggle);
        _out.WriteLine($"changed between the frames: still {cStill:P1}, the back layer's {cBack:P1}, its own level {cLevel:P1}, its own jiggle {cJiggle:P1}");
        Assert.True(cStill < 0.01, "control: the object with no mask moves");
        Assert.True(cBack > 0.05, "the object with the back layer's mask does not move");
        Assert.True(cLevel > 0.05, "the object with a level mask of its own does not move");
        Assert.True(cJiggle > 0.05, "the object with a jiggle mask of its own does not move");
    }
}
