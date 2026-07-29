using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// The vanilla scene's level hierarchy, as dumped by the Unity-editor script at
/// <c>Tools/UnityEditor/SMSModForgeLevelExtractor.cs</c> into
/// <c>Resources/VanillaLevelArt/vanilla_levels.json</c> (shipped next to the
/// exe by the csproj, same as <c>VanillaBustArt</c>).
/// <para/>
/// This is the BASELINE a vanilla extension is authored against: it's what lets
/// the editor tell "the author changed this object" from "this is just how the
/// vanilla scene already is", so an extension can store and apply only its
/// delta instead of rebuilding a structure the game already has.
/// <para/>
/// Absent catalog = every lookup returns null and callers fall back to the
/// author's manual choices — the editor still runs without the extraction
/// having been done.
/// </summary>
public static class VanillaLevelCatalog
{
    public sealed class Node
    {
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("path")] public string Path { get; set; } = "";
        [JsonProperty("activeSelf")] public bool ActiveSelf { get; set; }
        [JsonProperty("localPosition")] public float[] LocalPosition { get; set; } = new float[3];
        [JsonProperty("localEulerAngles")] public float[] LocalEulerAngles { get; set; } = new float[3];
        [JsonProperty("localScale")] public float[] LocalScale { get; set; } = { 1f, 1f, 1f };
        [JsonProperty("components")] public List<string> Components { get; set; } = new();
        /// <summary>The same components with their serialized values. Absent from
        /// catalogs written before the extractor recorded them, hence the empty
        /// default rather than a required field.</summary>
        [JsonProperty("componentValues")] public List<ComponentValues> ComponentValues { get; set; } = new();
        [JsonProperty("spriteRenderer")] public RendererInfo? SpriteRenderer { get; set; }
        [JsonProperty("children")] public List<Node> Children { get; set; } = new();

