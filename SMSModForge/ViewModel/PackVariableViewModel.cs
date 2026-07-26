using System;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>INPC wrapper for a <see cref="PackVariableDef"/>.</summary>
public sealed class PackVariableViewModel : ObservableObject
{
    public PackVariableDef Model { get; }

    public PackVariableViewModel(PackVariableDef model)
    {
        Model = model;
        InitialValues = new ObservableCollection<ListValueRow>();
        RebuildInitialValuesFromModel();
    }

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; OnPropertyChanged(); }
    }

    public PackVariableType Type
    {
        get => Model.Type;
        set
        {
            if (Model.Type == value) return;
            Model.Type = value;
            // Switching to List: make sure the backing default is a JSON array so
            // the initial-values editor round-trips cleanly.
            if (value == PackVariableType.List &&
                (string.IsNullOrWhiteSpace(Model.DefaultValue) || !Model.DefaultValue.TrimStart().StartsWith("[")))
            {
                Model.DefaultValue = "[]";
            }
            // Coerce the carried-over default into the new type's domain so
            // the type-specific editors never show an impossible value:
            // Bool → "true"/"false", Int → whole number (numeric leftovers
            // truncate), Float → number; unparsable leftovers become the
            // type's zero value.
            switch (value)
            {
                case PackVariableType.Bool:
                    if (!bool.TryParse(Model.DefaultValue, out _))
                        Model.DefaultValue = "false";
                    break;
                case PackVariableType.Int:
                    Model.DefaultValue = double.TryParse(Model.DefaultValue,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var dInt)
                        ? ((long)dInt).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : "0";
                    break;
                case PackVariableType.Float:
                    Model.DefaultValue = double.TryParse(Model.DefaultValue,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var dFloat)
                        ? dFloat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : "0";
                    break;
            }
            RebuildInitialValuesFromModel();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefaultValue));
            OnPropertyChanged(nameof(DefaultBool));
            OnPropertyChanged(nameof(IsNumeric));
            OnPropertyChanged(nameof(IsList));
            OnPropertyChanged(nameof(IsBool));
            OnPropertyChanged(nameof(IsScalarText));
        }
    }

    public string DefaultValue
    {
        get => Model.DefaultValue;
        set
        {
            var v = value ?? "";
            // Type-aware input limiting: numeric variables only accept text
            // that is (or is on its way to becoming) a number — a lone "-"
            // or a trailing "." are allowed as in-progress states. Anything
            // else is rejected and the textbox snaps back via the
            // PropertyChanged we raise without committing.
            if (Type == PackVariableType.Int &&
                !System.Text.RegularExpressions.Regex.IsMatch(v, @"^-?\d*$"))
            { OnPropertyChanged(); return; }
            if (Type == PackVariableType.Float &&
                !System.Text.RegularExpressions.Regex.IsMatch(v, @"^-?\d*\.?\d*$"))
            { OnPropertyChanged(); return; }
            Model.DefaultValue = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefaultBool));
        }
    }

    /// <summary>Checkbox editor surface for Bool variables — the only two
    /// values a bool default can hold. Writes normalize the stored string
    /// to lowercase "true"/"false".</summary>
    public bool DefaultBool
    {
        get => string.Equals(Model.DefaultValue?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        set
        {
            Model.DefaultValue = value ? "true" : "false";
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefaultValue));
        }
    }

    /// <summary>True for <see cref="PackVariableType.List"/> — drives the
    /// initial-values editor (vs. the scalar default-value box).</summary>
    public bool IsList => Type == PackVariableType.List;

    /// <summary>True for <see cref="PackVariableType.Bool"/> — swaps the
    /// scalar default textbox for a checkbox.</summary>
    public bool IsBool => Type == PackVariableType.Bool;

    /// <summary>The plain default-value textbox shows for every type that
    /// isn't handled by a dedicated editor (bool → checkbox, list → rows).
    /// Int/Float share it but get numeric-only input filtering in the
    /// <see cref="DefaultValue"/> setter.</summary>
    public bool IsScalarText => !IsList && !IsBool;

    // ── List initial values ──────────────────────────────────────────
    // For a List variable the default is a JSON array; this collection is the
    // friendly editor over it. Every edit re-serialises back into DefaultValue.

    public ObservableCollection<ListValueRow> InitialValues { get; }

    private void RebuildInitialValuesFromModel()
    {
        InitialValues.Clear();
        if (Type != PackVariableType.List) return;
        try
        {
            var arr = JArray.Parse(string.IsNullOrWhiteSpace(Model.DefaultValue) ? "[]" : Model.DefaultValue);
            foreach (var item in arr)
            {
                var s = (string)item;
                if (s != null) InitialValues.Add(new ListValueRow(s, SyncInitialValuesToModel, RemoveInitialValue));
            }
        }
        catch { /* malformed default → start empty */ }
    }

    private void SyncInitialValuesToModel()
    {
        var arr = new JArray();
        foreach (var r in InitialValues)
            if (!string.IsNullOrEmpty(r.Value)) arr.Add(r.Value);
        Model.DefaultValue = arr.ToString(Formatting.None);
        OnPropertyChanged(nameof(DefaultValue));
    }

    public ListValueRow AddInitialValue(string value = "")
    {
        var row = new ListValueRow(value, SyncInitialValuesToModel, RemoveInitialValue);
        InitialValues.Add(row);
        SyncInitialValuesToModel();
        return row;
    }

    public void RemoveInitialValue(ListValueRow row)
    {
        InitialValues.Remove(row);
        SyncInitialValuesToModel();
    }

    public bool Persisted
    {
        get => Model.Persisted;
        set { Model.Persisted = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Auto-refresh policy. See <see cref="PackVariableDef.RefreshMode"/>.
    /// Changing it raises <see cref="IsLevelScoped"/> so the scope
    /// picker can toggle its visibility.
    /// </summary>
    public PackVariableRefreshMode RefreshMode
    {
        get => Model.RefreshMode;
        set
        {
            if (Model.RefreshMode == value) return;
            Model.RefreshMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLevelScoped));
        }
    }

    /// <summary>
    /// Level token (<c>vanilla:&lt;goName&gt;</c> /
    /// <c>place:&lt;key&gt;</c>) that re-rolls this variable when it
    /// activates. Only meaningful for
    /// <see cref="PackVariableRefreshMode.LevelRandom"/>. Same format as
    /// the <c>LevelActive</c> condition's <c>level</c> param so the
    /// editor can reuse <c>LevelOptions</c> for both pickers.
    /// </summary>
    public string RefreshScope
    {
        get => Model.RefreshScope ?? "";
        set { Model.RefreshScope = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); OnPropertyChanged(); }
    }

    /// <summary>
    /// The level scope picker's IsEnabled binding source — only the
    /// <see cref="PackVariableRefreshMode.LevelRandom"/> mode uses the
    /// scope token, so the input is greyed out otherwise to make the
    /// relationship obvious.
    /// </summary>
    public bool IsLevelScoped => Model.RefreshMode == PackVariableRefreshMode.LevelRandom;

    /// <summary>Optional lower clamp (numeric variables only).</summary>
    public string MinValue
    {
        get => Model.MinValue ?? "";
        set
        {
            if (!IsValidNumericInput(value)) { OnPropertyChanged(); return; }
            Model.MinValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
        }
    }

    /// <summary>Optional upper clamp (numeric variables only).</summary>
    public string MaxValue
    {
        get => Model.MaxValue ?? "";
        set
        {
            if (!IsValidNumericInput(value)) { OnPropertyChanged(); return; }
            Model.MaxValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
        }
    }

    /// <summary>Same in-progress-number filter the default-value box uses —
    /// Int variables reject decimal points, Float allows one. Non-numeric
    /// types accept anything (the boxes are disabled for them anyway).</summary>
    private bool IsValidNumericInput(string? v)
    {
        v ??= "";
        if (Type == PackVariableType.Int)
            return System.Text.RegularExpressions.Regex.IsMatch(v, @"^-?\d*$");
        if (Type == PackVariableType.Float)
            return System.Text.RegularExpressions.Regex.IsMatch(v, @"^-?\d*\.?\d*$");
        return true;
    }

    /// <summary>
    /// True for <see cref="PackVariableType.Int"/> / <see cref="PackVariableType.Float"/>.
    /// The min/max clamp fields bind their <c>IsEnabled</c> to this — clamps
    /// are meaningless for bool/string variables.
    /// </summary>
    public bool IsNumeric => Type == PackVariableType.Int || Type == PackVariableType.Float;

    public string Description
    {
        get => Model.Description ?? "";
        set { Model.Description = string.IsNullOrEmpty(value) ? null : value; OnPropertyChanged(); }
    }
}

/// <summary>One initial value of a List variable. The model stores the list as
/// a JSON array on <c>DefaultValue</c>; each row calls back into the owning
/// <see cref="PackVariableViewModel"/> to keep that array in sync on every
/// edit. Mirrors <see cref="ActorOutfitViewModel"/>.</summary>
public sealed class ListValueRow : ObservableObject
{
    private readonly Action _onChanged;
    private string _value;

    public ListValueRow(string value, Action onChanged, Action<ListValueRow> removeCallback)
    {
        _value = value ?? "";
        _onChanged = onChanged;
        RemoveCommand = new RelayCommand(() => removeCallback(this));
    }

    public string Value
    {
        get => _value;
        set { if (_value == value) return; _value = value ?? ""; OnPropertyChanged(); _onChanged?.Invoke(); }
    }

    public RelayCommand RemoveCommand { get; }
}
