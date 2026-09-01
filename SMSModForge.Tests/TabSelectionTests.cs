using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The tab you are on stays the tab you are on.
/// <para/>
/// Chasing a report that the editor switches tabs by itself after adding a
/// unit: open a pack from the recent list, click through Characters, NPCs and
/// Places to Integration, press + Rule, and it jumps to Characters.
/// <para/>
/// Neither of these reproduces it, which is worth as much as a failing test
/// would have been — between them they rule out the view model, the command,
/// the button, and the TabControl's own reaction to a unit being added to the
/// tab you are looking at. What they do not cover is a real mouse: the tabs
/// here are selected directly rather than clicked, and no test can hand WPF the
/// input queue of somebody's actual session. The instrumentation in
/// <see cref="SMSModForge.Services.TabChangeWatch"/> covers that half.
/// </summary>
public class TabSelectionTests
{
    // Tab indices, same order as MainWindow's TabItems.
    private const int TabCharacters = 1, TabNpcs = 2, TabPlaces = 3, TabIntegration = 11;

    private readonly ITestOutputHelper _out;
    public TabSelectionTests(ITestOutputHelper output) => _out = output;

    /// <summary>The sample pack in the repo — a real pack on disk, and one that
    /// travels with the checkout rather than living on one machine.</summary>
    private static string PackDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "SamplePack");
                if (File.Exists(Path.Combine(candidate, "modpack.json"))) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("SamplePack is not above " + AppContext.BaseDirectory);
        }
    }

    [Fact]
    public void Adding_a_rule_does_not_move_the_view_models_tab()
    {
        // Load it directly first: OpenPackFromPath swallows a load failure into a
        // MessageBox, which would hang the run instead of failing it.
        Assert.NotNull(PackRepository.Load(PackDir));

        var vm = new MainViewModel();
        Assert.False(vm.HasUnsavedChanges, "a fresh view model would prompt on open");
        vm.OpenRecentCommand.Execute(PackDir);

        var seen = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            // An empty name means "every property", which would re-read
            // SelectedTabIndex through the binding and push it at the tabs.
            if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex) ||
                string.IsNullOrEmpty(e.PropertyName))
                seen.Add($"{e.PropertyName ?? "<all>"} -> {vm.SelectedTabIndex}");
        };

        foreach (var tab in new[] { TabCharacters, TabNpcs, TabPlaces, TabIntegration })
            vm.SelectedTabIndex = tab;
        seen.Clear();

        vm.AddIntegrationRuleCommand.Execute(null);

        foreach (var s in seen) _out.WriteLine(s);
        Assert.Equal(TabIntegration, vm.SelectedTabIndex);
        Assert.Empty(seen);
    }

    [Fact]
    public void Adding_a_rule_does_not_move_the_windows_tab()
    {
        Assert.NotNull(PackRepository.Load(PackDir));

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");

            Assert.False(vm.HasUnsavedChanges, "a fresh window would prompt on open");
            vm.OpenRecentCommand.Execute(PackDir);
            WindowHarness.Pump();

            // The tabs the report names, in the order it names them. Each is laid
            // out before the next, so its content is really built and torn down
            // the way walking through them does it.
            foreach (var index in new[] { TabCharacters, TabNpcs, TabPlaces, TabIntegration })
            {
                ((TabItem)tabs.Items[index]).IsSelected = true;
                WindowHarness.Pump();
                _out.WriteLine($"selected {index}, now on {tabs.SelectedIndex}");
            }
            Assert.Equal(TabIntegration, tabs.SelectedIndex);

            var add = TutorialAnchor.Find(window, "btn:addRule") as Button;
            Assert.True(add != null, "the + Rule button is not in the tree");

            // As close to a click as a test gets: the button takes keyboard focus
            // the way a click gives it — the window is activated for exactly this,
            // since an unactivated one has no focus to move — and then the peer
            // raises Click through the ordinary path rather than the command
            // being poked directly.
            add!.Focus();
            WindowHarness.Pump();
            Assert.IsType<Button>(System.Windows.Input.Keyboard.FocusedElement);

            ((IInvokeProvider)new ButtonAutomationPeer(add).GetPattern(PatternInterface.Invoke)!).Invoke();
            WindowHarness.Pump();

            _out.WriteLine($"after + Rule: tab {tabs.SelectedIndex}, " +
                           $"vm {vm.SelectedTabIndex}, rules {vm.IntegrationRules.Count}");

            Assert.Single(vm.IntegrationRules);   // the click really landed
            Assert.Equal(TabIntegration, tabs.SelectedIndex);
        });
    }
}
