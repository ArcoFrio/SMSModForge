using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>INPC wrapper for a <see cref="NodeConditionDef"/>. Mirrors
/// <see cref="NodeActionViewModel"/> — see that class for the rationale
/// behind <see cref="ParamRows"/> and the legacy named accessors.</summary>
public sealed class NodeConditionViewModel : ObservableObject
{
    private readonly Action<NodeConditionViewModel>? _removeCallback;

    /// <summary>
    /// Optional flag set by parent collections to mark this row as locked
    /// (the editor uses this for the auto-injected LevelActive start
    /// condition on every dialogue). When true, <see cref="RemoveCommand"/>
    /// reports <c>CanExecute = false</c> and the XAML disables the minus button.
    /// </summary>
    public bool IsLocked { get; }

    public NodeConditionDef Model { get; }

    /// <summary>
    /// Construct a condition VM.
    /// </summary>
    /// <param name="model">The underlying authored condition.</param>
    /// <param name="removeCallback">
    /// Parent collection's "remove this row" callback. The row's
    /// <see cref="RemoveCommand"/> invokes it with <c>this</c>. Null = the
    /// row can't be removed from the UI (rare; mainly defensive).
    /// </param>
    /// <param name="isLocked">
    /// When true, removal is explicitly disabled even if a callback was
    /// supplied (used to pin the LevelActive condition at index 0).
    /// </param>
    public NodeConditionViewModel(NodeConditionDef model,
                                   Action<NodeConditionViewModel>? removeCallback = null,
                                   bool isLocked = false,
                                   ConditionContext context = ConditionContext.Polled)
    {
        Model = model;
        _removeCallback = removeCallback;
        IsLocked = isLocked;
        Context = context;
        RemoveCommand = new RelayCommand(
            () => _removeCallback?.Invoke(this),
            () => !IsLocked && _removeCallback != null);
        CopyCommand = new RelayCommand(() => Services.EditorClipboard.SetConditions(new[] { Model }));
        NormalizeVariable();   // fold legacy GameVariable* into Variable* + source=vanilla
        RebuildParamRows();

        // Group recursion: a group (All/Any) owns a child list instead of
        // params. Wrap each child in its own VM, routing removal back here.
        Children = new ObservableCollection<NodeConditionViewModel>();
        if (NodeConditionTypes.IsGroup(Model.Type))
        {
            Model.Conditions ??= new List<NodeConditionDef>();
            foreach (var child in Model.Conditions)
                Children.Add(new NodeConditionViewModel(child, removeCallback: RemoveChild, context: Context));
        }
        AddLeafCommand  = new RelayCommand(() => AddLeaf());
        AddGroupCommand = new RelayCommand(AddGroup);
    }

    // ── Group support ──────────────────────────────────────────────────

    /// <summary>True when this is an <c>All</c>/<c>Any</c> group rather than a
    /// leaf condition. Drives <c>ConditionTemplateSelector</c>.</summary>
    public bool IsGroup => NodeConditionTypes.IsGroup(Model.Type);

    /// <summary>Child conditions of a group (empty for leaves).</summary>
    public ObservableCollection<NodeConditionViewModel> Children { get; }

    /// <summary>Display strings for the group AND/OR combo (index 0 = AND/All, 1 = OR/Any).</summary>
    public static IReadOnlyList<string> GroupModeOptions { get; } =
        new[] { "AND — all of these", "OR — any of these" };

