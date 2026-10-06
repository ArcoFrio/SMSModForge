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
}
