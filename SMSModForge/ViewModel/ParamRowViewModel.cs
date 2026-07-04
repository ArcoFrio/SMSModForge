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
    /// <inheritdoc cref="ParamSchema.Tooltip"/>
    public string Tooltip => Schema.Tooltip;

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
            // Don't write the default back into the dict — empty keys are
            // implicit, and clearing back to the default should round-trip
            // identically to "never set in the first place".
            if (string.IsNullOrEmpty(newValue))
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
