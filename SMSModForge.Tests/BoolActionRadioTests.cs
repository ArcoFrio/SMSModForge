using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Variable action sets a yes/no with the same True / False pair the
/// Variable condition checks it with.
/// <para/>
/// It was one tick labelled "set to true", and an unticked "set to true" reads
/// as "not set" as easily as "set to false" - while the condition beside it,
/// about the same variable, asked with two buttons (2026-09-27).
/// <para/>
/// Measured on what the window draws: two radios bound to one property with
/// one of them inverted only stay honest while every write reaches both, and
/// a view model test cannot see that.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class BoolActionRadioTests
{
    private readonly ITestOutputHelper _out;
    public BoolActionRadioTests(ITestOutputHelper o) => _out = o;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    /// <summary>A Set action on a node's finish, naming a yes/no variable of
    /// the pack's, on screen.</summary>
    private static (MainViewModel Vm, NodeActionViewModel Action) ASetOfAYesNo(MainWindow window)
    {
        var vm = (MainViewModel)window.DataContext;
        vm.AddVariableCommand.Execute(null);
        var variable = vm.Variables.Last();
        variable.Name = "metRiver";
        variable.Type = PackVariableType.Bool;

        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues") { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();

        vm.AddDialogueCommand.Execute(null);
        WindowHarness.Pump();
        if (vm.SelectedDialogue!.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
        vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
        WindowHarness.Pump();

        var action = vm.SelectedNode!.AddActionOnFinish();
        action.VarName = "metRiver";
        WindowHarness.Pump();
        Assert.True(action.VarValueIsBool, "the variable is not read as a yes/no, so this proves nothing");
        return (vm, action);
    }

    /// <summary>What the row draws for <paramref name="action"/>.</summary>
    private static List<T> InRow<T>(MainWindow window, NodeActionViewModel action) where T : FrameworkElement
        => Descendants<T>(window).Where(e => e.IsVisible && ReferenceEquals(e.DataContext, action)).ToList();

    private static (RadioButton True, RadioButton False) Pair(MainWindow window, NodeActionViewModel action)
    {
        var radios = InRow<RadioButton>(window, action);
        var yes = radios.FirstOrDefault(r => (r.Content as string) == Loc.T("condition.true"));
        var no = radios.FirstOrDefault(r => (r.Content as string) == Loc.T("condition.false"));
        Assert.True(yes != null && no != null,
            "no True / False pair on the action row; it draws: "
            + string.Join(", ", radios.Select(r => r.Content)));
        return (yes!, no!);
    }

    [Fact]
    public void AYesNoIsSetWithTwoButtons_NotATick()
    {
        WindowHarness.Run(window =>
        {
            var (_, action) = ASetOfAYesNo(window);
            var (yes, no) = Pair(window, action);
            _out.WriteLine($"value '{action.VarValue}': true={yes.IsChecked} false={no.IsChecked}");

            var ticks = InRow<CheckBox>(window, action)
                .Where(c => BindingOperations.GetBinding(c, ToggleButton.IsCheckedProperty)?.Path?.Path == nameof(NodeActionViewModel.VarValueBool))
                .ToList();
            Assert.Empty(ticks);

            // Exactly one of the two is picked: the value as the game reads it.
            Assert.NotEqual(yes.IsChecked == true, no.IsChecked == true);
            Assert.Equal(action.VarValueBool, yes.IsChecked == true);
        });
    }

    [Fact]
    public void PickingOneWritesTheValue_AndTheOtherLetsGo()
    {
        WindowHarness.Run(window =>
        {
            var (_, action) = ASetOfAYesNo(window);
            var (yes, no) = Pair(window, action);

            yes.IsChecked = true;          // what a click does
            WindowHarness.Pump();
            _out.WriteLine($"after True: '{action.Model.Params["value"]}', False picked {no.IsChecked}");
            Assert.Equal("true", action.Model.Params["value"]);
            Assert.False(no.IsChecked == true, "False stayed picked");

            // ...and back, on the first click.
            no.IsChecked = true;
            WindowHarness.Pump();
            Assert.Equal("false", action.Model.Params["value"]);
            Assert.False(yes.IsChecked == true, "True stayed picked");
        });
    }

    [Fact]
    public void ChangingTheValueElsewhereMovesTheButtons()
    {
        // An undo, a paste, a pack opened: the value changes under the pair.
        WindowHarness.Run(window =>
        {
            var (_, action) = ASetOfAYesNo(window);

            action.VarValue = "true";
            WindowHarness.Pump();
            var (yes, no) = Pair(window, action);
            Assert.True(yes.IsChecked == true, "True is not picked for a value of true");
            Assert.False(no.IsChecked == true);

            action.VarValue = "false";
            WindowHarness.Pump();
            Assert.True(no.IsChecked == true, "False is not picked for a value of false");
            Assert.False(yes.IsChecked == true);
        });
    }

    /// <summary>The action's buttons say what the condition's say, in every
    /// language: they are the same two answers.</summary>
    [Fact]
    public void TheWordsAreTheConditionsInEveryLanguage()
    {
        foreach (var language in Loc.Available())
        {
            var texts = Loc.Preview(language);
            Assert.Equal(texts.T("condition.true"), texts.T("params.true"));
            Assert.Equal(texts.T("condition.false"), texts.T("params.false"));
        }
    }
}