    /// <summary>0 = <c>All</c> (AND), 1 = <c>Any</c> (OR). Bound to the group header combo.</summary>
    public int GroupModeIndex
    {
        get => Model.Type == NodeConditionTypes.GroupAny ? 1 : 0;
        set
        {
            var newType = value == 1 ? NodeConditionTypes.GroupAny : NodeConditionTypes.GroupAll;
            if (Model.Type == newType) return;
            Model.Type = newType;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    /// <summary>Appends a leaf condition (default VariableEquals) to this group.</summary>
    public RelayCommand AddLeafCommand { get; }
    /// <summary>Appends a nested group (default AND) to this group.</summary>
    public RelayCommand AddGroupCommand { get; }

    private NodeConditionViewModel AddLeaf()
    {
        Model.Conditions ??= new List<NodeConditionDef>();
        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals };
        Model.Conditions.Add(def);
        var vm = new NodeConditionViewModel(def, removeCallback: RemoveChild, context: Context);
        Children.Add(vm);
        OnPropertyChanged(nameof(Display));
        return vm;
    }

    private void AddGroup()
    {
        Model.Conditions ??= new List<NodeConditionDef>();
        var def = new NodeConditionDef
        {
            Type = NodeConditionTypes.GroupAll,
            Conditions = new List<NodeConditionDef>(),
        };
        Model.Conditions.Add(def);
        Children.Add(new NodeConditionViewModel(def, removeCallback: RemoveChild, context: Context));
        OnPropertyChanged(nameof(Display));
    }

    private void RemoveChild(NodeConditionViewModel c)
    {
        Model.Conditions?.Remove(c.Model);
        Children.Remove(c);
        OnPropertyChanged(nameof(Display));
    }

    /// <summary>Removes this row from its parent collection. No-op when <see cref="IsLocked"/>.</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>Copies just this condition (with any nested group) to the clipboard.</summary>
    public RelayCommand CopyCommand { get; }

    /// <summary>Editor uses this to disable the Type combo on locked rows.</summary>
    public bool IsTypeEditable => !IsLocked;

    /// <summary>
    /// Per-Type schema rows. Rebuilt on every Type change; cleared when
    /// the type has no params (e.g. <c>AlwaysTrue</c>).
    /// </summary>
    public ObservableCollection<ParamRowViewModel> ParamRows { get; } = new();

    private void RebuildParamRows()
    {
        ParamRows.Clear();
        var schemas = ConditionSchemas.For(Model.Type);
        foreach (var schema in schemas)
        {
            var paramType = schema.Type;
            ParamRowViewModel? capturedRow = null;
            var row = new ParamRowViewModel(
                Model.Params, schema,
                onValueChanged: () =>
                {
                    OnPropertyChanged(nameof(Display));
                    OnPropertyChanged(nameof(ParamsAsText));
                    OnPropertyChanged(nameof(Level));
                    // A row that gates siblings (Timer's 'randomize') has just
                    // changed; re-evaluate every row's enabled state.
                    foreach (var r in ParamRows) r.RefreshEnabled();
                    // Re-check boolean variable detection for PackVarRef/BoolVarRef rows.
                    if (paramType == ParamType.PackVarRef || paramType == ParamType.BoolVarRef)
                        capturedRow?.RefreshBooleanDetection();
                });
            capturedRow = row;
            row.DefaultOf = k =>
            {
                foreach (var s in schemas) if (s.Key == k) return s.DefaultValue;
                return "";
            };
            row.IsBooleanVarChecker = MainViewModel.IsVariableBoolean;
            ParamRows.Add(row);
        }
    }

    public string Type
    {
        get => Model.Type;
        set
        {
            if (Model.Type == value) return;
            Model.Type = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(DisplayType));
            OnPropertyChanged(nameof(IsVariableFamily));
            RebuildParamRows();
        }
    }

    // ── Unified "Variable" presentation ─────────────────────────────────
    //
    // The six Variable* comparison types (and the legacy GameVariable* ones,
    // migrated up front by NormalizeVariable) collapse into ONE "Variable"
    // entry in the type picker. The row then shows a Source (Pack/Vanilla) +
    // Comparison + Name + Value editor. Source is the canonical 'source' param
    // ("vanilla", or absent for the pack default); the comparison stays encoded
    // in Model.Type so the runtime + validator are unchanged.

    /// <summary>Pseudo type-id shown in the picker for the whole Variable family.</summary>
    public const string VariableFamilyType = "Variable";

    /// <summary>How often this row's host evaluates it. Set at construction
    /// and inherited by nested group children; decides whether the
    /// per-evaluation <c>Random</c> gate is offered.</summary>
    public ConditionContext Context { get; }

