using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One outfit = one bust GameObject. Maps 1:1 to a current
/// <c>CreateNewBust(name, pathToCG, base, blink, mask, mouth, expression, hasMouth, hasExpression)</c>
/// call site, with the additional jiggle/particle data the editor authors.
/// </summary>
public sealed class OutfitDef
{
    /// <summary>
    /// Field key — the loader stores the resulting GameObject in a dictionary
    /// under this key, so e.g. <c>BustPacks.bustsByKey["newgirlSwim"]</c>
    /// resolves to the GameObject. Matches the static-field naming in
    /// <c>Characters.cs</c> (camelCase).
    /// </summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "newgirl";

    /// <summary>
    /// Name applied to the cloned GameObject via <c>newBust.name = ...</c>.
    /// Mirrors the first argument to the legacy <c>CreateNewBust</c>.
    /// </summary>
    [JsonProperty("gameObjectName", Order = 2)]
    public string GameObjectName { get; set; } = "NewgirlBase";

    /// <summary>Relative path (from pack root) to the base PNG. 256×256, RGBA.</summary>
    [JsonProperty("baseSprite", Order = 3)]
    public string BaseSprite { get; set; } = "";

    /// <summary>Relative path to the jiggle-mask PNG. R/G/B/A drive the shader.</summary>
    [JsonProperty("maskSprite", Order = 4)]
    public string MaskSprite { get; set; } = "";

    /// <summary>Relative path to the blink PNG (eyes-closed overlay).</summary>
    [JsonProperty("blinkSprite", Order = 5)]
    public string BlinkSprite { get; set; } = "";

    /// <summary>
    /// Whether this outfit has a blink frame at all. Mirrors
    /// <see cref="MouthSpec.Enabled"/> and <see cref="ExpressionSpec.Enabled"/>:
    /// off strips the <c>SpriteRenderer</c> from the bust's Blink child, so the
    /// eyes simply never close.
    /// <para/>
    /// Worth having because blink was the one overlay with no way to say "this
    /// bust hasn't got one". A missing blink sprite does not degrade — the
    /// runtime treats it as a broken outfit and skips the whole bust, so a
    /// character with no blink art does not appear in the game at all.
    /// <para/>
    /// Defaults to true and is only written when false, so every pack authored
    /// before this existed keeps exactly the bytes and behaviour it had.
    /// </summary>
    [JsonProperty("blinkEnabled", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
    public bool BlinkEnabled { get; set; } = true;

    public bool ShouldSerializeBlinkEnabled() => !BlinkEnabled;

    [JsonProperty("mouth", Order = 6)]
    public MouthSpec Mouth { get; set; } = new();

    [JsonProperty("expression", Order = 7)]
    public ExpressionSpec Expression { get; set; } = new();

    [JsonProperty("jiggle", Order = 8)]
    public JiggleParams Jiggle { get; set; } = new();

    [JsonProperty("particles", Order = 9, ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<ParticleRef> Particles { get; set; } = new() { new ParticleRef { Preset = "Wet" } };
}
