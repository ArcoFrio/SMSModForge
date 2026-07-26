using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>Default of a sibling param, looked up through the owner's
    /// schema list. Set by the parent when it builds the rows.</summary>
    internal System.Func<string, string> DefaultOf { get; set; } = _ => "";

    /// <summary>Re-evaluate <see cref="IsEnabled"/>. Called on sibling rows
    /// when any row in the same params dict is written.</summary>
    public void RefreshEnabled() => OnPropertyChanged(nameof(IsEnabled));

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
}
