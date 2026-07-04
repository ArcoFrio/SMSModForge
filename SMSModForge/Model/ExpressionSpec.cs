using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// Expression overlays. The bust hierarchy has an <c>Expressions</c>
/// GameObject with four fixed-named children: <c>Happy / Angry / Sad / Flirty</c>.
/// On disk: <c>{prefix}Happy.PNG</c>, <c>{prefix}Angry.PNG</c>, etc. Names are
/// frozen because GC2 actor expression strings reference them.
/// </summary>
public sealed class ExpressionSpec
{
    [JsonProperty("enabled", Order = 1)]
    public bool Enabled { get; set; } = true;

    [JsonProperty("prefix", Order = 2)]
    public string Prefix { get; set; } = "";

    /// <summary>Canonical expression list — must stay aligned with the GC2 actor expression names baked into the dialogue assets.</summary>
    [JsonIgnore]
    public static readonly string[] Names = { "Happy", "Angry", "Sad", "Flirty" };
}
