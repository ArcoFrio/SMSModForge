using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper for an <see cref="UpdateRuleDef"/>. Owns observable
/// collections of condition + action rows that bind the same way the
/// dialogue-node editor does, plus a code-mode toggle that switches
/// the editor to a JSON textbox for power users.
/// <para/>
/// In both picker and code modes the model is the single source of
/// truth — switching modes serialises / deserialises through the
/// same <see cref="UpdateRuleDef.Conditions"/> + <see cref="UpdateRuleDef.Actions"/>
/// lists. Round-trips through <see cref="PackRepository"/> without
/// data loss.
/// </summary>
public sealed class UpdateRuleViewModel : ObservableObject
{
    public UpdateRuleDef Model { get; }
    public ObservableCollection<NodeConditionViewModel> Conditions { get; }
    public ObservableCollection<NodeActionViewModel> Actions { get; }

    /// <summary>Else-if chain rows. Same conditions+actions group VM the
    /// place hooks use; first branch whose conditions pass wins when the
    /// main conditions fail (empty conditions = plain Else).</summary>
    public ObservableCollection<LevelHookViewModel> Branches { get; }

    public UpdateRuleViewModel(UpdateRuleDef model)
    {
        Model = model;
        Conditions = new ObservableCollection<NodeConditionViewModel>(
            model.Conditions.Select(c => new NodeConditionViewModel(c, RemoveCondition, context: ConditionContext.Rule)));
        Actions = new ObservableCollection<NodeActionViewModel>(
            model.Actions.Select(a => new NodeActionViewModel(a, RemoveAction)));
        Branches = new ObservableCollection<LevelHookViewModel>(
            model.Branches.Select(b => new LevelHookViewModel(b, RemoveBranch)));
        AddBranchCommand = new RelayCommand(AddBranch);

        // Initialise the code-mode buffer from the current model. The
        // textbox stays in sync with the model in picker mode, and the
        // model updates from the textbox when the user commits.
        _codeText = SerializeCurrentModel();

        // Per-list copy/paste/overwrite — same clipboard slots the dialogue
        // node editor uses, so conditions/actions travel freely between
        // dialogues, integration rules, and level hooks.
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

    public RelayCommand CopyConditionsCommand { get; }
    public RelayCommand PasteConditionsCommand { get; }
    public RelayCommand OverwriteConditionsCommand { get; }
    public RelayCommand CopyActionsCommand { get; }
    public RelayCommand PasteActionsCommand { get; }
    public RelayCommand OverwriteActionsCommand { get; }
    public RelayCommand AddBranchCommand { get; }

    public void AddBranch()
    {
        var def = new LevelHookDef();
        Model.Branches.Add(def);
        Branches.Add(new LevelHookViewModel(def, RemoveBranch));
    }

    public void RemoveBranch(LevelHookViewModel b)
    {
        Model.Branches.Remove(b.Model);
        Branches.Remove(b);
    }

    private void PasteConditions(bool overwrite)
    {
        var src = Services.EditorClipboard.Conditions;
        if (src == null || src.Count == 0) return;
        if (overwrite) { Model.Conditions.Clear(); Conditions.Clear(); }
        foreach (var def in Services.EditorClipboard.Clone(src))
        {
            Model.Conditions.Add(def);
            Conditions.Add(new NodeConditionViewModel(def, RemoveCondition, context: ConditionContext.Rule));
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

    public string Key
    {
        get => Model.Key;
        set
        {
            if (Model.Key == value) return;
            Model.Key = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public string DisplayName
    {
        get => Model.DisplayName;
        set
        {
            if (Model.DisplayName == value) return;
            Model.DisplayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public string Description
    {
        get => Model.Description ?? "";
        set
        {
            Model.Description = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }

    public UpdateRuleTriggerMode TriggerMode
    {
        get => Model.TriggerMode;
        set { Model.TriggerMode = value; OnPropertyChanged(); }
    }

    /// <summary>Log this rule's decisions in-game — see
    /// <see cref="UpdateRuleDef.DebugConditions"/>.</summary>
    public bool DebugConditions
    {
        get => Model.DebugConditions;
        set { Model.DebugConditions = value; OnPropertyChanged(); }
    }

    /// <summary>Values to repeat the rule for — a literal CSV or <c>$ListVar</c>.
    /// Blank = the rule runs once.</summary>
    public string ForEach
    {
        get => Model.ForEach ?? "";
        set
        {
            Model.ForEach = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
        }
    }

    /// <summary>Placeholder name substituted per value, written <c>{name}</c>.
    /// Blank stores null and the runtime defaults to <c>item</c>.</summary>
    public string ForEachAs
    {
        get => Model.ForEachAs ?? "";
        set
        {
            Model.ForEachAs = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// True when the rule's actions / conditions are being edited as
    /// raw JSON in the textbox instead of through the picker rows.
    /// Toggling this re-serialises the current state into
    /// <see cref="CodeText"/> (when switching INTO code mode) or
    /// re-parses <see cref="CodeText"/> back into model lists (when
    /// switching OUT). A parse error keeps the toggle in code mode
    /// so the user can fix the JSON before going back to the picker.
    /// </summary>
    public bool CodeMode
    {
        get => Model.CodeMode;
        set
        {
            if (Model.CodeMode == value) return;
            if (value)
            {
                // Picker → Code: snapshot the model into the textbox.
                _codeText = SerializeCurrentModel();
                _codeError = "";
            }
            else
            {
                // Code → Picker: parse + apply. Refuse the switch if
                // the JSON is broken; the user has to fix it first.
                if (!TryCommitCodeToModel())
                {
                    OnPropertyChanged();        // refresh CodeMode binding (no change)
                    OnPropertyChanged(nameof(CodeError));
                    return;
                }
            }
            Model.CodeMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CodeText));
            OnPropertyChanged(nameof(CodeError));
        }
    }

    private string _codeText = "";
    /// <summary>
    /// Raw JSON view of <see cref="Model"/>'s conditions + actions.
    /// Edited live in code mode; validated on every change so the
    /// user sees parse errors as they type.
    /// </summary>
    public string CodeText
    {
        get => _codeText;
        set
        {
            if (_codeText == value) return;
            _codeText = value ?? "";
            // Live validate so the inline error message updates
            // immediately. The model commit only happens when the
            // user toggles BACK to picker mode — we don't want every
            // keystroke to rebuild the row VMs.
            ValidateCode();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CodeError));
            OnPropertyChanged(nameof(IsCodeValid));
        }
    }

    private string _codeError = "";
    /// <summary>
    /// Empty string when <see cref="CodeText"/> parses cleanly, the
    /// parse error message otherwise. Bound to a TextBlock above the
    /// code textbox so authoring mistakes surface inline.
    /// </summary>
    public string CodeError
    {
        get => _codeError;
        private set { _codeError = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsCodeValid)); }
    }

    public bool IsCodeValid => string.IsNullOrEmpty(_codeError);

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : $"{DisplayName} ({Key})";

    public NodeConditionViewModel AddCondition()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals };
        Model.Conditions.Add(def);
        var vm = new NodeConditionViewModel(def, RemoveCondition, context: ConditionContext.Rule);
        Conditions.Add(vm);
        return vm;
    }

    /// <summary>Add an empty AND group (switchable to OR) to this rule's conditions.</summary>
    public NodeConditionViewModel AddConditionGroup()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.GroupAll, Conditions = new() };
        Model.Conditions.Add(def);
        var vm = new NodeConditionViewModel(def, RemoveCondition, context: ConditionContext.Rule);
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

    /// <summary>
    /// Serialise the rule's conditions + actions to a human-readable
    /// JSON object. Pretty-printed so the textbox is browsable;
    /// fields are stable-ordered to match the editor's mental model.
    /// </summary>
    private string SerializeCurrentModel()
    {
        var snapshot = new
        {
            triggerMode = Model.TriggerMode.ToString(),
            conditions = Model.Conditions,
            actions = Model.Actions,
            branches = Model.Branches,
        };
        return JsonConvert.SerializeObject(snapshot, Formatting.Indented);
    }

    /// <summary>
    /// Validate <see cref="CodeText"/> without committing. Sets
    /// <see cref="CodeError"/> for inline display.
    /// </summary>
    private void ValidateCode()
    {
        try
        {
            var token = JToken.Parse(_codeText);
            // Must be an object with at least conditions / actions array fields.
            if (token.Type != JTokenType.Object)
                throw new JsonReaderException("expected a JSON object at the root");
            var obj = (JObject)token;
            var condTok = obj["conditions"];
            if (condTok != null && condTok.Type != JTokenType.Array)
                throw new JsonReaderException("'conditions' must be an array");
            var actTok = obj["actions"];
            if (actTok != null && actTok.Type != JTokenType.Array)
                throw new JsonReaderException("'actions' must be an array");
            var brTok = obj["branches"];
            if (brTok != null && brTok.Type != JTokenType.Array)
                throw new JsonReaderException("'branches' must be an array");
            CodeError = "";
        }
        catch (System.Exception ex)
        {
            CodeError = ex.Message;
        }
    }

    /// <summary>
    /// Parse <see cref="CodeText"/> and write it into the underlying
    /// model + observable collections. Returns true on success;
    /// false if the JSON is broken (in which case the model and
    /// rows are untouched).
    /// </summary>
    private bool TryCommitCodeToModel()
    {
        ValidateCode();
        if (!IsCodeValid) return false;
        try
        {
            var obj = JObject.Parse(_codeText);
            // triggerMode optional — keep current if absent.
            string? modeStr = (string?)obj["triggerMode"];
            if (!string.IsNullOrEmpty(modeStr) &&
                System.Enum.TryParse<UpdateRuleTriggerMode>(modeStr, true, out var parsed))
            {
                Model.TriggerMode = parsed;
            }

            // conditions + actions + branches: replace wholesale.
            var newConditions = obj["conditions"]?.ToObject<System.Collections.Generic.List<NodeConditionDef>>()
                ?? new System.Collections.Generic.List<NodeConditionDef>();
            var newActions = obj["actions"]?.ToObject<System.Collections.Generic.List<NodeActionDef>>()
                ?? new System.Collections.Generic.List<NodeActionDef>();
            var newBranches = obj["branches"]?.ToObject<System.Collections.Generic.List<LevelHookDef>>()
                ?? new System.Collections.Generic.List<LevelHookDef>();
            Model.Conditions.Clear(); Model.Conditions.AddRange(newConditions);
            Model.Actions.Clear();    Model.Actions.AddRange(newActions);
            Model.Branches.Clear();   Model.Branches.AddRange(newBranches);

            Conditions.Clear();
            foreach (var c in Model.Conditions) Conditions.Add(new NodeConditionViewModel(c, RemoveCondition, context: ConditionContext.Rule));
            Actions.Clear();
            foreach (var a in Model.Actions) Actions.Add(new NodeActionViewModel(a, RemoveAction));
            Branches.Clear();
            foreach (var b in Model.Branches) Branches.Add(new LevelHookViewModel(b, RemoveBranch));

            OnPropertyChanged(nameof(TriggerMode));
            return true;
        }
        catch (System.Exception ex)
        {
            CodeError = ex.Message;
            return false;
        }
    }
}
