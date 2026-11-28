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

        /// <summary>The sprite's pivot as a share of its rect, (0,0) its bottom-left (Unity's <c>sprite.pivot /
        /// sprite.rect.size</c>), when it is not the centre (1.9.2). The extracted art is the sprite's rect alone, so
        /// one cut out of a whole level's canvas but pivoted on that canvas's centre (the Beach's water: 1.108,
        /// 2.387) drew a long way from where the game draws it when centred on its own rect. Absent: the centre.</summary>
        [JsonProperty("pivot")] public float[]? Pivot { get; set; }

        public float PivotX => Pivot is { Length: >= 2 } ? Pivot[0] : 0.5f;
        public float PivotY => Pivot is { Length: >= 2 } ? Pivot[1] : 0.5f;

        /// <summary>Renderer tint as RGBA hex. Its alpha is half the story of how
        /// solid the object looks.</summary>
        [JsonProperty("color")] public string Color { get; set; } = "FFFFFFFF";

        /// <summary>The MATERIAL's own alpha, when it exposes one. The other
        /// half: the game's reflection material carries _Alpha 0.58 while its
        /// renderer colour stays opaque white, so reading the colour alone shows
        /// a reflection at full strength.</summary>
        [JsonProperty("materialAlpha")] public float MaterialAlpha { get; set; } = 1f;

        /// <summary>Material name. The only reliable way to tell a reflection
        /// from an ordinary sprite: the geometry alone doesn't, since a mirrored
        /// scale is used for plenty of things that aren't reflections.</summary>
        [JsonProperty("material")] public string Material { get; set; } = "";

        /// <summary>The renderer tint's RGB as "#RRGGBB", or blank when it's
        /// plain white and so has nothing to say.</summary>
        public string TintRgb
        {
            get
            {
                if (string.IsNullOrEmpty(Color) || Color.Length < 6) return "";
                string rgb = Color.Substring(0, 6);
                return rgb.Equals("FFFFFF", System.StringComparison.OrdinalIgnoreCase) ? "" : "#" + rgb;
            }
        }

        /// <summary>True when this renderer uses a reflection material.</summary>
        public bool IsReflection =>
            !string.IsNullOrEmpty(Material) &&
            Material.IndexOf("Reflection", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Alpha a reflection falls back to when the catalog predates
        /// materialAlpha — the value the game's own reflection material uses.
        /// Better than treating it as opaque, which is what made the two
        /// Downtown reflections whose RENDERER colour is fully opaque draw as
        /// solid copies of the character.</summary>
        private const float ReflectionFallbackAlpha = 0.58f;

        /// <summary>How solid this renderer actually draws — both alphas together.</summary>
        public float EffectiveAlpha
        {
            get
            {
                float a = 1f;
                if (!string.IsNullOrEmpty(Color) && Color.Length >= 8 &&
                    int.TryParse(Color.Substring(6, 2), System.Globalization.NumberStyles.HexNumber,
                                 System.Globalization.CultureInfo.InvariantCulture, out var byteA))
                    a = byteA / 255f;

                float mat = MaterialAlpha <= 0f ? 1f : MaterialAlpha;
                // A reflection whose material alpha wasn't recorded would other-
                // wise inherit 1 and draw solid. Its own material never does.
                if (mat >= 0.999f && IsReflection) mat = ReflectionFallbackAlpha;
                return a * mat;
            }
        }
    }

    public sealed class Level
    {
        [JsonProperty("goName")] public string GoName { get; set; } = "";
        [JsonProperty("siblingIndex")] public int SiblingIndex { get; set; }
        [JsonProperty("activeSelf")] public bool ActiveSelf { get; set; }
        [JsonProperty("hierarchy")] public Node? Hierarchy { get; set; }

        /// <summary>Where the level shows a video, when it does (1.9.2; Tools/Scenes/level-video-stills.py).</summary>
        [JsonProperty("videoScreen")] public VideoScreen? VideoScreen { get; set; }

        /// <summary>Where the level stands from the game's camera, in world units:
        /// its parent's place (<see cref="LevelsX"/>, <see cref="LevelsY"/>) plus its
        /// own (1.9.2).</summary>
        [JsonIgnore] public float LevelPositionX => LevelsX + (Hierarchy?.X ?? 0f);
        [JsonIgnore] public float LevelPositionY => LevelsY + (Hierarchy?.Y ?? 0f);
    }

    /// <summary>
    /// Where a level shows a video (1.9.2): a VideoPlayer renders its clip into a
    /// render texture, and a camera-space canvas's Image, whose material samples
    /// it, shows it behind the level - the Inside Car's sunset through its windows,
    /// the Subway Train's tunnel. Read from the game's scene by
    /// Tools/Scenes/level-video-stills.py, with a still of the clip beside the
    /// level's art. The Image's centre and size are in the canvas's units (its
    /// scaler's reference resolution over the screen), y up from the centre.
    /// </summary>
    public sealed class VideoScreen
    {
        [JsonProperty("still")] public string Still { get; set; } = "Video.PNG";
        [JsonProperty("clip")] public string Clip { get; set; } = "";
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("width")] public double Width { get; set; } = 1920;
        [JsonProperty("height")] public double Height { get; set; } = 1080;
        [JsonProperty("scale")] public double Scale { get; set; } = 1;
        [JsonProperty("sortingLayer")] public string SortingLayer { get; set; } = "Default";
        [JsonProperty("sortingOrder")] public int SortingOrder { get; set; }
        [JsonProperty("referenceWidth")] public double ReferenceWidth { get; set; } = 1920;
        [JsonProperty("referenceHeight")] public double ReferenceHeight { get; set; } = 1080;
    }

    /// <summary>
    /// Where the game keeps its levels' parent, <c>5_Levels</c>, from its camera
    /// (at the origin): measured in the game (1.8E, 2026-11-19; scale 1). The
    /// extraction has each level's place under it, not the parent's own, so a
    /// level drawn at the camera's centre sat 13 game pixels above where the game
    /// shows it - and one placed at x -0.94 13 pixels right of it. Listed in
    /// GAME_UPDATE_CHECKS.md.
    /// </summary>
    public const float LevelsX = 0.685f, LevelsY = -0.2f;

    /// <summary>The game's level every pack place is built from (the plugin's
    /// PlaceFactory clones it): its "Light 2D" stays, so a pack's place is lit by
    /// the Beach's lamp (1.9.2).</summary>
    public const string PackPlacePrototype = "14_Beach";

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

    /// <summary>The level every pack place is made from (PlaceFactory clones it).</summary>
    public const string TemplateLevel = "14_Beach";

    /// <summary>The template level's NPCs object's own scale, measured (S17, after 1.10.0): what a pack place's NPCs
    /// keep in the game while the pack's NPCs node says 1.</summary>
    public const float TemplateNpcsScale = 0.96053f;

    /// <summary>
    /// The scale of a level's own NPCs object (<paramref name="goName"/>'s child named NPCs), which a pack's NPCs node
    /// grafts onto: the plugin sets a scale there only when it is not exactly 1 (PlaceFactory.BuildNodeRecursive), so at
    /// 1 the game keeps this one. <paramref name="fallback"/> where the catalog does not know the level or it has none.
    /// </summary>
    public static float NpcsObjectScale(string? goName, float fallback = 1f)
    {
        var children = FindLevel(goName)?.Hierarchy?.Children;
        if (children != null)
            foreach (var c in children)
                if (string.Equals(c.Name, "NPCs", StringComparison.OrdinalIgnoreCase) && c.ScaleY > 0) return c.ScaleY;
        return fallback;
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
    /// Whether a level object is one of the game's own NPCs: drawn with the
    /// game's NPC material (NPCCoreMat and its variants), and not the
    /// reflection material that draws their mirror on the floor.
    /// <para/>
    /// The material, because nothing else tells them apart: most are called
    /// "NPC", "NPC (2)", but the convention guests, the stage crowd, Adrian at
    /// the door and the villa's agents (named "Square") are NPCs too, and plenty
    /// of them stand outside any "NPCs" object.
    /// </summary>
    public static bool IsNpc(Node? node)
    {
        var sr = node?.SpriteRenderer;
        return sr != null && !sr.IsReflection
            && (sr.Material ?? "").StartsWith("NPCCore", System.StringComparison.Ordinal);
    }

    /// <summary>
    /// ABSOLUTE path to the PNG extracted for one node inside a level, or ""
    /// when it has none. The extractor writes every SpriteRenderer except the
    /// level's own and its secondary child into <c>_extra/&lt;path&gt;.PNG</c>,
    /// keyed by the object's path below the level — deterministic, so this
    /// reconstructs it rather than needing another field in the catalog.
    /// <para/>
    /// The level's own sprite is not in <c>_extra</c> (it is Base.PNG, the
    /// level layer), so the root resolves to nothing here. The level's second
    /// child is not either: the extractor wrote its sprite as Secondary.PNG, so
    /// this resolves it to that file and the preview draws it as the object it
    /// is, where it stands (1.9.2). It was drawn as a back layer, centred and
    /// fitted to the level's frame, which is right for nothing: in 30 levels the
    /// second child is no backdrop at all (an NPC, a potted plant, a dining
    /// table, drawn screen-sized behind the level), and the backdrops among them
    /// sit off the centre, scaled or flipped (the Pool, the Harbor District).
    /// </summary>
    public static string FindNodeArt(Level? level, Node? node)
    {
        if (level == null || node?.SpriteRenderer == null) return "";
        var root = ArtRoot();
        if (root == null) return "";
        var second = level.Hierarchy?.Children is { Count: > 1 } kids ? kids[1] : null;
        if (second != null && (ReferenceEquals(node, second) || node.Path == second.Path))
            return FindArt(level.GoName, "Secondary.PNG");

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
