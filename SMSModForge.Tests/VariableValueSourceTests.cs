using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The store picker beside a variable's value earns its place on screen.
/// <para/>
/// It used to be called "Compare to" and sat under Value on every variable
/// check, which read as a second operand — authors came away believing a
/// variable check always compares one variable against another. It is not an
/// operand: it picks which store a <c>$name</c> in the value is looked up in,
/// and a plain value ignores it entirely.
/// <para/>
/// So it is named the same thing the Set-variable editor already named it, and
/// it is only there once there is a <c>$name</c> to look up.
/// </summary>
public sealed class VariableValueSourceTests
{
    private readonly ITestOutputHelper _out;
    public VariableValueSourceTests(ITestOutputHelper o) => _out = o;

    private static NodeConditionViewModel Check(string value)
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableCompare };
        var vm = new NodeConditionViewModel(def);
        vm.VarName = "flag";
        vm.VarValue = value;
        return vm;
    }

    [Fact]
    public void AnOrdinaryCheckAgainstALiteralNeverMentionsAStore()
    {
        // The whole complaint, in one assertion: nothing on screen suggests a
        // second thing to compare against.
        foreach (string literal in new[] { "3", "true", "hello", "", "100%" })
        {
            var check = Check(literal);
            _out.WriteLine($"'{literal}' -> read-from row: {check.ValueNamesAVariable}");
            Assert.False(check.ValueNamesAVariable);
        }
    }

    [Fact]
    public void ComparingAgainstAnotherVariableAsksWhereToReadIt()
    {
        // ...and the moment it means something, it is there. This is the case
        // it was added for: the game's own steps compare two of its globals.
        var check = Check("$MCorruption");
        Assert.True(check.ValueNamesAVariable);

        check.VarValueSource = "Vanilla";
        Assert.Equal("Vanilla", check.VarValueSource);
        Assert.True(check.IsVanillaValueSource);
    }

    [Fact]
    public void TheOtherSpellingCountsToo()
    {
        // ${name} is the other way to write one, and a value can carry one
        // mid-text rather than only at the front.
        Assert.True(Check("${MCorruption}").ValueNamesAVariable);
        Assert.True(Check("over $threshold now").ValueNamesAVariable);
    }

    [Fact]
    public void TypingOneMakesTheRowAppearWithoutAnythingElseHappening()
    {
        // The row is driven off the value, so it has to be told when the value
        // changes - a property nothing raises is a row that never appears.
        var check = Check("3");
        int told = 0;
        check.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(NodeConditionViewModel.ValueNamesAVariable)) told++; };

        check.VarValue = "$other";

        Assert.True(check.ValueNamesAVariable);
        Assert.True(told > 0, "the value changed and nothing said the row should appear");
    }

    [Fact]
    public void TheSetVariableActionAsksTheSameQuestionTheSameWay()
    {
        // Two editors, one rule. The action already called it "Read from"; the
        // condition called the same control "Compare to", which is how one of
        // them ended up teaching people the wrong thing.
        var def = new NodeActionDef { Type = "SetVariable" };
        var vm = new NodeActionViewModel(def);

        vm.VarValue = "7";
        Assert.False(vm.ValueNamesAVariable);

        vm.VarValue = "$score";
        Assert.True(vm.ValueNamesAVariable);
    }

    [Fact]
    public void TheActionsFallbackCountsAsWell()
    {
        // It takes a $name on the same terms and is read from the same store,
        // so a fallback alone is reason enough to ask.
        var def = new NodeActionDef { Type = "PickRandomFromList" };
        var vm = new NodeActionViewModel(def);

        vm.VarValue = "";
        vm.VarFallback = "$whenEmpty";

        _out.WriteLine($"fallback '{vm.VarFallback}' -> read-from row: {vm.ValueNamesAVariable}");
        Assert.True(vm.ValueNamesAVariable);
    }

    [Fact]
    public void TheRowsThatShowAndHideAreBoundToSomethingThatExists()
    {
        // A binding to a property that is not there fails SILENTLY in WPF - the
        // control simply never appears - and a row whose whole job is to appear
        // only sometimes is indistinguishable from one that is broken.
        //
        // Nothing else in the suite realises the Dialogues tab's condition and
        // action editors, so a typo in these bindings would have gone unseen
        // until an author went looking for a store picker that never came back.
        using var watch = new BindingWatch();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (System.Windows.Controls.TabControl)window.FindName("MainTabs");
            for (int i = 0; i < tabs.Items.Count; i++)
                if (tabs.Items[i] is System.Windows.Controls.TabItem t
                    && (t.Header as string) == "Dialogues")
                { tabs.SelectedIndex = i; break; }
            WindowHarness.Pump();

            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();
            var dialogue = vm.SelectedDialogue;
            Assert.NotNull(dialogue);

            vm.AddNodeConditionCommand.Execute(null);
            vm.AddNodeActionOnStartCommand.Execute(null);
            WindowHarness.Pump();

            // Both states of the row, so both halves of the trigger are
            // exercised: nothing to look up, then something.
            foreach (var condition in dialogue!.Nodes.SelectMany(n => n.Conditions))
            {
                condition.VarName = "flag";
                condition.VarValue = "3";
                WindowHarness.Pump();
                condition.VarValue = "$other";
                WindowHarness.Pump();
            }
            foreach (var action in dialogue.Nodes.SelectMany(n => n.ActionsOnStart))
            {
                action.VarValue = "3";
                WindowHarness.Pump();
                action.VarValue = "$other";
                WindowHarness.Pump();
            }

            foreach (string complaint in watch.Complaints.Distinct().Take(6))
                _out.WriteLine(complaint);

            Assert.True(watch.Complaints.Count == 0,
                        $"{watch.Complaints.Count} broken binding(s); first: "
                        + (watch.Complaints.FirstOrDefault() ?? ""));
        });
    }

    /// <summary>Collects whatever WPF says about bindings, installed before the
    /// window: WPF decides whether to trace when a binding is created.</summary>
    private sealed class BindingWatch : System.Diagnostics.TraceListener, System.IDisposable
    {
        public System.Collections.Generic.List<string> Complaints { get; } = new();
        private readonly System.Diagnostics.SourceLevels _was;

        public BindingWatch()
        {
            System.Diagnostics.PresentationTraceSources.Refresh();
            _was = System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level;
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level =
                System.Diagnostics.SourceLevels.Warning;
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(this);
        }

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (string.IsNullOrEmpty(message)) return;

            // WPF's own noise, not ours. The stock TreeViewItem style binds its
            // content alignment to an ancestor ItemsControl that does not exist
            // while a container is being generated, and says so every time. It
            // is excluded by the two property names rather than by the word
            // "TreeViewItem", so a real broken binding on one still counts.
            if (message.Contains("Path=HorizontalContentAlignment", System.StringComparison.Ordinal)
                || message.Contains("Path=VerticalContentAlignment", System.StringComparison.Ordinal))
                return;

            if (message.Contains("path error", System.StringComparison.OrdinalIgnoreCase)
                || message.Contains("Cannot find", System.StringComparison.OrdinalIgnoreCase))
                Complaints.Add(message);
        }

        protected override void Dispose(bool disposing)
        {
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = _was;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void WhatIsStoredDoesNotChangeAtAll()
    {
        // None of this is a change to the manifest - it is the same param it
        // always was, shown when it means something. A pack written before must
        // read back identically.
        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableCompare };
        def.Params["name"] = "flag";
        def.Params["value"] = "$other";
        def.Params["valueSource"] = "vanilla";

        var vm = new NodeConditionViewModel(def);
        Assert.Equal("Vanilla", vm.VarValueSource);
        Assert.True(vm.ValueNamesAVariable);

        // ...and a plain one still writes nothing, rather than writing "pack".
        vm.VarValueSource = "Pack";
        Assert.DoesNotContain("valueSource", def.Params.Keys);
    }
}
