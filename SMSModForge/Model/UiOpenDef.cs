using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// How something arrives when it is switched on.
/// <para/>
/// Taken from the game's own, read out of a running build: a screen that
/// animates does it by snapping to a starting state and easing to its resting
/// one. The shop is alpha 0 and scale (1,0,1), both reaching normal over 0.3s
/// on QuadInOut - it fades in while unfolding from a flat line.
/// <para/>
/// Deliberately NOT on by default. Of the game's 29 on-enable triggers, five
/// fade, seven grow, and exactly one does both; durations run 0.3 to 3 and the
/// easings are three different curves. There is no house style to inherit, so
/// an animation is something a screen opts into rather than something every
/// screen gets.
/// </summary>
public sealed class UiOpenDef
{
    /// <summary>Fade up from invisible. Needs a CanvasGroup, which the runtime
    /// adds when it is missing.</summary>
    [JsonProperty("fade", Order = 1)]
    public bool Fade { get; set; }

    /// <summary>
    /// The far end of the movement: the scale it starts from when arriving, and
    /// the scale it finishes at when leaving. Null leaves the size alone.
    /// <para/>
    /// Three numbers rather than one, because the game uses all of it: (1,0,1)
    /// unfolds from flat, (0.5,0.5,0.5) grows from small, (2,2,1) shrinks into
    /// place, (0.5,1.5,0.5) squashes.
    /// </summary>
    [JsonProperty("scaleFrom", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
    public float[]? ScaleFrom { get; set; }

    /// <summary>Seconds. 0.3 is what the shop uses and the commonest in the
    /// game.</summary>
    [JsonProperty("duration", Order = 3)]
    public float Duration { get; set; } = 0.3f;

    /// <summary>One of <see cref="Easings"/>.</summary>
    [JsonProperty("easing", Order = 4)]
    public string Easing { get; set; } = QuadInOut;

    public const string Linear = "Linear";
    public const string QuadInOut = "QuadInOut";
    public const string BounceOut = "BounceOut";
    public const string ElasticOut = "ElasticOut";

    /// <summary>The curves the game itself uses, plus Linear. Offering more
    /// would be inventing options nothing in this game demonstrates.</summary>
    public static readonly string[] Easings = { Linear, QuadInOut, BounceOut, ElasticOut };

    /// <summary>Whether this actually does anything. Neither a fade nor a
    /// scale is an animation that plays for its duration and changes
    /// nothing.</summary>
    [JsonIgnore]
    public bool DoesAnything => Fade || ScaleFrom != null;

    public UiOpenDef Clone() => new UiOpenDef
    {
        Fade = Fade,
        ScaleFrom = ScaleFrom == null ? null : (float[])ScaleFrom.Clone(),
        Duration = Duration,
        Easing = Easing,
    };

    /// <summary>
    /// The curve, 0-1. The runtime has the same four and is what the game
    /// actually plays; this copy is what the editor's preview follows, so the
    /// two show the same movement.
    /// </summary>
    public static double Ease(double t, string? easing)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;

        if (easing == Linear) return t;

        if (easing == BounceOut)
        {
            const double n = 7.5625, d = 2.75;
            if (t < 1 / d) return n * t * t;
            if (t < 2 / d) { t -= 1.5 / d; return n * t * t + 0.75; }
            if (t < 2.5 / d) { t -= 2.25 / d; return n * t * t + 0.9375; }
            t -= 2.625 / d;
            return n * t * t + 0.984375;
        }

        if (easing == ElasticOut)
        {
            const double period = 0.3;
            double s = period / 4;
            return System.Math.Pow(2, -10 * t)
                 * System.Math.Sin((t - s) * (2 * System.Math.PI) / period) + 1;
        }

        return t < 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t);
    }

    /// <summary>
    /// The shop's way out: fold back to a flat line over 0.3s, no fade.
    /// <para/>
    /// Not the mirror of its arrival, which also fades - read from the close
    /// button, which folds ShopCore to (1,0,1), waits, and only then switches
    /// it off.
    /// </summary>
    public static UiOpenDef LikeTheGameClosing() => new UiOpenDef
    {
        Fade = false,
        ScaleFrom = new[] { 1f, 0f, 1f },
        Duration = 0.3f,
        Easing = QuadInOut,
    };

    /// <summary>The shop's: fade in while unfolding from flat, over 0.3s.</summary>
    public static UiOpenDef LikeTheGame() => new UiOpenDef
    {
        Fade = true,
        ScaleFrom = new[] { 1f, 0f, 1f },
        Duration = 0.3f,
        Easing = QuadInOut,
    };
}
