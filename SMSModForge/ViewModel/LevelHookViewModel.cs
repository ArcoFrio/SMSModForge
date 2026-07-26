using System;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper for one <see cref="LevelHookDef"/> — a conditions-gated
/// action group on a place's enter or exit edge. Reuses the exact condition
/// + action row VMs the dialogue-node editor binds, so the Places tab gets
/// the same editing experience (typed params, autocomplete, groups).
/// Mirrors <see cref="UpdateRuleViewModel"/>'s collection wiring.
/// </summary>
public sealed class LevelHookViewModel : ObservableObject
{
    public LevelHookDef Model { get; }
    private readonly Action<LevelHookViewModel> _removeCallback;

    public ObservableCollection<NodeConditionViewModel> Conditions { get; }
    public ObservableCollection<NodeActionViewModel> Actions { get; }

    public LevelHookViewModel(LevelHookDef model, Action<LevelHookViewModel> removeCallback)
    {
        Model = model;
        _removeCallback = removeCallback;
        // OneShot: hook conditions are checked on the level's activation edge,
        // once per enter/exit — not polled — so a single Random roll is valid.
        Conditions = new ObservableCollection<NodeConditionViewModel>(
            model.Conditions.Select(c => new NodeConditionViewModel(c, RemoveCondition,
                                                                    context: ConditionContext.OneShot)));
        Actions = new ObservableCollection<NodeActionViewModel>(
            model.Actions.Select(a => new NodeActionViewModel(a, RemoveAction)));

        RemoveCommand = new RelayCommand(() => _removeCallback(this));
        AddConditionCommand = new RelayCommand(() => AddCondition());
        AddConditionGroupCommand = new RelayCommand(() => AddConditionGroup());
        AddActionCommand = new RelayCommand(() => AddAction());

        // Same clipboard slots as dialogue nodes / integration rules, so
        // condition + action lists paste freely across all three editors.
        CopyConditionsCommand      = new RelayCommand(() => Services.EditorClipboard.SetConditions(Model.Conditions),
                                                      () => Model.Conditions.Count > 0);
        PasteConditionsCommand     = new RelayCommand(() => PasteConditions(overwrite: false),
                                                      () => Services.EditorClipboard.HasConditions);
        OverwriteConditionsCommand = new RelayCommand(() => PasteConditions(overwrite: true),
                                                      () => Services.EditorClipboard.HasConditions);
        CopyActionsCommand         = new RelayCommand(() => Services.EditorClipboard.SetActions(Model.Actions),
                                                      () => Model.Actions.Count > 0);
        PasteActionsCommand        = new RelayCommand(() => PasteActions(overwrite: false),
                                                      () => Services.EditorClipboard.HasActions);
        OverwriteActionsCommand    = new RelayCommand(() => PasteActions(overwrite: true),
                                                      () => Services.EditorClipboard.HasActions);
    }

    public RelayCommand RemoveCommand { get; }
    public RelayCommand AddConditionCommand { get; }
    public RelayCommand AddConditionGroupCommand { get; }
    public RelayCommand AddActionCommand { get; }
    public RelayCommand CopyConditionsCommand { get; }
    public RelayCommand PasteConditionsCommand { get; }
    public RelayCommand OverwriteConditionsCommand { get; }
    public RelayCommand CopyActionsCommand { get; }
    public RelayCommand PasteActionsCommand { get; }
    public RelayCommand OverwriteActionsCommand { get; }

    private void PasteConditions(bool overwrite)
    {
        var src = Services.EditorClipboard.Conditions;
        if (src == null || src.Count == 0) return;
        if (overwrite) { Model.Conditions.Clear(); Conditions.Clear(); }
        foreach (var def in Services.EditorClipboard.Clone(src))
        {
            Model.Conditions.Add(def);
            Conditions.Add(new NodeConditionViewModel(def, RemoveCondition, context: ConditionContext.OneShot));
        }
    }

    private void PasteActions(bool overwrite)
    {
        var src = Services.EditorClipboard.Actions;
        if (src == null || src.Count == 0) return;
        if (overwrite) { Model.Actions.Clear(); Actions.Clear(); }
        foreach (var def in Services.EditorClipboard.Clone(src))
        {
            Model.Actions.Add(def);
            Actions.Add(new NodeActionViewModel(def, RemoveAction));
        }
    }

    public NodeConditionViewModel AddCondition()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals };
        Model.Conditions.Add(def);
        var vm = new NodeConditionViewModel(def, RemoveCondition, context: ConditionContext.OneShot);
        Conditions.Add(vm);
        return vm;
    }

    public NodeConditionViewModel AddConditionGroup()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.GroupAll, Conditions = new() };
        Model.Conditions.Add(def);
        var vm = new NodeConditionViewModel(def, RemoveCondition, context: ConditionContext.OneShot);
        Conditions.Add(vm);
        return vm;
    }

    public void RemoveCondition(NodeConditionViewModel c)
    {
        Model.Conditions.Remove(c.Model);
        Conditions.Remove(c);
    }

    public NodeActionViewModel AddAction()
    {
        var def = new NodeActionDef { Type = NodeActionTypes.SetVariable };
        Model.Actions.Add(def);
        var vm = new NodeActionViewModel(def, RemoveAction);
        Actions.Add(vm);
        return vm;
    }

    public void RemoveAction(NodeActionViewModel a)
    {
        Model.Actions.Remove(a.Model);
        Actions.Remove(a);
    }
}
