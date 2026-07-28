using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper for a <see cref="NodeActionDef"/>. The editor binds the
/// type-discriminator dropdown and the params dictionary directly; the
/// <see cref="Display"/> property gives the list view a one-line preview.
/// <para/>
/// The action's per-Type parameter shape surfaces through
/// <see cref="ParamRows"/> — an observable collection of
/// <see cref="ParamRowViewModel"/> instances built from
/// <see cref="ActionSchemas.For"/> whenever <see cref="Type"/> changes.
/// The XAML row template iterates ParamRows and lets a
/// <c>ParamTypeTemplateSelector</c> pick the right editor per
/// <see cref="ParamSchema.Type"/>. The legacy named accessors
/// (<see cref="Actor"/>, <see cref="BustKey"/>, <see cref="Expression"/>,
/// <see cref="Scene"/>) stay on the VM so external code that previously
/// read them keeps working, but the editor itself no longer relies on them.
/// </summary>
public sealed class NodeActionViewModel : ObservableObject
{
    private readonly Action<NodeActionViewModel>? _removeCallback;

    public NodeActionDef Model { get; }

    public NodeActionViewModel(NodeActionDef model,
                                Action<NodeActionViewModel>? removeCallback = null)
    {
        Model = model;
        NormalizeSetActive();   // migrate legacy ActivateScene / path / targetKind → unified
        _removeCallback = removeCallback;
        RemoveCommand = new RelayCommand(
            () => _removeCallback?.Invoke(this),
            () => _removeCallback != null);
        CopyCommand = new RelayCommand(() => Services.EditorClipboard.SetActions(new[] { Model }));
        DiceBranches = new ObservableCollection<DiceBranchViewModel>();
        foreach (var b in Model.Branches)
            DiceBranches.Add(new DiceBranchViewModel(b, this));
        AddDiceBranchCommand = new RelayCommand(AddDiceBranch);
        RebuildParamRows();
    }

    // ── Dice-roll branches ────────────────────────────────────────────
    // The DiceRoll action holds weighted branches (chance % + one nested
    // action each). One roll executes exactly one branch; the editor
    // enforces the chances summing to 100 via DiceChanceTotal/IsDiceTotalValid.

    public bool IsDiceFamily => Model.Type == NodeActionTypes.DiceRoll;

    public ObservableCollection<DiceBranchViewModel> DiceBranches { get; }
    public RelayCommand AddDiceBranchCommand { get; }

    private void AddDiceBranch()
    {
        // Seed the new branch with whatever chance is still unclaimed, so
        // building up to exactly 100 is the path of least resistance.
        int remainder = 100 - DiceChanceTotal;
        var def = new DiceBranchDef { Chance = remainder > 0 ? remainder : 0 };
        Model.Branches.Add(def);
        DiceBranches.Add(new DiceBranchViewModel(def, this));
        NotifyDiceTotals();
    }

    public void RemoveDiceBranch(DiceBranchViewModel vm)
    {
        Model.Branches.Remove(vm.Model);
        DiceBranches.Remove(vm);
        NotifyDiceTotals();
    }

    /// <summary>Sum of every branch's chance — the UI paints it red until it's exactly 100.</summary>
    public int DiceChanceTotal
    {
        get
        {
            int total = 0;
            foreach (var b in DiceBranches) total += b.Model.Chance;
            return total;
        }
    }

    public bool IsDiceTotalValid => DiceChanceTotal == 100;

    public string DiceTotalLabel => "Total: " + DiceChanceTotal + "% (must be exactly 100%)";

    internal void NotifyDiceTotals()
    {
        OnPropertyChanged(nameof(DiceChanceTotal));
        OnPropertyChanged(nameof(IsDiceTotalValid));
        OnPropertyChanged(nameof(DiceTotalLabel));
    }

    /// <summary>Removes this row from its parent action list.</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>Copies just this action to the editor clipboard.</summary>
    public RelayCommand CopyCommand { get; }

