using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// How one sound in a line plays, where it is not as set by default (1.8.0) -
/// see <see cref="Shared.SoundCues"/>, which the game reads it with. Named by
/// the sound's place among the line's sounds and its pattern.
/// </summary>
public sealed class SoundCueDef
{
    [JsonProperty("at", Order = 0)]
    public int At { get; set; }

    [JsonProperty("pattern", Order = 1)]
    public string Pattern { get; set; } = "";

    /// <summary>Of the sound's own volume: 1 is as the sound is set, and it
    /// may go above (the author, 1.8.0).</summary>
    [JsonProperty("volume", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
    public float? Volume { get; set; }

    /// <summary>Seconds: after the line begins (the first), after the sound
    /// before it ends (the rest; negative overlaps them), or after the
    /// typewriter reaches it (<see cref="Typed"/>).</summary>
    [JsonProperty("delay", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
    public float? Delay { get; set; }

    /// <summary>The pattern's words are left out of the line the player reads.</summary>
    [JsonProperty("hidden", Order = 4, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool Hidden { get; set; }

    /// <summary>Starts when the typewriter reaches it.</summary>
    [JsonProperty("typed", Order = 5, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool Typed { get; set; }

    public Shared.SoundCues.Setting ToSetting() => new()
    {
        At = At, Pattern = Pattern, Volume = Volume, Delay = Delay, Hidden = Hidden, Typed = Typed,
    };
}
