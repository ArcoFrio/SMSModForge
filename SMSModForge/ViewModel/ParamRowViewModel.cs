using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper for one editor row inside an action or condition's params
/// list. Holds a reference to the schema (declarative shape) plus the
/// underlying <see cref="Dictionary{TKey,TValue}"/> the row's value lives
/// in. Reads and writes go through the dict so the action/condition VM's
/// <see cref="NodeActionViewModel.Display"/> stays consistent and the
/// serialised JSON shape is unchanged.
/// <para/>
/// The editor's <c>ParamTypeTemplateSelector</c> picks the right
/// <c>DataTemplate</c> per <see cref="Schema"/>.<see cref="ParamSchema.Type"/>;
/// each template binds back to <see cref="Value"/> or <see cref="BoolValue"/>
/// / <see cref="DoubleValue"/> depending on the control it renders.
/// </summary>
public sealed class ParamRowViewModel : ObservableObject
{
    private readonly Dictionary<string, string> _params;
    private readonly System.Action? _onValueChanged;

    /// <summary>Schema declaring this row's key, label, type, default and tooltip.</summary>
    public ParamSchema Schema { get; }

    /// <param name="paramsDict">The action or condition's params dict —
    /// shared with the parent VM, not copied, so writes stay in-sync.</param>
    /// <param name="schema">Per-key metadata.</param>
    /// <param name="onValueChanged">Optional callback fired after a write —
    /// lets the parent re-raise <c>PropertyChanged</c> for its
    /// <c>Display</c> property so the row list preview updates.</param>
    public ParamRowViewModel(Dictionary<string, string> paramsDict,
                              ParamSchema schema,
                              System.Action? onValueChanged = null)
    {
        _params = paramsDict;
        Schema = schema;
        _onValueChanged = onValueChanged;
    }

    /// <summary>Convenience accessors so XAML doesn't have to dive through Schema.</summary>
    public string Key => Schema.Key;
    /// <inheritdoc cref="ParamSchema.Label"/>
    public string Label => Schema.Label;
    /// <inheritdoc cref="ParamSchema.Type"/>
    public ParamType Type => Schema.Type;

    /// <summary>Re-read <see cref="Value"/> from the underlying params dict.
    /// Needed when something rewrites the model behind the row's back — a
    /// variable rename rewriting every reference, for instance — since the
    /// getter reads the dict live but bindings only refresh on notification.</summary>
    public void Refresh() => OnPropertyChanged(nameof(Value));
    /// <inheritdoc cref="ParamSchema.Tooltip"/>
    public string Tooltip => Schema.Tooltip;
    /// <summary>Options for a <see cref="ParamType.Choice"/> param's dropdown.</summary>
    public string[] FixedOptions => Schema.FixedOptions;

    /// <summary>One option: what is stored, and what the list says for it.</summary>
    public sealed record Choice(string Value, string Text);

    /// <summary><see cref="FixedOptions"/> as the dropdown shows them - each
    /// in words where it has any - while what it selects stays the value.</summary>
    public IReadOnlyList<Choice> FixedChoices
        => Schema.FixedOptions.Select(o => new Choice(o, ParamSchema.ChoiceText(Schema.Key, o))).ToList();

    /// <summary>True when this PackVarRef param references a boolean variable.
    /// Set by the parent VM so the BoolVarRef template can show True/False radios.
    /// Defaults to false; the parent calls <see cref="SetBooleanVariable(bool)"/> when
    /// building the row.</summary>
    public bool IsBooleanVariable { get; private set; }

    /// <summary>Mark this row as referencing a boolean variable (triggers INPC for visibility binding).</summary>
    public void SetBooleanVariable(bool value) { IsBooleanVariable = value; OnPropertyChanged(); }

    /// <summary>Callback the parent sets to check if a variable name is boolean.
    /// Null = no detection available (falls back to non-boolean rendering).</summary>
    internal System.Func<string, bool>? IsBooleanVarChecker { get; set; }

    /// <summary>
    /// What kind the named variable is. Set by the parent beside
    /// <see cref="IsBooleanVarChecker"/>.
    /// <para/>
    /// These rows name one of the PACK's own variables — the picker beside them
    /// is the pack's list — so the side is not a question here the way it is on
    /// a Variable condition, and the lookup is asked for the pack every time.
    /// </summary>
    internal System.Func<string, bool, Model.VariableKind>? VariableKindChecker { get; set; }

