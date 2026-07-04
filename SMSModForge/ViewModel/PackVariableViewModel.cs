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
                OnPropertyChanged(nameof(DefaultValue));
            }
            RebuildInitialValuesFromModel();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNumeric));
            OnPropertyChanged(nameof(IsList));
        }
    }

    public string DefaultValue
    {
        get => Model.DefaultValue;
        set { Model.DefaultValue = value; OnPropertyChanged(); }
    }

    /// <summary>True for <see cref="PackVariableType.List"/> — drives the
    /// initial-values editor (vs. the scalar default-value box).</summary>
    public bool IsList => Type == PackVariableType.List;

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
        set { Model.MinValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); OnPropertyChanged(); }
    }

    /// <summary>Optional upper clamp (numeric variables only).</summary>
    public string MaxValue
    {
        get => Model.MaxValue ?? "";
        set { Model.MaxValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); OnPropertyChanged(); }
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
