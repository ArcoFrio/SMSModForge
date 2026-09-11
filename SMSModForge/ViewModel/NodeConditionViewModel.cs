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
        NormalizeGoActive();   // fold legacy GameObjectActive 'path' into kind + target
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
            if (IsGoActiveFamily && GoCategoryRowKeys.Contains(schema.Key)) continue;
            if (IsInputFamily) continue;   // device / key / phase are their own row
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
            // Seed 'kind' the moment the type becomes GameObjectActive, so the
            // category row opens on a real choice rather than reading a missing
            // param as Direct Path without ever having said so.
            NormalizeGoActive();
            OnPropertyChanged(nameof(IsGoActiveFamily));
            OnPropertyChanged(nameof(GoCategory));
            OnPropertyChanged(nameof(GoTarget));
            OnPropertyChanged(nameof(GoOverlayLevel));
            OnPropertyChanged(nameof(IsGoOverlayCategory));
            OnPropertyChanged(nameof(IsGoTargetEnabled));
            OnPropertyChanged(nameof(GoOverlayOptions));
            OnPropertyChanged(nameof(IsInputFamily));
            OnPropertyChanged(nameof(InputDevice));
            OnPropertyChanged(nameof(InputKeyOptions));
            OnPropertyChanged(nameof(InputKeyToken));
            OnPropertyChanged(nameof(InputPhase));
            OnPropertyChanged(nameof(InputPhaseHelp));
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
        NodeConditionTypes.VariableCompare, NodeConditionTypes.VariableExists,

        // The ten this replaced. Still recognised, because a row is built from
        // whatever the pack says before the migration has had a chance to
        // rewrite it - and a condition that fell out of the family here would
        // lose its Source and Comparison pickers.
        NodeConditionTypes.VariableEquals, NodeConditionTypes.VariableGreaterThan,
        NodeConditionTypes.VariableGreaterOrEqual, NodeConditionTypes.VariableLessThan,
        NodeConditionTypes.VariableLessOrEqual,
        NodeConditionTypes.GameVariableEquals,
        NodeConditionTypes.GameVariableNumberGreaterThan,
        NodeConditionTypes.GameVariableNumberGreaterOrEqual,
        NodeConditionTypes.GameVariableNumberLessThan,
        NodeConditionTypes.GameVariableNumberLessOrEqual,
    };

    /// <summary>True for any of the six Variable* comparison types.</summary>
    public bool IsVariableFamily => _variableTypes.Contains(Model.Type);

    /// <summary>
    /// Rewrite any of the ten superseded variable types to VariableCompare.
    /// Idempotent.
    /// <para/>
    /// The same rewrite <see cref="Model.PackMigration"/> does on load - here as
    /// well because a row can be built from a condition the migration has not
    /// seen: one pasted from another pack, or one a translator produced.
    /// </summary>
    private void NormalizeVariable()
    {
        if (!VariableMerge.Rewrite(Model)) return;

        OnPropertyChanged(nameof(VarSource));
        OnPropertyChanged(nameof(VarComparison));
        OnPropertyChanged(nameof(IsVanillaSource));
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

    /// <summary>
    /// Which store a <c>$name</c> in the VALUE reads from, stored in the
    /// 'valueSource' param — the mirror of <see cref="VarSource"/>.
    /// <para/>
    /// A value is usually a literal, and then this changes nothing. It matters
    /// when the value names another variable: "is this equal to that one" can
    /// only be asked if the two can come from different stores, which is what
    /// the game's own conditions do — <c>Mainstory[MLove] &gt; Mainstory[MCorruption]</c>
    /// compares two vanilla globals, and before this there was no way to write
    /// that here at all.
    /// </summary>
    public string VarValueSource
    {
        get => string.Equals(GetParam("valueSource"), "vanilla", StringComparison.OrdinalIgnoreCase)
            ? "Vanilla" : "Pack";
        set
        {
            if (string.Equals(value, "Vanilla", StringComparison.OrdinalIgnoreCase))
                Model.Params["valueSource"] = "vanilla";
            else Model.Params.Remove("valueSource");
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsVanillaValueSource));
            OnPropertyChanged(nameof(Display));
        }
    }

    /// <summary>True when the value reads a vanilla global (drives its picker).</summary>
    public bool IsVanillaValueSource
        => string.Equals(GetParam("valueSource"), "vanilla", StringComparison.OrdinalIgnoreCase);

    /// <summary>Comparison label, mapped to/from the underlying Variable* type.</summary>
    public string VarComparison
    {
        get => Model.Type == NodeConditionTypes.VariableExists
            ? "exists"
            : VariableMerge.ComparisonOf(Model);
        set
        {
            if (value == VarComparison) return;

            // "exists" is still its own type: it asks whether the variable is
            // set at all, takes no value, and reads from a different place.
            if (value == "exists")
            {
                Type = NodeConditionTypes.VariableExists;
            }
            else
            {
                if (Model.Type != NodeConditionTypes.VariableCompare)
                    Type = NodeConditionTypes.VariableCompare;
                Model.Params["comparison"] = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(ShowVariableValue));
        }
    }

    /// <summary>The "exists" comparison takes no value — hide the value box for it.</summary>
    public bool ShowVariableValue => Model.Type != NodeConditionTypes.VariableExists;

    public string VarName
    {
        get => GetParam("name");
        set
        {
            SetParam("name", value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(VarValueIsBool));
            OnPropertyChanged(nameof(VarValueIsText));
            OnPropertyChanged(nameof(VarValueBool));
        }
    }

    // ── Bool variables compare with a tick box, not typed text ──────────
    //
    // A condition matches by comparing strings, so "True" fails against a
    // variable holding "true" and nothing says why. The same reasoning as the
    // action side, and the same hook — see NodeActionViewModel.

    /// <summary>Reports whether a pack variable of this name is a Bool. Set by
    /// MainViewModel; null before a pack is loaded, so the text box stays the
    /// fallback whenever the answer is not known.</summary>
    internal static Func<string, bool>? IsBoolVariableLookup;

    /// <summary>True when this condition compares a Bool variable's value.</summary>
    public bool VarValueIsBool =>
        IsBoolVariableLookup != null &&
        ShowVariableValue &&
        !string.IsNullOrWhiteSpace(VarName) &&
        IsBoolVariableLookup(VarName);

    /// <summary>The complement, for the text box beside it.</summary>
    public bool VarValueIsText => ShowVariableValue && !VarValueIsBool;

    /// <summary>The compared value as a tick box. Anything but "true" reads as
    /// false, matching the runtime, and writing uses the spelling it expects.</summary>
    public bool VarValueBool
    {
        get => string.Equals(VarValue?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        set
        {
            VarValue = value ? "true" : "false";
            OnPropertyChanged();
        }
    }

    public string VarValue
    {
        get => GetParam("value");
        set
        {
            SetParam("value", value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValueNamesAVariable));
        }
    }

    /// <summary>
    /// Whether the value names another variable rather than being one.
    /// <para/>
    /// The store picker beside it only means anything then — both its own
    /// tooltip and the runtime say a plain value ignores it — so that is the
    /// only time it is on screen. Left showing, a second Pack/Vanilla row
    /// sitting under Value reads as a second operand, and the whole editor
    /// starts to look like it insists on comparing one variable to another.
    /// <para/>
    /// A <c>$</c> anywhere, not only at the start: <c>${name}</c> is the other
    /// spelling, and a value can carry one mid-text.
    /// </summary>
    public bool ValueNamesAVariable => (VarValue ?? "").Contains('$');

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

    // ── InputKey: device, key, and what about it ───────────────────
    //
    // Three pickers rather than a typed key name. A key nobody can spell
    // wrong is the whole point: KeyCode names are not what is printed on the
    // caps (Alpha1, Return, Mouse0), so a text box here would be a field an
    // author guesses at and only finds out about in game.
    //
    // Device is an editor-side filter and is NOT stored. A mouse button is a
    // KeyCode like any other, so the manifest needs one key param either way,
    // and inferring the device back from the token means one less thing that
    // can disagree with itself.

    public bool IsInputFamily => Model.Type == NodeConditionTypes.InputKey;

    public static IReadOnlyList<string> InputDevices => InputKeys.Devices;
    public static IReadOnlyList<string> InputPhaseOptions => InputPhases.All;

    /// <summary>
    /// The device the author has picked, when they have picked one. Editor-only
    /// state, deliberately not a param: a mouse button is a KeyCode like any
    /// other, so the manifest has nothing to say about devices.
    /// <para/>
    /// It cannot be inferred from the key alone, which is what the first version
    /// tried. Switching to Mouse clears the key (a keyboard key is not in the
    /// mouse list), and an empty key infers back to Keyboard - so the picker
    /// snapped straight back and the list never changed.
    /// </summary>
    private string _inputDevice;

    /// <summary>Which picker to show. Falls back to reading the stored key, so a
    /// condition loaded from disk lands on the right one with nothing stored.</summary>
    public string InputDevice
    {
        get => _inputDevice ?? InputKeys.DeviceOf(GetParam("key"));
        set
        {
            if (value == InputDevice) return;
            _inputDevice = value;
            // The old key belongs to the other device's list; keeping it would
            // leave a name the new list cannot offer sitting in the box.
            SetParam("key", "");
            OnPropertyChanged();
            OnPropertyChanged(nameof(InputKeyOptions));
            OnPropertyChanged(nameof(InputKeyToken));
        }
    }

    /// <summary>
    /// The keys for the chosen device, as a GROUPED view so the dropdown can
    /// show headings.
    /// <para/>
    /// Built fresh on every read rather than cached: the list depends on the
    /// device picker, and a view held from the first read would keep showing the
    /// other device's keys. It is ninety-odd items off a static list, so the
    /// rebuild costs nothing worth caching around.
    /// </summary>
    public System.ComponentModel.ICollectionView InputKeyOptions
    {
        get
        {
            var src = new System.Windows.Data.CollectionViewSource
            {
                Source = InputKeys.For(InputDevice).ToList(),
            };
            src.GroupDescriptions.Add(
                new System.Windows.Data.PropertyGroupDescription(nameof(InputKeyOption.Group)));
            return src.View;
        }
    }

    /// <summary>The stored KeyCode name. Bound by SelectedValue, so the token is
    /// what lands in the param while the author only ever sees the label.</summary>
    public string InputKeyToken
    {
        get => GetParam("key");
        set
        {
            SetParam("key", value ?? "");
            // Keep the filter honest if a key arrives from anywhere but the
            // picker - a paste, or an undo restoring the other device's key.
            if (!string.IsNullOrEmpty(value)) _inputDevice = InputKeys.DeviceOf(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(InputDevice));
        }
    }

    public string InputPhase
    {
        get
        {
            var p = GetParam("phase");
            return string.IsNullOrEmpty(p) ? InputPhases.Pressed : p;
        }
        set
        {
            SetParam("phase", value ?? InputPhases.Pressed);
            OnPropertyChanged();
            OnPropertyChanged(nameof(InputPhaseHelp));
        }
    }

    /// <summary>One line under the phase picker saying what the chosen phase
    /// does. The four are easy to mix up and the difference between a moment
    /// and a state is the thing that decides whether the condition works.</summary>
    public string InputPhaseHelp => InputPhases.Describe(InputPhase);


    // ── GameObjectActive: the Set-Active category row, on a condition ───
    //
    // The condition asks about exactly what SetGameObjectActive sets, so it
    // addresses its object the same way: a Category drives the canonical
    // 'kind' param (Bust / GameObjects / Scene / Direct Path), 'target' is
    // what to look up, and 'overlayLevel' scopes a GameObjects target to one
    // level so a same-named overlay in the level being left cannot answer for
    // the one being entered.
    //
    // The option providers below are NodeActionViewModel's own statics rather
    // than copies of them. A target list that differed between the action that
    // switches something on and the condition that reads it back would be a
    // bug in whichever of the two was written second.

    /// <summary>Param keys the shared category row renders, so the
    /// schema-driven rows skip them and nothing is drawn twice.</summary>
    private static readonly HashSet<string> GoCategoryRowKeys = new()
    {
        "kind", "target", "overlayLevel",
        "path",   // legacy, migrated by NormalizeGoActive
    };

    /// <summary>True for the one condition that resolves a GameObject the way
    /// the Set-Active action does. Drives which controls the row shows.</summary>
    public bool IsGoActiveFamily => Model.Type == NodeConditionTypes.GameObjectActive;

    /// <summary>Categories offered here — the same four the Set-Active action
    /// offers. No Places: asking whether a whole level is on screen is what
    /// LevelActive already does, and it resolves place tokens through the
    /// registry rather than by name.</summary>
    public static IReadOnlyList<string> GoCategories => NodeActionViewModel.SetActiveCategories;

    /// <summary>Migrate a pre-category condition (<c>path</c> alone) to the
    /// canonical <c>kind</c> + <c>target</c> shape. Idempotent — safe to run on
    /// every bind and on every type change.</summary>
    private void NormalizeGoActive()
    {
        if (!IsGoActiveFamily) return;
        if (!Model.Params.ContainsKey("target") &&
            Model.Params.TryGetValue("path", out var legacy) &&
            !string.IsNullOrEmpty(legacy))
            Model.Params["target"] = legacy;
        Model.Params.Remove("path");
        // A path written before categories existed was resolved by name, which
        // is what Direct Path means.
        if (!Model.Params.ContainsKey("kind"))
            Model.Params["kind"] = NodeActionViewModel.CatPath;
    }

    /// <summary>How the target resolves, stored in the canonical <c>kind</c> param.</summary>
    public string GoCategory
    {
        get => Model.Params.TryGetValue("kind", out var k) && !string.IsNullOrEmpty(k)
            ? NodeActionViewModel.NormalizeCategory(k)
            : NodeActionViewModel.CatPath;
        set
        {
            if (value == GoCategory) return;
            Model.Params["kind"] = value;
            // The old target belongs to the old category's list; keeping it would
            // leave a name the new list cannot offer sitting in the box, looking
            // chosen.
            Model.Params.Remove("target");
            if (value != NodeActionViewModel.CatOverlay) Model.Params.Remove("overlayLevel");
            OnPropertyChanged();
            OnPropertyChanged(nameof(GoTarget));
            OnPropertyChanged(nameof(GoOverlayLevel));
            OnPropertyChanged(nameof(IsGoOverlayCategory));
            OnPropertyChanged(nameof(IsGoTargetEnabled));
            OnPropertyChanged(nameof(GoOverlayOptions));
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(ParamsAsText));
        }
    }

    /// <summary>What to look up: a scene key under the Scene category, a
    /// GameObject name or hierarchy path under any other.</summary>
    public string GoTarget
    {
        get => Model.Params.TryGetValue("target", out var t) ? t : "";
        set
        {
            if (string.IsNullOrEmpty(value)) Model.Params.Remove("target");
            else Model.Params["target"] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(ParamsAsText));
        }
    }

    /// <summary>GameObjects category only: which level the object lives in, as a
    /// level token. Empty resolves globally, which is the pre-category
    /// behaviour and can answer with a same-named object in another level.</summary>
    public string GoOverlayLevel
    {
        get => Model.Params.TryGetValue("overlayLevel", out var l) ? l : "";
        set
        {
            if (string.IsNullOrEmpty(value)) Model.Params.Remove("overlayLevel");
            else Model.Params["overlayLevel"] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GoOverlayOptions));
            OnPropertyChanged(nameof(IsGoTargetEnabled));
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(ParamsAsText));
        }
    }

    public bool IsGoOverlayCategory => GoCategory == NodeActionViewModel.CatOverlay;

    /// <summary>The GameObjects target list is level-scoped, so its combo stays
    /// disabled until a level is chosen. Every other category is always on.</summary>
    public bool IsGoTargetEnabled =>
        !IsGoOverlayCategory || !string.IsNullOrEmpty(GoOverlayLevel);

    /// <summary>Strictly the chosen level's GameObjects — no whole-pack fallback,
    /// since the combo is disabled until a level is picked and a name from some
    /// other level could never resolve inside this one.</summary>
    public IEnumerable<string> GoOverlayOptions =>
        string.IsNullOrEmpty(GoOverlayLevel)
            ? Array.Empty<string>()
            : NodeActionViewModel.StrictOverlayProvider?.Invoke(GoOverlayLevel)
              ?? Array.Empty<string>();

    /// <summary>Levels that actually carry GameObjects — pack places and vanilla
    /// extensions alike.</summary>
    public IEnumerable<NavigatorTargetOption> GoOverlayLevelOptions =>
        NodeActionViewModel.OverlayLevelProvider?.Invoke()
        ?? Array.Empty<NavigatorTargetOption>();

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
