using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The Variables tab says where a variable is used - what sets it, what reads
/// it, what shows it in its words - and goes there on a click (the author,
/// 1.8.0: "that is a great idea").
/// </summary>
public sealed class VariableUsesTests
{
    private static NodeActionDef Action(string type, params (string Key, string Value)[] ps)
    {
        var a = new NodeActionDef { Type = type };
        foreach (var (k, v) in ps) a.Params[k] = v;
        return a;
    }

    private static NodeConditionDef Condition(string name, string source = "")
    {
        var c = new NodeConditionDef { Type = NodeConditionTypes.VariableCompare };
        c.Params["name"] = name;
        if (source.Length > 0) c.Params["source"] = source;
        return c;
    }

    internal static ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("uses.pack");
        pack.Variables.Add(new PackVariableDef { Name = "gold", Type = PackVariableType.Int });
        pack.Variables.Add(new PackVariableDef { Name = "loot", Type = PackVariableType.List });
        var d = new DialogueDef { Key = "shop", DisplayName = "Shop" };
        var pay = new DialogueNodeDef { Id = 1, Text = "You have [PV:gold] coins." };
        pay.ActionsOnFinish.Add(Action(NodeActionTypes.SetVariable, ("name", "gold"), ("value", "0")));
        d.Nodes.Add(pay);
        d.RootNodeIds.Add(1);
        d.StartConditions.Add(Condition("gold"));
        pack.Dialogues.Add(d);

        var rule = new UpdateRuleDef { Key = "tally" };
        rule.Actions.Add(Action(NodeActionTypes.CountList, ("fromList", "loot"), ("name", "gold")));
        // The game's own variable of the same name is another variable.
        rule.Conditions.Add(Condition("gold", source: "vanilla"));
        pack.IntegrationRules.Add(rule);
        return pack;
    }

    [Fact]
    public void EachPlaceSaysHowItUsesTheVariable()
    {
        var uses = VariableRenamer.Uses(Pack(), "gold");
        var said = uses.Select(u => (u.Kind, u.Path)).ToList();

        Assert.Equal(new (VariableRenamer.UseKind, string)[]
        {
            (VariableRenamer.UseKind.Sets, "dialogues[shop].nodes[id=1]"),
            (VariableRenamer.UseKind.Sets, "integrationRules[tally]"),
            (VariableRenamer.UseKind.Reads, "dialogues[shop]"),
            (VariableRenamer.UseKind.Shows, "dialogues[shop].nodes[id=1]"),
        }, said);
    }

    [Fact]
    public void AListCountedIsRead_AndTheGamesVariableOfTheSameNameIsNotThisOne()
    {
        var loot = VariableRenamer.Uses(Pack(), "loot");
        Assert.Equal(VariableRenamer.UseKind.Reads, Assert.Single(loot).Kind);

        // The rule's vanilla condition names "gold" too, and is not listed.
        Assert.DoesNotContain(VariableRenamer.Uses(Pack(), "gold"),
            u => u.Kind == VariableRenamer.UseKind.Reads && u.Path == "integrationRules[tally]");
    }

    /// <summary>The control: a variable nothing names is used nowhere.</summary>
    [Fact]
    public void AVariableNothingNamesIsUsedNowhere()
        => Assert.Empty(VariableRenamer.Uses(Pack(), "silver"));
}

/// <summary>The panel itself, in a real window.</summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class VariableUsesPanelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-uses-" + Guid.NewGuid().ToString("N"));

    public VariableUsesPanelTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    [Fact]
    public void ThePanelListsTheUses_AndAClickGoesThere()
    {
        WindowHarness.Run(window =>
        {
            PackRepository.Save(VariableUsesTests.Pack(), _dir);
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = MainTab.Variables;
            vm.SelectedVariable = vm.Variables.Single(v => v.Name == "gold");
            WindowHarness.Pump();
            window.UpdateLayout();

            var box = (GroupBox)window.FindName("VariableUsesBox");
            Assert.True(box.IsVisible, "the uses panel is not on screen");
            Assert.Equal(4, vm.SelectedVariableUses.Count);
            Assert.Contains("4", vm.SelectedVariableUsesHeading);

            var list = (ItemsControl)window.FindName("VariableUsesList");
            var links = Descendants<TextBlock>(list).SelectMany(t => t.Inlines.OfType<Hyperlink>()).ToList();
            Assert.Equal(4, links.Count);

            // The line that shows it: to the shop's node.
            var shows = links.Single(l => ((VariableUseRow)l.DataContext).Use.Kind == VariableRenamer.UseKind.Shows);
            shows.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, shows));
            WindowHarness.Pump();
            WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
            Assert.Equal(MainTab.Dialogues, vm.SelectedTabIndex);
            Assert.Equal("shop", vm.SelectedDialogue?.Key);
            Assert.Equal(1, vm.SelectedNode?.Id);
        });
    }

    [Fact]
    public void TheListIsReadAgainWhenTheTabIsOpened()
    {
        WindowHarness.Run(window =>
        {
            PackRepository.Save(VariableUsesTests.Pack(), _dir);
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = MainTab.Variables;
            vm.SelectedVariable = vm.Variables.Single(v => v.Name == "loot");
            WindowHarness.Pump();
            Assert.Single(vm.SelectedVariableUses);

            // Used somewhere new while the author was on another tab.
            vm.SelectedTabIndex = MainTab.Dialogues;
            vm.Pack.Dialogues.Single().Nodes.Single().ActionsOnStart.Add(
                new NodeActionDef { Type = NodeActionTypes.ClearList, Params = { ["list"] = "loot" } });
            vm.SelectedTabIndex = MainTab.Variables;
            WindowHarness.Pump();
            Assert.Equal(2, vm.SelectedVariableUses.Count);
        });
    }
}
