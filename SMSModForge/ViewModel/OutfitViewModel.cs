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
        set { Model.GameObjectName = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
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
        set { Model.BlinkSprite = value; OnPropertyChanged(); }
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

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    private bool _expandedBeforeFilter = true;
    public void StashExpansion() => _expandedBeforeFilter = IsExpanded;
    public void RestoreExpansion() => IsExpanded = _expandedBeforeFilter;

    public string FilterKey => Display;
    public System.Collections.Generic.IEnumerable<IFilterableTreeNode> FilterChildren
        => System.Linq.Enumerable.Empty<IFilterableTreeNode>();
}
