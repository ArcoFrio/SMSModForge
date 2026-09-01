using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using SMSModForge.Model;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The tab you are on stays the tab you are on.
/// <para/>
/// From a report that the editor switched tabs by itself after + Rule on the
/// Integration tab.
/// <para/>
/// The first two here never reproduced it, and that was the useful part:
/// between them they ruled out the view model, the command, the button, and the
/// TabControl's own reaction to a unit landing in the list it is showing, which
/// left the click itself. The third reproduces it, and it needed the half of a
/// click no automation peer performs — the mouse capture, and the release that
/// gives it up.
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

    // ── The reported jump ────────────────────────────────────────────
    //
    // Every left-hand unit list sits under a ToolBar, and a ToolBar is a
    // FOCUS SCOPE. The point of one is that a toolbar never keeps focus:
    // you click a button on it and focus goes back to what you were working
    // in, which WPF does on the button’s LostMouseCapture, at the end of the
    // click.
    //
    // "What you were working in" is the enclosing scope’s remembered element,
    // and clicking a tab header leaves that a TabItem. Focus arriving at a
    // TabItem makes it select itself. So a click on + Rule could end by
    // selecting a different tab - whichever one the window scope still
    // remembered - and that is what the editor was seen doing.

    [Fact]
    public void A_toolbar_click_leaves_focus_on_the_button()
    {
        // The mechanism, with no contrived state: press and release + Rule
        // and see where focus ends up. Before the fix it came back as a
        // TabItem - harmless only because that TabItem happened to be the tab
        // already showing.
        Assert.NotNull(PackRepository.Load(PackDir));

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            vm.OpenRecentCommand.Execute(PackDir);
            WindowHarness.Pump();

            var integration = (TabItem)tabs.Items[TabIntegration];
            integration.IsSelected = true;
            WindowHarness.Pump();
            integration.Focus();          // what a click on the header does
            WindowHarness.Pump();

            ClickToolbarButton(window, "btn:addRule");

            _out.WriteLine("focus after the click: " +
                           Keyboard.FocusedElement?.GetType().Name);
            Assert.IsType<Button>(Keyboard.FocusedElement);
        });
    }

    /// <summary>Press and release a toolbar button the way a mouse does it.
    /// The release is the half that moves focus, and it needs real mouse
    /// capture - an automation peer on its own invokes the command without any
    /// of this, which is why the first tests here saw nothing.</summary>
    private static void ClickToolbarButton(MainWindow window, string anchorId)
    {
        window.UpdateLayout();
        var button = TutorialAnchor.Find(window, anchorId) as Button;
        Assert.True(button != null, anchorId + " is not in the tree");
        button!.Focus();
        Mouse.Capture(button);
        WindowHarness.Pump();
        ((IInvokeProvider)new ButtonAutomationPeer(button)
            .GetPattern(PatternInterface.Invoke)!).Invoke();
        Mouse.Capture(null);
        WindowHarness.Pump();
    }
}
