using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One vanilla canvas, in full: every object under it with its geometry, its
/// picture, its text and its effects.
/// <para/>
/// The heavy half of the extraction — about 7.4 MB across the game's 49
/// surfaces, because it records every rectangle of every object. Loaded one
/// surface at a time and only when something needs to draw it, which is why it
/// is kept apart from <see cref="VanillaUiCatalog"/>: choosing what to edit
/// needs a list, and only editing needs the tree.
/// </summary>
public sealed class VanillaUiSurface
{
    public sealed class Resolved
    {
        [JsonProperty("min")] public float[] Min { get; set; } = new float[2];
        [JsonProperty("max")] public float[] Max { get; set; } = new float[2];
        [JsonProperty("size")] public float[] Size { get; set; } = new float[2];

        /// <summary>"direct", "rebuilt" or "unverified" — how much this
        /// rectangle is worth. Unverified means the object is inside a layout
        /// group that has never run because the object is switched off, so the
        /// value stored is the authored one and NOT where the game will draw
        /// it: the moment the game switches it on, the group moves it.</summary>
        [JsonProperty("trust")] public string Trust { get; set; } = "";

        public bool IsTrustworthy => Trust != "unverified";
    }

    public sealed class Rect
    {
        [JsonProperty("anchorMin")] public float[] AnchorMin { get; set; } = { 0.5f, 0.5f };
        [JsonProperty("anchorMax")] public float[] AnchorMax { get; set; } = { 0.5f, 0.5f };
        [JsonProperty("pivot")] public float[] Pivot { get; set; } = { 0.5f, 0.5f };
        [JsonProperty("anchoredPosition")] public float[] AnchoredPosition { get; set; } = new float[2];
        [JsonProperty("sizeDelta")] public float[] SizeDelta { get; set; } = new float[2];
        [JsonProperty("size")] public float[] Size { get; set; } = new float[2];
        [JsonProperty("localScale")] public float[] LocalScale { get; set; } = { 1f, 1f, 1f };
        [JsonProperty("localEuler")] public float[] LocalEuler { get; set; } = new float[3];
        [JsonProperty("resolved")] public Resolved? Resolved { get; set; }
    }

    public sealed class Image
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
        [JsonProperty("color")] public string Color { get; set; } = "#FFFFFFFF";
        [JsonProperty("type")] public string Type { get; set; } = "Simple";
        [JsonProperty("fillCenter")] public bool FillCenter { get; set; } = true;
        [JsonProperty("preserveAspect")] public bool PreserveAspect { get; set; }
        [JsonProperty("pixelsPerUnitMultiplier")] public float PixelsPerUnitMultiplier { get; set; } = 1f;
        [JsonProperty("sprite")] public string Sprite { get; set; } = "";
        [JsonProperty("spriteKey")] public string SpriteKey { get; set; } = "";
        [JsonProperty("border")] public float[] Border { get; set; } = new float[4];
        [JsonProperty("spritePixelsPerUnit")] public float SpritePixelsPerUnit { get; set; } = 100f;

