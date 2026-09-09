using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Works out what a vanilla UI extension actually CHANGES, so the manifest
/// stores — and the runtime applies — only that.
/// <para/>
/// Authoring one means editing a screen the pack does not own. The tree is
/// seeded from the vanilla surface so the author can see what is there, and
/// almost every node stays exactly as the game has it. Writing all of them out
/// would bloat the manifest and, worse, read as though the pack were asserting
/// values it merely copied — which matters at the next game update, when a
/// re-asserted value silently pins a screen to the shape it had when the pack
/// was written.
/// <para/>
/// So at save time every bound node is compared against the vanilla one:
/// <list type="bullet">
///   <item>Override flags are set from the COMPARISON, not from the author
///   having remembered to tick a box.</item>
///   <item>A bound node that changes nothing and has no surviving descendant is
///   dropped entirely; it would have been a no-op at runtime.</item>
/// </list>
/// Nodes the pack created are always kept, and a base with no vanilla entry
/// falls back to the author's own flags, so an editor without the extraction
/// still behaves.
/// <para/>
/// The pass never touches the live model. It builds pruned copies and hands
/// back a restore action, so saving cannot quietly rewrite what is on screen —
/// the same contract as <see cref="VanillaDelta"/>, for the same reason.
/// </summary>
public static class VanillaUiDelta
{
    /// <summary>Comparison tolerance. Every number here has been through the
    /// extractor's five-decimal text form, so an exact compare would report
    /// differences that are only rounding.</summary>
    private const double Epsilon = 1e-4;

    /// <summary>Look up the vanilla node an extension is anchored to.</summary>
    public delegate VanillaUiSurface.Node? BaselineLookup(string source, string bindPath);

    /// <summary>
    /// Swap each vanilla UI extension's node list for its pruned delta. Returns
    /// an action that puts the originals back — call it in a finally.
    /// </summary>
    public static Action PrepareForSave(ModPack pack, BaselineLookup? lookup = null)
    {
        var restores = new List<Action>();
        if (pack?.Uis == null) return () => { };

        lookup ??= DefaultLookup;

        foreach (var extension in pack.Uis)
        {
            // A UI of the pack's own has nothing to be a delta against: every
            // object in it is the pack's assertion, and pruning would compare
            // it to a screen that does not exist.
            if (!extension.IsVanillaBased) continue;

            var original = extension.Nodes;
            var pruned = Prune(original, extension.Source, lookup);
            if (ReferenceEquals(pruned, original)) continue;

            extension.Nodes = pruned;
            var capture = extension;
            restores.Add(() => capture.Nodes = original);
        }

        return () => { foreach (var restore in restores) restore(); };
    }

    /// <summary>How many nodes an extension would store before and after
    /// pruning. For telling an author that their edit is three properties
    /// rather than four hundred objects.</summary>
    public static (int Before, int After) Measure(UiDef extension,
                                                  BaselineLookup? lookup = null)
    {
        if (extension == null) return (0, 0);
        if (!extension.IsVanillaBased) return (Count(extension.Nodes), Count(extension.Nodes));
        var pruned = Prune(extension.Nodes, extension.Source, lookup ?? DefaultLookup);
        return (Count(extension.Nodes), Count(pruned));
    }

    private static VanillaUiSurface.Node? DefaultLookup(string source, string bindPath)
    {
        var entry = VanillaUiCatalog.Find(source);
        if (entry == null) return null;
        var surface = Rendering.VanillaUiLibrary.SurfaceFor(entry);
        var root = surface?.Base(entry.Id);
        return root == null ? null : NodeAt(root, bindPath);
    }

    /// <summary>Walk a path like "Header/Title" down from a base. Segments that
    /// name a repeated sibling carry the same #n suffix the catalog uses, so an
    /// author who binds to the second of two objects called Image keeps binding
    /// to that one.</summary>
    public static VanillaUiSurface.Node? NodeAt(VanillaUiSurface.Node root, string? path)
    {
        if (root == null) return null;

        // "." is the base itself. It needs a spelling of its own because an
        // empty Bind means "this object is new", and the base's own row would
        // otherwise be indistinguishable from an object the pack invented -
        // which kept the whole tree instead of pruning it.
        if (string.IsNullOrEmpty(path) || path == ".") return root;

        var here = root;
        foreach (string raw in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string name = raw;
            int nth = 1;
            int at = raw.LastIndexOf('#');
            if (at > 0 && int.TryParse(raw[(at + 1)..], out int parsed))
            {
                name = raw[..at];
                nth = Math.Max(1, parsed);
            }

            here = here.Children.Where(c => c.Name == name).Skip(nth - 1).FirstOrDefault();
            if (here == null) return null;
        }
        return here;
    }

