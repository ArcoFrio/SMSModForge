using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// Mouth frames. The bust hierarchy has a <c>Mouth</c> GameObject with four
/// numbered children (1, 2, 3, 4), each a <c>SpriteRenderer</c>. The editor
/// authors a <em>prefix</em>; on disk the four files are
/// <c>{prefix}1.PNG</c> … <c>{prefix}4.PNG</c>. This matches the
/// existing <c>CreateNewBust</c> on-disk convention exactly.
/// </summary>
public sealed class MouthSpec
{
    /// <summary>
    /// If false, the loader strips the <c>SpriteRenderer</c>s from the four
    /// mouth child GameObjects (matches the legacy <c>hasMouth=false</c> path
    /// used by the Solid Snake cameo).
    /// </summary>
    [JsonProperty("enabled", Order = 1)]
    public bool Enabled { get; set; } = true;

    /// <summary>Path prefix relative to pack root, e.g. <c>"Sprites/Newgirl/Mouth"</c>.</summary>
    [JsonProperty("prefix", Order = 2)]
    public string Prefix { get; set; } = "";
}