    /// <summary>
    /// The types this row's combo offers. Polled hosts (dialogue start
    /// conditions, integration rules, button visibility) get the safe list;
    /// one-shot hosts (dialogue node conditions, level hooks) additionally
    /// get <c>Random</c>, which is only meaningful when evaluated once.
    /// The Variable* family is folded into a single "Variable" entry (the
    /// row exposes Source + Comparison separately).
    /// </summary>
    public IReadOnlyList<string> AvailableTypes => Context switch
    {
        ConditionContext.OneShot => _oneShotTypes ??= BuildPicker(NodeConditionTypes.AllOneShot),
        ConditionContext.Rule    => _ruleTypes    ??= BuildPicker(NodeConditionTypes.AllRule),
        _                        => _polledTypes  ??= BuildPicker(NodeConditionTypes.All),
    };

    // Built on first use, NOT in a static field initializer: those run in
    // declaration order, and BuildPicker reads the _variableTypes /
    // _legacyVariableTypes sets declared further down — which would still be
    // null, throwing a TypeInitializationException the first time any
    // condition row was constructed.
    private static string[]? _polledTypes;
    private static string[]? _oneShotTypes;
    private static string[]? _ruleTypes;

    private static string[] BuildPicker(string[] source) => source
        .Where(t => !_variableTypes.Contains(t) && !_legacyVariableTypes.Contains(t))
        .Concat(new[] { VariableFamilyType })
        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static readonly HashSet<string> _legacyVariableTypes = new()
    {
        NodeConditionTypes.GameVariableEquals, NodeConditionTypes.GameVariableNumberGreaterThan,
        NodeConditionTypes.GameVariableNumberGreaterOrEqual, NodeConditionTypes.GameVariableNumberLessThan,
        NodeConditionTypes.GameVariableNumberLessOrEqual,
    };

    public static IReadOnlyList<string> VariableSources { get; } = new[] { "Pack", "Vanilla" };
    public static IReadOnlyList<string> VariableComparisons { get; } =
        new[] { "equals", "greater than", "greater or equal", "less than", "less or equal", "exists" };

    private static readonly HashSet<string> _variableTypes = new()
    {
        NodeConditionTypes.VariableEquals, NodeConditionTypes.VariableGreaterThan,
        NodeConditionTypes.VariableGreaterOrEqual, NodeConditionTypes.VariableLessThan,
        NodeConditionTypes.VariableLessOrEqual, NodeConditionTypes.VariableExists,
    };

    /// <summary>True for any of the six Variable* comparison types.</summary>
    public bool IsVariableFamily => _variableTypes.Contains(Model.Type);

    /// <summary>Rewrite a legacy GameVariable* condition to Variable* + source=vanilla. Idempotent.</summary>
    private void NormalizeVariable()
    {
        string mapped = Model.Type switch
        {
            NodeConditionTypes.GameVariableEquals                 => NodeConditionTypes.VariableEquals,
            NodeConditionTypes.GameVariableNumberGreaterThan      => NodeConditionTypes.VariableGreaterThan,
            NodeConditionTypes.GameVariableNumberGreaterOrEqual   => NodeConditionTypes.VariableGreaterOrEqual,
            NodeConditionTypes.GameVariableNumberLessThan         => NodeConditionTypes.VariableLessThan,
            NodeConditionTypes.GameVariableNumberLessOrEqual      => NodeConditionTypes.VariableLessOrEqual,
            _ => null,
        };
        if (mapped != null)
        {
            Model.Type = mapped;
            Model.Params["source"] = "vanilla";
        }
    }

    /// <summary>Type shown in the row's combo: one "Variable" entry for the family, else the real type.</summary>
    public string DisplayType
    {
        get => IsVariableFamily ? VariableFamilyType : Model.Type;
        set
        {
            if (value == DisplayType) return;
            Type = value == VariableFamilyType ? NodeConditionTypes.VariableEquals : value;
        }
    }

