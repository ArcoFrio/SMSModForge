using System.Collections.Generic;

namespace SMSModForge.Model;

/// <summary>
/// Catalog of district radial-menu names on the World Map of Starmaker
/// Story 1.8E — i.e. the children of
/// <c>World_Map/Canvas/Core/Radial_Buttons</c>. These are the legal values
/// for <see cref="MapButtonDef.District"/>.
/// <para/>
/// Static (not loaded from disk) because districts are baked into the base
/// game's scene; the editor uses this list to populate the district picker.
/// </summary>
public static class WorldMapDistricts
{
    /// <summary>
    /// One district radial menu. <see cref="GoName"/> is the literal
    /// <c>Radial_Buttons</c> child name (matches what the runtime looks
    /// up via <c>Transform.Find</c>); <see cref="DisplayName"/> is what
    /// the editor shows the author.
    /// </summary>
    public readonly record struct District(string GoName, string DisplayName);

    public static IReadOnlyList<District> All { get; } = new District[]
    {
        // World_Map > Canvas > Core > Radial_Buttons children.
        new("Seaside",  "Seaside"),
        new("TheLine",  "The Line"),
        new("NeonRow",  "Neon Row (Nightlife)"),
        new("Shopside", "Shopside (Shopping)"),
        new("Foundry",  "Foundry (Harbor)"),
    };

    public static District? FindByGoName(string goName)
    {
        foreach (var d in All)
            if (d.GoName == goName) return d;
        return null;
    }
}