        public bool IsSliced => string.Equals(Type, "Sliced", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class Text
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
        [JsonProperty("kind")] public string Kind { get; set; } = "";
        [JsonProperty("value")] public string Value { get; set; } = "";
        [JsonProperty("font")] public string Font { get; set; } = "";
        [JsonProperty("fontSize")] public string FontSize { get; set; } = "";
        [JsonProperty("color")] public string Color { get; set; } = "#FFFFFFFF";
        [JsonProperty("alignment")] public string Alignment { get; set; } = "";
        [JsonProperty("wordWrapping")] public string WordWrapping { get; set; } = "";
        [JsonProperty("characterSpacing")] public string CharacterSpacing { get; set; } = "";
        [JsonProperty("lineSpacing")] public string LineSpacing { get; set; } = "";

        /// <summary>TextMeshPro reports numbers through reflection, so they
        /// arrive as text. A value that will not parse is treated as absent
        /// rather than as zero — a font size of nothing draws nothing, which
        /// is visible, where a size of zero is silence.</summary>
        public double? Number(string? raw)
            => double.TryParse(raw, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out double v)
               ? v : null;

        public double Size => Number(FontSize) ?? 36;
        public bool Wraps => !string.Equals(WordWrapping, "False", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class Effect
    {
        [JsonProperty("color")] public string Color { get; set; } = "#000000FF";
        [JsonProperty("distance")] public float[] Distance { get; set; } = { 1f, -1f };
        [JsonProperty("useGraphicAlpha")] public bool UseGraphicAlpha { get; set; } = true;
    }

    public sealed class Group
    {
        [JsonProperty("alpha")] public float Alpha { get; set; } = 1f;
        [JsonProperty("blocksRaycasts")] public bool BlocksRaycasts { get; set; } = true;
    }

    public sealed class Node
    {
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("siblingIndex")] public int SiblingIndex { get; set; }
        [JsonProperty("activeSelf")] public bool ActiveSelf { get; set; } = true;
        [JsonProperty("activeInHierarchy")] public bool ActiveInHierarchy { get; set; } = true;
        [JsonProperty("components")] public List<string> Components { get; set; } = new();
        [JsonProperty("rect")] public Rect? Rect { get; set; }
        [JsonProperty("image")] public Image? Image { get; set; }
        [JsonProperty("rawImage")] public Image? RawImage { get; set; }
        [JsonProperty("text")] public Text? Text { get; set; }
        [JsonProperty("shadow")] public Effect? Shadow { get; set; }
        [JsonProperty("outline")] public Effect? Outline { get; set; }
        [JsonProperty("canvasGroup")] public Group? CanvasGroup { get; set; }
        [JsonProperty("mask")] public object? Mask { get; set; }
        [JsonProperty("rectMask")] public object? RectMask { get; set; }
        [JsonProperty("children")] public List<Node> Children { get; set; } = new();

        /// <summary>Whether this object clips what is drawn inside it. Mask and
        /// RectMask2D differ in how the game implements them, but from the
        /// outside both mean the same thing to a preview.</summary>
        public bool Clips => Mask != null || RectMask != null;

        public IEnumerable<Node> Walk()
        {
            yield return this;
            foreach (var child in Children)
                foreach (var n in child.Walk())
                    yield return n;
        }

        public Node? Find(string name)
            => Walk().FirstOrDefault(n => n.Name == name);
    }

    public sealed class CanvasInfo
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
        [JsonProperty("renderMode")] public string RenderMode { get; set; } = "";
        [JsonProperty("referencePixelsPerUnit")] public float ReferencePixelsPerUnit { get; set; } = 100f;
        [JsonProperty("rect")] public float[] Rect { get; set; } = new float[2];
    }

    [JsonProperty("path")] public string Path { get; set; } = "";
    [JsonProperty("scene")] public string Scene { get; set; } = "";
    [JsonProperty("liveAtLoad")] public bool LiveAtLoad { get; set; }
    [JsonProperty("canvas")] public CanvasInfo Canvas { get; set; } = new();
    [JsonProperty("root")] public Node Root { get; set; } = new();

    public float Width => Canvas.Rect.Length > 0 ? Canvas.Rect[0] : 0;
    public float Height => Canvas.Rect.Length > 1 ? Canvas.Rect[1] : 0;

    /// <summary>Whether this surface produced geometry at all. A canvas whose
    /// component is disabled is never given a size by Unity, so everything
    /// under it measured as a point — six of the game's surfaces are like
    /// that, and a file of zeroes is the absence of data rather than data.</summary>
    public bool IsDrawable => Width > 0 && Height > 0;

    /// <summary>One of the canvas's children — a UI in its own right. Ambiguous
    /// names carry the same #n suffix the catalog assigns, so a base named
    /// there resolves to the same object here.</summary>
    public Node? Base(string nameOrId)
    {
        if (string.IsNullOrEmpty(nameOrId)) return null;
        string name = nameOrId.Contains('/')
            ? nameOrId[(nameOrId.LastIndexOf('/') + 1)..] : nameOrId;

        int at = name.LastIndexOf('#');
        if (at > 0 && int.TryParse(name[(at + 1)..], out int nth))
        {
            string bare = name[..at];
            return Root.Children.Where(c => c.Name == bare).Skip(nth - 1).FirstOrDefault();
        }
        return Root.Children.FirstOrDefault(c => c.Name == name);
    }

    public static VanillaUiSurface? Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonConvert.DeserializeObject<VanillaUiSurface>(File.ReadAllText(path))
                : null;
        }
        catch { return null; }
    }
}
