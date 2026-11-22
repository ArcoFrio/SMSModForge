using System;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// The light every pack place starts with (1.10.0): the plugin builds a pack
/// place from a clone of the template level (<see cref="VanillaLevelCatalog.PackPlacePrototype"/>,
/// the Beach), whose "Light 2D" stays. The author (2026-11-19): it "should be
/// kept as is but allowing the user to customize or remove it the same way a
/// normal light would".
/// <para/>
/// So the place's tree carries it as a bound node, seeded from the catalog
/// like a vanilla extension's objects (<see cref="VanillaDelta.Rebase"/>): it
/// is only in memory until the author changes it, and the save writes it only
/// then (<see cref="VanillaDelta.PrepareForSave"/>) - a place nobody touched
/// saves exactly as before, and the light in the game is the template's.
/// </summary>
public static class TemplateLight
{
    /// <summary>The template level's child that holds its light.</summary>
    public const string ObjectName = "Light 2D";   // English on purpose: the game object's own name, which the plugin finds it by.

    /// <summary>The template level, from the catalog (null without one).</summary>
    public static VanillaLevelCatalog.Level? Level => VanillaLevelCatalog.FindLevel(VanillaLevelCatalog.PackPlacePrototype);

    /// <summary>The template's light object, from the catalog (null without one).</summary>
    public static VanillaLevelCatalog.Node? Baseline
        => VanillaLevelCatalog.RootChildren(Level).FirstOrDefault(c =>
               string.Equals(c.Name, ObjectName, StringComparison.Ordinal) && c.ComponentValues.Any(v => v.Type == "Light2D"));

    /// <summary>Whether a top-level node of a pack place is the template's light.</summary>
    public static bool Is(GameObjectDef n)
        => n.Bind && string.Equals(n.Name, ObjectName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Put the template's light in a place's tree, before the NPCs (the author's
    /// own objects keep their places in the list), if it is not there -
    /// or anchor the one read from the file to the catalog, as a vanilla
    /// extension's objects are. Nothing without the catalog: then a place keeps
    /// what its file says and the game keeps the template's light.
    /// Returns the node, or null.
    /// </summary>
    public static GameObjectDef? Seed(PlaceDef place)
    {
        var baseline = Baseline;
        var level = Level;
        if (baseline == null || level == null) return null;
        var node = place.GameObjects.FirstOrDefault(Is);
        if (node == null)
        {
            node = new GameObjectDef { Name = ObjectName, Bind = true };
            int at = place.GameObjects.FindIndex(g => g.IsNpcRoot);
            place.GameObjects.Insert(at < 0 ? place.GameObjects.Count : at, node);
        }
        VanillaDelta.Rebase(node, baseline, level);
        return node;
    }
}
