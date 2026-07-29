using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Every component type the vanilla extraction saw, and the parameter names it
/// saw on each.
/// <para/>
/// Built from <see cref="VanillaLevelCatalog"/> rather than hand-written: the
/// game ships around fifty component types across its levels, they change
/// between game versions, and a hard-coded list would be wrong the moment one
/// does. What the levels actually use is the honest answer to "what can a pack
/// attach", and it comes for free with the extraction the vanilla-extension
/// feature already depends on.
/// </summary>
public static class VanillaComponentCatalog
{
    /// <summary>
    /// Unity's own components. Offered but flagged, because configuring them by
    /// name mostly does not work: their serialized names are engine internals
    /// (<c>m_CastShadows</c>) that don't match anything settable by reflection,
    /// unlike a game script's plain fields. Attaching one still works; only its
    /// parameters are unreliable.
    /// </summary>
    private static readonly HashSet<string> EngineTypes = new(StringComparer.Ordinal)
    {
        "AudioDistortionFilter", "AudioLowPassFilter", "AudioSource", "BoxCollider2D",
        "Canvas", "CanvasGroup", "CanvasRenderer", "CanvasScaler", "CircleCollider2D",
        "GraphicRaycaster", "Image", "Light", "Light2D", "LineRenderer", "MeshCollider",
        "MeshFilter", "MeshRenderer", "ParticleSystem", "ParticleSystemRenderer",
        "Shadow", "ShadowCaster2D", "SpriteMask", "SpriteRenderer", "TextMeshProUGUI",
        "VideoPlayer", "Volume",
    };

    public sealed class Entry
    {
        public string Type = "";
        /// <summary>Parameter names seen on this type, with an example value from
        /// the levels — enough to author against without guessing spellings.</summary>
        public readonly Dictionary<string, string> Parameters = new(StringComparer.Ordinal);
        public bool IsEngineComponent => EngineTypes.Contains(Type);
    }

    private static Dictionary<string, Entry>? _byType;

    /// <summary>Every discovered type, engine components last and each group
    /// alphabetical — the game's own scripts are what an author is looking for.</summary>
    public static IReadOnlyList<Entry> All =>
        Build().Values.OrderBy(e => e.IsEngineComponent).ThenBy(e => e.Type, StringComparer.Ordinal).ToList();

    public static Entry? Find(string type)
        => type != null && Build().TryGetValue(type, out var e) ? e : null;

    /// <summary>Type names for a dropdown: the pack's own four first, then
    /// everything the extraction found.</summary>
    public static IReadOnlyList<string> TypeNames()
    {
        var names = new List<string>(PackComponentType.BuiltIn);
        foreach (var e in All)
            if (!PackComponentType.IsBuiltIn(e.Type)) names.Add(e.Type);
        return names;
    }

    private static Dictionary<string, Entry> Build()
    {
        if (_byType != null) return _byType;
        var map = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var level in VanillaLevelCatalog.All)
            Collect(level?.Hierarchy, map);
        _byType = map;
        return map;
    }

    private static void Collect(VanillaLevelCatalog.Node? n, Dictionary<string, Entry> into)
    {
        if (n == null) return;
        foreach (var cv in n.ComponentValues)
        {
            if (string.IsNullOrEmpty(cv.Type)) continue;
            if (!into.TryGetValue(cv.Type, out var e))
                into[cv.Type] = e = new Entry { Type = cv.Type };
            foreach (var p in cv.Params)
                if (!e.Parameters.ContainsKey(p.Key)) e.Parameters[p.Key] = p.Value?.ToString() ?? "";
        }
        foreach (var c in n.Children) Collect(c, into);
    }

    /// <summary>Drop the cache — the catalog is reloaded when the extraction is
    /// replaced, and this is derived from it.</summary>
    public static void Reset() => _byType = null;
}
