using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The bars over the lists (the author, 2026-10-21): every tab's the same -
/// Add, Add folder, Rename, Duplicate, Delete, then what only that tab has -
/// each key written under its button without making the button bigger, and a
/// tip for the keys that have no button. Measured on the window as drawn.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NavigatorBarTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public NavigatorBarTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-navigators-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const int TabNpcs = MainTab.Npcs, TabScenes = MainTab.Scenes, TabQuests = MainTab.Quests;

    private MainViewModel Opened(MainWindow window)
    {
        var pack = PackRepository.CreateEmpty("navigators.pack");
        pack.Scenes.Add(new SceneDef { Key = "BeachDay", DisplayName = "Beach Day" });
        pack.Npcs.Add(new NpcDef { Key = "Guard", DisplayName = "Guard" });
        PackRepository.Save(pack, _dir);
        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        WindowHarness.Pump();
        return vm;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    private static NavigatorBar BarOver(MainWindow window, string list)
        => Descendants<NavigatorBar>(window).Single(b => b.IsVisible && (b.List as FrameworkElement)?.Name == list);

    /// <summary>The roles of what a bar shows, left to right as drawn.</summary>
    private static List<string> RolesAsDrawn(NavigatorBar bar)
        => bar.Items.OfType<StackPanel>()
              .Where(s => s.IsVisible)
              .OrderBy(s => s.TranslatePoint(new Point(0, 0), bar).Y).ThenBy(s => s.TranslatePoint(new Point(0, 0), bar).X)
              .Select(s => (s.Children[0] as FrameworkElement)?.Tag as string ?? "own")
              .Select(t => t.StartsWith("nav:", StringComparison.Ordinal) ? t.Substring(4) : t)
              .ToList();

    private static readonly string[] Order = { "add", "folder", "rename", "duplicate", "delete" };

    [Fact]
    public void EveryListsBarHasTheSameButtonsInTheSameOrder()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var tabs = (TabControl)window.FindName("MainTabs");
            var seen = new List<string>();
            var wrong = new List<string>();
            for (int i = 0; i < tabs.Items.Count; i++)
            {
                vm.SelectedTabIndex = i;
                WindowHarness.Pump();
                window.UpdateLayout();
                foreach (var bar in Descendants<NavigatorBar>(window).Where(b => b.IsVisible))
                {
                    var roles = RolesAsDrawn(bar);
                    string name = $"tab {i} / {(bar.List as FrameworkElement)?.Name}";
                    seen.Add($"{name}: {string.Join(" ", roles)}");

                    // The standard ones in the standard order, whatever is between them.
                    var standard = roles.Where(r => Order.Contains(r)).ToList();
                    if (!standard.SequenceEqual(standard.OrderBy(r => Array.IndexOf(Order, r))))
                        wrong.Add($"{name} is out of order: {string.Join(" ", roles)}");
                    if (!roles.Contains("add") || !roles.Contains("delete"))
                        wrong.Add($"{name} lacks Add or Delete");
                    // A tab's own records get the lot.
                    if (bar.RenameCommand != null && !(roles.Contains("rename") && roles.Contains("duplicate")))
                        wrong.Add($"{name} lacks Rename or Duplicate");
                    if (roles.Last() != "keys") wrong.Add($"{name} does not end with the keys sign");
                    if (bar.List == null) wrong.Add($"{name} governs no list, so its keys do nothing");
                }
            }
            _out.WriteLine(string.Join("\n", seen));
            // The control: the bars were really found - the tabs' lists, and the
            // game's places, conversations and quests under three of them.
            Assert.True(seen.Count >= 16, $"only {seen.Count} bars found");
            Assert.Empty(wrong);
        });
    }

    [Fact]
    public void EachKeyIsWrittenUnderItsButtonNotInIt()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            vm.SelectedTabIndex = TabScenes;
            WindowHarness.Pump();
            window.UpdateLayout();
            var bar = BarOver(window, "SceneTreeView");

            var expected = new Dictionary<string, string>
            {
                ["add"] = Loc.T("navigator.addKeys"),
                ["rename"] = "F2",
                ["duplicate"] = Loc.T("menu.duplicateItem.keys"),
                ["delete"] = Loc.T("menu.deleteItem.keys"),
            };
            foreach (var (role, keys) in expected)
            {
                var button = bar.Role(role)!;
                var slot = (StackPanel)button.Parent;
                var cap = slot.Children.OfType<TextBlock>().Single();
                double capTop = cap.TranslatePoint(new Point(0, 0), button).Y;
                _out.WriteLine($"{role}: '{cap.Text}', button {button.ActualHeight:0.#} high, cap starts at {capTop:0.#}");
                Assert.Equal(keys, cap.Text);
                Assert.True(cap.IsVisible && cap.ActualHeight > 0, role + "'s key is not drawn");
                // Below the button's own box, not inside it.
                Assert.True(capTop >= button.ActualHeight - 1.5, $"{role}'s key is drawn inside its button");
            }
        });
    }

    private static void Press(UIElement list, Key key)
    {
        list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(list), 0, key)
        { RoutedEvent = Keyboard.KeyDownEvent });
        WindowHarness.Pump();
    }

    [Fact]
    public void TheKeysUnderTheButtonsAreTheKeysTheListAnswers()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            vm.SelectedTabIndex = TabScenes;
            WindowHarness.Pump();
            var list = (UIElement)window.FindName("SceneTreeView");

            int before = vm.Scenes.Count;
            Press(list, Key.Insert);
            Assert.Equal(before + 1, vm.Scenes.Count);

            vm.SelectedScene = vm.Scenes.Single(s => s.Key == "BeachDay");
            string? asked = null;
            vm.PromptForText = (_, label, _) => { asked = label; return null; };
            Press(list, Key.F2);
            Assert.Equal(Loc.T("rename.displayName"), asked);

            // The NPCs' list answered no key at all before it had a bar.
            vm.SelectedTabIndex = TabNpcs;
            WindowHarness.Pump();
            vm.SelectedNpc = vm.Npcs.Single();
            WindowHarness.Pump();
            Press((UIElement)window.FindName("NpcTreeView"), Key.Delete);
            Assert.Empty(vm.Npcs);
        });
    }

    [Fact]
    public void TheKeyboardSignListsOnlyTheKeysItsListHas()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            vm.SelectedTabIndex = TabQuests;
            WindowHarness.Pump();
            window.UpdateLayout();

            string own = BarOver(window, "QuestTreeView").MoreKeysText();
            string games = BarOver(window, "VanillaQuestList").MoreKeysText();
            _out.WriteLine(own + "\n--\n" + games);

            Assert.Contains(Loc.T("navigator.moreKeys.copyPaste"), own);
            Assert.Contains(Loc.T("navigator.moreKeys.f12"), own);
            Assert.DoesNotContain(Loc.T("navigator.moreKeys.copyPaste"), games);
            Assert.DoesNotContain(Loc.T("navigator.moreKeys.f12"), games);
            Assert.Contains(Loc.T("navigator.moreKeys.undo"), games);
        });
    }
}
