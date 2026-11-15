using System.Linq;
using SMSModForge.Model;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The notes beside dropdown items (the author, 1.7.0), the way Direct Path
/// has them: every action, condition, category, signal and operation a list
/// offers says in a few words what it is - and a new one that nobody wrote a
/// note for shows none, rather than a missing text.
/// </summary>
public sealed class OptionNotesTests
{
    private readonly ITestOutputHelper _out;
    public OptionNotesTests(ITestOutputHelper o) => _out = o;

    private void EveryOneHasANote(string kind, System.Collections.Generic.IEnumerable<string> items)
    {
        foreach (string item in items)
        {
            string note = OptionNotes.NoteFor(kind, item);
            _out.WriteLine($"{kind}: {item} - {note}");
            Assert.False(string.IsNullOrWhiteSpace(note), $"{item} has no note in the {kind} list");
        }
    }

    [Fact]
    public void EveryActionTypeOffered() => EveryOneHasANote(OptionNotes.ActionType, new MainViewModel().ActionTypes);

    [Theory]
    [InlineData(ConditionContext.Polled)]
    [InlineData(ConditionContext.OneShot)]
    [InlineData(ConditionContext.Rule)]
    public void EveryConditionTypeOffered(ConditionContext context)
        => EveryOneHasANote(OptionNotes.ConditionType,
                            new NodeConditionViewModel(new NodeConditionDef(), context: context).AvailableTypes);

    [Fact]
    public void EveryTargetCategory()
        => EveryOneHasANote(OptionNotes.Category,
                            NodeActionViewModel.SetActiveCategories.Concat(NodeActionViewModel.SetSpriteCategories).Distinct());

    [Fact]
    public void EveryOneOfTheGamesSignals() => EveryOneHasANote(OptionNotes.Signal, VanillaSignals.All);

    [Fact]
    public void EveryVariableOperation() => EveryOneHasANote(OptionNotes.VariableOperation, NodeActionViewModel.VariableOperations);

    [Fact]
    public void TheComponentsMadeForPacks_AndAWarningOnUnitysOwn()
    {
        EveryOneHasANote(OptionNotes.Component, PackComponentType.BuiltIn);
        var engine = VanillaComponentCatalog.All.FirstOrDefault(e => e.IsEngineComponent);
        if (engine != null)
            Assert.Equal(SMSModForge.Localization.Loc.T("note.component.engine"), OptionNotes.NoteFor(OptionNotes.Component, engine.Type));
    }

    [Fact]
    public void AGameQuestIsDescribedByItsJournal()
    {
        var quest = VanillaQuests.All.First(q => !string.IsNullOrWhiteSpace(q.Description));
        string note = OptionNotes.NoteFor(OptionNotes.GameQuest, new NavigatorTargetOption(quest.Name, quest.PlainTitle));
        _out.WriteLine($"{quest.Name}: {note}");
        Assert.False(string.IsNullOrWhiteSpace(note));
        Assert.True(note.Length <= 90, "a note is a line, not a paragraph");
        Assert.DoesNotContain("<", note);
    }

    [Fact]
    public void SomethingWithoutANoteShowsNone()
    {
        // The control: no note is empty, never a key or an English stand-in.
        Assert.Equal("", OptionNotes.NoteFor(OptionNotes.ActionType, "NotAnAction"));
        Assert.Equal("", OptionNotes.NoteFor("", "SwitchMusic"));
        Assert.Equal("", OptionNotes.NoteFor(OptionNotes.Signal, "DialogueEnd"));
        // "Variable" is two different things, by list.
        Assert.NotEqual(OptionNotes.NoteFor(OptionNotes.ActionType, "Variable"),
                        OptionNotes.NoteFor(OptionNotes.ConditionType, "Variable"));
    }