    // ── Pruning ──────────────────────────────────────────────────────

    private static List<UiNodeDef> Prune(List<UiNodeDef> nodes, string source,
                                         BaselineLookup lookup)
    {
        var kept = new List<UiNodeDef>();
        bool changed = false;

        foreach (var node in nodes)
        {
            var pruned = PruneOne(node, source, lookup, out bool dropped);
            if (dropped) { changed = true; continue; }
            if (!ReferenceEquals(pruned, node)) changed = true;
            kept.Add(pruned);
        }

        return changed ? kept : nodes;
    }

    private static UiNodeDef PruneOne(UiNodeDef node, string source, BaselineLookup lookup,
                                      out bool dropped)
    {
        dropped = false;

        // Order is decided before pruning, because it changes what pruning may
        // throw away: once a parent's children are being placed explicitly,
        // every one of them has to survive to carry its place.
        var vanillaHere = node.IsBound ? lookup(source, node.Bind) : null;
        bool ordered = vanillaHere != null && OrderAsserted(node, vanillaHere);

        var children = ordered
            ? KeepAllInOrder(node.Children, source, lookup)
            : Prune(node.Children, source, lookup);
        bool childrenChanged = !ReferenceEquals(children, node.Children);

        // A node the pack created is its own assertion and is always kept, as
        // are its children.
        if (!node.IsBound)
        {
            if (!childrenChanged) return node;
            var copy = Copy(node);
            copy.Children = children;
            return copy;
        }

        var vanilla = vanillaHere;
        if (vanilla == null)
        {
            // No baseline to compare against — an editor without the
            // extraction, or a game that has moved on. The author's own flags
            // are all there is, so they are honoured rather than second-guessed.
            if (!childrenChanged) return node;
            var asIs = Copy(node);
            asIs.Children = children;
            return asIs;
        }

        var made = Copy(node);
        made.Children = children;
        made.OverrideRect = RectDiffers(node.Rect, vanilla.Rect);
        made.OverrideImage = ImageDiffers(node.Image, vanilla.Image);
        made.OverrideText = TextDiffers(node.Text, vanilla.Text);
        made.OverrideActive = node.StartActive != vanilla.ActiveSelf;

        // Alpha, shadow and outline are seeded from the game like everything
        // else, so merely HAVING one is not an assertion - it is a copy. Left
        // as "not null means the pack means it", every object carrying a
        // CanvasGroup survived pruning forever, and 318 of them do.
        bool alphaDiffers = AlphaDiffers(node.Alpha, vanilla.CanvasGroup);
        bool shadowDiffers = EffectDiffers(node.Shadow, vanilla.Shadow);
        bool outlineDiffers = EffectDiffers(node.Outline, vanilla.Outline);

        // One question, asked in one place. This used to be a second list of
        // the same conditions, and it drifted the moment a new kind of change
        // was added: a click attached to one of the game's objects was saved
        // correctly and still shown in the tree as untouched, because only one
        // of the two lists had heard of clicks.
        bool asserts = Asserts(node, vanilla);

        if (!asserts && made.Children.Count == 0)
        {
            dropped = true;
            return node;
        }

        // Drop the values that are not being asserted, so the manifest carries
        // the change and nothing else. A reader can then see what the pack does
        // by reading it, which is the point.
        if (!made.OverrideRect) made.Rect = new UiRectDef();
        if (!made.OverrideImage) made.Image = null;
        if (!made.OverrideText) made.Text = null;
        if (!alphaDiffers) made.Alpha = null;
        if (!shadowDiffers) made.Shadow = null;
        if (!outlineDiffers) made.Outline = null;
        return made;
    }

    /// <summary>
    /// Whether a node asserts anything against the vanilla object it is bound
    /// to — the same question the pruning pass asks, exposed so the editor can
    /// mark a changed row using the rule that actually decides what is saved.
    /// <para/>
    /// Two implementations of "has this been touched" would drift, and the one
    /// on screen drifting from the one on disk is the worst version of that: an
    /// author would see no marker and still ship a change, or see a marker for
    /// something that never reaches the pack.
    /// </summary>
    public static bool Asserts(UiNodeDef node, VanillaUiSurface.Node vanilla)
    {
        if (node == null || vanilla == null) return false;
        return RectDiffers(node.Rect, vanilla.Rect)
            || ImageDiffers(node.Image, vanilla.Image)
            || TextDiffers(node.Text, vanilla.Text)
            || node.StartActive != vanilla.ActiveSelf
            || AlphaDiffers(node.Alpha, vanilla.CanvasGroup)
            || EffectDiffers(node.Shadow, vanilla.Shadow)
            || EffectDiffers(node.Outline, vanilla.Outline)
            || node.ActiveConditions.Count > 0
            || node.Components.Count > 0
            // Behaviour and arrangement the game's object did not have. Nothing
            // is compared against vanilla for these: vanilla has nothing of the
            // kind to compare against, so having one at all IS the change.
            || node.OnClick.Count > 0
            || node.ClickConditions.Count > 0
            || !string.IsNullOrEmpty(node.HoverTint)
            || node.Layout != null;
    }