    /// <summary>
    /// One row per declared param for the current <see cref="Type"/>.
    /// Repopulated by <see cref="RebuildParamRows"/> whenever Type
    /// changes; an empty schema (e.g. <c>EndDialogue</c>) just leaves
    /// the collection empty.
    /// </summary>
    public ObservableCollection<ParamRowViewModel> ParamRows { get; } = new();

    private void RebuildParamRows()
    {
        ParamRows.Clear();
        var schemas = ActionSchemas.For(Model.Type);
        foreach (var schema in schemas)
        {
            var paramType = schema.Type;
            ParamRowViewModel? capturedRow = null;
            var row = new ParamRowViewModel(
                Model.Params, schema,
                onValueChanged: () =>
                {
                    // Keep the legacy named-accessor INPC + the
                    // Display + ParamsAsText preview in sync so
                    // downstream bindings update with every keystroke.
                    OnPropertyChanged(nameof(Display));
                    OnPropertyChanged(nameof(ParamsAsText));
                    OnPropertyChanged(nameof(Actor));
                    OnPropertyChanged(nameof(BustKey));
                    OnPropertyChanged(nameof(Expression));
                    OnPropertyChanged(nameof(Scene));
                    // Mirror the condition rows: a write may flip a sibling's
                    // EnabledWhen gate. No action schema declares one today,
                    // but wiring it here means adding one just works.
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

    // ── Typed shortcuts for well-known param keys ────────────────────
    //
    // These predate the ParamRows-based renderer; they're kept around
    // because some templates still bind them directly (and because they
    // expose a focused INPC for each well-known key). Writes go through
    // the same Model.Params dict the ParamRows do, so the two views
    // can't drift.

    /// <summary>Pack-local actor key (the <c>actor</c> param).</summary>
    public string Actor
    {
        get => Model.Params.TryGetValue("actor", out var v) ? v : "";
        set
        {
            if (string.IsNullOrEmpty(value)) Model.Params.Remove("actor");
            else Model.Params["actor"] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(ParamsAsText));
        }
    }

    /// <summary>Bust GameObject name (the <c>bustKey</c> param on SetActorBust).</summary>
    public string BustKey
    {
        get => Model.Params.TryGetValue("bustKey", out var v) ? v : "";
        set
        {
            if (string.IsNullOrEmpty(value)) Model.Params.Remove("bustKey");
            else Model.Params["bustKey"] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(ParamsAsText));
        }
    }

    /// <summary>Actor expression key (the <c>expression</c> param on SetActorExpression).</summary>
    public string Expression
    {
        get => Model.Params.TryGetValue("expression", out var v) ? v : "";
        set
        {
            if (string.IsNullOrEmpty(value)) Model.Params.Remove("expression");
            else Model.Params["expression"] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(ParamsAsText));
        }
    }

    /// <summary>Pack-local scene key (the <c>scene</c> param on ActivateScene).</summary>
    public string Scene
    {
        get => Model.Params.TryGetValue("scene", out var v) ? v : "";
        set
        {
            if (string.IsNullOrEmpty(value)) Model.Params.Remove("scene");
            else Model.Params["scene"] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(ParamsAsText));
        }
    }

