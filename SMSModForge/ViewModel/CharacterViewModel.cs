using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// One character: the speaker and the bust in a single editor. Absorbs what
/// <c>ActorViewModel</c> used to hold — see <see cref="CharacterDef"/> for why
/// the two were one thing all along.
/// </summary>
public sealed class CharacterViewModel : ObservableObject, IFilterableTreeNode
{
    public CharacterDef Model { get; }
    public ObservableCollection<OutfitViewModel> Outfits { get; }
    public ObservableCollection<ActorExpressionViewModel> Expressions { get; }

    /// <summary>Extra vanilla busts this character can switch to. Only used by a
    /// vanilla-sourced character — a pack-sourced one wears its own outfits.</summary>
    public ObservableCollection<ActorOutfitViewModel> VanillaOutfits { get; }

    /// <summary>
    /// The other characters in the pack, for deduplicating derived names.
    /// A callback rather than a snapshot because the list changes underneath.
    /// </summary>
    private readonly Func<IEnumerable<CharacterViewModel>> _siblings;

    /// <summary>
    /// Whether the derived names still track the display name.
    /// <para/>
    /// True only for a character created in this session and not yet
    /// hand-edited. Anything loaded from disk starts false, which is what keeps
    /// migration's promise: renaming an existing character's display name never
    /// moves the GameObject the runtime builds or the key dialogue matches on.
    /// </summary>
    private bool _namesFollowDisplay;

    public CharacterViewModel(CharacterDef model,
                              Func<IEnumerable<CharacterViewModel>>? siblings = null,
                              bool isNew = false)
    {
        Model = model;
        _siblings = siblings ?? System.Array.Empty<CharacterViewModel>;
        _namesFollowDisplay = isNew;

        Outfits = new ObservableCollection<OutfitViewModel>(model.Outfits.Select(o => new OutfitViewModel(o)));
        ViewSort.Alphabetical(Outfits, nameof(OutfitViewModel.Key));

        Expressions = new ObservableCollection<ActorExpressionViewModel>(
            model.Expressions.Select(e => new ActorExpressionViewModel(e)));

        VanillaOutfits = new ObservableCollection<ActorOutfitViewModel>(
            model.VanillaOutfits.Select(o => new ActorOutfitViewModel(o, SyncVanillaOutfits, RemoveVanillaOutfit)));
    }

    // ── Identity ──────────────────────────────────────────────────────────

    public string DisplayName
    {
        get => Model.DisplayName;
        set
        {
            Model.DisplayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            if (_namesFollowDisplay) DeriveNames();
        }
    }