    // ── Comparisons ──────────────────────────────────────────────────

    private static bool AlphaDiffers(float? authored, VanillaUiSurface.Group? vanilla)
    {
        if (authored == null) return false;                     // asserting nothing
        if (vanilla == null) return true;                       // adding a group
        return Math.Abs(authored.Value - vanilla.Alpha) > Epsilon;
    }

    private static bool EffectDiffers(UiEffectDef? authored, VanillaUiSurface.Effect? vanilla)
    {
        if (authored == null) return false;
        if (vanilla == null) return true;
        return !SameColor(authored.Color, vanilla.Color)
            || !Same(authored.Distance, vanilla.Distance);
    }

    private static bool RectDiffers(UiRectDef? authored, VanillaUiSurface.Rect? vanilla)
    {
        // A vanilla object with no RectTransform recorded has nothing to be
        // compared against and draws nothing either way, so treating the
        // authored default as a change would keep the object for no reason.
        if (vanilla == null) return false;
        if (authored == null) return false;
        return !Same(authored.AnchorMin, vanilla.AnchorMin)
            || !Same(authored.AnchorMax, vanilla.AnchorMax)
            || !Same(authored.Pivot, vanilla.Pivot)
            || !Same(authored.Position, vanilla.AnchoredPosition)
            || !Same(authored.Size, vanilla.SizeDelta)
            || !Same(authored.Scale, vanilla.LocalScale)
            || Math.Abs(authored.RotationZ - Third(vanilla.LocalEuler)) > Epsilon;
    }

    private static bool ImageDiffers(UiImageDef? authored, VanillaUiSurface.Image? vanilla)
    {
        if (authored == null) return false;              // not asserting one
        if (vanilla == null) return true;                // adding one where there was none
        // Compared against the same disambiguated form the seed produced, or
        // the nine shared names would each read as an edit for ever.
        string vanillaName = Rendering.VanillaUiLibrary.Assets.NameForKey(vanilla.SpriteKey);
        if (vanillaName.Length == 0) vanillaName = vanilla.Sprite;

        return !string.Equals(authored.Sprite, vanillaName, StringComparison.Ordinal)
            || !string.Equals(authored.Type, vanilla.Type, StringComparison.OrdinalIgnoreCase)
            || !SameColor(authored.Tint, vanilla.Color)
            || authored.FillCenter != vanilla.FillCenter
            || authored.PreserveAspect != vanilla.PreserveAspect
            || Math.Abs(authored.PixelsPerUnitMultiplier
                        - vanilla.PixelsPerUnitMultiplier) > Epsilon;
    }

    private static bool TextDiffers(UiTextDef? authored, VanillaUiSurface.Text? vanilla)
    {
        if (authored == null) return false;
        if (vanilla == null) return true;
        return !string.Equals(authored.Value, vanilla.Value, StringComparison.Ordinal)
            || !string.Equals(authored.Font, vanilla.Font, StringComparison.Ordinal)
            || Math.Abs(authored.Size - vanilla.Size) > Epsilon
            || !SameColor(authored.Color, vanilla.Color)
            || !string.Equals(authored.Alignment, vanilla.Alignment,
                              StringComparison.OrdinalIgnoreCase)
            || authored.Wrap != vanilla.Wraps
            || Math.Abs(authored.LineSpacing - (vanilla.Number(vanilla.LineSpacing) ?? 0)) > Epsilon
            || Math.Abs(authored.CharacterSpacing
                        - (vanilla.Number(vanilla.CharacterSpacing) ?? 0)) > Epsilon;
    }

    /// <summary>Colours compare by value rather than by spelling, so "#ffffff"
    /// and "#FFFFFFFF" are not recorded as a change the author never made.</summary>
    private static bool SameColor(string? a, string? b)
    {
        var one = Rendering.UiColor.Parse(a);
        var two = Rendering.UiColor.Parse(b);
        return one.B == two.B && one.G == two.G && one.R == two.R && one.A == two.A;
    }

    private static bool Same(float[]? authored, float[]? vanilla)
        => Same(authored?.Select(v => (double)v).ToArray(), vanilla);

