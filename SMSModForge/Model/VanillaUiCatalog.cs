using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// The vanilla game's UI, as the list of things a pack can extend.
/// <para/>
/// Built by <c>Tools/Ui/BuildUiCatalog.py</c> from the surface extraction, and
/// shipped as <c>VanillaUi/vanilla_ui_bases.json</c> next to the exe. It is the
/// small half of that extraction on purpose: the full trees run to several
/// megabytes because they hold every rectangle of every object, and none of
/// that is needed to answer the first question the tab asks, which is "what is
/// there to extend, and which one did you mean".
/// <para/>
/// Absent catalog means every lookup returns null and the tab offers nothing to
/// extend. The editor still opens, the same as it does without the level
/// catalog — an extraction that has not been run is a missing convenience, not
/// a broken install.
/// </summary>
public static class VanillaUiCatalog
{
    /// <summary>Token prefix for a vanilla UI base, matching the <c>vanilla:</c>
    /// form places already use so the two read alike.</summary>
    public const string TokenPrefix = "vanillaui:";

    // ── The shipped shape ────────────────────────────────────────────

    public sealed class Base
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("objects")] public int Objects { get; set; }
        [JsonProperty("onAtLoad")] public bool OnAtLoad { get; set; }
        [JsonProperty("siblingIndex")] public int SiblingIndex { get; set; }

        /// <summary>Whether this base's name is shared with a sibling, so its
        /// path alone does not identify it and its id carries a suffix.</summary>
        [JsonProperty("ambiguousName")] public bool AmbiguousName { get; set; }

        /// <summary>How many of each interesting component the base contains —
        /// enough to tell a tooltip from a whole screen in a picker, without
        /// loading the tree.</summary>
        [JsonProperty("contents")] public Dictionary<string, int> Contents { get; set; } = new();

        /// <summary>Set on load, not stored: which surface this came from.</summary>
        [JsonIgnore] public Surface Surface { get; internal set; } = null!;

        public string Token => TokenPrefix + Id;

        /// <summary>A one-line description for a picker: what it is made of.</summary>
        public string Summary
        {
            get
            {
                if (Objects == 0) return "empty";
                var parts = Contents
                    .Where(kv => kv.Value > 0)
                    .OrderByDescending(kv => kv.Value)
                    .Take(3)
                    .Select(kv => kv.Value + " " + Friendly(kv.Key));
                string made = string.Join(", ", parts);
                return made.Length == 0
                    ? Objects + " objects"
                    : Objects + " objects — " + made;
            }
        }

        private static string Friendly(string component) => component switch
        {
            "TextMeshProUGUI" => "labels",
            "Text" => "labels",
            "Image" => "images",
            "Trigger" => "clickable",
            "Button" => "buttons",
            "Slider" => "sliders",
            "Toggle" => "toggles",
            "ScrollRect" => "scroll areas",
            "InputField" => "text fields",
            _ => component,
        };
    }

    public sealed class Surface
    {
        /// <summary>How this surface is addressed. Usually its path, but two
        /// sibling GameObjects in this scene are both called Canvas under the
        /// same level, so those carry a #n suffix in sibling order. Without it
        /// their children collide exactly - both levels have a Button - and the
        /// lookup silently keeps whichever it read last.</summary>
        [JsonProperty("id")] public string Id { get; set; } = "";

        /// <summary>Whether this surface's path is shared with another, so its
        /// id carries a suffix.</summary>
        [JsonProperty("ambiguousPath")] public bool AmbiguousPath { get; set; }

        /// <summary>The real path in the scene, suffix-free. What to show a
        /// person; <see cref="Id"/> is what to store.</summary>
        [JsonProperty("path")] public string Path { get; set; } = "";
        [JsonProperty("scene")] public string Scene { get; set; } = "";
        [JsonProperty("liveAtLoad")] public bool LiveAtLoad { get; set; }

        /// <summary>False when the canvas produced no geometry — its Canvas
        /// component is disabled, so Unity never gave its RectTransform a size
        /// and everything under it measured as a point. Those cannot be
        /// previewed, and offering them would promise something the editor
        /// cannot draw.</summary>
        [JsonProperty("usable")] public bool Usable { get; set; } = true;

        /// <summary>Whether this surface is the gameplay canvas — the one whose
        /// CanvasGroup the game dims when it hides the interface.</summary>
        [JsonProperty("dimsWithGameplayUi")] public bool DimsWithGameplayUi { get; set; }

        [JsonProperty("referenceResolution")] public float[]? ReferenceResolution { get; set; }
        [JsonProperty("matchWidthOrHeight")] public float? MatchWidthOrHeight { get; set; }
        [JsonProperty("uiScaleMode")] public string UiScaleMode { get; set; } = "";
        [JsonProperty("sortingOrder")] public int SortingOrder { get; set; }
        [JsonProperty("bases")] public List<Base> Bases { get; set; } = new();
    }

    private sealed class CatalogFile
    {
        [JsonProperty("gameplayCanvas")] public string GameplayCanvas { get; set; } = "";
        [JsonProperty("surfaces")] public List<Surface> Surfaces { get; set; } = new();
    }

    // ── Access ───────────────────────────────────────────────────────

    private static readonly object Gate = new();
    private static bool _loaded;
    private static List<Surface> _surfaces = new();
    private static Dictionary<string, Base> _byId = new(StringComparer.OrdinalIgnoreCase);
    private static string _gameplayCanvas = "";

    public static bool IsAvailable { get { EnsureLoaded(); return _byId.Count > 0; } }

    public static IReadOnlyList<Surface> Surfaces { get { EnsureLoaded(); return _surfaces; } }

    /// <summary>Every base, across every surface.</summary>
    public static IEnumerable<Base> AllBases
    {
        get { EnsureLoaded(); return _surfaces.SelectMany(s => s.Bases); }
    }

    /// <summary>Bases on surfaces the editor can actually draw. What a picker
    /// should offer, since the rest cannot be previewed.</summary>
    public static IEnumerable<Base> UsableBases
        => AllBases.Where(b => b.Surface.Usable);

    /// <summary>The canvas whose CanvasGroup the game dims when it hides the
    /// interface. A pack UI that should vanish with the rest goes under it.</summary>
    public static string GameplayCanvas { get { EnsureLoaded(); return _gameplayCanvas; } }

    /// <summary>Look up a base by token or by bare path. Unknown returns null,
    /// which callers treat as "the author named something this catalog does not
    /// have" rather than as an error — a pack written against a newer game can
    /// reach a base this build has never heard of.</summary>
    public static Base? Find(string? tokenOrPath)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(tokenOrPath)) return null;
        string id = tokenOrPath.StartsWith(TokenPrefix, StringComparison.OrdinalIgnoreCase)
            ? tokenOrPath.Substring(TokenPrefix.Length)
            : tokenOrPath;
        return _byId.TryGetValue(id.Trim(), out var found) ? found : null;
    }

    /// <summary>Whether a base sits under the gameplay canvas, and so is hidden
    /// when the game hides the interface. Unknown bases answer false rather
    /// than guessing.</summary>
    public static bool DimsWithGameplayUi(string? tokenOrPath)
        => Find(tokenOrPath)?.Surface.DimsWithGameplayUi ?? false;

    // ── Loading ──────────────────────────────────────────────────────

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Gate)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory,
                                           "VanillaUi", "vanilla_ui_bases.json");
                if (!File.Exists(path)) return;
                var parsed = JsonConvert.DeserializeObject<CatalogFile>(File.ReadAllText(path));
                if (parsed?.Surfaces == null) return;

                var byId = new Dictionary<string, Base>(StringComparer.OrdinalIgnoreCase);
                foreach (var surface in parsed.Surfaces)
                    foreach (var b in surface.Bases)
                    {
                        b.Surface = surface;
                        if (!string.IsNullOrEmpty(b.Id)) byId[b.Id] = b;
                    }

                _surfaces = parsed.Surfaces;
                _byId = byId;
                _gameplayCanvas = parsed.GameplayCanvas;
            }
            catch
            {
                // A malformed catalog must not stop the editor opening. The tab
                // simply has nothing to offer, which is the same state as an
                // extraction that was never run.
                _surfaces = new List<Surface>();
                _byId = new Dictionary<string, Base>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Test seam: forget what was loaded so the next call reads again.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            _loaded = false;
            _surfaces = new List<Surface>();
            _byId = new Dictionary<string, Base>(StringComparer.OrdinalIgnoreCase);
            _gameplayCanvas = "";
        }
    }
}
