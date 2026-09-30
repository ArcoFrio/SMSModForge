using System;
using System.IO;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// Resolves a bust GO name to its on-disk art folder.
/// <para/>
/// Vanilla bust art is <em>shipped</em> with the editor — the build copies
/// every PNG under <c>SMSModForge/Resources/VanillaBustArt/</c> to a sibling
/// <c>VanillaBustArt/</c> folder next to <c>SMSModForge.exe</c>. The raw
/// PNGs originate from a Unity-editor script
/// (<c>Tools/UnityEditor/SMSModForgeArtExtractor.cs</c>) that the user runs
/// inside the vanilla game's Unity project once, then commits the result.
/// <para/>
/// Pack-authored outfits keep their existing per-outfit paths under the
/// pack root. They take precedence over vanilla art so a pack that
/// re-skins an existing character displays its own sprites.
/// </summary>
public static class VanillaArtResolver
{
    /// <summary>
    /// Returns the shipped <c>VanillaBustArt</c> folder if it exists. The
    /// .csproj copies it next to <c>SMSModForge.exe</c> on every build,
    /// so this is the canonical location at run-time.
    /// </summary>
    public static string? FindArtRoot()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "VanillaBustArt");
        return Directory.Exists(shipped) ? shipped : null;
    }

    /// <summary>
    /// Locate <c>Base.PNG</c> for a bust. Pack outfits take precedence —
    /// a pack that re-skins a vanilla character overrides the shipped
    /// art, whether it draws the bust itself or replaces the base texture on
    /// one of the game's - and the vanilla folder is the fallback.
    /// </summary>
    public static string? FindBaseSpritePath(string bustGoName, ModPack pack, string? packRoot)
    {
        if (string.IsNullOrEmpty(bustGoName)) return null;

        foreach (var c in pack.Characters)
            foreach (var o in c.Outfits)
            {
                if (!string.Equals(o.GameObjectName, bustGoName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(packRoot)) continue;
                var replaced = Replacement(o, Shared.SpriteSlotNames.Base, packRoot);
                if (replaced != null) return replaced;
                if (!string.IsNullOrEmpty(o.BaseSprite))
                {
                    var abs = Path.Combine(packRoot, o.BaseSprite.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(abs)) return abs;
                }
            }

        var artRoot = FindArtRoot();
        if (artRoot == null) return null;
        var vp = Path.Combine(artRoot, bustGoName, "Base.PNG");
        return File.Exists(vp) ? vp : null;
    }

    /// <summary>
    /// Locate the art for one FACE of a bust - a face, not a line's expression
    /// key: turn a key into its face with <see cref="ExpressionFaces.FaceFor"/>
    /// first, the way the game does.
    /// <para/>
    /// Where the game gets it, in the same order: a face the pack paints on one
    /// of the game's busts (replacing one it has, or adding one it never had);
    /// the pack's own bust, <c>{prefix}{face}.PNG</c>; and the game's own art,
    /// <c>Expression{face}.PNG</c> in the bust's folder.
    /// </summary>
    public static string? FindExpressionSpritePath(string bustGoName, string face, ModPack pack, string? packRoot)
    {
        if (string.IsNullOrEmpty(bustGoName) || string.IsNullOrEmpty(face)) return null;

        foreach (var c in pack.Characters)
            foreach (var o in c.Outfits)
            {
                if (!string.Equals(o.GameObjectName, bustGoName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(packRoot)) continue;
                var replaced = Replacement(o, Shared.SpriteSlotNames.Expression(face), packRoot);
                if (replaced != null) return replaced;
                if (o.Expression.Enabled && Shared.GameArt.IsBorrowed(o.Expression.Prefix))
                {
                    // Borrowed from one of the game's busts: that bust's face.
                    var root = FindArtRoot();
                    var borrowed = root == null ? null
                        : Path.Combine(root, Shared.GameArt.BustOf(o.Expression.Prefix)!, "Expression" + face + ".PNG");
                    if (borrowed != null && File.Exists(borrowed)) return borrowed;
                }
                else if (o.Expression.Enabled && !string.IsNullOrEmpty(o.Expression.Prefix))
                {
                    var rel = o.Expression.Prefix + face + ".PNG";
                    var abs = Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(abs)) return abs;
                }
            }

        var artRoot = FindArtRoot();
        if (artRoot == null) return null;
        var vp = Path.Combine(artRoot, bustGoName, "Expression" + face + ".PNG");
        return File.Exists(vp) ? vp : null;
    }

    /// <summary>
    /// The shipped copy of one file of the game's bust a borrowing field names
    /// (<see cref="Shared.GameArt"/>) - <c>Blink.PNG</c>, <c>Mask.PNG</c>,
    /// <c>Mouth1.PNG</c> - or null when there is none. Smaller than the game's
    /// own, which the game uses; for the editor to show and start from.
    /// </summary>
    public static string? GameArtFile(string? field, string file)
    {
        string? bust = Shared.GameArt.BustOf(field);
        string? root = FindArtRoot();
        if (string.IsNullOrEmpty(bust) || root == null) return null;
        string path = Path.Combine(root, bust, file);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The file a pack paints over <paramref name="slot"/> of one of
    /// the game's busts, if it names one that is there. A slot ticked with no
    /// file, or a file that is missing, leaves the game's own - as in game.</summary>
    private static string? Replacement(OutfitDef outfit, string slot, string packRoot)
    {
        foreach (var entry in outfit.SpriteOverrides)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Sprite)) continue;
            if (!string.Equals(entry.Slot, slot, StringComparison.Ordinal)) continue;
            var abs = Path.Combine(packRoot, entry.Sprite.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(abs)) return abs;
        }
        return null;
    }
}
