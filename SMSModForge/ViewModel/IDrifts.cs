namespace SMSModForge.ViewModel;

/// <summary>
/// Something in a place that drifts as the cursor moves, by Parallax settings of its own: an object
/// (<see cref="GameObjectViewModel"/>) and a character standing in the place (<see cref="NpcPlacementViewModel"/>,
/// after 1.10.0). The preview's "Moves with" reads and sets it as one choice (<see cref="Drifting"/>).
/// </summary>
public interface IDrifts : System.ComponentModel.INotifyPropertyChanged
{
    /// <summary>Drifts at all: off, it stays with what it is under.</summary>
    bool ParallaxEnabled { get; set; }

    /// <summary>Its strength left to the level's back picture's (no strength of its own).</summary>
    bool ParallaxInherits { get; set; }

    float ParallaxStrength { get; set; }

    /// <summary>Whether it drifts reversed, as it will in the game: its own, or unset the back picture's.</summary>
    bool ParallaxReversed { get; set; }

    /// <summary>Its own reversal; null drifts as the back picture's effect has it.</summary>
    bool? ParallaxReversedOwn { get; set; }

    /// <summary><see cref="Drifting.Back"/>, <see cref="Drifting.Front"/> or <see cref="Drifting.Own"/>.</summary>
    string Drift { get; }

    void DriftWith(string kind, float backStrength = 0.75f);
}

/// <summary>
/// The three ways a thing drifts, read from its Parallax settings and written back to them - nothing new is kept
/// (after 1.10.0, the author: "add the checkboxes for either moving with background, foreground, or manually set
/// it"): with the level's back picture (Parallax on, the strength left to the back's - what the game gives an
/// object that drifts with the level), with the front (Parallax off: it stays with the level, or with what it is
/// under), or by a strength of its own.
/// </summary>
public static class Drifting
{
    public const string Back = "back", Front = "front", Own = "own";

    public static string Of(IDrifts d) => !d.ParallaxEnabled ? Front : d.ParallaxInherits ? Back : Own;

    /// <summary>Drift as <paramref name="kind"/>. Own starts where it drifts now - at <paramref name="backStrength"/>,
    /// the back picture's, from Back; at none of its own from Front - so choosing it moves nothing until its strength
    /// is changed.</summary>
    public static void Apply(IDrifts d, string kind, float backStrength)
    {
        switch (kind)
        {
            case Front:
                d.ParallaxEnabled = false;
                break;
            case Back:
                d.ParallaxEnabled = true;
                d.ParallaxInherits = true;
                // All of the back picture's drift: a reversal of its own, left from Own, would take it the other
                // way from the back picture it is said to move with.
                d.ParallaxReversedOwn = null;
                break;
            default:
                float from = !d.ParallaxEnabled ? 0f : d.ParallaxInherits ? backStrength : d.ParallaxStrength;
                d.ParallaxEnabled = true;
                d.ParallaxStrength = (float)System.Math.Round(from, 2);
                break;
        }
    }
}
