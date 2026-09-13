using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// One editable list of conditions over a model list: add, add group, and the
/// copy / paste / overwrite every other condition editor offers.
/// <para/>
/// The same shape <see cref="LevelHookViewModel"/> and
/// <see cref="UpdateRuleViewModel"/> each wire by hand, pulled out for the
/// Quests tab, which needs three of these lists without an action list beside
/// every one. Rows are the same row view models everywhere, so a condition
/// pasted here from a dialogue is edited the same way it was there.
/// </summary>
public sealed class ConditionListViewModel : ObservableObject
{
    private readonly List<NodeConditionDef> _model;
    private readonly ConditionContext _context;

    public ObservableCollection<NodeConditionViewModel> Items { get; }

    public ConditionListViewModel(List<NodeConditionDef> model, ConditionContext context)
    {
        _model = model;
        _context = context;
        Items = new ObservableCollection<NodeConditionViewModel>(model.Select(Wrap));
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Count));

        AddCommand = new RelayCommand(() => Add());
        AddGroupCommand = new RelayCommand(() => AddGroup());
        CopyCommand = new RelayCommand(() => Services.EditorClipboard.SetConditions(_model), () => _model.Count > 0);
        PasteCommand = new RelayCommand(() => Paste(overwrite: false), () => Services.EditorClipboard.HasConditions);
        OverwriteCommand = new RelayCommand(() => Paste(overwrite: true), () => Services.EditorClipboard.HasConditions);
    }

    public int Count => Items.Count;

    public RelayCommand AddCommand { get; }
    public RelayCommand AddGroupCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand OverwriteCommand { get; }

    private NodeConditionViewModel Wrap(NodeConditionDef def)
        => new(def, Remove, context: _context);

    public NodeConditionViewModel Add()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableCompare };
        _model.Add(def);
        var vm = Wrap(def);
        Items.Add(vm);
        return vm;
    }

    public NodeConditionViewModel AddGroup()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.GroupAll, Conditions = new() };
        _model.Add(def);
        var vm = Wrap(def);
        Items.Add(vm);
        return vm;
    }

    public void Remove(NodeConditionViewModel row)
    {
        _model.Remove(row.Model);
        Items.Remove(row);
    }

    private void Paste(bool overwrite)
    {
        var src = Services.EditorClipboard.Conditions;
        if (src == null || src.Count == 0) return;
        if (overwrite) { _model.Clear(); Items.Clear(); }
        foreach (var def in Services.EditorClipboard.Clone(src))
        {
            _model.Add(def);
            Items.Add(Wrap(def));
        }
    }
}

/// <summary>The action-list counterpart of <see cref="ConditionListViewModel"/>.</summary>
public sealed class ActionListViewModel : ObservableObject
{
    private readonly List<NodeActionDef> _model;

    public ObservableCollection<NodeActionViewModel> Items { get; }

    public ActionListViewModel(List<NodeActionDef> model)
    {
        _model = model;
        Items = new ObservableCollection<NodeActionViewModel>(model.Select(Wrap));
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Count));

        AddCommand = new RelayCommand(() => Add());
        CopyCommand = new RelayCommand(() => Services.EditorClipboard.SetActions(_model), () => _model.Count > 0);
        PasteCommand = new RelayCommand(() => Paste(overwrite: false), () => Services.EditorClipboard.HasActions);
        OverwriteCommand = new RelayCommand(() => Paste(overwrite: true), () => Services.EditorClipboard.HasActions);
    }

    public int Count => Items.Count;

    public RelayCommand AddCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand OverwriteCommand { get; }

    private NodeActionViewModel Wrap(NodeActionDef def) => new(def, Remove);

    public NodeActionViewModel Add()
    {
        var def = new NodeActionDef { Type = NodeActionTypes.SetVariable };
        _model.Add(def);
        var vm = Wrap(def);
        Items.Add(vm);
        return vm;
    }

    public void Remove(NodeActionViewModel row)
    {
        _model.Remove(row.Model);
        Items.Remove(row);
    }

    private void Paste(bool overwrite)
    {
        var src = Services.EditorClipboard.Actions;
        if (src == null || src.Count == 0) return;
        if (overwrite) { _model.Clear(); Items.Clear(); }
        foreach (var def in Services.EditorClipboard.Clone(src))
        {
            _model.Add(def);
            Items.Add(Wrap(def));
        }
    }
}
