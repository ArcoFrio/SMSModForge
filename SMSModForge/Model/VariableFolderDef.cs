using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// A cosmetic variable-organizing folder — editor-only. Stored in
/// <c>modpack.json</c> under <see cref="ModPack.VariableFolders"/>, which the
/// runtime plugin never reads (unknown key). Folders nest arbitrarily and hold
/// variable <em>names</em> (a variable not listed in any folder shows at the
/// tree root). Purely a view grouping — it doesn't change how variables ship.
/// Mirrors <see cref="DialogueFolderDef"/>.
/// </summary>
public sealed class VariableFolderDef
{
    [JsonProperty("name", Order = 1)]
    public string Name { get; set; } = "";

    [JsonProperty("folders", Order = 2)]
    public List<VariableFolderDef> Folders { get; set; } = new();

    /// <summary>Variable names placed directly in this folder, in display order.</summary>
    [JsonProperty("variables", Order = 3)]
    public List<string> Variables { get; set; } = new();

    public bool ShouldSerializeFolders() => Folders != null && Folders.Count > 0;
    public bool ShouldSerializeVariables() => Variables != null && Variables.Count > 0;
}
