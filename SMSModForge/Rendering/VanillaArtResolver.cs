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
    /// art — and the vanilla folder is the fallback.
    /// </summary>
    public static string? FindBaseSpritePath(string bustGoName, ModPack pack, string? packRoot)
    {
        if (string.IsNullOrEmpty(bustGoName)) return null;

        foreach (var c in pack.Characters)
            foreach (var o in c.Outfits)
                if (string.Equals(o.GameObjectName, bustGoName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(o.BaseSprite) && !string.IsNullOrEmpty(packRoot))
                {
                    var abs = Path.Combine(packRoot, o.BaseSprite.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(abs)) return abs;
                }

        var artRoot = FindArtRoot();
        if (artRoot == null) return null;
        var vp = Path.Combine(artRoot, bustGoName, "Base.PNG");
        return File.Exists(vp) ? vp : null;
    }

    /// <summary>
    /// Locate the expression PNG for a (bust, expression key) pair.
    /// Pack outfits use the <c>ExpressionPrefix + expressionKey + ".PNG"</c>
    /// convention; vanilla art uses <c>Expression&lt;Key&gt;.PNG</c> inside
    /// the bust's folder.
    /// </summary>
    public static string? FindExpressionSpritePath(string bustGoName, string expressionKey, ModPack pack, string? packRoot)
    {
        if (string.IsNullOrEmpty(bustGoName) || string.IsNullOrEmpty(expressionKey)) return null;

        foreach (var c in pack.Characters)
            foreach (var o in c.Outfits)
                if (string.Equals(o.GameObjectName, bustGoName, StringComparison.OrdinalIgnoreCase) && o.Expression.Enabled && !string.IsNullOrEmpty(o.Expression.Prefix) && !string.IsNullOrEmpty(packRoot))
                {
                    var rel = o.Expression.Prefix + expressionKey + ".PNG";
                    var abs = Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(abs)) return abs;
                }

        var artRoot = FindArtRoot();
        if (artRoot == null) return null;
        var vp = Path.Combine(artRoot, bustGoName, "Expression" + expressionKey + ".PNG");
        return File.Exists(vp) ? vp : null;
    }
}
