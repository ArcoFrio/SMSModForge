using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// A cosmetic organizing folder for any left-bar unit list — editor-only,
/// ignored by the runtime (unknown key). One shape shared by every tab that
/// gained folders after the hand-rolled Dialogues / Variables / Integration
/// trees: folders nest arbitrarily and hold unit <em>keys</em> (a unit not
/// listed in any folder shows at the tree root). Purely a view grouping —
/// it never changes what ships or how it runs.
/// </summary>
public sealed class UnitFolderDef
{
    [JsonProperty("name", Order = 1)]
    public string Name { get; set; } = "";

    [JsonProperty("folders", Order = 2)]
    public List<UnitFolderDef> Folders { get; set; } = new();

    /// <summary>Unit keys placed directly in this folder, in display order.</summary>
    [JsonProperty("items", Order = 3)]
    public List<string> Items { get; set; } = new();

    public bool ShouldSerializeFolders() => Folders != null && Folders.Count > 0;
    public bool ShouldSerializeItems() => Items != null && Items.Count > 0;
}