    public string Type
    {
        get => Model.Type;
        set
        {
            if (Model.Type == value) return;
            Model.Type = value;
            // Entering the dice family with no branches yet: seed a 50/50
            // pair so the row is immediately usable (and already sums to 100).
            if (value == NodeActionTypes.DiceRoll && DiceBranches.Count == 0)
            {
                AddDiceBranch();
                AddDiceBranch();
                foreach (var b in DiceBranches) b.Chance = 50;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            NotifySetActiveFamily();
            // Rebuild typed-param rows for the new schema. Params dict
            // is intentionally left intact so a user fixing a typo on
            // the Type combo doesn't lose every value they entered.
            RebuildParamRows();
        }
    }

    // ── Unified "Set Active" presentation ────────────────────────────────
    //
    // SetGameObjectActive is the single Set-Active action. A Category drives the
    // canonical 'kind' param (Bust / Level Overlay / Scene / Direct Path); the
    // target is the canonical 'target' param and 'active' the toggle. The runtime
    // dispatches on 'kind' — Scene through the scene registry (+ its sound on
    // activate), the rest by resolving a GameObject. Legacy actions (ActivateScene,
    // or SetGameObjectActive with 'path'/'targetKind') are migrated up-front by
    // NormalizeSetActive so the rest of this class only deals with the canonical form.

    public const string CatBust    = "Bust";
    public const string CatOverlay = "GameObjects";
    public const string CatScene   = "Scene";
    public const string CatPath    = "Direct Path";

    /// <summary>Legacy token for <see cref="CatOverlay"/> before the rename.
    /// Migrated to <see cref="CatOverlay"/> on load; the runtime still accepts it.</summary>
    private const string CatOverlayLegacy = "Level Overlay";

    /// <summary>Map a stored category token to its canonical value (migrating the
    /// pre-rename "Level Overlay" token).</summary>
    private static string NormalizeCategory(string kind)
        => kind == CatOverlayLegacy ? CatOverlay : kind;

    /// <summary>The four Set-Active categories shown in the row's dropdown.</summary>
    public static readonly IReadOnlyList<string> SetActiveCategories =
        new[] { CatBust, CatOverlay, CatScene, CatPath };

    /// <summary>Migrate any legacy Set-Active encoding to the canonical
    /// SetGameObjectActive { kind, target, active } shape. Idempotent — safe to
    /// run on every (re)bind. Runs in the constructor so the property accessors
    /// below only ever see canonical params.</summary>
    private void NormalizeSetActive()
    {
        if (Model.Type == NodeActionTypes.ActivateScene)
        {
            var key = Model.Params.TryGetValue("scene", out var s) ? s
                    : Model.Params.TryGetValue("target", out var t) ? t : "";
            Model.Type = NodeActionTypes.SetGameObjectActive;
            Model.Params.Remove("scene");
            Model.Params["kind"] = CatScene;
            if (!string.IsNullOrEmpty(key)) Model.Params["target"] = key;
            Model.Params["active"] = "true";   // legacy ActivateScene was activate-only
        }
        else if (Model.Type == NodeActionTypes.SetGameObjectActive)
        {
            if (!Model.Params.ContainsKey("target") && Model.Params.TryGetValue("path", out var pth))
                Model.Params["target"] = pth;
            Model.Params.Remove("path");
            if (!Model.Params.ContainsKey("kind"))
                Model.Params["kind"] = Model.Params.TryGetValue("targetKind", out var tk) && !string.IsNullOrEmpty(tk)
                    ? tk : CatPath;
            Model.Params.Remove("targetKind");
            // Migrate the pre-rename overlay token so the UI + re-save use the new one.
            if (Model.Params.TryGetValue("kind", out var kv) && kv == CatOverlayLegacy)
                Model.Params["kind"] = CatOverlay;
            if (!Model.Params.ContainsKey("active")) Model.Params["active"] = "true";
        }
    }

    /// <summary>True for the unified Set-Active action (SetGameObjectActive — and,
    /// defensively, a not-yet-migrated ActivateScene). Drives which controls the
    /// action row shows.</summary>
    public bool IsSetActiveFamily =>
        Model.Type == NodeActionTypes.SetGameObjectActive || Model.Type == NodeActionTypes.ActivateScene;

    /// <summary>True when the Scene category is selected. (Scene supports both
    /// activate and deactivate, so this no longer hides the Active checkbox.)</summary>
    public bool IsSceneCategory => Category == CatScene;

    /// <summary>The action type shown in the row's Type combo. Maps any stray
    /// ActivateScene onto the unified SetGameObjectActive entry.</summary>
    public string DisplayType
    {
        get => Model.Type == NodeActionTypes.ActivateScene ? NodeActionTypes.SetGameObjectActive
             : IsVariableFamily ? VariableFamilyType
             : Model.Type;
        set
        {
            if (value == DisplayType) return;
            if (value == NodeActionTypes.SetGameObjectActive) SetCategory(CatPath);  // enter Set-Active family
            else if (value == VariableFamilyType) SetVarOperation("Set");            // enter Variable family
            else Type = value;                                                       // leaving to a schema-driven type
        }
    }

    // ── Unified "Variable" presentation ─────────────────────────────────
    //
    // SetVariable + IncrementVariable collapse into ONE "Variable" entry in the
    // picker. The row shows Source (Pack/Vanilla) + Operation (Set/Increment) +
    // Name + Value/Delta. Source is the canonical 'source' param ("vanilla", or
    // absent for the pack default); the operation stays encoded in Model.Type.

    /// <summary>Pseudo type-id shown in the picker for the Variable family.</summary>
    public const string VariableFamilyType = "Variable";

    public static IReadOnlyList<string> VariableSources { get; } = new[] { "Pack", "Vanilla" };
    public const string OpRandomFromList = "Random from list";
    public const string OpCountList = "List count";
    public static IReadOnlyList<string> VariableOperations { get; } = new[] { "Set", "Increment", OpRandomFromList, OpCountList };

    /// <summary>True for SetVariable / IncrementVariable / PickRandomFromList / CountList.</summary>
    public bool IsVariableFamily =>
        Model.Type == NodeActionTypes.SetVariable ||
        Model.Type == NodeActionTypes.IncrementVariable ||
        Model.Type == NodeActionTypes.PickRandomFromList ||
        Model.Type == NodeActionTypes.CountList;

    /// <summary>Switch the action into the Variable family for the given operation, keeping name.</summary>
    private void SetVarOperation(string operation)
    {
        if (string.Equals(operation, "Increment", StringComparison.OrdinalIgnoreCase))
            Model.Type = NodeActionTypes.IncrementVariable;
        else if (string.Equals(operation, OpRandomFromList, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(operation, OpCountList, StringComparison.OrdinalIgnoreCase))
        {
            Model.Type = string.Equals(operation, OpCountList, StringComparison.OrdinalIgnoreCase)
                ? NodeActionTypes.CountList : NodeActionTypes.PickRandomFromList;
            // List-source operations have no Pack/Vanilla source, scalar value or delta.
            Model.Params.Remove("source");
            Model.Params.Remove("value");
            Model.Params.Remove("delta");
        }
        else
            Model.Type = NodeActionTypes.SetVariable;

        // Leaving the list-source operations drops their source-list param.
        if (Model.Type != NodeActionTypes.PickRandomFromList &&
            Model.Type != NodeActionTypes.CountList)
            Model.Params.Remove("fromList");

        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(DisplayType));
        OnPropertyChanged(nameof(IsVariableFamily));
        OnPropertyChanged(nameof(VarOperation));
        OnPropertyChanged(nameof(IsIncrement));
        OnPropertyChanged(nameof(IsRandomFromList));
        OnPropertyChanged(nameof(VarFromList));
        OnPropertyChanged(nameof(VarSource));
        OnPropertyChanged(nameof(IsVanillaSource));
        OnPropertyChanged(nameof(Display));
        RebuildParamRows();
    }

    /// <summary>Set / Increment / Random-from-list / List-count, mapped to/from the underlying type.</summary>
    public string VarOperation
    {
        get => Model.Type == NodeActionTypes.IncrementVariable ? "Increment"
             : Model.Type == NodeActionTypes.PickRandomFromList ? OpRandomFromList
             : Model.Type == NodeActionTypes.CountList ? OpCountList
             : "Set";
        set { if (value != VarOperation) SetVarOperation(value); }
    }

    /// <summary>True for IncrementVariable — shows the Delta box instead of Value.</summary>
    public bool IsIncrement => Model.Type == NodeActionTypes.IncrementVariable;

    /// <summary>True for the Random-from-list / List-count operations — they show a
    /// "From list" picker instead of Value/Delta/Source, and their result (a picked
    /// element / the entry count) lands in the <see cref="VarName"/> target.</summary>
    public bool IsRandomFromList =>
        Model.Type == NodeActionTypes.PickRandomFromList ||
        Model.Type == NodeActionTypes.CountList;

    /// <summary>Source the Random-from-list operation draws from: a List variable
    /// name or a comma-separated literal. Stored in the <c>fromList</c> param so it
    /// never collides with the Pack/Vanilla <c>source</c> toggle.</summary>
    public string VarFromList { get => GetParam("fromList"); set { SetParam("fromList", value); OnPropertyChanged(); } }

    /// <summary>True only for the random PICK — <see cref="IsRandomFromList"/> also
    /// covers List count, which has no candidate set to filter and nothing to fall
    /// back to. Gates the Excluding / If-none-left rows.</summary>
    public bool IsPickRandomOnly => Model.Type == NodeActionTypes.PickRandomFromList;

    /// <summary>Entries removed from <see cref="VarFromList"/> before the pick —
    /// usually <c>$SomeOccupiedList</c>. Empty means no filtering.</summary>
    public string VarExcluding { get => GetParam("excluding"); set { SetParam("excluding", value); OnPropertyChanged(); } }

    /// <summary>Written to the target when nothing survives the exclusion. Empty
    /// clears the target, which is the original behaviour.</summary>
    public string VarFallback { get => GetParam("fallback"); set { SetParam("fallback", value); OnPropertyChanged(); } }

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

    public bool IsVanillaSource => string.Equals(GetParam("source"), "vanilla", StringComparison.OrdinalIgnoreCase);

    public string VarName  { get => GetParam("name");  set { SetParam("name", value);  OnPropertyChanged(); } }
    public string VarValue { get => GetParam("value"); set { SetParam("value", value); OnPropertyChanged(); } }
    public string VarDelta { get => GetParam("delta"); set { SetParam("delta", value); OnPropertyChanged(); } }

    /// <summary>Set-Active category, stored in the canonical <c>kind</c> param.</summary>
    public string Category
    {
        get => Model.Params.TryGetValue("kind", out var k) && !string.IsNullOrEmpty(k) ? NormalizeCategory(k) : CatPath;
        set { if (value != Category) SetCategory(value); }
    }

    /// <summary>Unified target — a scene key (Scene category) or a GameObject
    /// name / hierarchy path (everything else). Canonical <c>target</c> param.</summary>
    public string Target
    {
        get => Model.Params.TryGetValue("target", out var t) ? t : "";
        set
        {
            if (string.IsNullOrEmpty(value)) Model.Params.Remove("target");
            else Model.Params["target"] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    /// <summary>SetActive(active) toggle. Defaults true. Applies to every category
    /// (Scene deactivation just turns the scene GO off, silently).</summary>
    public bool Active
    {
        get => !Model.Params.TryGetValue("active", out var v) || !bool.TryParse(v, out var b) || b;
        set { Model.Params["active"] = value ? "true" : "false"; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    /// <summary>
    /// Level Overlay category only: which level the overlay lives in, as a level
    /// token (canonical <c>overlayLevel</c> param). Set this to the level you're
    /// transitioning <em>into</em> so the runtime resolves the overlay inside
    /// that level rather than a same-named GameObject left active in the previous
    /// one. Empty = resolve the overlay globally (the legacy behaviour).
    /// </summary>
    public string OverlayLevel
    {
        get => Model.Params.TryGetValue("overlayLevel", out var l) ? l : "";
        set
        {
            if (string.IsNullOrEmpty(value)) Model.Params.Remove("overlayLevel");
            else Model.Params["overlayLevel"] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverlayOptions));
            OnPropertyChanged(nameof(IsOverlayTargetEnabled));
        }
    }

    /// <summary>Levels offered in the Set-Active row's Level dropdown — only
    /// ones that actually carry GameObjects (pack places + vanilla
    /// extensions). Same provider as the Fade/Move/Spin family.</summary>
    public IEnumerable<NavigatorTargetOption> OverlayLevelOptions =>
        OverlayLevelProvider?.Invoke() ?? Array.Empty<NavigatorTargetOption>();

    /// <summary>Target combo enable gate for the Set-Active row: the Extra
    /// GameObjects target list is level-scoped, so it's disabled until a
    /// Level is chosen. Other categories always enabled.</summary>
    public bool IsOverlayTargetEnabled =>
        Category != CatOverlay || !string.IsNullOrEmpty(OverlayLevel);

    /// <summary>
    /// Resolves a level token to its overlay GameObject names (empty token →
    /// every overlay in the pack). Set once by the MainViewModel so a Level
    /// Overlay row can list the overlays of the level chosen in
    /// <see cref="OverlayLevel"/> — "pick the level first, the overlay second".
    /// </summary>
    public static Func<string, IEnumerable<string>>? OverlayProvider;

    /// <summary>
    /// Strict variant of <see cref="OverlayProvider"/>: exactly the given
    /// level's overlay names, empty for an empty/unknown token — no
    /// whole-pack fallback. Backs the Set-Active GameObjects target,
    /// which is disabled until a level is chosen, so it must never offer
    /// names that can't resolve inside the chosen level.
    /// </summary>
    public static Func<string, IEnumerable<string>>? StrictOverlayProvider;

    /// <summary>Level tokens that actually carry GameObjects — feeds
    /// the Set-Active row's level dropdown. Set once by the MainViewModel.</summary>
    public static Func<IEnumerable<NavigatorTargetOption>>? OverlayLevelProvider;

    /// <summary>
    /// The selected node's inferred level — the overlay-list fallback when an
    /// action's <see cref="OverlayLevel"/> isn't set yet. Maintained by the
    /// MainViewModel on each selected-node change.
    /// </summary>
    public static string InferredOverlayLevel = "";

    /// <summary>Overlay names for the Set-Active GameObjects target
    /// dropdown — STRICTLY the chosen <see cref="OverlayLevel"/>'s (the combo
    /// is disabled until one is picked, see <see cref="IsOverlayTargetEnabled"/>,
    /// so no inferred-level or whole-pack fallback applies here).</summary>
    public IEnumerable<string> OverlayOptions =>
        string.IsNullOrEmpty(OverlayLevel)
            ? Array.Empty<string>()
            : StrictOverlayProvider?.Invoke(OverlayLevel) ?? Array.Empty<string>();

    // ── Category + Target for GameObject-targeting actions ────────────────
    //
    // FadeSprite / MoveGameObject / SpinGameObject share a Category + Target
    // (+ Level for overlays) row — the same idea as Set-Active, but keeping
    // their own params (alpha, x/y, speed…). 'kind' + 'target' + 'overlayLevel'
    // are canonical; a legacy action with only 'path'/'target' and no 'kind'
    // still resolves (Direct Path tolerates a level token at runtime).

    private static readonly HashSet<string> _goCategoryTypes = new()
    {
        NodeActionTypes.FadeSprite, NodeActionTypes.MoveGameObject, NodeActionTypes.SpinGameObject,
    };
    public bool IsGoCategoryFamily => _goCategoryTypes.Contains(Model.Type);

    public static IReadOnlyList<string> GoCategories { get; } =
        new[] { CatPath, CatOverlay, "Places", CatBust };

    public string GoCategory
    {
        get => Model.Params.TryGetValue("kind", out var k) && !string.IsNullOrEmpty(k) ? NormalizeCategory(k) : CatPath;
        set
        {
            if (value == GoCategory) return;
            if (value == CatPath) Model.Params.Remove("kind"); else Model.Params["kind"] = value;
            if (value != CatOverlay) Model.Params.Remove("overlayLevel");
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGoOverlayCategory));
            OnPropertyChanged(nameof(GoOverlayOptions));
            OnPropertyChanged(nameof(GoOverlayLevelOptions));
            OnPropertyChanged(nameof(IsGoOverlayTargetEnabled));
            OnPropertyChanged(nameof(Display));
        }
    }
    public bool IsGoOverlayCategory => GoCategory == CatOverlay;

    /// <summary>Canonical target; falls back to legacy 'path' (FadeSprite) for display.</summary>
    public string GoTarget
    {
        get => Model.Params.TryGetValue("target", out var t) ? t
             : Model.Params.TryGetValue("path", out var p) ? p : "";
        set
        {
            if (string.IsNullOrEmpty(value)) Model.Params.Remove("target");
            else Model.Params["target"] = value;
            Model.Params.Remove("path");   // migrate off the legacy key on first edit
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public string GoOverlayLevel
    {
        get => Model.Params.TryGetValue("overlayLevel", out var l) ? l : "";
        set
        {
            if (string.IsNullOrEmpty(value)) Model.Params.Remove("overlayLevel");
            else Model.Params["overlayLevel"] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GoOverlayOptions));
            OnPropertyChanged(nameof(IsGoOverlayTargetEnabled));
        }
    }

    /// <summary>Overlay names for the GameObjects target dropdown —
    /// STRICTLY the chosen level's (no whole-pack fallback; the combo is
    /// disabled until a level is picked, see <see cref="IsGoOverlayTargetEnabled"/>).</summary>
    public IEnumerable<string> GoOverlayOptions =>
        string.IsNullOrEmpty(GoOverlayLevel)
            ? Array.Empty<string>()
            : StrictOverlayProvider?.Invoke(GoOverlayLevel) ?? Array.Empty<string>();

    /// <summary>Levels offered in the Set-Active row's level dropdown — only
    /// ones that actually carry GameObjects.</summary>
    public IEnumerable<NavigatorTargetOption> GoOverlayLevelOptions =>
        OverlayLevelProvider?.Invoke() ?? Array.Empty<NavigatorTargetOption>();

    /// <summary>The target combo is a dead end for the GameObjects
    /// category until a level is chosen (targets are level-scoped), so it's
    /// disabled then. Every other category keeps it enabled.</summary>
    public bool IsGoOverlayTargetEnabled =>
        !IsGoOverlayCategory || !string.IsNullOrEmpty(GoOverlayLevel);

    /// <summary>Switch the action into the unified Set-Active form for
    /// <paramref name="category"/>, keeping the existing target and clearing any
    /// legacy keys.</summary>
    private void SetCategory(string category)
    {
        var carried = Target;
        Model.Type = NodeActionTypes.SetGameObjectActive;
        Model.Params["kind"] = category;
        if (string.IsNullOrEmpty(carried)) Model.Params.Remove("target");
        else Model.Params["target"] = carried;
        if (!Model.Params.ContainsKey("active")) Model.Params["active"] = "true";
        // Drop any legacy keys so the row never round-trips a stale encoding.
        Model.Params.Remove("path");
        Model.Params.Remove("scene");
        Model.Params.Remove("targetKind");
        // overlayLevel only means something for Level Overlay.
        if (category != CatOverlay) Model.Params.Remove("overlayLevel");
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(IsSceneCategory));
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(Active));
        OnPropertyChanged(nameof(OverlayLevel));
        OnPropertyChanged(nameof(OverlayOptions));
        OnPropertyChanged(nameof(OverlayLevelOptions));
        OnPropertyChanged(nameof(IsOverlayTargetEnabled));
        OnPropertyChanged(nameof(Display));
        NotifySetActiveFamily();
    }

    private void NotifySetActiveFamily()
    {
        OnPropertyChanged(nameof(DisplayType));
        OnPropertyChanged(nameof(IsSetActiveFamily));
        OnPropertyChanged(nameof(IsSceneCategory));
        // Variable family too, so its row hides/shows the moment the Type
        // changes (otherwise Source/Operation linger until the row rebinds).
        OnPropertyChanged(nameof(IsVariableFamily));
        OnPropertyChanged(nameof(VarOperation));
        OnPropertyChanged(nameof(IsIncrement));
        OnPropertyChanged(nameof(IsRandomFromList));
        OnPropertyChanged(nameof(VarFromList));
        OnPropertyChanged(nameof(VarSource));
        OnPropertyChanged(nameof(IsVanillaSource));
        OnPropertyChanged(nameof(IsDiceFamily));
        // GameObject category/target family (Fade/Move/Spin).
        OnPropertyChanged(nameof(IsGoCategoryFamily));
        OnPropertyChanged(nameof(GoCategory));
        OnPropertyChanged(nameof(IsGoOverlayCategory));
        OnPropertyChanged(nameof(GoTarget));
        OnPropertyChanged(nameof(GoOverlayLevel));
        OnPropertyChanged(nameof(GoOverlayOptions));
        OnPropertyChanged(nameof(GoOverlayLevelOptions));
        OnPropertyChanged(nameof(IsGoOverlayTargetEnabled));
    }

    public Dictionary<string, string> Params => Model.Params;

    /// <summary>Convenience for reading/writing a named param from XAML.</summary>
    public string GetParam(string key) => Model.Params.TryGetValue(key, out var v) ? v : "";
    public void SetParam(string key, string value)
    {
        if (string.IsNullOrEmpty(value)) Model.Params.Remove(key);
        else Model.Params[key] = value;
        OnPropertyChanged(nameof(Display));
    }

    /// <summary>Compact "Type — param1=value, param2=value" preview.</summary>
    public string Display
    {
        get
        {
            if (string.IsNullOrEmpty(Type)) return "(empty action)";
            if (Model.Params.Count == 0) return Type;
            var pairs = new List<string>(Model.Params.Count);
            foreach (var kv in Model.Params) pairs.Add(kv.Key + "=" + kv.Value);
            return Type + " — " + string.Join(", ", pairs);
        }
    }

    /// <summary>
    /// The params dictionary round-tripped through a single multi-line string,
    /// one <c>key=value</c> per line. Kept on the VM for the rare consumer
    /// that wants a raw fallback view; the editor's main row template no
    /// longer renders it.
    /// </summary>
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
            // ParamRows reads from Model.Params, but our row VMs cache
            // values internally between writes — rebuild so they pick
            // up the just-parsed dict contents.
            RebuildParamRows();
        }
    }
}

