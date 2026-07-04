using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One pack-authored sound effect. The runtime loads
/// <see cref="AudioPath"/> from disk into an
/// <see cref="UnityEngine.AudioClip"/> at pack init and registers it
/// under <see cref="Key"/>; dialogue actions then trigger the clip
/// via the <c>PlaySFX</c> node action.
/// <para/>
/// SFX are transient — they play through a single shared
/// <see cref="UnityEngine.AudioSource.PlayOneShot(UnityEngine.AudioClip, float)"/>
/// per pack (under a dedicated <c>SfxPlayer</c> GameObject parented to
/// <c>12_AudioPlayer</c>) so multiple effects can overlap freely
/// without spawning per-clip GameObjects. The counterpart to music
/// (<see cref="MusicDef"/>), which is persistent ambient state and
/// owns its own GO under the same parent.
/// </summary>
public sealed class SfxDef
{
    /// <summary>
    /// Pack-local key. Referenced from <c>PlaySFX</c> node actions
    /// as <c>clip = &lt;key&gt;</c>. Independent of the on-disk file
    /// name so authors can swap audio files without rewriting every
    /// dialogue.
    /// </summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "sfx1";

    /// <summary>Human-facing label shown in the editor's SFX list.</summary>
    [JsonProperty("displayName", Order = 2)]
    public string DisplayName { get; set; } = "New SFX";

    /// <summary>
    /// Relative path (from pack root) to the audio file. OGG, WAV
    /// and MP3 are recognised — the runtime picks the
    /// <see cref="UnityEngine.AudioType"/> from the file extension.
    /// Loaded asynchronously at pack build time via
    /// <c>UnityWebRequestMultimedia</c>; the clip is registered when
    /// the request completes.
    /// </summary>
    [JsonProperty("audioPath", Order = 3)]
    public string AudioPath { get; set; } = "";

    /// <summary>
    /// Optional default playback volume (0..1). Used by the
    /// <c>PlaySFX</c> action when its own <c>volume</c> param is not
    /// supplied, and as the base volume for auto-pattern-matched
    /// plays. Unset = 1.0 (full volume).
    /// </summary>
    [JsonProperty("defaultVolume", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
    public float? DefaultVolume { get; set; }

    /// <summary>
    /// Text patterns whose appearance in a dialogue node's text
    /// auto-triggers this SFX, mirroring the host mod's
    /// <c>CreateSFX(textPattern, ...)</c> + <c>OnDialogueLineStart</c>
    /// flow. Patterns are matched verbatim (case-insensitive
    /// substring) — convention is the asterisk-bracketed form
    /// <c>*plap*</c>, <c>*smooch*</c> etc. so they read inline in
    /// dialogue scripts. One SFX can carry multiple patterns
    /// (the host mod maps both <c>*yank*</c> and <c>*yeet*</c> to
    /// the same Yank clip) — the runtime fires once per match per
    /// pattern per node. Empty list = the SFX only plays when
    /// explicitly invoked through the <c>PlaySFX</c> node action.
    /// </summary>
    [JsonProperty("textPatterns", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
    public System.Collections.Generic.List<string>? TextPatterns { get; set; }
}
