using System;

namespace SMSModForge.Rendering;

/// <summary>
/// The frames pack art is authored for, and how art of another size is fitted
/// to them.
/// <para/>
/// This exists so the two previews and the validator cannot disagree about it.
/// The runtime has its own copy in the plugin — a separate assembly that shares
/// no code with the editor — and the numbers there are the same numbers, for
/// the same reason: a preview is only worth having if it shows what the game
/// will draw.
/// <para/>
/// The rule is uniform scale. A sprite's world size is pixels divided by its
/// pixels-per-unit, so multiplying the frame's pixels-per-unit by how many
/// times too big the art is leaves the world size alone. One factor for both
/// axes, so nothing is ever stretched; the larger of the two ratios wins, so
/// art of the wrong shape sits INSIDE its frame rather than spilling out of it.
/// </summary>
internal static class ArtFit
{
    /// <summary>A bust frame, and every overlay drawn on the same rig.</summary>
    public const int BustPixels = 256;

    /// <summary>A level layer, matching the game's own.</summary>
    public const int LevelWidth = 2048;
    public const int LevelHeight = 1136;

    /// <summary>A level's mask.</summary>
    public const int LevelMaskWidth = 256;
    public const int LevelMaskHeight = 143;

    /// <summary>
    /// How many times too big this art is for its frame: 1 when it matches, 2
    /// when it is twice the size, 0.5 when it is half.
    /// <para/>
    /// Returns 1 for anything nonsensical, so a caller dividing by it is never
    /// handed a zero.
    /// </summary>
    public static double Scale(double width, double height, double frameW, double frameH)
    {
        if (width <= 0 || height <= 0 || frameW <= 0 || frameH <= 0) return 1.0;
        double scale = Math.Max(width / frameW, height / frameH);
        return scale > 0 ? scale : 1.0;
    }

    /// <summary>The scale for a level layer.</summary>
    public static double LevelScale(double width, double height)
        => Scale(width, height, LevelWidth, LevelHeight);

    /// <summary>The scale for a bust or one of its overlays.</summary>
    public static double BustScale(double width, double height)
        => Scale(width, height, BustPixels, BustPixels);
}