    /// <summary>Dialogue reference. Derived until hand-edited; editing it pins
    /// both names, since a half-derived pair is more surprising than neither.</summary>
    public string Key
    {
        get => Model.Key;
        set
        {
            if (Model.Key == value) return;
            Model.Key = value;
            _namesFollowDisplay = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(NamesAreDerived));
        }
    }

    /// <summary>GameObject the runtime builds the bust under.</summary>
    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value) return;
            Model.Name = value;
            _namesFollowDisplay = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NamesAreDerived));
        }
    }

    /// <summary>Whether the names are still tracking the display name — shown
    /// in the advanced panel so it is clear why they move on their own.</summary>
    public bool NamesAreDerived => _namesFollowDisplay;

    private void DeriveNames()
    {
        var others = _siblings().Where(c => !ReferenceEquals(c, this)).ToList();
        Model.Key = CharacterDef.UniqueIdentifier(Model.DisplayName, others.Select(c => c.Model.Key));
        Model.Name = CharacterDef.UniqueIdentifier(Model.DisplayName, others.Select(c => c.Model.Name));
        OnPropertyChanged(nameof(Key));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Display));
    }

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : DisplayName;

    // ── Bust source ───────────────────────────────────────────────────────

    public BustSource BustSource
    {
        get => Model.BustSource;
        set
        {
            if (Model.BustSource == value) return;
            Model.BustSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPackBust));
            OnPropertyChanged(nameof(IsVanillaBust));
            OnPropertyChanged(nameof(HasNoBust));
            OnPropertyChanged(nameof(WearableBusts));
            BustSourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsPackBust => Model.BustSource == BustSource.Pack;
    public bool IsVanillaBust => Model.BustSource == BustSource.Vanilla;
    public bool HasNoBust => Model.BustSource == BustSource.None;

    /// <summary>
    /// The game's busts, for the picker. Whole records rather than names so the
    /// dropdown can group by character — the catalog runs to hundreds of
    /// entries, and "Anna_Bust" is only findable if Anna is what you scan for.
    /// </summary>
    public IReadOnlyList<SMSModForge.Model.VanillaBusts.VanillaBust> VanillaBustCatalog
        => SMSModForge.Model.VanillaBusts.All;

    public string VanillaBust
    {
        get => Model.VanillaBust;
        set
        {
            if (Model.VanillaBust == value) return;
            Model.VanillaBust = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(WearableBusts));
            BustSourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string DefaultOutfit
    {
        get => Model.DefaultOutfit;
        set
        {
            if (Model.DefaultOutfit == value) return;
            Model.DefaultOutfit = value ?? "";
            OnPropertyChanged();
            BustSourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Bust names a dialogue node can switch this character to.</summary>
    public IEnumerable<string> WearableBusts => Model.WearableBusts;

    /// <summary>Raised when the bust source, default outfit or vanilla bust
    /// changes, so dialogue-side derived properties can refresh without every
    /// consumer walking the character list.</summary>
    public event EventHandler? BustSourceChanged;

    // ── Name colour ───────────────────────────────────────────────────────

    public string NameColor
    {
        get => Model.NameColor ?? "";
        set
        {
            string? normalised = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (Model.NameColor == normalised) return;
            Model.NameColor = normalised;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NameColorBrush));
            OnPropertyChanged(nameof(NameColorValue));
        }
    }

    public System.Windows.Media.Brush NameColorBrush
        => TryParseColor(NameColor, out var c)
           ? new System.Windows.Media.SolidColorBrush(c)
           : System.Windows.Media.Brushes.White;

    public System.Windows.Media.Color NameColorValue
    {
        get => TryParseColor(NameColor, out var c) ? c : System.Windows.Media.Colors.White;
        set => NameColor = value.A == 255
            ? $"#{value.R:X2}{value.G:X2}{value.B:X2}"
            : $"#{value.R:X2}{value.G:X2}{value.B:X2}{value.A:X2}";
    }

    private static bool TryParseColor(string hex, out System.Windows.Media.Color c)
    {
        c = System.Windows.Media.Colors.White;
        if (string.IsNullOrEmpty(hex)) return false;
        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color p)
            { c = p; return true; }
        }
        catch { /* malformed hex — the swatch falls back to white */ }
        return false;
    }

    // ── Typewriter voice ──────────────────────────────────────────────────
    // Getters read the nullable model with fallbacks so merely viewing a
    // character never materialises a TypewriterDef and dirties the pack;
    // setters create on write.

    public string[] VoiceTemplates { get; } = { "Male", "Female", "Custom" };

    private TypewriterDef TwEdit => Model.Typewriter ??= new TypewriterDef();

    public string VoiceTemplate
    {
        get => Model.Typewriter?.Template switch { "M" => "Male", "F" => "Female", _ => "Custom" };
        set
        {
            switch (value)
            {
                case "Male":
                    TwEdit.Template = "M"; TwEdit.Frequency = 45; TwEdit.PitchMin = 0.6f; TwEdit.PitchMax = 0.9f;
                    break;
                case "Female":
                    TwEdit.Template = "F"; TwEdit.Frequency = 45; TwEdit.PitchMin = 1.0f; TwEdit.PitchMax = 1.5f;
                    break;
                default:
                    TwEdit.Template = "Custom";
                    break;
            }
            _frequencyText = _pitchMinText = _pitchMaxText = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TypewriterFrequencyText));
            OnPropertyChanged(nameof(TypewriterPitchMinText));
            OnPropertyChanged(nameof(TypewriterPitchMaxText));
        }
    }

    public bool TypewriterEnabled
    {
        get => Model.Typewriter?.Enabled ?? true;
        set { if ((Model.Typewriter?.Enabled ?? true) == value) return; TwEdit.Enabled = value; OnPropertyChanged(); }
    }

    // Raw-text backing so a mid-edit "0." isn't snapped back before the
    // fraction lands.
    private string? _frequencyText;
    public string TypewriterFrequencyText
    {
        get => _frequencyText ??= (Model.Typewriter?.Frequency ?? 45)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            _frequencyText = value ?? "";
            if (int.TryParse(_frequencyText.Trim(), System.Globalization.NumberStyles.Integer,
                             System.Globalization.CultureInfo.InvariantCulture, out var n))
            { TwEdit.Frequency = n; MarkCustom(); }
            OnPropertyChanged();
        }
    }

    private string? _pitchMinText;
    public string TypewriterPitchMinText
    {
        get => _pitchMinText ??= (Model.Typewriter?.PitchMin ?? 1.0f)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            _pitchMinText = value ?? "";
            if (float.TryParse(_pitchMinText.Trim(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var f))
            { TwEdit.PitchMin = f; MarkCustom(); }
            OnPropertyChanged();
        }
    }

    private string? _pitchMaxText;
    public string TypewriterPitchMaxText
    {
        get => _pitchMaxText ??= (Model.Typewriter?.PitchMax ?? 1.5f)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            _pitchMaxText = value ?? "";
            if (float.TryParse(_pitchMaxText.Trim(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var f))
            { TwEdit.PitchMax = f; MarkCustom(); }
            OnPropertyChanged();
        }
    }

    private void MarkCustom()
    {
        if (Model.Typewriter != null && Model.Typewriter.Template != "Custom")
        {
            Model.Typewriter.Template = "Custom";
            OnPropertyChanged(nameof(VoiceTemplate));
        }
    }

    // ── Collections ───────────────────────────────────────────────────────

    public void AddOutfit()
    {
        string baseName = string.IsNullOrWhiteSpace(Name) ? "Outfit" : Name;
        string key = CharacterDef.UniqueIdentifier(baseName + " New", Outfits.Select(o => o.Key));
        var def = new OutfitDef { Key = key, GameObjectName = key };
        Model.Outfits.Add(def);
        Outfits.Add(new OutfitViewModel(def));
        OnPropertyChanged(nameof(WearableBusts));
    }

    public void RemoveOutfit(OutfitViewModel vm)
    {
        Model.Outfits.Remove(vm.Model);
        Outfits.Remove(vm);
        OnPropertyChanged(nameof(WearableBusts));
    }

    public ActorExpressionViewModel AddExpression()
    {
        var def = new ActorExpressionDef { Key = "Happy", ExpressionGoName = "Happy" };
        Model.Expressions.Add(def);
        var vm = new ActorExpressionViewModel(def);
        Expressions.Add(vm);
        return vm;
    }

    public void RemoveExpression(ActorExpressionViewModel vm)
    {
        Model.Expressions.Remove(vm.Model);
        Expressions.Remove(vm);
    }

    private void SyncVanillaOutfits()
    {
        Model.VanillaOutfits = VanillaOutfits.Select(o => o.BustGoName).ToList();
        OnPropertyChanged(nameof(WearableBusts));
    }

    public ActorOutfitViewModel AddVanillaOutfit(string bustGoName = "")
    {
        var vm = new ActorOutfitViewModel(bustGoName, SyncVanillaOutfits, RemoveVanillaOutfit);
        VanillaOutfits.Add(vm);
        SyncVanillaOutfits();
        return vm;
    }

    public void RemoveVanillaOutfit(ActorOutfitViewModel vm)
    {
        VanillaOutfits.Remove(vm);
        SyncVanillaOutfits();
    }

    // ── Sidebar search (IFilterableTreeNode) ──────────────────────────────

    private bool _isFilteredIn = true;
    public bool IsFilteredIn
    {
        get => _isFilteredIn;
        set { if (_isFilteredIn == value) return; _isFilteredIn = value; OnPropertyChanged(); }
    }

    // Collapsed on load: a pack opens to a list of characters you can scan,
    // not a wall of bust editors.
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    private bool _expandedBeforeFilter;
    public void StashExpansion() => _expandedBeforeFilter = IsExpanded;
    public void RestoreExpansion() => IsExpanded = _expandedBeforeFilter;

    public string FilterKey => $"{Name} {DisplayName} {Key} {VanillaBust}";
    public IEnumerable<IFilterableTreeNode> FilterChildren => Outfits;
}