    /// <summary>
    /// The small note beside the name saying what kind it holds, or empty.
    /// <para/>
    /// Same note, same words, as the one on a Variable condition or action: a
    /// row that names a variable should say what it is looking at wherever it
    /// appears, and these rows are where the list actions, the dice actions and
    /// the GC2-global conditions name theirs.
    /// <para/>
    /// Every one of these pickers is EDITABLE, which is why a note earns its
    /// place even on the filtered ones: a List picker offers only lists, and
    /// still takes whatever somebody types into it.
    /// </summary>
    public string VarKindNote
    {
        get
        {
            bool vanilla;
            switch (Schema.Type)
            {
                // The pack's own.
                case ParamType.PackVarRef:
                case ParamType.ListVarRef:
                case ParamType.BoolVarRef:
                    vanilla = false;
                    break;

                // A GC2 global, which is the game's. Its own doc said there was
                // no authoring-time enumeration of these; there is one now, and
                // this is where it earns the most - a free-text box with no
                // list behind it is exactly where a name goes wrong.
                case ParamType.GameVarRef:
                    vanilla = true;
                    break;

                default:
                    return "";
            }

            var kind = VariableKindChecker?.Invoke(Value ?? "", vanilla)
                       ?? Model.VariableKind.Unknown;
            return Model.VariableTypes.Label(kind);
        }
    }

