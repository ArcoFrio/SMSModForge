using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A Bool variable's default value, picked with two buttons rather than one
/// tick.
/// <para/>
/// A tick box says "starts true" and says nothing at all about the other case:
/// unticked reads as "not set" just as easily as "starts false", and a default
/// is the one thing an author wants stated rather than implied. The Variable
/// condition and the Set-variable action already ask it this way, so the answer
/// now looks the same everywhere it is given.
/// <para/>
/// Measured on the buttons the window draws: two radios bound to one property
/// with one of them inverted only stay honest while every write raises
/// PropertyChanged for what both are bound to, and a view model test cannot
/// see that.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class VariableDefaultRadioTests
{
    private readonly ITestOutputHelper _out;
    public VariableDefaultRadioTests(ITestOutputHelper o) => _out = o;

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

    private static void ShowTab(MainWindow window, string header)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == header) { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();
    }

    /// <summary>The pair, found by what they say rather than by name.</summary>
    private static (RadioButton True, RadioButton False) Pair(MainWindow window)
    {
        var radios = Descendants<RadioButton>(window).Where(r => r.IsVisible).ToList();
        var yes = radios.FirstOrDefault(r => (r.Content as string) == "Starts true");
        var no = radios.FirstOrDefault(r => (r.Content as string) == "Starts false");
        Assert.True(yes != null && no != null, "the Starts true / Starts false pair is not on screen");
        return (yes!, no!);
    }

    private static PackVariableViewModel ABoolVariable(MainViewModel vm)
    {
        vm.AddVariableCommand.Execute(null);
        var v = vm.Variables.Last();
        v.Type = PackVariableType.Bool;
        return v;
    }

    [Fact]
    public void ABoolVariableIsAskedWithTwoButtons_AndTheyShowWhatItHolds()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowTab(window, "Variables");
            var v = ABoolVariable(vm);
            vm.SelectedVariable = v;
            WindowHarness.Pump();

            var (yes, no) = Pair(window);
            _out.WriteLine($"default '{v.DefaultValue}': true={yes.IsChecked} false={no.IsChecked}");

            // A new Bool starts false, and the pair says so rather than
            // showing two empty circles.
            Assert.False(v.DefaultBool);
            Assert.True(no.IsChecked == true, "neither button is picked for a variable that starts false");
            Assert.False(yes.IsChecked == true);
        });
    }

    [Fact]
    public void PickingOneWritesTheDefault_AndTheOtherLetsGo()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowTab(window, "Variables");
            var v = ABoolVariable(vm);
            vm.SelectedVariable = v;
            WindowHarness.Pump();

            var (yes, no) = Pair(window);

            yes.IsChecked = true;          // what a click does
            WindowHarness.Pump();

            _out.WriteLine($"after picking true: model '{v.Model.DefaultValue}', false button {no.IsChecked}");
            Assert.Equal("true", v.Model.DefaultValue);
            Assert.False(no.IsChecked == true, "the other button stayed picked");

            // ...and back, on the first click rather than the second.
            no.IsChecked = true;
            WindowHarness.Pump();

            Assert.Equal("false", v.Model.DefaultValue);
            Assert.False(yes.IsChecked == true);
        });
    }

    [Fact]
    public void ChangingTheValueElsewhereMovesTheButtons()
    {
        // The half a checkbox could never get wrong and a bound pair can: the
        // model changing under them, from an undo or from loading a pack.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowTab(window, "Variables");
            var v = ABoolVariable(vm);
            vm.SelectedVariable = v;
            WindowHarness.Pump();

            v.DefaultValue = "true";
            WindowHarness.Pump();

            var (yes, no) = Pair(window);
            _out.WriteLine($"written as text: true={yes.IsChecked} false={no.IsChecked}");
            Assert.True(yes.IsChecked == true, "the buttons did not follow the value");
            Assert.False(no.IsChecked == true);
        });
    }

    [Fact]
    public void OnlyABoolIsAskedThisWay()
    {
        // The control: a String variable has a text box there, and asking it
        // with two buttons would be wrong rather than merely unhelpful.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowTab(window, "Variables");
            var v = ABoolVariable(vm);
            vm.SelectedVariable = v;
            WindowHarness.Pump();
            Pair(window);                  // there while it is a Bool...

            v.Type = PackVariableType.String;
            WindowHarness.Pump();

            var radios = Descendants<RadioButton>(window)
                         .Where(r => r.IsVisible && (r.Content as string ?? "").StartsWith("Starts "))
                         .ToList();
            _out.WriteLine($"as a String: {radios.Count} button(s)");
            Assert.Empty(radios);          // ...and gone once it is not
        });
    }
}
