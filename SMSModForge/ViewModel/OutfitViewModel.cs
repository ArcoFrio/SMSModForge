using System;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// Wraps an <see cref="OutfitDef"/> with INPC so the editor view updates on
/// every keystroke. Direct field exposure is fine — we don't need transforms,
/// the binding pipes value changes straight back to the POCO.
/// </summary>
public sealed class OutfitViewModel : ObservableObject, IFilterableTreeNode, IMaskEditorHost
{
    public OutfitDef Model { get; }

    public OutfitViewModel(OutfitDef model) { Model = model; }

    public string Key
    {
        get => Model.Key;
        set { Model.Key = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string GameObjectName
    {
        get => Model.GameObjectName;
        set
        {
            // Key follows the name unless it was deliberately made to differ.
            // AddOutfit sets both to the same value and only this one is
            // editable, so without this a rename left Key on the original and
            // the tree label ("Key (GameObjectName)") went stale — while every
            // actual reference in the pack uses GameObjectName.
            // A BLANK name still counts as tracking. Clearing the box and
            // retyping is the normal way to rename, and comparing Key against
            // an empty GameObjectName reads as "deliberately different" — which
            // detached the two permanently after the first keystroke.
            bool keyTracked = string.IsNullOrWhiteSpace(Model.GameObjectName)
                              || string.Equals(Model.Key, Model.GameObjectName,
                                               StringComparison.OrdinalIgnoreCase);
            Model.GameObjectName = value;
            if (keyTracked && !string.IsNullOrWhiteSpace(value)) Model.Key = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Key));
            OnPropertyChanged(nameof(Display));
        }
    }

    public string BaseSprite
    {
        get => Model.BaseSprite;
        set { Model.BaseSprite = value; OnPropertyChanged(); }
    }

    public string MaskSprite
    {
        get => Model.MaskSprite;
        set { Model.MaskSprite = value; OnPropertyChanged(); }
    }

    public string BlinkSprite
    {
        get => Model.BlinkSprite;
        set { Model.BlinkSprite = value; OnPropertyChanged(); SeedPrefixesFromBlink(); }
    }

    /// <summary>
    /// Put the blink art's folder into whichever prefix field is still empty.
    /// <para/>
    /// The overlays of one outfit almost always sit together, so by the time
    /// the blink path is filled in the folder for the mouth and expression
    /// frames is already known — and typing it a third time by hand is where
    /// the typos come from. Only the folder is copied: the filename part is the
    /// author's to choose, and guessing it would be worse than leaving it.
    /// <para/>
    /// Empty fields only, so this never overwrites an answer. It also fires only
    /// once the path names a PNG, which keeps it from filling the prefix with
    /// half a folder while someone is still typing the blink path.
    /// <para/>
    /// Runs on edits, not on load: an outfit read from disk keeps exactly the
    /// prefixes it was saved with.
    /// </summary>
    private void SeedPrefixesFromBlink()
    {
        string path = Model.BlinkSprite ?? "";
        if (!path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) return;

        string folder = FolderOf(path);
        if (folder.Length == 0) return;

        if (string.IsNullOrWhiteSpace(MouthPrefix)) MouthPrefix = folder;
        if (string.IsNullOrWhiteSpace(ExpressionPrefix)) ExpressionPrefix = folder;
    }

