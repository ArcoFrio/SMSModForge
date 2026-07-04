using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// Reference to a particle effect attached as a child of <c>MBase1</c>. The
/// canonical preset is <c>"Wet"</c> — corresponds to the cloned
/// <c>Anna_Towel/MBase1/Particle System</c> the mod already wires today.
/// Custom presets point at a JSON sibling file holding a full module dump.
/// </summary>
public sealed class ParticleRef
{
    /// <summary>
    /// Preset name. Built-in: <c>"Wet"</c>. Use <c>"custom"</c> to indicate a
    /// per-pack file is attached.
    /// </summary>
    [JsonProperty("preset", Order = 1)]
    public string Preset { get; set; } = "Wet";

    /// <summary>
    /// When <see cref="Preset"/> is <c>"custom"</c>, path (relative to pack
    /// root) of the JSON describing the ParticleSystem modules.
    /// </summary>
    [JsonProperty("file", NullValueHandling = NullValueHandling.Ignore, Order = 2)]
    public string? File { get; set; }

    /// <summary>
    /// Optional name override for the GameObject the loader attaches under
    /// <c>MBase1</c>. Defaults match the mod's current behaviour: a child
    /// named <c>"Wet"</c> for the Wet preset.
    /// </summary>
    [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore, Order = 3)]
    public string? Name { get; set; }

    /// <summary>
    /// Whether the particle GameObject starts active. In the host mod, most
    /// busts carry an inactive "Wet" child that is activated only for
    /// specific swim variants (e.g. <c>AnisSwimWet</c>). Defaults to
    /// <c>false</c> (inactive) matching that convention.
    /// </summary>
    [JsonProperty("active", Order = 4)]
    public bool Active { get; set; } = false;
}
