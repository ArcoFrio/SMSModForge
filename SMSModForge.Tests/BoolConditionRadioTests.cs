using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The True/False pair on a condition that compares a yes/no variable.
/// <para/>
/// Two radios in one group, both bound to the same property with one of them
/// inverted, is a shape WPF only keeps honest while every write raises
/// <c>PropertyChanged</c> for what BOTH are bound to. Miss one and the pair
/// goes on showing the value it had while the model holds another — and then
/// clicking the radio that looks unchecked writes the value that is already
/// there, so the click does nothing and nothing says why.
/// <para/>
/// Driven through the real window rather than the view model, because the view
/// model on its own cannot show this: the drift is between two bindings, and
/// both of them have to be live to see it.
/// </summary>
public sealed class BoolConditionRadioTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly Func<string, bool, VariableKind>? _was = NodeConditionViewModel.VariableKindLookup;

    public BoolConditionRadioTests(ITestOutputHelper o)
    {
        _out = o;
        // Every name in these tests is a yes/no variable, which is what puts
        // the radios on screen in the first place.
        NodeConditionViewModel.VariableKindLookup = (_, _) => VariableKind.YesNo;
    }

    public void Dispose() => NodeConditionViewModel.VariableKindLookup = _was;

    private static NodeConditionViewModel BoolCondition(string value)
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableCompare };
        def.Params["name"] = "Flag";
        def.Params["value"] = value;
        return new NodeConditionViewModel(def, _ => { });
    }

    /// <summary>
    /// The view model half: writing the value through the text box path must
    /// tell the radios, or they keep showing the old answer.
    /// </summary>
    [Fact]
    public void WritingTheValueAsTextTellsTheRadios()
    {
        var row = BoolCondition("true");
        Assert.True(row.VarValueIsBool);
        Assert.True(row.VarValueBool);

        var raised = new List<string>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        row.VarValue = "false";

        _out.WriteLine("raised: " + string.Join(", ", raised));
        Assert.False(row.VarValueBool);
        Assert.Contains(nameof(row.VarValueBool), raised);
    }

    /// <summary>
    /// And the control: writing through the radios' own property still says so,
    /// so a fix that only covered the text box would not pass this.
    /// </summary>
    [Fact]
    public void WritingTheValueAsABoolAlsoSaysSo()
    {
        var row = BoolCondition("true");
        var raised = new List<string>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        row.VarValueBool = false;

        Assert.Equal("false", row.VarValue);
        Assert.Contains(nameof(row.VarValueBool), raised);
        Assert.Contains(nameof(row.VarValue), raised);
    }

    /// <summary>
    /// The root cause, pinned on its own: the converter the False radio binds
    /// through has to invert in BOTH directions. Returning the value unchanged
    /// on the way back is what made clicking False write true — and then the
    /// outgoing radio's own write, which happens first, was overwritten by it.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TheInverseConverterInvertsBothWays(bool given, bool expected)
    {
        var c = SMSModForge.View.Converters.InverseBoolConverter.Instance;
        Assert.Equal(expected, c.Convert(given, typeof(bool), null!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(expected, c.ConvertBack(given, typeof(bool), null!, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>An indeterminate box says nothing rather than saying false.</summary>
    [Fact]
    public void NoAnswerWritesNothing()
    {
        var c = SMSModForge.View.Converters.InverseBoolConverter.Instance;
        Assert.Same(System.Windows.Data.Binding.DoNothing,
                    c.ConvertBack(null!, typeof(bool), null!, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The Set-variable action carries the same pair, written the same
    /// way, and had the same hole.</summary>
    [Fact]
    public void TheActionSidesRadiosHearItToo()
    {
        var was = NodeActionViewModel.VariableKindLookup;
        try
        {
            NodeActionViewModel.VariableKindLookup = (_, _) => VariableKind.YesNo;

            var def = new NodeActionDef { Type = NodeActionTypes.SetVariable };
            def.Params["name"] = "Flag";
            def.Params["value"] = "true";
            var row = new NodeActionViewModel(def, _ => { });
            Assert.True(row.VarValueBool);

            var raised = new List<string>();
            row.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
            row.VarValue = "false";

            _out.WriteLine("raised: " + string.Join(", ", raised));
            Assert.False(row.VarValueBool);
            Assert.Contains(nameof(row.VarValueBool), raised);
        }
        finally { NodeActionViewModel.VariableKindLookup = was; }
    }

    /// <summary>
    /// The shape the author hit: a copied condition, pasted, then switched.
    /// The paste builds a new row over a cloned definition, so this is also
    /// the check that the clone carries the value at all.
    /// </summary>
    [Fact]
    public void APastedConditionSwitchesOnTheFirstClick()
    {
        var original = BoolCondition("true");
        var clone = SMSModForge.Services.EditorClipboard.Clone(new[] { original.Model })[0];
        var pasted = new NodeConditionViewModel(clone, _ => { });

        Assert.True(pasted.VarValueIsBool);
        Assert.True(pasted.VarValueBool);

        // What the False radio does: write through the inverted binding.
        var raised = new List<string>();
        pasted.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        pasted.VarValueBool = false;

        Assert.Equal("false", clone.Params["value"]);
        Assert.False(pasted.VarValueBool);
        // ...and the True radio is told, so it stops showing itself as picked.
        Assert.Contains(nameof(pasted.VarValueBool), raised);
    }
}