        public float X => LocalPosition.Length > 0 ? LocalPosition[0] : 0f;
        public float Y => LocalPosition.Length > 1 ? LocalPosition[1] : 0f;
        public float RotationZ => LocalEulerAngles.Length > 2 ? LocalEulerAngles[2] : 0f;
        public float ScaleX => LocalScale.Length > 0 ? LocalScale[0] : 1f;
        public float ScaleY => LocalScale.Length > 1 ? LocalScale[1] : 1f;
    }

    /// <summary>The SpriteRenderer details the preview needs to draw a vanilla
    /// object the way the game does.</summary>
    public sealed class RendererInfo
    {
        [JsonProperty("sprite")] public string Sprite { get; set; } = "";
        [JsonProperty("sortingLayer")] public string SortingLayer { get; set; } = "";
        [JsonProperty("sortingOrder")] public int SortingOrder { get; set; }
        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
        [JsonProperty("pixelsPerUnit")] public float PixelsPerUnit { get; set; } = 100f;
    }

    public sealed class Level
    {
        [JsonProperty("goName")] public string GoName { get; set; } = "";
        [JsonProperty("siblingIndex")] public int SiblingIndex { get; set; }
        [JsonProperty("activeSelf")] public bool ActiveSelf { get; set; }
        [JsonProperty("hierarchy")] public Node? Hierarchy { get; set; }
    }

    /// <summary>One component on a node, with the serialized values the
    /// extractor read off it. Keys are Unity's own property paths.</summary>
    public sealed class ComponentValues
    {
        [JsonProperty("type")] public string Type { get; set; } = "";
        [JsonProperty("params")] public Dictionary<string, object?> Params { get; set; } = new();
    }

    private sealed class CatalogFile
    {
        [JsonProperty("levels")] public List<Level> Levels { get; set; } = new();
    }

    private static readonly object _gate = new();
    private static bool _loaded;
    private static Dictionary<string, Level> _byGoName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True once a catalog file was found and parsed. False means the
    /// extraction hasn't been run (or shipped) — callers should degrade to
    /// whatever the author set by hand rather than assuming "no differences".</summary>
    public static bool IsAvailable { get { EnsureLoaded(); return _byGoName.Count > 0; } }

    public static IReadOnlyCollection<Level> All { get { EnsureLoaded(); return _byGoName.Values; } }

    /// <summary>The catalog entry for a vanilla level GO name, or null.</summary>
    public static Level? FindLevel(string? goName)
    {
        if (string.IsNullOrWhiteSpace(goName)) return null;
        EnsureLoaded();
        return _byGoName.TryGetValue(goName, out var lv) ? lv : null;
    }

    /// <summary>
    /// The catalog entry for a level token as stored on a vanilla extension
    /// (<c>vanilla:&lt;goName&gt;</c>). Anything else — a pack place, a blank —
    /// has no vanilla baseline.
    /// </summary>
    public static Level? FindLevelByToken(string? token)
    {
        const string prefix = "vanilla:";
        if (string.IsNullOrWhiteSpace(token) ||
            !token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return FindLevel(token.Substring(prefix.Length));
    }

    /// <summary>
    /// Walk a slash path relative to the level root (<c>NPCs/Group_1</c>) and
    /// return the node it names, or null. Matching is by name, case-insensitive,
    /// which is how every other GameObject lookup in the editor addresses them.
    /// </summary>
    public static Node? FindNode(Level? level, string? relativePath)
    {
        if (level?.Hierarchy == null || string.IsNullOrWhiteSpace(relativePath)) return null;
        var cur = level.Hierarchy;
        foreach (var seg in relativePath.Split('/'))
        {
            if (string.IsNullOrEmpty(seg)) continue;
            Node? next = null;
            foreach (var c in cur.Children)
                if (string.Equals(c.Name, seg, StringComparison.OrdinalIgnoreCase)) { next = c; break; }
            if (next == null) return null;
            cur = next;
        }
        return cur;
    }

    /// <summary>Direct children of a level's root, i.e. the top level of what a
    /// vanilla extension's GameObjects list sits alongside.</summary>
    public static IReadOnlyList<Node> RootChildren(Level? level)
        => level?.Hierarchy?.Children ?? (IReadOnlyList<Node>)Array.Empty<Node>();

    // ── Level art ─────────────────────────────────────────────────────────

    /// <summary>The shipped <c>VanillaLevelArt</c> folder next to the exe, or
    /// null when the extraction hasn't been deployed.</summary>
    public static string? ArtRoot()
    {
        var shipped = System.IO.Path.Combine(AppContext.BaseDirectory, "VanillaLevelArt");
        return Directory.Exists(shipped) ? shipped : null;
    }

    /// <summary>
    /// ABSOLUTE path to one of a vanilla level's extracted sprites
    /// (<c>Base.PNG</c> / <c>Secondary.PNG</c>), or "" when absent. Absolute on
    /// purpose: the preview builds its path as
    /// <c>Path.Combine(packRoot, sprite)</c>, and Path.Combine yields the second
    /// argument when it's already rooted — so vanilla art drops into the same
    /// preview as pack art with no special-casing there.
    /// </summary>
    public static string FindArt(string? levelGoName, string fileName)
    {
        if (string.IsNullOrWhiteSpace(levelGoName)) return "";
        var root = ArtRoot();
        if (root == null) return "";
        var path = System.IO.Path.Combine(root, Sanitize(levelGoName), fileName);
        return File.Exists(path) ? path : "";
    }

    /// <summary>
    /// ABSOLUTE path to the PNG extracted for one node inside a level, or ""
    /// when it has none. The extractor writes every SpriteRenderer except the
    /// level's own and its secondary child into <c>_extra/&lt;path&gt;.PNG</c>,
    /// keyed by the object's path below the level — deterministic, so this
    /// reconstructs it rather than needing another field in the catalog.
    /// <para/>
    /// Because the two level-art sprites are deliberately NOT in <c>_extra</c>,
    /// the root and secondary nodes simply resolve to nothing here, which is
    /// what keeps the preview from drawing the backdrop a second time on top of
    /// itself.
    /// </summary>
    public static string FindNodeArt(Level? level, Node? node)
    {
        if (level == null || node?.SpriteRenderer == null) return "";
        var root = ArtRoot();
        if (root == null) return "";

        // node.Path starts with the level's own name; the extractor keyed the
        // file by the remainder.
        string rel = node.Path ?? "";
        int slash = rel.IndexOf('/');
        if (slash < 0) return "";                       // the level root itself
        rel = rel.Substring(slash + 1);
        if (string.IsNullOrEmpty(rel)) return "";

        var parts = rel.Split('/');
        for (int i = 0; i < parts.Length; i++) parts[i] = Sanitize(parts[i]);
        var path = System.IO.Path.Combine(root, Sanitize(level.GoName), "_extra",
                                          System.IO.Path.Combine(parts) + ".PNG");
        return File.Exists(path) ? path : "";
    }

    /// <summary>Mirror of the extractor's folder-name sanitising, so a level
    /// whose GO name contains characters a path can't hold still resolves.</summary>
    private static string Sanitize(string s)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        return sb.ToString();
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = System.IO.Path.Combine(
                    AppContext.BaseDirectory, "VanillaLevelArt", "vanilla_levels.json");
                if (!File.Exists(path)) return;
                var parsed = JsonConvert.DeserializeObject<CatalogFile>(File.ReadAllText(path));
                if (parsed?.Levels == null) return;
                var map = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
                foreach (var lv in parsed.Levels)
                    if (!string.IsNullOrEmpty(lv.GoName)) map[lv.GoName] = lv;
                _byGoName = map;
            }
            catch
            {
                // A malformed catalog must not stop the editor opening — the
                // delta pass just falls back to the author's manual flags.
                _byGoName = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