    /// <summary>
    /// False when <see cref="ParamSchema.EnabledWhen"/> names a sibling param
    /// that doesn't currently hold <see cref="ParamSchema.EnabledWhenValue"/>.
    /// Bound to the editor's IsEnabled so a param that doesn't apply in the
    /// current mode is greyed out rather than silently ignored.
    /// <para/>
    /// Reads the shared params dict live, so <see cref="RefreshEnabled"/> is
    /// all a sibling's write needs to trigger to update this row.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            if (string.IsNullOrEmpty(Schema.EnabledWhen)) return true;
            _params.TryGetValue(Schema.EnabledWhen, out var gate);
            // Fall back to the controlling param's own default when it hasn't
            // been written yet, so an untouched row starts in the right state.
            if (string.IsNullOrEmpty(gate)) gate = DefaultOf(Schema.EnabledWhen);
            return string.Equals(gate, Schema.EnabledWhenValue,
                                 System.StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// False when <see cref="ParamSchema.ShownWhen"/> names a sibling param
    /// that doesn't hold one of <see cref="ParamSchema.ShownWhenValues"/>: the
    /// row is not drawn at all. Read live, like <see cref="IsEnabled"/>.
    /// </summary>
    public bool IsShown
    {
        get
        {
            if (string.IsNullOrEmpty(Schema.ShownWhen)) return true;
            _params.TryGetValue(Schema.ShownWhen, out var gate);
            if (string.IsNullOrEmpty(gate)) gate = DefaultOf(Schema.ShownWhen);
            foreach (string v in Schema.ShownWhenValues)
                if (string.Equals(gate, v, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>Default of a sibling param, looked up through the owner's
    /// schema list. Set by the parent when it builds the rows.</summary>
    internal System.Func<string, string> DefaultOf { get; set; } = _ => "";

    /// <summary>Re-evaluate <see cref="IsEnabled"/>. Called on sibling rows
    /// when any row in the same params dict is written.</summary>
    public void RefreshEnabled()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsShown));

        // The filter depends on a sibling too, and a picker still offering
        // stills after the author switched the target to a scene would be a
        // dead end they could only get out of by knowing about "All files".
        OnPropertyChanged(nameof(PickerFilter));
    }

    /// <summary>
    /// What the file dialog for this row offers.
    /// <para/>
    /// Animation is a Scenes-category feature, so a sprite aimed at a scene
    /// offers moving art and one aimed at anything else does not. That is the
    /// same condition the validator uses to reject an animated sprite pointed
    /// elsewhere, read from the same sibling param — so the picker cannot
    /// offer a file the validator will refuse.
    /// </summary>
    public string PickerFilter
    {
        get
        {
            if (Schema.Type != ParamType.SpriteRef) return View.PickerFilters.StillArt;

            _params.TryGetValue("kind", out var target);
            return string.Equals(target, "Scene", System.StringComparison.OrdinalIgnoreCase)
                ? View.PickerFilters.SceneArt
                : View.PickerFilters.StillArt;
        }
    }

    /// <summary>
    /// The current value as a string. Missing keys fall back to
    /// <see cref="ParamSchema.DefaultValue"/> so a fresh action with an
    /// empty params dict still shows sensible placeholder text.
    /// <para/>
    /// Empty writes remove the key from the dict; this keeps the JSON
    /// output trim instead of accumulating every empty key the user
    /// ever clicked into.
    /// </summary>
    public string Value
    {
        get => _params.TryGetValue(Schema.Key, out var v) ? v : (Schema.DefaultValue ?? "");
        set
        {
            string newValue = value ?? "";
            // A percentage is a whole number in [0,100]: reject anything else
            // rather than storing it. That keeps the field from holding "0.3"
            // (which reads as 0.3% at runtime — the exact confusion the %
            // suffix exists to prevent) or an out-of-range 101+. Rejecting on
            // the way in means intermediate typing still works: "1" → "10" →
            // "100" are all valid, only the "101" keystroke bounces. The
            // snap-back to the last good value is the OnPropertyChanged here.
            if (Schema.Type == ParamType.Percent && newValue.Length > 0 &&
                (!System.Text.RegularExpressions.Regex.IsMatch(newValue, @"^\d{1,3}$") ||
                 int.Parse(newValue, System.Globalization.CultureInfo.InvariantCulture) > 100))
            { OnPropertyChanged(); return; }

            // Don't write the default back into the dict — empty keys are
            // implicit, and clearing back to the default should round-trip
            // identically to "never set in the first place". The exception is a
            // param where empty is itself a value (clearing a variable): there
            // the key is kept holding "", so "deliberately cleared" stays
            // distinguishable from "never filled in".
            if (string.IsNullOrEmpty(newValue) && !Schema.EmptyIsAValue)
                _params.Remove(Schema.Key);
            else
                _params[Schema.Key] = newValue;

            OnPropertyChanged();
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(DoubleValue));
            _onValueChanged?.Invoke();
        }
    }

    /// <summary>
    /// Bool-typed view of <see cref="Value"/>. Writes round-trip through
    /// the underlying dict as the literal strings "true" / "false". Used
    /// by the <see cref="ParamType.Bool"/> template's CheckBox.
    /// </summary>
    public bool BoolValue
    {
        get
        {
            var raw = Value;
            return bool.TryParse(raw, out var b) && b;
        }
        set => Value = value ? "true" : "false";
    }

    /// <summary>
    /// Double-typed view of <see cref="Value"/>. The template for
    /// <see cref="ParamType.Int"/> / <see cref="ParamType.Float"/>
    /// currently binds <see cref="Value"/> as a string (so invalid input
    /// doesn't crash the editor); this helper is kept for any future
    /// numeric-stepper UI and for the rare programmatic numeric read.
    /// </summary>
    public double DoubleValue
    {
        get => double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        set => Value = value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Re-evaluate whether this row's value references a boolean variable.
    /// Call this when the value changes and the row type is PackVarRef/BoolVarRef.</summary>
    public void RefreshBooleanDetection()
    {
        if (Schema.Type != ParamType.PackVarRef && Schema.Type != ParamType.BoolVarRef)
        {
            if (IsBooleanVariable) { IsBooleanVariable = false; OnPropertyChanged(); }

            // The boolean answer is only asked on those two, but the NOTE is
            // shown on four - so it is raised before this returns rather than
            // after, or a List row's note never changes.
            OnPropertyChanged(nameof(VarKindNote));
            return;
        }
        var varName = Value;
        bool isBool = IsBooleanVarChecker?.Invoke(varName) ?? false;
        if (IsBooleanVariable != isBool)
        {
            IsBooleanVariable = isBool;
            OnPropertyChanged();
        }

        // Raised whether or not the boolean answer moved: "score" to "note" is
        // not-a-bool either way round, and the note still has to change.
        OnPropertyChanged(nameof(VarKindNote));
    }
}