    /// <summary>
    /// A pack variable's note is its Description (the author, 1.9.0): found by
    /// its name, exactly or in other capitals, or as the outfit picker lists it
    /// ("$name"). Controls: none for a variable with no description, a name the
    /// pack lacks, or the same name asked for as one of the game's variables.
    /// </summary>
    [Fact]
    public void APacksVariableIsNotedWithItsDescription()
    {
        var vm = new MainViewModel();
        const string said = "Set once the player has met Anna at the beach.";
        vm.Variables.Add(new PackVariableViewModel(new PackVariableDef { Name = "metAnna", Type = PackVariableType.Bool, Description = said }));
        vm.Variables.Add(new PackVariableViewModel(new PackVariableDef { Name = "plain", Type = PackVariableType.Int }));
        vm.Variables.Add(new PackVariableViewModel(new PackVariableDef { Name = "spaced", Type = PackVariableType.String, Description = "  \r\n  " }));

        Assert.Equal(said, OptionNotes.NoteFor(OptionNotes.PackVariable, "metAnna"));
        Assert.Equal(said, OptionNotes.NoteFor(OptionNotes.PackVariable, "$metAnna"));
        Assert.Equal(said, OptionNotes.NoteFor(OptionNotes.PackVariable, "METANNA"));
        Assert.Equal("", OptionNotes.NoteFor(OptionNotes.PackVariable, "plain"));
        Assert.Equal("", OptionNotes.NoteFor(OptionNotes.PackVariable, "spaced"));
        Assert.Equal("", OptionNotes.NoteFor(OptionNotes.PackVariable, "nobody"));
        Assert.Equal("", OptionNotes.NoteFor(OptionNotes.GameVariable, "metAnna"));
    }

    /// <summary>
    /// A description of many paragraphs: blank lines are closed up, and past
    /// <see cref="MainViewModel.PackVariableNoteLength"/> it is cut at a word
    /// with an ellipsis. A short one comes back whole (the control).
    /// </summary>
    [Fact]
    public void ALongDescriptionIsCutAtAWord_AndBlankLinesClosedUp()
    {
        var vm = new MainViewModel();
        string essay = string.Join(" ", Enumerable.Range(1, 120).Select(i => "word" + i));
        vm.Variables.Add(new PackVariableViewModel(new PackVariableDef { Name = "essay", Type = PackVariableType.Int, Description = essay }));
        vm.Variables.Add(new PackVariableViewModel(new PackVariableDef { Name = "lines", Type = PackVariableType.Int,
                                                                        Description = "First line.\r\n\r\n\r\nSecond line.\r\nThird." }));

        string note = OptionNotes.NoteFor(OptionNotes.PackVariable, "essay");
        _out.WriteLine(note);
        Assert.True(note.Length <= MainViewModel.PackVariableNoteLength + 1, $"{note.Length} characters");
        Assert.EndsWith("…", note);
        string kept = note.TrimEnd('…');
        Assert.StartsWith(kept, essay);
        Assert.Equal(' ', essay[kept.Length]);     // cut between words, not inside one
        Assert.Equal("First line.\nSecond line.\nThird.", OptionNotes.NoteFor(OptionNotes.PackVariable, "lines"));
    }

    /// <summary>
    /// Every dropdown of the pack's variables in the window asks for their
    /// descriptions, under the name: a box listing them that says nothing would
    /// be the one place an author looks and finds no note. Read from
    /// MainWindow.xaml, so a new box that forgets fails here.
    /// </summary>
    [Fact]
    public void EveryListOfThePacksVariablesAsksForTheirDescriptions()
    {
        string xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(GameRun.RepoRoot(), "SMSModForge", "MainWindow.xaml"));
        var boxes = System.Text.RegularExpressions.Regex.Matches(xaml, @"<ComboBox\b[^>]*/>|<ComboBox\b.*?</ComboBox>",
                                                                  System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(b => b.Contains("VariableNameOptionsGrouped") || b.Contains("OutfitVariableOptions"))
            .ToList();
        _out.WriteLine($"{boxes.Count} dropdowns list the pack's variables");
        Assert.True(boxes.Count >= 10, $"the scan found {boxes.Count} dropdowns, fewer than the window has");
        foreach (string b in boxes)
        {
            bool asks = b.Contains("view:OptionNotes.Kind=\"packVariable\"")
                        || b.Contains("<Setter Property=\"view:OptionNotes.Kind\" Value=\"packVariable\"/>");
            bool under = b.Contains("ItemContainerStyle=\"{StaticResource HintedItem}\"")
                         || b.Contains("<Setter Property=\"ItemContainerStyle\" Value=\"{StaticResource HintedItem}\"/>");
            Assert.True(asks && under, "a dropdown of the pack's variables without their notes:\n" + b.Substring(0, System.Math.Min(400, b.Length)));
        }
    }
}
