using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// A local transform for one part of a placed NPC (its body, shadow, blink
/// overlay or particle emitter). Lives on the <see cref="NpcPlacementDef"/> —
/// not the <see cref="NpcDef"/> — because where each part sits is a property of
/// the appearance in a level, not of the reusable pose. All nine channels are
/// present so the Unity-style gizmo can drive any of them uniformly.
/// <para/>
/// Position is in local units under the part's parent; rotation is local euler
/// degrees; scale is a multiplier (negative X mirrors). Defaults are identity.
/// </summary>
public sealed class NpcTransform
{
    [JsonProperty("x", Order = 1)] public float X { get; set; }
    [JsonProperty("y", Order = 2)] public float Y { get; set; }
    [JsonProperty("z", Order = 3)] public float Z { get; set; }

    [JsonProperty("rotX", Order = 4)] public float RotX { get; set; }
    [JsonProperty("rotY", Order = 5)] public float RotY { get; set; }
    [JsonProperty("rotZ", Order = 6)] public float RotZ { get; set; }

    [JsonProperty("scaleX", Order = 7)] public float ScaleX { get; set; } = 1f;
    [JsonProperty("scaleY", Order = 8)] public float ScaleY { get; set; } = 1f;
    [JsonProperty("scaleZ", Order = 9)] public float ScaleZ { get; set; } = 1f;

    public NpcTransform Clone() => (NpcTransform)MemberwiseClone();
}