    /// <summary>Everything up to and including the last separator, in the
    /// forward-slash form pack paths are stored in. Empty when the path names a
    /// file at the pack root, which has no folder to copy.</summary>
    private static string FolderOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string norm = path.Replace('\\', '/');
        int slash = norm.LastIndexOf('/');
        return slash < 0 ? "" : norm.Substring(0, slash + 1);
    }

    public bool BlinkEnabled
    {
        get => Model.BlinkEnabled;
        set { Model.BlinkEnabled = value; OnPropertyChanged(); }
    }

    public bool MouthEnabled
    {
        get => Model.Mouth.Enabled;
        set { Model.Mouth.Enabled = value; OnPropertyChanged(); }
    }

    public string MouthPrefix
    {
        get => Model.Mouth.Prefix;
        set { Model.Mouth.Prefix = value; OnPropertyChanged(); }
    }

    public bool ExpressionEnabled
    {
        get => Model.Expression.Enabled;
        set { Model.Expression.Enabled = value; OnPropertyChanged(); }
    }

    public string ExpressionPrefix
    {
        get => Model.Expression.Prefix;
        set { Model.Expression.Prefix = value; OnPropertyChanged(); }
    }

    // Jiggle params — bound directly to sliders in the view.
    public float JiggleSpeed
    {
        get => Model.Jiggle.Speed;
        set { Model.Jiggle.Speed = value; OnPropertyChanged(); }
    }
    public float JiggleStrength
    {
        get => Model.Jiggle.Strength;
        set { Model.Jiggle.Strength = value; OnPropertyChanged(); }
    }
    public float JiggleFrequency
    {
        get => Model.Jiggle.Frequency;
        set { Model.Jiggle.Frequency = value; OnPropertyChanged(); }
    }
    public float NoiseScale
    {
        get => Model.Jiggle.NoiseScale;
        set { Model.Jiggle.NoiseScale = value; OnPropertyChanged(); }
    }
    public float NoiseSpeed
    {
        get => Model.Jiggle.NoiseSpeed;
        set { Model.Jiggle.NoiseSpeed = value; OnPropertyChanged(); }
    }
    public float NoiseStrength
    {
        get => Model.Jiggle.NoiseStrength;
        set { Model.Jiggle.NoiseStrength = value; OnPropertyChanged(); }
    }
    public string Tint
    {
        get => Model.Jiggle.Tint;
        set { Model.Jiggle.Tint = value; OnPropertyChanged(); }
    }
    public bool PixelSnap
    {
        get => Model.Jiggle.PixelSnap;
        set { Model.Jiggle.PixelSnap = value; OnPropertyChanged(); }
    }

    public bool ParticleActive
    {
        get => Model.Particles.Count > 0 && Model.Particles[0].Active;
        set
        {
            if (Model.Particles.Count == 0)
                Model.Particles.Add(new ParticleRef());
            Model.Particles[0].Active = value;
            OnPropertyChanged();
        }
    }

    public string Display => $"{Key} ({GameObjectName})";

    // ── IMaskEditorHost ────────────────────────────────────────────────

    string IMaskEditorHost.Key => Model.Key;
    public string PoseSpritePath => BaseSprite;
    public string MaskPath
    {
        get => MaskSprite;
        set => MaskSprite = value;
    }

    // ── Live mask under edit (shared with MaskEditorWindow)

    private byte[]? _liveMaskBgra;
    /// <summary>
    /// In-progress mask buffer from the mask editor. When non-null, the live
    /// preview reads this instead of the file-loaded mask, so brush strokes
    /// show through the shader without touching disk. The mask editor sets it
    /// while open and clears (or commits to file) on close.
    /// </summary>
    public byte[]? LiveMaskBgra
    {
        get => _liveMaskBgra;
        set { _liveMaskBgra = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Bumped every time the mask editor finishes a stamp — gives the preview
    /// a cheap "buffer contents changed" signal even though the array
    /// reference is the same.
    /// </summary>
    private int _liveMaskRevision;
    public int LiveMaskRevision
    {
        get => _liveMaskRevision;
        set { _liveMaskRevision = value; OnPropertyChanged(); }
    }

    // ── Sidebar search (IFilterableTreeNode) ──────────────────────────────
    private bool _isFilteredIn = true;
    public bool IsFilteredIn
    {
        get => _isFilteredIn;
        set { if (_isFilteredIn == value) return; _isFilteredIn = value; OnPropertyChanged(); }
    }

    // Collapsed on load. A character's busts are tall editors, and a pack with
    // a few of them opens to a wall of controls you have to scroll past to
    // reach the one you want; starting shut makes the list navigable and costs
    // one click to open. Only the initial state — expansion is remembered
    // normally from then on.
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    // Matches the initial state, so clearing a search doesn't expand busts the
    // user never opened.
    private bool _expandedBeforeFilter;
    public void StashExpansion() => _expandedBeforeFilter = IsExpanded;
    public void RestoreExpansion() => IsExpanded = _expandedBeforeFilter;

    public string FilterKey => Display;
    public System.Collections.Generic.IEnumerable<IFilterableTreeNode> FilterChildren
        => System.Linq.Enumerable.Empty<IFilterableTreeNode>();
}
