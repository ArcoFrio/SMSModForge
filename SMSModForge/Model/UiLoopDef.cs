using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// How an object keeps moving while it is shown (1.8.3) - where
/// <see cref="UiOpenDef"/> plays once as it arrives, this repeats:
/// <list type="bullet">
///   <item>a pulse: its fade (<see cref="Alpha"/>) and/or size
///   (<see cref="Scale"/>) go from one value to the other and back once a
///   pass - a glow breathing;</item>
///   <item>a sweep: it moves from <see cref="MoveFrom"/> to
///   <see cref="MoveTo"/> and starts again - a highlight under a parent that
///   clips its children becomes a shine running along a line.</item>
/// </list>
/// Each value is relative to the object as it is: fade and size multiply its
/// own, the move is added to its place. The game has no looping animation of
/// its own to attach (its Blackjack pulse is a coroutine inside the cards),
/// and ModForge's sprite components drive SpriteRenderers, not screens.
/// </summary>
public sealed class UiLoopDef
{
    /// <summary>The fade from and to, as fractions of its own (0.3 and 0.8),
    /// or null to leave it.</summary>
    [JsonProperty("alpha", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
    public float[]? Alpha { get; set; }

    /// <summary>The size from and to, as fractions of its own, or null.</summary>
    [JsonProperty("scale", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
    public float[]? Scale { get; set; }

    /// <summary>Where the sweep starts, x and y from its own place, or null;
    /// both ends or neither.</summary>
    [JsonProperty("moveFrom", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
    public float[]? MoveFrom { get; set; }

    [JsonProperty("moveTo", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
    public float[]? MoveTo { get; set; }

    /// <summary>Seconds a pass takes.</summary>
    [JsonProperty("seconds", Order = 5)]
    public float Seconds { get; set; } = 2f;

    /// <summary>Seconds resting at the start between passes.</summary>
    [JsonProperty("pause", Order = 6)]
    public float Pause { get; set; }

    /// <summary>Seconds before the first pass, so copies of one object -
    /// one per item of a list - need not all beat in step.</summary>
    [JsonProperty("delay", Order = 7)]
    public float Delay { get; set; }

    /// <summary>The sweep's curve: one of <see cref="UiOpenDef.Easings"/>. A
    /// pulse always eases back and forth.</summary>
    [JsonProperty("easing", Order = 8)]
    public string Easing { get; set; } = UiOpenDef.Linear;

    public bool ShouldSerializePause() => Pause != 0f;
    public bool ShouldSerializeDelay() => Delay != 0f;
    public bool ShouldSerializeEasing() => Sweeps && Easing != UiOpenDef.Linear;

    [JsonIgnore]
    public bool Pulses => Alpha is { Length: 2 } || Scale is { Length: 2 };

    [JsonIgnore]
    public bool Sweeps => MoveFrom is { Length: 2 } && MoveTo is { Length: 2 };

    /// <summary>Whether anything moves at all.</summary>
    [JsonIgnore]
    public bool DoesAnything => Pulses || Sweeps;

    public UiLoopDef Clone() => new()
    {
        Alpha = (float[]?)Alpha?.Clone(),
        Scale = (float[]?)Scale?.Clone(),
        MoveFrom = (float[]?)MoveFrom?.Clone(),
        MoveTo = (float[]?)MoveTo?.Clone(),
        Seconds = Seconds,
        Pause = Pause,
        Delay = Delay,
        Easing = Easing,
    };
}
