using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One pack-authored music track for the in-game audio player. The
/// runtime clones the vanilla <c>12_AudioPlayer/Beach</c> template
/// GameObject, swaps its <see cref="UnityEngine.AudioSource.clip"/>
/// for an <see cref="AudioPath"/> loaded from disk, and leaves the
/// new track inactive.
/// <para/>
/// Pack tracks become siblings of the vanilla / the host mod music GOs
/// under <c>12_AudioPlayer</c>, so the existing <c>SwitchMusic</c>
/// node action picks them up automatically — its name lookup hits
/// pack tracks the same way it hits vanilla ones.
/// <para/>
/// Mirrors a host music-player pattern pattern.
/// the host mod loads its <c>MyTrack</c> from the
/// <c>otherbundle</c> asset bundle; packs load from on-disk audio
/// files because shipping an asset bundle defeats the point of pack
/// authoring.
/// </summary>
public sealed class MusicDef
{
    /// <summary>
    /// The music GameObject's runtime name. Also what
    /// <c>SwitchMusic</c> actions name in their <c>music</c> param.
    /// Pick something distinct from vanilla music GO names
    /// (<c>MyTrack</c>, etc.) to avoid colliding with them.
    /// </summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "music1";

    /// <summary>Human-facing label shown in the editor's music list.</summary>
    [JsonProperty("displayName", Order = 2)]
    public string DisplayName { get; set; } = "New Music";

    /// <summary>
    /// Relative path (from pack root) to the audio file. OGG, WAV
    /// and MP3 are recognised — the runtime picks the
    /// <see cref="UnityEngine.AudioType"/> from the file extension.
    /// Loaded asynchronously at pack build time via
    /// <c>UnityWebRequestMultimedia</c>; the
    /// <see cref="UnityEngine.AudioSource.clip"/> is assigned when
    /// the request completes.
    /// </summary>
    [JsonProperty("audioPath", Order = 3)]
    public string AudioPath { get; set; } = "";

    /// <summary>
    /// Optional loop override. When unset, the cloned template's
    /// loop flag carries through (vanilla Beach music loops).
    /// </summary>
    [JsonProperty("loop", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
    public bool? Loop { get; set; }

    /// <summary>
    /// Optional volume override (0..1). When unset, the cloned
    /// template's volume carries through.
    /// </summary>
    [JsonProperty("volume", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
    public float? Volume { get; set; }
}
