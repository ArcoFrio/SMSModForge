using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Validation;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A screen's "Opens when" (1.8.0, the author: "having no conditions should
/// never mean that it's always open, but having all conditions met should mean
/// that it opens"). The list was in the pack format from the start and nothing
/// offered or read it. Here: the editor offers it on a screen of the pack's
/// own and not on a change to one of the game's, what is added reaches the
/// saved pack, and the validator checks it as the rule the game runs it as.
/// That the game opens the screen is InGameSuiteTests' F3.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UiOpenConditionsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-opens-" + Guid.NewGuid().ToString("N"));
    private readonly string _saved = Path.Combine(Path.GetTempPath(), "smsmodforge-opens-saved-" + Guid.NewGuid().ToString("N"));

    public UiOpenConditionsTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_saved);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
        try { Directory.Delete(_saved, true); } catch (IOException) { }
    }

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

    private static void Click(Button button)
        => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    /// <summary>The "Opens when" sections the window is showing.</summary>
    private static List<Expander> OpensWhen(MainWindow window)
    {
        window.UpdateLayout();
        WindowHarness.Pump();
        return Descendants<Expander>(window)
               .Where(e => e.IsVisible && (e.Header as string) == Loc.T("ui.opensWhen"))
               .ToList();
    }

    private MainViewModel OnTheUiTab(MainWindow window)
    {
        var pack = PackRepository.CreateEmpty("opens.pack");
        pack.Variables.Add(new PackVariableDef { Name = "shopOpen", Type = PackVariableType.Bool, DefaultValue = "false" });
        PackRepository.Save(pack, _dir);

        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Ui;
        WindowHarness.Pump();
        return vm;
    }

    [Fact]
    public void A_screen_of_the_packs_own_offers_it_and_what_is_added_is_saved()
    {
        WindowHarness.Run(window =>
        {
            var vm = OnTheUiTab(window);
            vm.AddOwnUiCommand.Execute(null);
            var ui = vm.SelectedUi!;
            WindowHarness.Pump();

            // Offered, and closed: nothing opens this screen by itself yet.
            var opens = Assert.Single(OpensWhen(window));
            Assert.False(opens.IsExpanded);
            Assert.False(ui.OpensByConditions);
            Assert.Empty(ui.Model.OpenConditions);

            opens.IsExpanded = true;
            window.UpdateLayout();
            WindowHarness.Pump();

            // Its "+ Condition" is the screen's own, as drawn.
            var add = Assert.Single(Descendants<Button>(opens).Where(b => b.IsVisible
                && ReferenceEquals(b.Command, ui.AddOpenConditionCommand)));
            Assert.Equal(Loc.T("ui.addActiveCondition"), add.Content as string);
            Click(add);
            WindowHarness.Pump();

            var row = Assert.Single(ui.OpenConditions);
            Assert.True(ui.OpensByConditions);
            Assert.Same(row.Model, Assert.Single(ui.Model.OpenConditions));
            row.Model.Params["name"] = "shopOpen";
            row.Model.Params["value"] = "true";

            // The help is drawn under the conditions.
            Assert.Contains(Descendants<TextBlock>(opens),
                t => t.IsVisible && t.Text == Loc.T("ui.opensWhen.help"));

            // Through the real save, the form the game reads.
            PackRepository.Save(vm.Pack, _saved);
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(_saved, "modpack.json")));
            var saved = (JObject)((JArray)manifest["uis"]!).Single(u => (string?)u["id"] == ui.Model.Id);
            _out.WriteLine(saved["openConditions"]!.ToString());
            var condition = (JObject)((JArray)saved["openConditions"]!).Single();
            Assert.Equal(NodeConditionTypes.VariableCompare, (string?)condition["type"]);   // the one variable check, as saved
            Assert.Equal("shopOpen", (string?)condition["params"]!["name"]);
            Assert.Equal("true", (string?)condition["params"]!["value"]);

            // And taking it out takes it out of the pack.
            row.RemoveCommand.Execute(null);
            Assert.Empty(ui.Model.OpenConditions);
            Assert.False(ui.OpensByConditions);
        });
    }

    [Fact]
    public void A_change_to_one_of_the_games_screens_does_not_offer_it()
    {
        // The game's own screens open when the game opens them; a pack's
        // change to one has no id of its own to open it by. Control: the same
        // window shows it for a screen of the pack's own.
        WindowHarness.Run(window =>
        {
            var vm = OnTheUiTab(window);
            vm.AddOwnUiCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Single(OpensWhen(window));

            vm.AddVanillaUiCommand.Execute(null);
            WindowHarness.Pump();
            Assert.True(vm.SelectedUi!.ShowsScreenPicker);
            Assert.Empty(OpensWhen(window));
        });
    }

    [Fact]
    public void The_validator_checks_them_as_a_rule()
    {
        var pack = PackRepository.CreateEmpty("opens.pack");
        pack.Variables.Add(new PackVariableDef { Name = "shopOpen", Type = PackVariableType.Bool, DefaultValue = "false" });
        var ui = new UiDef { Id = "shop", Name = "Shop" };
        ui.Nodes.Add(new UiNodeDef { Name = "Panel" });
        pack.Uis.Add(ui);

        // "Is it set at all": the variable check the validator follows a name
        // through (see the note in PLAN 12.17 on the unified one).
        static NodeConditionDef Exists(string name)
        {
            var c = new NodeConditionDef { Type = NodeConditionTypes.VariableExists };
            c.Params["name"] = name;
            return c;
        }
        ui.OpenConditions.Add(Exists("shopOpen"));
        Assert.DoesNotContain(PackValidator.Validate(pack, _dir), i => i.Where.StartsWith("uis[shop].openConditions"));

        // A variable the pack does not have: named, at the condition.
        ui.OpenConditions.Add(Exists("nobodyDeclaredThis"));
        var issues = PackValidator.Validate(pack, _dir).Where(i => i.Where.StartsWith("uis[shop].openConditions")).ToList();
        foreach (var i in issues) _out.WriteLine($"{i.Severity} {i.Where}: {i.Message}");
        Assert.Contains(issues, i => i.Where.StartsWith("uis[shop].openConditions[1]") && i.Message.Contains("nobodyDeclaredThis"));
        Assert.DoesNotContain(issues, i => i.Where.StartsWith("uis[shop].openConditions[0]"));

        // Checked as a rule: a Timer restarts when the screen opens, so it is
        // not the "only works in a rule" warning a polled list gets (control:
        // the same Timer on a wallpaper's unlock).
        var timer = new NodeConditionDef { Type = NodeConditionTypes.Timer };
        timer.Params["seconds"] = "5";
        ui.OpenConditions.Add(timer);
        var wallpaper = new WallpaperDef { Key = "w", DisplayName = "W" };
        wallpaper.UnlockConditions.Add(new NodeConditionDef { Type = NodeConditionTypes.Timer, Params = { ["seconds"] = "5" } });
        pack.Wallpapers.Add(wallpaper);
        var outside = PackValidator.Validate(pack, _dir).Where(i => i.Code == "condition.timerOutsideRule").ToList();
        Assert.DoesNotContain(outside, i => i.Where.StartsWith("uis[shop]"));
        Assert.Contains(outside, i => i.Where.StartsWith("wallpapers[w]"));
    }
}
