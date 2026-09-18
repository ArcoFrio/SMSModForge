using System.Linq;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;
using V = SMSModForge.Shared.QuestVocabulary;

namespace SMSModForge.Tests;

/// <summary>
/// What a quest shows in the journal as the player moves through it: a quest
/// description that a task changes once it is done, and a subtask kept out of
/// sight until it starts.
/// <para/>
/// The game side of both - the journal reading the description and the hidden
/// flag - is read off the game's IL and cannot be run here. This holds the half
/// that can: what the pack stores, and what the tab offers and says.
/// </summary>
public sealed class QuestJournalOptionsTests
{
    private readonly ITestOutputHelper _out;
    public QuestJournalOptionsTests(ITestOutputHelper o) => _out = o;

    private const int TabQuests = 13;

    [Fact]
    public void BothAreWrittenOnlyWhenSet()
    {
        var pack = new ModPack();
        var quest = new QuestDef { Key = "letters", Title = "Letters" };
        var plain = new QuestTaskDef { Key = "plain", Name = "Plain" };
        var told = new QuestTaskDef { Key = "told", Name = "Told", QuestDescription = "The letter was signed M." };
        var hidden = new QuestTaskDef { Key = "hidden", Name = "Hidden", HideUntilStarted = true };
        told.Subtasks.Add(hidden);
        quest.Tasks.Add(plain);
        quest.Tasks.Add(told);
        pack.Quests.Add(quest);

        string json = PackRepository.SerializeAsSaved(pack);
        _out.WriteLine(json.Substring(json.IndexOf("\"quests\"")));

        // A task that sets neither writes neither: every pack saved before this
        // stays byte for byte what it was.
        int plainAt = json.IndexOf("\"key\": \"plain\"");
        int toldAt = json.IndexOf("\"key\": \"told\"");
        string plainJson = json.Substring(plainAt, toldAt - plainAt);
        Assert.DoesNotContain("questDescription", plainJson);
        Assert.DoesNotContain("hideUntilStarted", plainJson);

        Assert.Contains("\"questDescription\": \"The letter was signed M.\"", json);
        Assert.Contains("\"hideUntilStarted\": true", json);

        var back = PackRepository.Deserialize(json)!;
        var tasks = back.Quests[0].AllTasks().ToDictionary(t => t.Key);
        Assert.Equal("The letter was signed M.", tasks["told"].QuestDescription);
        Assert.True(tasks["hidden"].HideUntilStarted);
        Assert.False(tasks["plain"].HideUntilStarted);
        Assert.Equal("", tasks["plain"].QuestDescription);
    }

    /// <summary>A quest with one task and two subtasks under it.</summary>
    private static (QuestViewModel Quest, QuestTaskViewModel First, QuestTaskViewModel Second) TwoSubtasks()
    {
        var def = new QuestDef { Key = "q", Title = "Q" };
        var parent = new QuestTaskDef { Key = "parent", Name = "Parent" };
        parent.Subtasks.Add(new QuestTaskDef { Key = "first", Name = "First" });
        parent.Subtasks.Add(new QuestTaskDef { Key = "second", Name = "Second" });
        def.Tasks.Add(parent);

        var quest = new QuestViewModel(def);
        quest.RebuildTaskRows();
        return (quest, quest.TaskRows.Single(r => r.Key == "first"), quest.TaskRows.Single(r => r.Key == "second"));
    }

    [Theory]
    [InlineData(V.InOrder, "second", "once the subtask before it is done")]
    [InlineData(V.InOrder, "first", "as soon as its task starts")]
    [InlineData(V.AnyOrder, "second", "as soon as that task starts")]
    [InlineData(V.AnyOne, "second", "as soon as that task starts")]
    [InlineData(V.ByAction, "second", "never shown")]
    public void TheNoteSaysWhenAHiddenSubtaskAppears(string completion, string which, string expected)
    {
        // Worked out from how the game starts subtasks under each kind of task,
        // which is the only thing that decides when a hidden one is seen.
        var (quest, first, second) = TwoSubtasks();
        quest.TaskRows.Single(r => r.Key == "parent").Completion = completion;

        var row = which == "first" ? first : second;
        row.HideUntilStarted = true;

        _out.WriteLine($"{completion} / {which}: {row.HideUntilStartedNote}");
        Assert.Contains(expected, row.HideUntilStartedNote);
    }

    [Fact]
    public void ChangingTheParentChangesWhatItsSubtasksSay()
    {
        var (quest, _, second) = TwoSubtasks();
        second.HideUntilStarted = true;
        Assert.Contains("before it is done", second.HideUntilStartedNote);

        int told = 0;
        second.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuestTaskViewModel.HideUntilStartedNote)) told++;
        };
        quest.TaskRows.Single(r => r.Key == "parent").Completion = V.AnyOrder;

        Assert.True(told > 0, "the subtask was not told its parent changed, so its note on screen stays stale");
        Assert.Contains("as soon as that task starts", second.HideUntilStartedNote);
    }

    [Fact]
    public void TheTickIsOfferedOnSubtasksAndTheDescriptionOnEveryTask()
    {
        // A top-level task needs no tick: the game's journal already leaves out
        // one that has not started. Measured on the window, where a Visibility
        // bound to the wrong name would still compile.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = TabQuests;
            vm.AddQuestCommand.Execute(null);
            var quest = vm.SelectedQuest!;
            quest.AddTaskCommand.Execute(null);
            WindowHarness.Pump();

            var description = (TextBox)window.FindName("QuestTaskDescriptionBox");
            var hidden = (CheckBox)window.FindName("QuestTaskHiddenBox");

            _out.WriteLine($"top-level: description {description.IsVisible}, tick {hidden.IsVisible}");
            Assert.True(description.IsVisible, "no description box on a top-level task");
            Assert.False(hidden.IsVisible, "the tick is offered on a top-level task, where it does nothing");

            quest.AddSubtaskCommand.Execute(null);
            quest.SelectedTask = quest.TaskRows.Last();
            WindowHarness.Pump();

            _out.WriteLine($"subtask: description {description.IsVisible}, tick {hidden.IsVisible}");
            Assert.True(description.IsVisible);
            Assert.True(hidden.IsVisible, "no tick on a subtask");

            // And both reach the pack.
            description.Text = "Marco knows more than he says.";
            hidden.IsChecked = true;
            WindowHarness.Pump();

            var sub = vm.Pack.Quests[0].Tasks[0].Subtasks[0];
            Assert.Equal("Marco knows more than he says.", sub.QuestDescription);
            Assert.True(sub.HideUntilStarted);
        });
    }
}
