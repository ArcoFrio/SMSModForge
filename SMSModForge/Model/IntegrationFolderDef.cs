using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// A cosmetic rule-organizing folder — editor-only. Stored in
/// <c>modpack.json</c> under <see cref="ModPack.IntegrationFolders"/>, which the
/// runtime plugin never reads (unknown key). Folders nest arbitrarily and hold
/// rule <em>keys</em> (a rule not listed in any folder shows at the tree root).
/// Purely a view grouping — it doesn't change how or when rules run.
/// Mirrors <see cref="VariableFolderDef"/> / <see cref="DialogueFolderDef"/>.
/// </summary>
public sealed class IntegrationFolderDef
{
    [JsonProperty("name", Order = 1)]
    public string Name { get; set; } = "";

    [JsonProperty("folders", Order = 2)]
    public List<IntegrationFolderDef> Folders { get; set; } = new();

    /// <summary>Rule keys placed directly in this folder, in display order.</summary>
    [JsonProperty("rules", Order = 3)]
    public List<string> Rules { get; set; } = new();

    public bool ShouldSerializeFolders() => Folders != null && Folders.Count > 0;
    public bool ShouldSerializeRules() => Rules != null && Rules.Count > 0;
}
