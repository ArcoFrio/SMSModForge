using System;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// Wraps a <see cref="NavigatorButtonDef"/> with INPC. Two-way bound by the
/// Places editor UI (target picker, label, music dropdown). The
/// <see cref="TargetDisplay"/> derives the user-facing rendering of the
/// target token (e.g. <c>"vanilla:14_Beach"</c> →
/// <c>"Vanilla → Beach (14_Beach)"</c>) so the UI never has to parse the
/// raw token form.
/// </summary>
public sealed class NavigatorButtonViewModel : ObservableObject
{
    private readonly Action<NavigatorButtonViewModel>? _removeCallback;
    private readonly Action<NavigatorButtonViewModel, int>? _moveCallback;

    public NavigatorButtonDef Model { get; }

    public ObservableCollection<NodeConditionViewModel> Conditions { get; }

    /// <param name="moveCallback">
    /// Parent's reorder hook: invoked with <c>this</c> and a delta of
    /// <c>-1</c> (move earlier) or <c>+1</c> (move later) in instantiation
    /// order. The parent swaps the entry in both its VM collection and the
    /// underlying model list so the manifest's <c>navigatorButtons</c>
    /// order — which is the on-screen left→right order at runtime —
    /// round-trips. Null disables reordering.
    /// </param>
    public NavigatorButtonViewModel(NavigatorButtonDef model,
                                     Action<NavigatorButtonViewModel>? removeCallback = null,
                                     Action<NavigatorButtonViewModel, int>? moveCallback = null)
    {
        Model = model;
        _removeCallback = removeCallback;
        _moveCallback = moveCallback;
        RemoveCommand = new RelayCommand(
            () => _removeCallback?.Invoke(this),
            () => _removeCallback != null);
        MoveUpCommand = new RelayCommand(
            () => _moveCallback?.Invoke(this, -1),
            () => _moveCallback != null);
        MoveDownCommand = new RelayCommand(
            () => _moveCallback?.Invoke(this, +1),
            () => _moveCallback != null);
        AddConditionCommand = new RelayCommand(AddCondition);
        AddConditionGroupCommand = new RelayCommand(AddConditionGroup);

        Conditions = new ObservableCollection<NodeConditionViewModel>(
            model.Conditions.Select(c => new NodeConditionViewModel(c, RemoveCondition)));
    }

    /// <summary>Removes this row from its parent navigator-button list.</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>Moves this button one slot earlier in instantiation order.</summary>
    public RelayCommand MoveUpCommand { get; }

    /// <summary>Moves this button one slot later in instantiation order.</summary>
    public RelayCommand MoveDownCommand { get; }

    public RelayCommand AddConditionCommand { get; }
    public RelayCommand AddConditionGroupCommand { get; }

    public string Target
    {
        get => Model.Target;
        set
        {
            if (Model.Target == value) return;
            Model.Target = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TargetDisplay));
        }
    }

    public string Label
    {
        get => Model.Label;
        set { Model.Label = value; OnPropertyChanged(); }
    }

    public string Music
    {
        get => Model.Music;
        set { Model.Music = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Human-readable rendering of the target ref. Used as the label inside
    /// the target combo box, so a glance at the list reads naturally even
    /// when the underlying token is a packId/key pair.
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
}
