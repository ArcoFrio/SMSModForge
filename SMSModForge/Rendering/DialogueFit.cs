using System.Collections.Generic;

namespace SMSModForge.Rendering;

/// <summary>
/// Whether a line fits the game's dialogue box, and at what size.
/// <para/>
/// <b>What the game does</b>, measured in a running game (2026-09-24, the
/// dialogue box's own TextMeshPro component): the line is typed at 38 points
/// in a box 1010 wide and 125 deep, and auto-sizing shrinks it as far as 28 to
/// make it fit. What still does not fit at 28 is NOT cut: the box's overflow
/// mode is Overflow, so the words run on below it, out of the panel and over
/// whatever is underneath.
/// <para/>
/// <b>How this decides</b>: the line is laid out with the game's own font,
/// advances and kerning (<see cref="TmpTextLayout"/>) and its height taken the
/// way TextMeshPro takes it for auto-sizing - from the top of the first line to
/// the bottom of the last. It agrees with the game for ordinary text and is a
/// prediction, not the game; a line right on the edge is worth a look in game.
/// <para/>
/// <b>Not guessed</b>: a line with letters the game's font does not have is
/// drawn by a Windows font behind it (Chinese, Japanese, Korean...), with its
/// own widths this cannot see. For those the answer is "cannot tell" rather
/// than a number that looks exact and is not.
/// </summary>
public static class DialogueFit
{
    /// <summary>The box the line is typed in, in the text's own units.</summary>
    public const double BoxWidth = 1010;
    public const double BoxHeight = 125;

    public enum Verdict
    {
        /// <summary>Fits at the full size.</summary>
        Fits,
        /// <summary>Fits once the game shrinks it; <see cref="Result.PointSize"/> says to what.</summary>
        Shrinks,
        /// <summary>Does not fit even at the smallest size, and runs out of the box.</summary>
        Spills,
        /// <summary>Has letters the game's font lacks; not measured.</summary>
        CannotTell,
    }

    public readonly record struct Result(Verdict Verdict, double PointSize, int Lines);

    /// <summary>How <paramref name="line"/> - markup and all - fits the box
    /// when typed in <paramref name="font"/>.</summary>
    public static Result Of(TmpFont font, string? line)
    {
        var runs = new List<StyledRun>();
        string text = line ?? "";
        foreach (var span in DialogueMarkup.Parse(text))
        {
            if (span.IsTag) continue;
            runs.Add(new StyledRun(span.Of(text), span.Style.Scale));
        }
        if (runs.Count == 0) return new Result(Verdict.Fits, DialogueLook.BodyPointSize, 0);

        var full = Measure(font, runs, DialogueLook.BodyPointSize);
        if (full.Layout.Missing.Count > 0) return new Result(Verdict.CannotTell, DialogueLook.BodyPointSize, full.Layout.Lines);
        if (full.Height <= BoxHeight) return new Result(Verdict.Fits, DialogueLook.BodyPointSize, full.Layout.Lines);

        var smallest = Measure(font, runs, DialogueLook.BodyMinPointSize);
        if (smallest.Height > BoxHeight) return new Result(Verdict.Spills, DialogueLook.BodyMinPointSize, smallest.Layout.Lines);

        // The largest size that fits, to a tenth of a point: fitting only gets
        // easier as the size goes down, so halving the gap finds it.
        double fits = DialogueLook.BodyMinPointSize, fails = DialogueLook.BodyPointSize;
        int lines = smallest.Layout.Lines;
        while (fails - fits > 0.1)
        {
            double mid = (fits + fails) / 2;
            var at = Measure(font, runs, mid);
            if (at.Height <= BoxHeight) { fits = mid; lines = at.Layout.Lines; }
            else fails = mid;
        }
        return new Result(Verdict.Shrinks, System.Math.Floor(fits * 10) / 10, lines);
    }

    private readonly record struct Measured(TextLayout Layout, double Height);

    private static Measured Measure(TmpFont font, List<StyledRun> runs, double size)
    {
        var layout = TmpTextLayout.MeasureRuns(font, runs, size, BoxWidth, DialogueLook.Align);
        if (layout.Lines == 0) return new Measured(layout, 0);

        // The layout's height ends with a whole line's height after the last
        // line; TextMeshPro stops at the last line's descender. The difference
        // is the line gap, at the size of that line.
        double s = font.ScaleFor(size);
        double gap = (font.Face.LineHeight - (font.Face.AscentLine - font.Face.DescentLine)) * s;
        return new Measured(layout, layout.Height - System.Math.Max(0, gap));
    }
}
