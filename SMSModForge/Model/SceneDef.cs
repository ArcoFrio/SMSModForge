using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// Override mode for a scene's sound. The vanilla scene prototype carries a
/// <c>Trigger</c> on the root GO that plays a positional audio clip on
/// activation. Pack scenes can drop that trigger and instead emit a named
/// GC2 signal — <c>kiss</c> or <c>flash</c> — when activated. <see cref="None"/>
/// keeps the cloned trigger intact (rarely what authors want, but available
/// for the case where the prototype's default sound is fine).
/// </summary>
[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum SceneSoundMode
{
    /// <summary>No override — keep whatever the cloned prototype carries.</summary>
    None,

    /// <summary>Strip the prototype trigger and emit the <c>kiss</c> signal on activation.</summary>
    Kiss,

    /// <summary>Strip the prototype trigger and emit the <c>flash</c> signal on activation.</summary>
    Flash,

    /// <summary>Strip the prototype trigger and stay silent.</summary>
    Silent,
}

/// <summary>
/// One CG / story scene authored by a pack. At runtime the plugin clones a
/// vanilla scene prototype under <c>4_CG_Manager-Sexy</c>, swaps its
/// <c>Core/Art</c> sprite (the actual scene art) and its <c>Core</c> sprite
/// (the frame), and replaces the prototype's audio trigger per
/// <see cref="Sound"/>.
/// <para/>
/// Pack scenes are kept inactive at build time; dialogue actions
/// (<c>ActivateScene</c> / <c>DeactivateAllScenes</c>) drive
/// <see cref="UnityEngine.GameObject.SetActive(bool)"/> at runtime.
/// </summary>
public sealed class SceneDef
{
    /// <summary>
    /// Pack-local key. Used to address the scene from a dialogue action
    /// (<c>ActivateScene</c> takes <c>scene = &lt;key&gt;</c>). Becomes the
    /// scene GO name at runtime, prefixed with the pack id so two packs
    /// can both define a "kiss01" without colliding.
    /// </summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "scene1";

    /// <summary>Human-facing label shown in the editor's scene list.</summary>
    [JsonProperty("displayName", Order = 2)]
    public string DisplayName { get; set; } = "New Scene";

    /// <summary>
    /// Relative path (from pack root) to the scene-art PNG. Mapped onto the
    /// cloned prototype's <c>Core/Art</c> SpriteRenderer at load time —
    /// matches the host mod <c>CreateNewPicScene</c> behaviour. Mutually
    /// exclusive with <see cref="ExternalSpritePath"/>; pack-relative wins
    /// when both are non-empty.
    /// </summary>
    [JsonProperty("sceneSprite", Order = 3)]
    public string SceneSprite { get; set; } = "";

    /// <summary>
    /// Absolute on-disk path to the scene-art PNG. Transitional fallback
    /// for packs porting the host mod content without copying the 135 CG
    /// PNGs into the pack folder. The pack manifest can reference
    /// <c>the host mod's external scene-asset folder</c>
    /// directly. Long-term, prefer <see cref="SceneSprite"/> for
    /// self-contained packs.
    /// </summary>
    [JsonProperty("externalSpritePath", Order = 31, NullValueHandling = NullValueHandling.Ignore)]
    public string? ExternalSpritePath { get; set; }

    /// <summary>
    /// Frame source — exactly one of <see cref="VanillaFrame"/> or
    /// <see cref="CustomFrameSprite"/> should be non-empty.
    /// </summary>
    /// <remarks>
    /// Stable name from <see cref="VanillaFrames"/>. When set, the plugin
    /// loads the matching PNG from its own <c>VanillaFrames\</c> resource
    /// folder (shipped alongside the plugin DLL) and assigns it to the
    /// cloned scene's <c>Core</c> SpriteRenderer.
    /// </remarks>
    [JsonProperty("vanillaFrame", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
    public string? VanillaFrame { get; set; }

    /// <summary>
    /// Relative path (from pack root) to a custom frame PNG. When non-empty
    /// this takes precedence over <see cref="VanillaFrame"/> — the runtime
    /// loads the file from disk and assigns it to the cloned scene's
    /// <c>Core</c> SpriteRenderer.
    /// </summary>
    [JsonProperty("customFrameSprite", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
    public string? CustomFrameSprite { get; set; }

    /// <summary>
    /// How the scene's audio behaves on activation. See <see cref="SceneSoundMode"/>.
    /// Defaults to <see cref="SceneSoundMode.Silent"/> — the prototype's audio is
    /// rarely the right fit for a pack-authored scene.
    /// </summary>
    [JsonProperty("sound", Order = 6)]
    public SceneSoundMode Sound { get; set; } = SceneSoundMode.Silent;
}
