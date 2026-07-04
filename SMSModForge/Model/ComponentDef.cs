using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SMSModForge.Model;

/// <summary>
/// The generic utility components a pack can attach to an Extra GameObject
/// (overlay). Each mirrors a vanilla game component by name and maps to a
/// reusable MonoBehaviour in the plugin runtime.
/// </summary>
public enum PackComponentType
{
    /// <summary>Fade a SpriteRenderer in: alpha 0 → target over a duration.</summary>
    FadeInSprite,
    /// <summary>Fade a SpriteRenderer out: current alpha → 0 over a duration; optionally deactivate after.</summary>
    FadeOutSprite,
    /// <summary>Activate one random child on enable, disable the rest.</summary>
    RandomChildActivator,
    /// <summary>Toggle a SpriteRenderer's alpha between min/max on an interval (blink / pulse).</summary>
    BlinkingSprite,
}

/// <summary>
/// One generic utility component attached to an <see cref="OverlayDef"/>. The
/// union of fields below covers every <see cref="PackComponentType"/>; the
/// <c>ShouldSerialize*</c> gates keep the JSON to just the fields the chosen
/// type actually uses, and the editor shows the same subset. At runtime the
/// plugin's <c>PackComponentFactory</c> reads these back and configures the
/// matching MonoBehaviour.
/// </summary>
public sealed class ComponentDef
{
    [JsonProperty("type", Order = 1)]
    [JsonConverter(typeof(StringEnumConverter))]
    public PackComponentType Type { get; set; } = PackComponentType.FadeInSprite;

    // ── FadeInSprite ──────────────────────────────────────────────────
    [JsonProperty("fadeDuration", Order = 2)]
    public float FadeDuration { get; set; } = 1f;
    [JsonProperty("targetAlpha", Order = 3)]
    public float TargetAlpha { get; set; } = 1f;

    // ── FadeOutSprite ─────────────────────────────────────────────────
    [JsonProperty("duration", Order = 4)]
    public float Duration { get; set; } = 1f;
    [JsonProperty("deactivateOnComplete", Order = 5)]
    public bool DeactivateOnComplete { get; set; } = false;

    // ── Shared by both fades (optional lead-in) ───────────────────────
    [JsonProperty("startDelay", Order = 6)]
    public float StartDelay { get; set; } = 0f;

    // ── RandomChildActivator ──────────────────────────────────────────
    [JsonProperty("reshuffleOnEnable", Order = 7)]
    public bool ReshuffleOnEnable { get; set; } = true;

    // ── BlinkingSprite ────────────────────────────────────────────────
    [JsonProperty("blinkInterval", Order = 8)]
    public float BlinkInterval { get; set; } = 0.5f;
    [JsonProperty("minAlpha", Order = 9)]
    public float MinAlpha { get; set; } = 0f;
    [JsonProperty("maxAlpha", Order = 10)]
    public float MaxAlpha { get; set; } = 1f;

    // Serialize only the fields the chosen type uses.
    public bool ShouldSerializeFadeDuration() => Type == PackComponentType.FadeInSprite;
    public bool ShouldSerializeTargetAlpha() => Type == PackComponentType.FadeInSprite;
    public bool ShouldSerializeDuration() => Type == PackComponentType.FadeOutSprite;
    public bool ShouldSerializeDeactivateOnComplete() => Type == PackComponentType.FadeOutSprite;
    public bool ShouldSerializeStartDelay() => Type == PackComponentType.FadeInSprite || Type == PackComponentType.FadeOutSprite;
    public bool ShouldSerializeReshuffleOnEnable() => Type == PackComponentType.RandomChildActivator;
    public bool ShouldSerializeBlinkInterval() => Type == PackComponentType.BlinkingSprite;
    public bool ShouldSerializeMinAlpha() => Type == PackComponentType.BlinkingSprite;
    public bool ShouldSerializeMaxAlpha() => Type == PackComponentType.BlinkingSprite;
}
