using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The four the author picked from the mockups (1.8.0): panel help folded to a
/// line with "more" (Classic) or behind a "?" in the tile's band (EX), counts
/// on the EX rail, and the EX header's Save, Validate and Export with a dot
/// while there is something to save.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class HelpAndRailTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-help-" + Guid.NewGuid().ToString("N"));

    public HelpAndRailTests() => Directory.CreateDirectory(_dir);

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

    private void Open(MainWindow window, MainViewModel vm)
    {
        var pack = PackRepository.CreateEmpty("help.pack");
        var d = new DialogueDef { Key = "chat", DisplayName = "Chat" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Hi." });
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _dir);
        vm.OpenPackFromPath(_dir);
        vm.SelectedTabIndex = MainTab.Dialogues;
        vm.SelectedDialogue = vm.Dialogues.Single();
        WindowHarness.Pump();
        window.UpdateLayout();
    }

    private static HelpText NodesHelp(MainWindow window)
        => Descendants<HelpText>(window).Single(h => h.Key == "dialogues.clickNodeSelectChild");

    private static TextBlock Words(HelpText help) => help.Children.OfType<TextBlock>().Last();

    [Fact]
    public void ClassicFoldsTheHelpToALine_MoreShowsItAll_AndItIsRemembered()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var was = ThemeManager.Current;
            try
            {
                ThemeManager.Apply(ThemeManager.All.First(t => t.Name == "Light"));
                Open(window, vm);
                var help = NodesHelp(window);
                var words = Words(help);
                Assert.False(help.IsOpen);
                Assert.Equal(TextWrapping.NoWrap, words.TextWrapping);
                double folded = help.ActualHeight;

                // "more", as a person clicks it.
                var more = help.Children.OfType<TextBlock>().First().Inlines.OfType<Hyperlink>().Single();
                Assert.Equal(Loc.T("help.more"), new TextRange(more.ContentStart, more.ContentEnd).Text);
                more.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, more));
                WindowHarness.Pump();
                window.UpdateLayout();
                Assert.True(help.IsOpen);
                Assert.True(help.ActualHeight > folded * 2, $"unfolded it is {help.ActualHeight:0} high, folded {folded:0}");
                Assert.True(EditorPrefs.GetHelpOpen("dialogues.clickNodeSelectChild"), "the panel's choice was not remembered");
            }
            finally
            {
                EditorPrefs.SetHelpOpen("dialogues.clickNodeSelectChild", false);
                ThemeManager.Apply(was);
            }
        });
    }

    [Fact]
    public void ExKeepsTheHelpBehindAQuestionMarkInThePanelsBand()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var was = ThemeManager.Current;
            try
            {
                ThemeManager.Apply(ThemeManager.All.First(t => t.Name == "Frost"));
                Open(window, vm);
                var help = NodesHelp(window);
                var box = (GroupBox)window.FindName("DialogueNodesBox");
                var question = (ToggleButton)box.Template.FindName("help", box);
                Assert.True(question.IsVisible, "no ? on a panel with help");
                Assert.False(Words(help).IsVisible, "the help shows before the ? is pressed");

                question.IsChecked = true;
                WindowHarness.Pump();
                window.UpdateLayout();
                Assert.True(Words(help).IsVisible, "pressing ? showed nothing");
                Assert.Equal(TextWrapping.Wrap, Words(help).TextWrapping);

                // The control: a panel with no help has no ?.
                var identity = (GroupBox)window.FindName("DialogueIdentityBox");
                Assert.False(((ToggleButton)identity.Template.FindName("help", identity)).IsVisible);
            }
            finally
            {
                EditorPrefs.SetHelpOpen("dialogues.clickNodeSelectChild", false);
                ThemeManager.Apply(was);
            }
        });
    }

    [Fact]
    public void TheExRailCountsEachTabsRecords_AndBadgesTheIssues()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var was = ThemeManager.Current;
            try
            {
                ThemeManager.Apply(ThemeManager.All.First(t => t.Name == "Frost"));
                Open(window, vm);
                var tabs = (TabControl)window.FindName("MainTabs");
                TabItem Tab(string key) => tabs.Items.OfType<TabItem>().Single(t => t.Header as string == Loc.T(key));
                TextBlock Count(TabItem tab) => (TextBlock)tab.Template.FindName("count", tab);

                Assert.Equal(vm.Dialogues.Count.ToString(), Count(Tab("dialogues.dialogues")).Text);
                Assert.True(Count(Tab("dialogues.dialogues")).IsVisible);
                // None shown at nought.
                Assert.Equal(0, vm.Wallpapers.Count);
                Assert.False(Count(Tab("wallpapers.wallpapers")).IsVisible);

                vm.ValidateCommand.Execute(null);
                WindowHarness.Pump();
                var home = tabs.Items.OfType<TabItem>().First();
                var badge = (Border)home.Template.FindName("badge", home);
                Assert.Equal(vm.Issues.Count > 0, badge.IsVisible);
            }
            finally { ThemeManager.Apply(was); }
        });
    }

    [Fact]
    public void TheExHeaderSaves_AndSaysWhileThereIsSomethingToSave()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var was = ThemeManager.Current;
            try
            {
                ThemeManager.Apply(ThemeManager.All.First(t => t.Name == "Frost"));
                Open(window, vm);
                var tabs = (TabControl)window.FindName("MainTabs");
                var commands = (StackPanel)tabs.Template.FindName("Commands", tabs);
                var unsaved = (StackPanel)tabs.Template.FindName("Unsaved", tabs);
                var buttons = commands.Children.OfType<Button>().ToList();
                Assert.Same(vm.SavePackCommand, buttons[0].Command);
                Assert.Same(vm.ValidateCommand, buttons[1].Command);
                Assert.Same(vm.ExportPackCommand, buttons[2].Command);

                WindowHarness.Pump();
                Assert.False(unsaved.IsVisible, "a pack just opened says it has unsaved changes");

                vm.SelectedDialogue!.DisplayName = "Chat at the pier";
                vm.Undo.Checkpoint();
                WindowHarness.Pump();
                WindowHarness.Wait(TimeSpan.FromMilliseconds(100));
                window.UpdateLayout();
                Assert.True(unsaved.IsVisible, "an edit does not show the unsaved dot");

                buttons[0].Command.Execute(null);
                WindowHarness.Pump();
                WindowHarness.Wait(TimeSpan.FromMilliseconds(100));
                window.UpdateLayout();
                Assert.False(unsaved.IsVisible, "saved, the dot stays");
            }
            finally { ThemeManager.Apply(was); }
        });
    }
}
