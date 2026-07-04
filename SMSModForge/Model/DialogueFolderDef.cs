using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// A cosmetic dialogue-organizing folder — editor-only. Stored in
/// <c>modpack.json</c> under <see cref="ModPack.DialogueFolders"/>, which the
/// runtime plugin never reads (unknown key). Folders nest arbitrarily and hold
/// dialogue <em>keys</em> (a dialogue not listed in any folder shows at the
/// tree root). Purely a view grouping — it doesn't change how dialogues ship.
/// </summary>
public sealed class DialogueFolderDef
{
    [JsonProperty("name", Order = 1)]
    public string Name { get; set; } = "";

    [JsonProperty("folders", Order = 2)]
    public List<DialogueFolderDef> Folders { get; set; } = new();

    /// <summary>Dialogue keys placed directly in this folder, in display order.</summary>
    [JsonProperty("dialogues", Order = 3)]
    public List<string> Dialogues { get; set; } = new();

    public bool ShouldSerializeFolders() => Folders != null && Folders.Count > 0;
    public bool ShouldSerializeDialogues() => Dialogues != null && Dialogues.Count > 0;
}
