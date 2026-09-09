using System.Collections.Generic;

namespace SMSModForge.Model;

/// <summary>
/// Catalog of vanilla bust GameObjects under <c>2_Bust_Manager</c> in
/// <c>CoreGameScene</c> in Starmaker Story 1.8E. Used by the Characters tab
/// so a character can borrow an existing in-game bust instead of the pack
/// shipping art of its own.
/// <para/>
/// The pack plugin treats vanilla bust references identically to pack
/// bust references — both are GameObject-name lookups under
/// <c>2_Bust_Manager</c>. The vanilla list just spares the author from
/// remembering the exact name.
/// <para/>
/// Derived from <c>a decompiled scene-hierarchy dump of the target game</c>
/// (lines 30323–45446).
/// <para/>
/// NOT every bust in that dump is listed here, and refreshing this list is
/// therefore not a matter of re-running the extraction. A number of busts
/// sit in the scene without any content that shows them — unreleased work,
/// which this tool has no business advertising. They are excluded
/// deliberately and are not named anywhere in this repository.
/// <para/>
/// To refresh against a new game version: run the bust audit in the
/// SMSDiagDebug plugin (F9 scans what references each bust, F11 exports the
/// art of the ones nothing references), review the shortlist with the game
/// authors, and add only what is confirmed shipped. Pasting the raw
/// extraction back in would undo this.
/// </summary>
public static class VanillaBusts
{
    public sealed record VanillaBust(string GoName, string Character);

    /// <summary>
    /// Every direct child of <c>2_Bust_Manager</c>. The <see cref="Character"/>
    /// field is a best-effort grouping derived from the GO name (the part
    /// before the first underscore or `Bust`-suffix) — used by the editor
    /// to render an "Anna ▸" / "Charlotte ▸" style grouped picker if we
    /// ever upgrade the UI. The grouping isn't authoritative; some names
    /// don't fit a clean prefix and fall back to themselves.
    /// <para/>
    /// The list itself lives in <c>Shared/VanillaCastData.cs</c>, compiled
    /// into the runtime plugin as well: the editor offers these to an author
    /// and the plugin has to resolve the ones an author used, and a second
    /// copy would drift the first time one was corrected.
    /// </summary>
    public static readonly IReadOnlyList<VanillaBust> All =
        System.Array.ConvertAll(SMSModForge.Shared.VanillaCastData.Busts,
                                b => new VanillaBust(b.GoName, b.Character));

    /// <summary>Lookup by GO name. Returns null if the name isn't in the catalog.</summary>
    public static VanillaBust? FindByGoName(string goName)
    {
        foreach (var b in All)
            if (b.GoName == goName) return b;
        return null;
    }
}