    private static bool Same(double[]? authored, float[]? vanilla)
    {
        if (authored == null || vanilla == null) return authored == null && vanilla == null;
        int n = Math.Min(authored.Length, vanilla.Length);
        for (int i = 0; i < n; i++)
            if (Math.Abs(authored[i] - vanilla[i]) > Epsilon) return false;
        return true;
    }

    private static double Third(float[]? euler) => euler is { Length: > 2 } ? euler[2] : 0;

    // ── Copying ──────────────────────────────────────────────────────

    /// <summary>A shallow copy that shares nothing the pass writes to. The live
    /// model must come out of a save exactly as it went in.</summary>
    private static UiNodeDef Copy(UiNodeDef from) => new()
    {
        Name = from.Name,
        Rect = from.Rect,
        Image = from.Image,
        Text = from.Text,
        Alpha = from.Alpha,
        Shadow = from.Shadow,
        Outline = from.Outline,
        ClipChildren = from.ClipChildren,
        StartActive = from.StartActive,
        ActiveConditions = from.ActiveConditions,
        Components = from.Components,
        Children = from.Children,
        Bind = from.Bind,
        OverrideRect = from.OverrideRect,
        OverrideImage = from.OverrideImage,
        OverrideText = from.OverrideText,
        OverrideActive = from.OverrideActive,
        SiblingIndex = from.SiblingIndex,
        OnClick = from.OnClick,
        ClickConditions = from.ClickConditions,
        HoverTint = from.HoverTint,
        Layout = from.Layout,
    };

    // ── Order ─────────────────────────────────────────────────
    //
    // What the game does on its own, if nothing says otherwise: objects it
    // already owns stay where they are, and objects the pack creates are added
    // on the end. So an author who has not rearranged anything is describing
    // exactly that, and it costs nothing to store.
    //
    // Anything else - a vanilla object moved in front of another, or one of the
    // pack's own placed BETWEEN two vanilla ones rather than after them - is a
    // decision, and has to be written down.

    /// <summary>Whether this parent's children need their places recorded.</summary>
    private static bool OrderAsserted(UiNodeDef node, VanillaUiSurface.Node vanilla)
    {
        int last = -1;
        bool seenNew = false;

        foreach (var child in node.Children)
        {
            if (!child.IsBound) { seenNew = true; continue; }

            // A vanilla object sitting after one the pack added: left alone the
            // added one would go on the end, behind it, not in front.
            if (seenNew) return true;

            int at = VanillaIndexOf(vanilla, LastSegment(child.Bind));
            if (at < 0) continue;            // bound to something no longer there
            if (at <= last) return true;     // two vanilla objects swapped over
            last = at;
        }
        return false;
    }

    /// <summary>Prune a parent's children as usual, but keep every one of them
    /// and stamp its place. An object that asserts nothing else is still
    /// asserting where it goes.</summary>
    private static List<UiNodeDef> KeepAllInOrder(List<UiNodeDef> nodes, string source,
                                                  BaselineLookup lookup)
    {
        var kept = new List<UiNodeDef>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var pruned = PruneOne(nodes[i], source, lookup, out bool dropped);

            // Dropped means "identical to the game's own", so nothing but the
            // name, the binding and the place needs to survive.
            var copy = dropped
                ? new UiNodeDef { Name = nodes[i].Name, Bind = nodes[i].Bind }
                : ReferenceEquals(pruned, nodes[i]) ? Copy(nodes[i]) : pruned;

            copy.SiblingIndex = i;
            kept.Add(copy);
        }
        return kept;
    }

    /// <summary>Where a bind segment's object sits among the vanilla children,
    /// or -1. The #n suffix counts repeats of a name, exactly as
    /// <see cref="NodeAt"/> reads it.</summary>
    private static int VanillaIndexOf(VanillaUiSurface.Node parent, string segment)
    {
        if (parent == null || string.IsNullOrEmpty(segment)) return -1;

        string name = segment;
        int nth = 1;
        int at = segment.LastIndexOf('#');
        if (at > 0 && int.TryParse(segment[(at + 1)..], out int parsed))
        {
            name = segment[..at];
            nth = Math.Max(1, parsed);
        }

        int seen = 0;
        for (int i = 0; i < parent.Children.Count; i++)
            if (parent.Children[i].Name == name && ++seen == nth) return i;
        return -1;
    }

    /// <summary>The part of a bind path that names the object within its own
    /// parent.</summary>
    private static string LastSegment(string bind)
    {
        if (string.IsNullOrEmpty(bind) || bind == ".") return "";
        int slash = bind.LastIndexOf('/');
        return slash < 0 ? bind : bind[(slash + 1)..];
    }

    private static int Count(List<UiNodeDef>? nodes)
        => nodes == null ? 0 : nodes.Count + nodes.Sum(n => Count(n.Children));
}