    /// <summary>Pack (default) vs Vanilla GC2 global, stored in the 'source' param.</summary>
    public string VarSource
    {
        get => string.Equals(GetParam("source"), "vanilla", StringComparison.OrdinalIgnoreCase) ? "Vanilla" : "Pack";
        set
        {
            if (string.Equals(value, "Vanilla", StringComparison.OrdinalIgnoreCase)) Model.Params["source"] = "vanilla";
            else Model.Params.Remove("source");
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsVanillaSource));
            OnPropertyChanged(nameof(Display));
        }
    }

    /// <summary>True when the Vanilla source is selected (drives the name picker's list).</summary>
    public bool IsVanillaSource => string.Equals(GetParam("source"), "vanilla", StringComparison.OrdinalIgnoreCase);

    /// <summary>Comparison label, mapped to/from the underlying Variable* type.</summary>
    public string VarComparison
    {
        get => Model.Type switch
        {
            NodeConditionTypes.VariableGreaterThan    => "greater than",
            NodeConditionTypes.VariableGreaterOrEqual => "greater or equal",
            NodeConditionTypes.VariableLessThan       => "less than",
            NodeConditionTypes.VariableLessOrEqual    => "less or equal",
            NodeConditionTypes.VariableExists         => "exists",
            _                                          => "equals",
        };
        set
        {
            string t = value switch
            {
                "greater than"     => NodeConditionTypes.VariableGreaterThan,
                "greater or equal" => NodeConditionTypes.VariableGreaterOrEqual,
                "less than"        => NodeConditionTypes.VariableLessThan,
                "less or equal"    => NodeConditionTypes.VariableLessOrEqual,
                "exists"           => NodeConditionTypes.VariableExists,
                _                  => NodeConditionTypes.VariableEquals,
            };
            if (t == Model.Type) return;
            Type = t;   // keeps params (name/value/source); RebuildParamRows is harmless (rows hidden for the family)
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowVariableValue));
        }
    }

    /// <summary>The "exists" comparison takes no value — hide the value box for it.</summary>
    public bool ShowVariableValue => Model.Type != NodeConditionTypes.VariableExists;

    public string VarName  { get => GetParam("name");  set { SetParam("name", value);  OnPropertyChanged(); } }
    public string VarValue { get => GetParam("value"); set { SetParam("value", value); OnPropertyChanged(); } }

    public bool Negate
    {
        get => Model.Negate;
        set { Model.Negate = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public Dictionary<string, string> Params => Model.Params;

    public string GetParam(string key) => Model.Params.TryGetValue(key, out var v) ? v : "";
    public void SetParam(string key, string value)
    {
        if (string.IsNullOrEmpty(value)) Model.Params.Remove(key);
        else Model.Params[key] = value;
        OnPropertyChanged(nameof(Display));
    }

    public string Display
    {
        get
        {
            if (string.IsNullOrEmpty(Type)) return "(empty condition)";
            string prefix = Negate ? "NOT " : "";
            if (IsGroup)
                return prefix + (Type == NodeConditionTypes.GroupAny ? "ANY" : "ALL")
                       + " of " + Children.Count;
            if (Model.Params.Count == 0) return prefix + Type;
            var pairs = new List<string>(Model.Params.Count);
            foreach (var kv in Model.Params) pairs.Add(kv.Key + "=" + kv.Value);
            return prefix + Type + " — " + string.Join(", ", pairs);
        }
    }

    /// <summary>
    /// Shortcut for the <c>level</c> param used by
    /// <see cref="NodeConditionTypes.LevelActive"/>. Predates ParamRows
    /// but kept around because the pinned LevelActive condition still
    /// binds through it for clarity.
    /// </summary>
    public string Level
    {
        get => Model.Params.TryGetValue("level", out var v) ? v : "";
        set
        {
            if (string.IsNullOrEmpty(value)) Model.Params.Remove("level");
            else Model.Params["level"] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(ParamsAsText));
        }
    }

    /// <summary>Same shape as <see cref="NodeActionViewModel.ParamsAsText"/>.</summary>
    public string ParamsAsText
    {
        get
        {
            if (Model.Params.Count == 0) return "";
            var lines = new List<string>(Model.Params.Count);
            foreach (var kv in Model.Params) lines.Add(kv.Key + "=" + kv.Value);
            return string.Join("\n", lines);
        }
        set
        {
            Model.Params.Clear();
            if (string.IsNullOrWhiteSpace(value)) { OnPropertyChanged(); OnPropertyChanged(nameof(Display)); RebuildParamRows(); return; }
            foreach (var raw in value.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                Model.Params[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            RebuildParamRows();
        }
    }
}
