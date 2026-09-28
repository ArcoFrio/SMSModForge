using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;
using V = SMSModForge.Shared.QuestVocabulary;

namespace SMSModForge.Tests;

/// <summary>
/// The Quests tab and the quest rows, in the real window: that what is typed
/// reaches the pack, and that the rows show what their view models say.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class QuestTabTests
{
    private readonly ITestOutputHelper _out;
    public QuestTabTests(ITestOutputHelper o) => _out = o;

    private const int TabQuests = 13;

    [Fact]
    public void TheTabIsLastAndNothingElseMoved()
    {
        // Several constants in the window and the view model address tabs by
        // number, and the issue list jumps by them. Pinned so a new tab cannot
        // quietly renumber the ones before it.
        WindowHarness.Run(window =>
        {
            var tabs = (TabControl)window.FindName("MainTabs");
            var headers = tabs.Items.OfType<TabItem>().Select(t => t.Header?.ToString() ?? "").ToList();
            _out.WriteLine(string.Join(" | ", headers));

            Assert.Equal("Quests", headers[TabQuests]);
            Assert.Equal(TabQuests, headers.Count - 1);
            Assert.Equal("UI", headers[12]);
            Assert.Equal("Integration", headers[11]);
        });
    }

    [Fact]
    public void WhatIsTypedOnTheTabReachesThePack()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            tabs.SelectedIndex = TabQuests;
            WindowHarness.Pump();

            vm.AddQuestCommand.Execute(null);
            WindowHarness.Pump();
            var quest = Assert.Single(vm.Pack.Quests);

            var title = (TextBox)window.FindName("QuestTitleBox");
            title.Text = "Lost Letters (Mira)";
            WindowHarness.Pump();
            Assert.Equal("Lost Letters (Mira)", quest.Title);
            Assert.Equal("LostLettersMira", quest.Key);

            vm.SelectedQuest!.AddTaskCommand.Execute(null);
            WindowHarness.Pump();
            var name = (TextBox)window.FindName("QuestTaskNameBox");
            Assert.True(name.IsVisible, "the selected task's editor is not on screen");
            name.Text = "Ask the fisherman.";
            WindowHarness.Pump();
            Assert.Equal("Ask the fisherman.", quest.Tasks[0].Name);

            // The list shows the row, and the row is what was typed.
            var list = (ListBox)window.FindName("QuestTaskList");
            Assert.Equal(1, list.Items.Count);
            var shown = Texts(list).ToList();
            _out.WriteLine(string.Join(" / ", shown));
            Assert.Contains("Ask the fisherman.", shown);
        });
    }

    [Fact]
    public void WhatHappensToPlayersWhoFinishedItIsPickedOnTheTab_ForThePacksOwnQuestsOnly()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = TabQuests;
            vm.AddQuestCommand.Execute(null);
            WindowHarness.Pump();
            var quest = Assert.Single(vm.Pack.Quests);

            var panel = (GroupBox)window.FindName("QuestStepsAddedPanel");
            var leave = (RadioButton)window.FindName("StepsAddedLeave");
            var reopen = (RadioButton)window.FindName("StepsAddedReopen");
            var over = (RadioButton)window.FindName("StepsAddedStartOver");
            Assert.True(panel.IsVisible, "the choice is not on screen for a quest of the pack's own");
            Assert.True(leave.IsChecked);

            reopen.IsChecked = true;   // what a click does
            WindowHarness.Pump();
            Assert.Equal(SMSModForge.Shared.QuestGrowth.Reopen, quest.WhenStepsAdded);
            Assert.False(leave.IsChecked);

            over.IsChecked = true;
            WindowHarness.Pump();
            Assert.Equal(SMSModForge.Shared.QuestGrowth.StartOver, quest.WhenStepsAdded);
            Assert.False(reopen.IsChecked);

            // One of the game's quests: not offered.
            vm.AddVanillaQuestCommand.Execute(null);
            WindowHarness.Pump();
            Assert.False(panel.IsVisible, "the choice is offered on one of the game's quests");
        });
    }

    [Fact]
    public void TheCompletionPickerWaitsForSubtasks()
    {
        // It means nothing on a task with none, and a live picker there would
        // invite a choice with no effect.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = TabQuests;
            vm.AddQuestCommand.Execute(null);
            var quest = vm.SelectedQuest!;
            quest.AddTaskCommand.Execute(null);
            WindowHarness.Pump();

            var picker = (ComboBox)window.FindName("QuestTaskCompletionPicker");
            Assert.False(picker.IsEnabled);

            quest.AddSubtaskCommand.Execute(null);   // under the selected task
            quest.SelectedTask = quest.TaskRows[0];
            WindowHarness.Pump();
            Assert.True(picker.IsEnabled);
        });
    }

    [Fact]
    public void AQuestRowShowsTheTaskListOnlyWhereItMeansSomething()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.Pack.Quests.Add(new QuestDef
            {
                Key = "letters", Title = "Lost Letters",
                Tasks = { new QuestTaskDef { Key = "ask", Name = "Ask" }, new QuestTaskDef { Key = "collect", Name = "Collect", CountTo = 3 } },
            });

            var def = new NodeActionDef { Type = NodeActionTypes.Quest };
            def.Params[V.QuestParam] = "letters";
            var row = new NodeActionViewModel(def);

            // Host the real row template in the window, so its resources resolve.
            var host = new ContentControl
            {
                Content = row,
                ContentTemplate = (DataTemplate)window.FindResource("ActionRowTemplate"),
            };
            window.Content = host;
            WindowHarness.Pump();

            var taskPicker = Named<ComboBox>(host, "QuestTaskPicker");
            var operation = Named<ComboBox>(host, "QuestOperationPicker");
            Assert.NotNull(taskPicker);
            Assert.NotNull(operation);

            Assert.Equal(V.Start, operation!.SelectedItem);
            Assert.False(taskPicker!.IsVisible, "a task list on 'start quest', which acts on the whole quest");

            row.QuestOperation = V.AddToCounter;
            WindowHarness.Pump();
            Assert.True(taskPicker.IsVisible);
            Assert.Equal(new[] { "ask", "collect" },
                         taskPicker.Items.OfType<QuestTaskOption>().Select(o => o.Token));

            // Choosing from the list stores the key, not the label.
            taskPicker.SelectedValue = "collect";
            WindowHarness.Pump();
            Assert.Equal("collect", def.Params[V.TaskParam]);

            // And back: an operation on the whole quest takes the list away again.
            row.QuestOperation = V.Track;
            WindowHarness.Pump();
            Assert.False(taskPicker.IsVisible, "the task list stayed after switching to 'track quest'");
        });
    }

    /// <summary>A quest row in the window, with its pickers realised.</summary>
    private static (NodeActionViewModel Row, NodeActionDef Def, ContentControl Host, TextBox Elsewhere) VanillaRow(MainWindow window)
    {
        var def = new NodeActionDef { Type = NodeActionTypes.Quest };
        var row = new NodeActionViewModel(def) { QuestOperation = V.CompleteTask };
        row.QuestPicker.Source = "Vanilla";
        row.QuestPicker.QuestKey = "Astrid Quest";

        var host = new ContentControl { Content = row, ContentTemplate = (DataTemplate)window.FindResource("ActionRowTemplate") };
        // Something else to click into, the way a person leaves a dropdown.
        var elsewhere = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(host);
        panel.Children.Add(elsewhere);
        window.Content = panel;
        WindowHarness.Pump();
        return (row, def, host, elsewhere);
    }

    /// <summary>Type into a dropdown the way a person does, and return what it
    /// is left showing.</summary>
    private static List<string> TypeAndRead(ComboBox box, string text)
    {
        var typing = (TextBox)box.Template.FindName("PART_EditableTextBox", box);
        // No Focus() here, as in ComboBoxSearchTests: the search clears itself
        // when the box loses focus, and where focus settles once a popup opens
        // is up to WPF's timing - which made a test that focused the box pass
        // or fail from one run to the next.
        box.IsDropDownOpen = true;
        WindowHarness.Pump();
        typing.Text = text;
        typing.SelectionStart = text.Length;
        typing.SelectionLength = 0;
        WindowHarness.Pump();

        var shown = new List<string>();
        for (int i = 0; i < box.Items.Count; i++)
            if (box.ItemContainerGenerator.ContainerFromIndex(i) is ComboBoxItem item && item.Visibility == Visibility.Visible)
                shown.Add(box.Items[i]!.ToString()!);
        return shown;
    }

    [Fact]
    public void TheTaskListIsSearchedByWhatItShows()
    {
        WindowHarness.Run(window =>
        {
            var (_, def, host, elsewhere) = VanillaRow(window);
            var tasks = Named<ComboBox>(host, "QuestTaskPicker")!;

            // Chosen from the list, the box reads the task, not its id.
            tasks.SelectedValue = "2076169433";
            WindowHarness.Pump();
            Assert.Equal("2076169433", def.Params[V.TaskParam]);
            Assert.Equal("Go back home.", tasks.Text);

            // Typing part of a name narrows the list to it.
            var shown = TypeAndRead(tasks, "astrid");
            _out.WriteLine(string.Join(" | ", shown));
            Assert.Contains("Help Astrid find whatever she is looking for.", shown);
            Assert.Contains("Meet with Astrid.", shown);
            Assert.DoesNotContain("Go back home.", shown);

            // Typing something that is no task, then leaving: the row keeps a
            // real task - whatever the typing last completed to, as every
            // editable dropdown in the editor does - and the box shows that
            // task again rather than the half-typed search.
            TypeAndRead(tasks, "nothing like any task");
            Assert.True(def.Params.ContainsKey(V.TaskParam), "a search that matched nothing cleared the task");
            tasks.IsDropDownOpen = false;
            elsewhere.Focus();
            WindowHarness.Pump();
            Assert.False(tasks.IsKeyboardFocusWithin, "focus never left the dropdown, so this proves nothing");

            var kept = def.Params[V.TaskParam];
            var keptTask = VanillaQuests.Find("Astrid Quest")!.Task(kept);
            _out.WriteLine($"kept {kept} = {keptTask?.Name}; box reads '{tasks.Text}'");
            Assert.NotNull(keptTask);
            Assert.Equal(keptTask!.Name, tasks.Text);
        });
    }

    [Fact]
    public void DropdownsThatAppearOnTheTabLaterSearchToo()
    {
        // Reported: no search on the Quests tab's dropdowns. The earlier test
        // built its row already showing, which is exactly the case that worked -
        // these start hidden and appear once a mode or a type is chosen.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = TabQuests;
            WindowHarness.Pump();

            vm.AddQuestCommand.Execute(null);
            var quest = vm.SelectedQuest!;
            quest.AddTaskCommand.Execute(null);
            var task = quest.SelectedTask!;
            WindowHarness.Pump();

            // The variable a counter follows: hidden until it counts, from a variable.
            task.Counts = true;
            task.CounterSource = QuestTaskViewModel.CountedByVariable;
            task.CountVariableSource = "Vanilla";
            WindowHarness.Pump();
            var countVariable = (ComboBox)window.FindName("QuestTaskCountVariablePicker");
            var found = TypeAndRead(countVariable, "heal");
            _out.WriteLine($"counter variable: {found.Count} of {countVariable.Items.Count}: {string.Join(", ", found.Take(8))}");
            Assert.Contains("health", found);
            // Every entry left showing matches. A count alone would not do: a
            // list this long only builds the rows near the top, so an unnarrowed
            // one shows far fewer than it holds.
            Assert.All(found, name => Assert.Contains("heal", name, System.StringComparison.OrdinalIgnoreCase));
            countVariable.IsDropDownOpen = false;
            WindowHarness.Pump();

            // A completion condition added as a Variable one, then switched to a quest.
            var condition = task.CompletionConditions.Add();
            WindowHarness.Pump();
            condition.DisplayType = NodeConditionTypes.QuestState;
            condition.QuestPicker.Source = "Vanilla";
            WindowHarness.Pump();
            var lists = (FrameworkElement)window.FindName("QuestTaskCompletionLists");
            var questBox = Descendants<ComboBox>(lists).First(b => b.Name == "QuestNamePicker");
            var quests = TypeAndRead(questBox, "into the dark");
            _out.WriteLine("quest: " + string.Join(", ", quests));
            Assert.Equal(new[] { "Astrid Quest" }, quests);
        });
    }

    [Fact]
    public void TypingIntoARealRowKeepsEveryLetter()
    {
        // The first-letter report, on the kind of box it was reported on: a
        // Variable condition's name, bound to its row, grouped, in the window.
        WindowHarness.Run(window =>
        {
            var row = new NodeConditionViewModel(new NodeConditionDef { Type = NodeConditionTypes.VariableCompare })
            {
                VarSource = "Vanilla",
            };
            var host = new ContentControl { Content = row, ContentTemplate = (DataTemplate)window.FindResource("LeafConditionTemplate") };
            window.Content = host;
            window.Activate();
            WindowHarness.Pump();

            var name = Descendants<ComboBox>(host).First(b => b.IsEditable && b.IsVisible);
            var typing = (TextBox)name.Template.FindName("PART_EditableTextBox", name);
            typing.Focus();
            WindowHarness.Pump();
            Assert.True(typing.IsKeyboardFocused, "the box never had the keyboard, so this proves nothing");

            foreach (char c in "towe")
            {
                TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, typing, c.ToString()));
                WindowHarness.Pump();
            }

            _out.WriteLine($"box '{name.Text}' caret {typing.SelectionStart}+{typing.SelectionLength}; row holds '{row.VarName}'");
            Assert.StartsWith("towe", name.Text, System.StringComparison.OrdinalIgnoreCase);
            Assert.Equal(4, typing.SelectionStart);
            Assert.StartsWith("towe", row.VarName, System.StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void TheQuestListIsSearchedByItsTitlesToo()
    {
        // Stored as "Astrid Quest", listed as "Into the Dark (Astrid)": a search
        // of the stored name alone found nothing for a word from the list.
        WindowHarness.Run(window =>
        {
            var (_, _, host, _) = VanillaRow(window);
            var quests = Named<ComboBox>(host, "QuestNamePicker")!;

            var byTitle = TypeAndRead(quests, "into the dark");
            _out.WriteLine(string.Join(" | ", byTitle));
            Assert.Equal(new[] { "Astrid Quest" }, byTitle);

            // The control: the stored name still finds it.
            quests.IsDropDownOpen = false;
            WindowHarness.Pump();
            Assert.Contains("Astrid Quest", TypeAndRead(quests, "astrid q"));
        });
    }

    [Fact]
    public void TheTabHasNoBrokenBindings()
    {
        // A binding to a property that is not there does not throw: the field
        // just shows nothing and edits go nowhere. The only place it is said is
        // the binding trace, so that is what this reads.
        using var watch = new BindingWatch();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            tabs.SelectedIndex = TabQuests;
            WindowHarness.Pump();

            vm.AddQuestCommand.Execute(null);
            var quest = vm.SelectedQuest!;
            quest.Title = "Lost Letters";
            quest.AddTaskCommand.Execute(null);
            quest.SelectedTask!.Name = "Search";
            quest.AddSubtaskCommand.Execute(null);
            quest.SelectedTask!.Counts = true;
            WindowHarness.Pump();
            quest.SelectedTask = quest.TaskRows[0];
            WindowHarness.Pump();

            // The control that the tab really realised: an unrealised page binds
            // nothing and would pass this trivially.
            Assert.True(((TextBox)window.FindName("QuestTaskNameBox")).IsVisible);

            // A task with subtasks shows the note where its lists would be...
            var lists = (FrameworkElement)window.FindName("QuestTaskCompletionLists");
            var note = (TextBlock)window.FindName("QuestTaskNoCompletionNote");
            Assert.False(lists.IsVisible, "a task with subtasks offers its own completion lists");
            Assert.True(note.IsVisible);

            // ...and a subtask shows the lists, filled, with their rows realised.
            var sub = quest.TaskRows[1];
            quest.SelectedTask = sub;
            sub.CompletionConditions.Add();
            sub.CompletionActions.Add();
            quest.StartConditions.Add();
            WindowHarness.Pump();
            Assert.True(lists.IsVisible);
            Assert.False(note.IsVisible);
            Assert.NotEmpty(Descendants<ContentPresenter>(lists));

            // The variable row appears only once the count follows a variable.
            var variableRow = (FrameworkElement)window.FindName("QuestTaskCountVariableRow");
            sub.Counts = true;
            WindowHarness.Pump();
            Assert.False(variableRow.IsVisible, "the variable row shows while Quest actions set the count");
            sub.CounterSource = QuestTaskViewModel.CountedByVariable;
            sub.CountVariableSource = "Vanilla";
            sub.CountVariable = "health";
            WindowHarness.Pump();
            Assert.True(variableRow.IsVisible);
            Assert.Equal("health", ((ComboBox)window.FindName("QuestTaskCountVariablePicker")).Text);

            Report(watch);
        });
    }

    [Fact]
    public void TheQuestRowsHaveNoBrokenBindings()
    {
        using var watch = new BindingWatch();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.Pack.Quests.Add(new QuestDef
            {
                Key = "letters", Title = "Lost Letters",
                Tasks = { new QuestTaskDef { Key = "collect", Name = "Collect", CountTo = 3 } },
            });

            // The three rows that name a quest, each through its real template.
            var action = new NodeActionViewModel(new NodeActionDef { Type = NodeActionTypes.Quest });
            action.QuestOperation = V.AddToCounter;
            action.QuestPicker.QuestKey = "letters";
            var state = new NodeConditionViewModel(new NodeConditionDef { Type = NodeConditionTypes.QuestState });
            state.QuestPicker.QuestKey = "letters";
            var counter = new NodeConditionViewModel(new NodeConditionDef { Type = NodeConditionTypes.QuestCounter });
            counter.QuestPicker.Source = "Vanilla";
            counter.QuestPicker.QuestKey = "Astrid Quest";

            var panel = new StackPanel();
            panel.Children.Add(new ContentControl { Content = action, ContentTemplate = (DataTemplate)window.FindResource("ActionRowTemplate") });
            panel.Children.Add(new ContentControl { Content = state, ContentTemplate = (DataTemplate)window.FindResource("LeafConditionTemplate") });
            panel.Children.Add(new ContentControl { Content = counter, ContentTemplate = (DataTemplate)window.FindResource("LeafConditionTemplate") });
            window.Content = panel;
            WindowHarness.Pump();

            Assert.NotNull(Named<ComboBox>(panel, "QuestOperationPicker"));
            Assert.NotNull(Named<ComboBox>(panel, "QuestStatePicker"));

            Report(watch);
        });
    }

    private void Report(BindingWatch watch)
    {
        foreach (var complaint in watch.Complaints.Distinct().Take(6))
            _out.WriteLine(complaint);
        Assert.True(watch.Complaints.Count == 0,
            $"{watch.Complaints.Count} broken binding(s); first: " + (watch.Complaints.FirstOrDefault() ?? ""));
    }

    private sealed class BindingWatch : System.Diagnostics.TraceListener, System.IDisposable
    {
        public List<string> Complaints { get; } = new();
        private readonly System.Diagnostics.SourceLevels _was;

        public BindingWatch()
        {
            System.Diagnostics.PresentationTraceSources.Refresh();
            _was = System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level;
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Warning;
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(this);
        }

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (string.IsNullOrEmpty(message)) return;
            // Every tab's folder tree says this about its own rows when the
            // list is rebuilt - the SFX tab gives the identical pair - so it is
            // the shared tree, not anything on the Quests tab.
            if (message.Contains("target element is 'TreeViewItem'", System.StringComparison.Ordinal)
                && message.Contains("ContentAlignment", System.StringComparison.Ordinal))
                return;
            if (message.Contains("path error", System.StringComparison.OrdinalIgnoreCase)
                || message.Contains("Cannot find", System.StringComparison.OrdinalIgnoreCase))
                Complaints.Add(message);
        }

        public void Dispose()
        {
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = _was;
        }
    }

    private static T? Named<T>(DependencyObject root, string name) where T : FrameworkElement
        => Descendants<T>(root).FirstOrDefault(e => e.Name == name);

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

    private static IEnumerable<string> Texts(DependencyObject root)
        => Descendants<TextBlock>(root).Select(t => t.Text);
}