/// <summary>
/// One weighted branch of a DiceRoll action row: an editable chance (with
/// text backing so partial input isn't reformatted mid-typing) plus a nested
/// full <see cref="NodeActionViewModel"/> for the branch's single action.
/// The nested action's remove button removes the whole branch.
/// </summary>
public sealed class DiceBranchViewModel : ObservableObject
{
    public DiceBranchDef Model { get; }
    private readonly NodeActionViewModel _owner;

    public DiceBranchViewModel(DiceBranchDef model, NodeActionViewModel owner)
    {
        Model = model;
        _owner = owner;
        Action = new NodeActionViewModel(model.Action, _ => owner.RemoveDiceBranch(this));
        RemoveCommand = new RelayCommand(() => owner.RemoveDiceBranch(this));
    }

    /// <summary>Nested action editor for what this branch does when rolled.</summary>
    public NodeActionViewModel Action { get; }

    public RelayCommand RemoveCommand { get; }

    public int Chance
    {
        get => Model.Chance;
        set
        {
            if (Model.Chance == value) return;
            Model.Chance = value;
            _chanceText = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChanceText));
            _owner.NotifyDiceTotals();
        }
    }

    // Raw-text backing (same pattern as the SFX volume fields) so a
    // mid-edit value isn't snapped back while typing.
    private string? _chanceText;
    public string ChanceText
    {
        get => _chanceText ??= Model.Chance.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            _chanceText = value ?? "";
            if (int.TryParse(_chanceText.Trim(), out var n) && n >= 0)
            {
                Model.Chance = n;
                _owner.NotifyDiceTotals();
            }
            OnPropertyChanged();
        }
    }
}
