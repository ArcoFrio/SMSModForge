using System;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// Wraps a <see cref="MapButtonDef"/> with INPC. Two-way bound by the
/// Map Buttons tab UI. <see cref="TargetDisplay"/> and
/// <see cref="DistrictDisplay"/> derive friendly renderings of the raw
/// tokens so the row label reads naturally in lists.
/// </summary>
public sealed class MapButtonViewModel : ObservableObject
{
    private readonly Action<MapButtonViewModel>? _removeCallback;

    public MapButtonDef Model { get; }

    /// <summary>Visibility-condition rows. Uses the shared, groupable
    /// <see cref="NodeConditionViewModel"/> so the recursive AND/OR condition
    /// template is the same as dialogues and rules.</summary>
    public ObservableCollection<NodeConditionViewModel> Conditions { get; }

    public MapButtonViewModel(MapButtonDef model,
                              Action<MapButtonViewModel>? removeCallback = null)
    {
        Model = model;
        _removeCallback = removeCallback;
        RemoveCommand = new RelayCommand(
            () => _removeCallback?.Invoke(this),
            () => _removeCallback != null);
        AddConditionCommand = new RelayCommand(AddCondition);
        AddConditionGroupCommand = new RelayCommand(AddConditionGroup);

        Conditions = new ObservableCollection<NodeConditionViewModel>(
            model.Conditions.Select(c => new NodeConditionViewModel(c, RemoveCondition)));
    }

    /// <summary>Removes this row from the parent map-buttons list.</summary>
    public RelayCommand RemoveCommand { get; }

    public RelayCommand AddConditionCommand { get; }
    public RelayCommand AddConditionGroupCommand { get; }

    private void AddCondition()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals };
        Model.Conditions.Add(def);
        Conditions.Add(new NodeConditionViewModel(def, RemoveCondition));
    }

    private void AddConditionGroup()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.GroupAll, Conditions = new() };
        Model.Conditions.Add(def);
        Conditions.Add(new NodeConditionViewModel(def, RemoveCondition));
    }

    private void RemoveCondition(NodeConditionViewModel vm)
    {
        Model.Conditions.Remove(vm.Model);
        Conditions.Remove(vm);
    }

    public string Target
    {
        get => Model.Target;
        set
        {
            if (Model.Target == value) return;
            Model.Target = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TargetDisplay));
            OnPropertyChanged(nameof(Display));
        }
    }

    public string District
    {
        get => Model.District;
        set
        {
            if (Model.District == value) return;
            Model.District = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DistrictDisplay));
            OnPropertyChanged(nameof(Display));
        }
    }

    public string Label
    {
        get => Model.Label;
        set
        {
            if (Model.Label == value) return;
            Model.Label = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public string Music
    {
        get => Model.Music;
        set { Model.Music = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Friendly rendering of the target ref (same pattern as
    /// <see cref="NavigatorButtonViewModel.TargetDisplay"/>).
    /// </summary>
    public string TargetDisplay
    {
        get
        {
            if (!PlaceTargetRef.TryParse(Target, out var r)) return "(unset)";
            return r.Kind switch
            {
                PlaceTargetKind.Vanilla => $"Vanilla → {VanillaPlaces.FindByGoName(r.Key)?.DisplayName ?? r.Key} ({r.Key})",
                PlaceTargetKind.Self    => $"This pack → {r.Key}",
                PlaceTargetKind.Pack    => $"Pack {r.PackId} → {r.Key}",
                _                       => Target,
            };
        }
    }

    /// <summary>Friendly rendering of the district token.</summary>
    public string DistrictDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(District)) return "(unset)";
            var d = WorldMapDistricts.FindByGoName(District);
            return d.HasValue ? d.Value.DisplayName : District;
        }
    }

    /// <summary>
    /// One-line summary used in the master list. Format:
    /// <c>[District] Label → Target</c>.
    /// </summary>
    public string Display
    {
        get
        {
            var label = string.IsNullOrWhiteSpace(Label) ? "(no label)" : Label;
            return $"[{DistrictDisplay}] {label} → {TargetDisplay}";
        }
    }
}
