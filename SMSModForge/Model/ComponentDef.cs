using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Model;

/// <summary>
/// The generic utility components a pack can attach to a GameObject
/// (overlay). Each mirrors a vanilla game component by name and maps to a
/// reusable MonoBehaviour in the plugin runtime.
/// </summary>
public static class PackComponentType
{
    /// <summary>Fade a SpriteRenderer in: alpha 0 → target over a duration.</summary>
    public const string FadeInSprite = "FadeInSprite";
    /// <summary>Fade a SpriteRenderer out: current alpha → 0 over a duration; optionally deactivate after.</summary>
    public const string FadeOutSprite = "FadeOutSprite";
    /// <summary>Activate one random child on enable, disable the rest.</summary>
    public const string RandomChildActivator = "RandomChildActivator";
    /// <summary>Toggle a SpriteRenderer's alpha between min/max on an interval (blink / pulse).</summary>
    public const string BlinkingSprite = "BlinkingSprite";

    /// <summary>The four the plugin reimplements itself, each with a purpose-built
    /// editor below. Anything else names a component the GAME defines, which the
    /// runtime resolves and configures by reflection.</summary>
    public static readonly string[] BuiltIn =
        { FadeInSprite, FadeOutSprite, RandomChildActivator, BlinkingSprite };

    public static bool IsBuiltIn(string type) => System.Array.IndexOf(BuiltIn, type) >= 0;
}

/// <summary>
/// One generic utility component attached to a <see cref="GameObjectDef"/>. The
/// union of fields below covers every <see cref="PackComponentType"/>; the
/// <c>ShouldSerialize*</c> gates keep the JSON to just the fields the chosen
/// type actually uses, and the editor shows the same subset. At runtime the
/// plugin's <c>PackComponentFactory</c> reads these back and configures the
/// matching MonoBehaviour.
/// </summary>
public sealed class ComponentDef
{
    /// <summary>
    /// A free-form type NAME rather than an enum, so a pack can attach one of
    /// the game's own components (ParallaxMouseEffect, OffsetScrolling…) and not
    /// only the four reimplemented here. The four keep their purpose-built
    /// editors; anything else is authored as name/value pairs and configured by
    /// reflection at runtime.
    /// </summary>
    [JsonProperty("type", Order = 1)]
    public string Type { get; set; } = PackComponentType.FadeInSprite;

    /// <summary>
    /// Parameters for a component the game defines, kept flat alongside
    /// <see cref="Type"/> — the same shape the built-in fields serialize to, and
    /// the shape the runtime reads. Extension data so any key round-trips
    /// without this class having to know it.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken> Params { get; set; } = new Dictionary<string, JToken>();

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

    // Serialize only the fields the chosen type uses. A game component uses none
    // of them — its values live in Params.
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
