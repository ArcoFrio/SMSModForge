using System;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>INPC wrapper for an <see cref="ActorDef"/>.</summary>
public sealed class ActorViewModel : ObservableObject
{
    public ActorDef Model { get; }
    public ObservableCollection<ActorExpressionViewModel> Expressions { get; }

    /// <summary>
    /// The bust GO names this actor can wear. Dialogue nodes pick one to
    /// switch the actor's outfit mid-dialogue. Each row keeps
    /// <see cref="ActorDef.Outfits"/> in sync via <see cref="SyncOutfitsToModel"/>.
    /// </summary>
    public ObservableCollection<ActorOutfitViewModel> Outfits { get; }

    public ActorViewModel(ActorDef model)
    {
        Model = model;
        Expressions = new ObservableCollection<ActorExpressionViewModel>(
            model.Expressions.Select(e => new ActorExpressionViewModel(e)));
        Outfits = new ObservableCollection<ActorOutfitViewModel>(
            model.Outfits.Select(o => new ActorOutfitViewModel(o, SyncOutfitsToModel, RemoveOutfit)));
    }

    public string Key
    {
        get => Model.Key;
        set { Model.Key = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string DisplayName
    {
        get => Model.DisplayName;
        set { Model.DisplayName = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string DefaultBustKey
    {
        get => Model.DefaultBustKey;
        set
        {
            if (Model.DefaultBustKey == value) return;
            Model.DefaultBustKey = value;
            OnPropertyChanged();
            DefaultBustKeyChanged?.Invoke(this, System.EventArgs.Empty);
        }
    }

    /// <summary>
    /// Speech-bubble name colour as a hex string (e.g. <c>"#FF8844"</c>).
    /// Edits hit <see cref="NameColorBrush"/> + <see cref="NameColorValue"/>
    /// (notification only) so the preview swatch and the Win32 colour-picker
    /// dialog round-trip without each binding having to parse hex itself.
    /// </summary>
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

    /// <summary>
    /// The parsed <see cref="System.Windows.Media.Color"/> as a
    /// <see cref="System.Windows.Media.SolidColorBrush"/> for the swatch
    /// preview. Falls back to white on parse failure so a malformed hex
    /// still renders something visible while the user fixes the value.
    /// </summary>
    public System.Windows.Media.Brush NameColorBrush
    {
        get
        {
            if (TryParseColor(NameColor, out var c))
                return new System.Windows.Media.SolidColorBrush(c);
            return System.Windows.Media.Brushes.White;
        }
    }

    /// <summary>Parsed <see cref="System.Windows.Media.Color"/>; <see cref="System.Windows.Media.Colors.White"/> on failure.</summary>
    public System.Windows.Media.Color NameColorValue
    {
        get
        {
            if (TryParseColor(NameColor, out var c)) return c;
            return System.Windows.Media.Colors.White;
        }
        set
        {
            // Round-tripped through the hex setter so all the existing
            // change-notification fires consistently.
            string hex = value.A == 255
                ? $"#{value.R:X2}{value.G:X2}{value.B:X2}"
                : $"#{value.R:X2}{value.G:X2}{value.B:X2}{value.A:X2}";
            NameColor = hex;
        }
    }

    private static bool TryParseColor(string hex, out System.Windows.Media.Color c)
    {
        c = System.Windows.Media.Colors.White;
        if (string.IsNullOrEmpty(hex)) return false;
        try
        {
            var converted = System.Windows.Media.ColorConverter.ConvertFromString(hex);
            if (converted is System.Windows.Media.Color parsed) { c = parsed; return true; }
        }
        catch { /* fall through */ }
        return false;
    }

    // ── Typewriter voice ──────────────────────────────────────────────
    // Getters read the nullable model with sensible fallbacks so merely
    // *viewing* an actor never materialises a TypewriterDef (which would
    // spuriously dirty the pack); setters call TwEdit to create-on-write.

    /// <summary>Available voice presets shown in the template dropdown.</summary>
    public string[] VoiceTemplates { get; } = { "Male", "Female", "Custom" };

    /// <summary>Create-on-write accessor for the actor's typewriter settings.</summary>
    private TypewriterDef TwEdit => Model.Typewriter ??= new TypewriterDef();

    /// <summary>
    /// Named preset selector. Picking Male/Female stamps that preset's
    /// frequency + pitch range; any manual edit below flips this to Custom.
    /// </summary>
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
            _frequencyText = null; _pitchMinText = null; _pitchMaxText = null;
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

    // Raw-text backing (same reason as SfxViewModel.DefaultVolumeText): a
    // mid-edit "0." mustn't be snapped back to "0" before the fraction lands.
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

    /// <summary>A hand-tweaked value no longer matches a named preset.</summary>
    private void MarkCustom()
    {
        if (Model.Typewriter != null && Model.Typewriter.Template != "Custom")
        {
            Model.Typewriter.Template = "Custom";
            OnPropertyChanged(nameof(VoiceTemplate));
        }
    }

    /// <summary>
    /// Raised when <see cref="DefaultBustKey"/> changes. The MainViewModel
    /// subscribes to refresh derived properties like
    /// <c>SelectedNodeActorBustKey</c> without each consumer having to
    /// walk the actor list itself.
    /// </summary>
    public event System.EventHandler? DefaultBustKeyChanged;

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : $"{DisplayName} ({Key})";

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

    // ── Outfit collection ops ─────────────────────────────────────────

    /// <summary>
    /// Flatten the outfit row VMs back into the model's plain
    /// <c>List&lt;string&gt;</c>. Called after every add / remove / edit so
    /// the on-disk shape always reflects the editor state.
    /// </summary>
    private void SyncOutfitsToModel()
        => Model.Outfits = Outfits.Select(o => o.BustGoName).ToList();

    public ActorOutfitViewModel AddOutfit(string bustGoName = "")
    {
        var vm = new ActorOutfitViewModel(bustGoName, SyncOutfitsToModel, RemoveOutfit);
        Outfits.Add(vm);
        SyncOutfitsToModel();
        return vm;
    }

    public void RemoveOutfit(ActorOutfitViewModel vm)
    {
        Outfits.Remove(vm);
        SyncOutfitsToModel();
    }
}

/// <summary>
/// INPC wrapper for a single entry of <see cref="ActorDef.Outfits"/>. The
/// model stores outfits as a plain <c>List&lt;string&gt;</c>; each VM holds
/// one bust GO name and calls back into the owning <see cref="ActorViewModel"/>
/// so that list stays in sync on every edit. The per-row
/// <see cref="RemoveCommand"/> lets the minus button delete the row
/// without routing through the MainViewModel.
/// </summary>
public sealed class ActorOutfitViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private string _bustGoName;

    public ActorOutfitViewModel(string bustGoName, Action onChanged,
                                Action<ActorOutfitViewModel> removeCallback)
    {
        _bustGoName = bustGoName ?? "";
        _onChanged = onChanged;
        RemoveCommand = new RelayCommand(() => removeCallback(this));
    }

    public string BustGoName
    {
        get => _bustGoName;
        set
        {
            if (_bustGoName == value) return;
            _bustGoName = value ?? "";
            OnPropertyChanged();
            _onChanged?.Invoke();
        }
    }

    public RelayCommand RemoveCommand { get; }

    /// <summary>So an editable ComboBox round-trips the GO name as its text.</summary>
    public override string ToString() => _bustGoName;
}

public sealed class ActorExpressionViewModel : ObservableObject
{
    public ActorExpressionDef Model { get; }
    public ActorExpressionViewModel(ActorExpressionDef model) { Model = model; }

    public string Key
    {
        get => Model.Key;
        set { Model.Key = value; OnPropertyChanged(); }
    }

    public string ExpressionGoName
    {
        get => Model.ExpressionGoName;
        set { Model.ExpressionGoName = value; OnPropertyChanged(); }
    }
}
