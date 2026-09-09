using System.Linq;
using SMSModForge.Model;
using SMSModForge.Validation;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Editing a vanilla conversation: choosing one, seeing what has been changed,
/// and putting any of it back.
/// <para/>
/// The two halves have to agree. A marker that says a line is changed when it
/// is not makes 118 unchanged lines look edited; a reset that leaves something
/// behind makes "put it back" a lie. Both are asserted against each other here
/// rather than separately.
/// </summary>
public sealed class VanillaDialogueEditingTests
{
    private readonly ITestOutputHelper _out;
    public VanillaDialogueEditingTests(ITestOutputHelper o) => _out = o;

    private const string Anna = "8_Room_Talk/Beach/AnnaBeachDefault";

    private static DialogueViewModel Extension()
    {
        var vm = new DialogueViewModel(new DialogueDef());
        vm.WantsVanilla = true;
        vm.VanillaSource = VanillaDialogueCatalog.Find(Anna);
        return vm;
    }

    [Fact]
    public void ChoosingAConversationFillsItIn()
    {
        var vm = new DialogueViewModel(new DialogueDef());
        Assert.False(vm.IsVanillaBased);
        Assert.False(vm.ShowsSourcePicker);
        Assert.Empty(vm.Nodes);

        vm.WantsVanilla = true;
        Assert.True(vm.ShowsSourcePicker);

        vm.VanillaSource = VanillaDialogueCatalog.Find(Anna);

        Assert.True(vm.IsVanillaBased);
        Assert.Equal(118, vm.Nodes.Count);
        Assert.Equal(118, vm.Model.Nodes.Count);
        Assert.Equal("AnnaBeachDefault", vm.Model.DisplayName);
        Assert.Equal("unchanged from the game", vm.ChangeSummary);

        // The room's gate is readable but not the pack's to assert.
        Assert.Single(vm.VanillaGates);
        Assert.Empty(vm.Model.StartConditions);
    }

    /// <summary>
    /// The control for every marker below: a freshly seeded conversation marks
    /// nothing.
    /// <para/>
    /// Without it, "this line is changed" would be indistinguishable from a
    /// comparison that always says yes.
    /// </summary>
    [Fact]
    public void NothingIsMarkedUntilSomethingChanges()
    {
        var vm = Extension();
        Assert.All(vm.Model.Nodes, node => Assert.False(vm.HasChanges(node)));

        var line = vm.Model.Nodes.First(n => n.Id == vm.Model.RootNodeIds[0]);
        line.Text = "Come on, I brought sunscreen.";

        Assert.True(vm.HasChanges(line));
        Assert.Equal(new[] { "text" }, vm.ChangedFields(line));
        Assert.Equal(1, vm.Model.Nodes.Count(vm.HasChanges));
        Assert.Equal("1 line changed", vm.ChangeSummary);
    }

    [Fact]
    public void ResettingOneFieldPutsThatFieldBack()
    {
        var vm = Extension();
        var line = vm.Model.Nodes.First(n => n.Id == vm.Model.RootNodeIds[0]);

        string original = line.Text;
        line.Text = "Something else entirely.";
        line.Tag = "marked";
        Assert.Equal(new[] { "text", "tag" }, vm.ChangedFields(line).OrderByDescending(f => f).ToArray());

        vm.ResetField(line, "text");

        // The one field is back; the other is still the author's.
        Assert.Equal(original, line.Text);
        Assert.Equal("marked", line.Tag);
        Assert.Equal(new[] { "tag" }, vm.ChangedFields(line));

        vm.ResetField(line, "tag");
        Assert.False(vm.HasChanges(line));
        Assert.Equal("unchanged from the game", vm.ChangeSummary);
    }

    [Fact]
    public void ResettingALinePutsAllOfItBack()
    {
        var vm = Extension();
        var line = vm.Model.Nodes.First(n => n.Id == vm.Model.RootNodeIds[0]);

        string text = line.Text;
        int conditions = line.Conditions.Count;

        line.Text = "Changed.";
        line.Conditions.Clear();
        line.ActionsOnStart.Add(new NodeActionDef { Type = NodeActionTypes.EmitSignal });
        Assert.True(vm.HasChanges(line));

        vm.ResetNode(line);

        Assert.Equal(text, line.Text);
        Assert.Equal(conditions, line.Conditions.Count);
        Assert.Empty(line.ActionsOnStart);
        Assert.False(vm.HasChanges(line));
    }

    /// <summary>Resetting the conversation brings back lines that were deleted,
    /// which resetting line by line cannot.</summary>
    [Fact]
    public void ResettingTheConversationBringsBackDeletedLines()
    {
        var vm = Extension();
        vm.Model.Nodes.RemoveAt(5);
        vm.Model.Nodes[0].Text = "Changed.";
        Assert.Equal(117, vm.Model.Nodes.Count);
        Assert.Contains("removed", vm.ChangeSummary);

        vm.ResetAll();

        Assert.Equal(118, vm.Model.Nodes.Count);
        Assert.Equal(118, vm.Nodes.Count);
        Assert.Equal("unchanged from the game", vm.ChangeSummary);
    }

    /// <summary>
    /// What the markers say and what the manifest stores must agree.
    /// <para/>
    /// They are computed by the same code for that reason — the row asks
    /// against the live model while the author types, and the save asks again
    /// on the way to disk. If those ever disagreed, an author would be shown
    /// one thing and ship another.
    /// </summary>
    [Fact]
    public void WhatIsMarkedIsWhatIsStored()
    {
        var vm = Extension();
        var vanilla = VanillaDialogueCatalog.Open(Anna)!;

        vm.Model.Nodes[3].Text = "One.";
        vm.Model.Nodes[9].Tag = "two";

        var marked = vm.Model.Nodes.Where(vm.HasChanges).Select(n => n.Id).OrderBy(i => i).ToList();
        var stored = VanillaDialogueDelta.Prune(vm.Model.Nodes, vanilla)
                                         .Select(n => n.Id).OrderBy(i => i).ToList();

        _out.WriteLine($"marked {marked.Count}, stored {stored.Count}");
        Assert.Equal(marked, stored);
        Assert.Equal(2, stored.Count);
    }

    /// <summary>
    /// Open, edit, save, re-open — through the view model, the way an author
    /// does it.
    /// <para/>
    /// The delta tests prove the manifest is small; this proves the editor is
    /// still usable afterwards. They are different failures: a pack that saves
    /// perfectly and re-opens as two lines is worse than one that stores too
    /// much.
    /// </summary>
    [Fact]
    public void AnEditedConversationSurvivesBeingSavedAndReopened()
    {
        var pack = new ModPack();
        var vm = Extension();
        pack.Dialogues.Add(vm.Model);

        vm.Model.Nodes.First(n => n.Id == vm.Model.RootNodeIds[0]).Text = "Sunscreen?";
        Assert.Equal("1 line changed", vm.ChangeSummary);

        string json = PackRepository.SerializeAsSaved(pack);
        var reopened = PackRepository.Deserialize(json)!;

        // The view model is what an author gets back, and it has all of it.
        var back = new DialogueViewModel(reopened.Dialogues.Single());
        Assert.Equal(118, back.Nodes.Count);
        Assert.True(back.IsVanillaBased);
        Assert.Equal("1 line changed", back.ChangeSummary);

        var line = back.Model.Nodes.First(n => n.Id == back.Model.RootNodeIds[0]);
        Assert.Equal("Sunscreen?", line.Text);
        Assert.Equal(new[] { "text" }, back.ChangedFields(line));

        // Everything else came back as the game has it, and says so.
        Assert.Equal(1, back.Model.Nodes.Count(back.HasChanges));

        // And it can still be put back, after a round trip.
        back.ResetField(line, "text");
        Assert.False(back.HasChanges(line));
        Assert.Equal("unchanged from the game", back.ChangeSummary);
    }

    /// <summary>
    /// The row itself knows whether it differs, and follows an edit.
    /// <para/>
    /// A marker that is right only until the author types would be worse than
    /// none: a line they just changed reading "unchanged" is the one case where
    /// they would trust it and be wrong.
    /// </summary>
    [Fact]
    public void ARowMarksItselfAndKeepsUp()
    {
        var vm = Extension();
        var row = vm.Nodes.First(n => n.Model.Id == vm.Model.RootNodeIds[0]);

        Assert.True(row.IsVanillaLine);
        Assert.False(row.IsChangedFromVanilla);
        Assert.Equal("", row.ChangedFieldsText);
        Assert.False(row.ResetToVanillaCommand.CanExecute(null));

        int marked = 0;
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(row.IsChangedFromVanilla)) marked++;
        };

        row.Text = "Sunscreen?";

        Assert.True(row.IsChangedFromVanilla);
        Assert.Contains("text", row.ChangedFieldsText);
        Assert.True(marked > 0, "the row was never told its marker might have changed");
        Assert.True(row.ResetToVanillaCommand.CanExecute(null));

        row.ResetToVanillaCommand.Execute(null);
        Assert.False(row.IsChangedFromVanilla);
    }

    /// <summary>A row added after the conversation was built is adopted too —
    /// the wiring is a subscription, not a call at each site.</summary>
    [Fact]
    public void ARowAddedLaterIsStillPartOfTheConversation()
    {
        var vm = Extension();
        var added = new DialogueNodeViewModel(new DialogueNodeDef { Id = 4242, Text = "New." });
        Assert.Null(added.Owner);

        vm.Nodes.Add(added);

        Assert.Same(vm, added.Owner);
        Assert.True(added.IsVanillaLine);
    }

    /// <summary>
    /// The game's cast counts as declared.
    /// <para/>
    /// A change to one of the game's conversations speaks with the game's own
    /// actors, who will never be in the pack. Reported as unknown, every
    /// untouched line of a 118-line conversation was a warning about it saying
    /// what it already said.
    /// </summary>
    [Fact]
    public void TheGamesOwnActorsAreNotReportedAsMissing()
    {
        var pack = new ModPack();
        pack.Dialogues.Add(Extension().Model);

        var issues = PackValidator.Validate(pack, "")
            .Where(i => i.Code == "node.unknownActor")
            .ToList();

        foreach (var issue in issues.Take(3)) _out.WriteLine(issue.Message);
        Assert.Empty(issues);
    }

    /// <summary>
    /// The control for that: an actor who is in neither the pack nor the
    /// conversation is still reported.
    /// </summary>
    [Fact]
    public void AnActorInNeitherThePackNorTheSceneIsStillReported()
    {
        var pack = new ModPack();
        var vm = Extension();
        pack.Dialogues.Add(vm.Model);

        vm.Model.Nodes[0].Actor = "SomebodyWhoIsNotInThisScene";

        var issues = PackValidator.Validate(pack, "")
            .Where(i => i.Code == "node.unknownActor")
            .ToList();

        Assert.Single(issues);
        Assert.Contains("SomebodyWhoIsNotInThisScene", issues[0].Message);

        // And a NEW line given to somebody already in the conversation is fine,
        // which is the point of allowing the cast at all.
        vm.Model.Nodes[0].Actor = "Adrian";
        Assert.Empty(PackValidator.Validate(pack, "").Where(i => i.Code == "node.unknownActor"));
    }

    /// <summary>
    /// The summary keeps up with what the author does.
    /// <para/>
    /// It used to be worked out when a conversation was picked and never again,
    /// so it read "unchanged from the game" however much had been done to it —
    /// which is worse than showing nothing, because it is an answer.
    /// </summary>
    [Fact]
    public void TheSummaryFollowsEditsAndAdditions()
    {
        var vm = Extension();
        int told = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.ChangeSummary)) told++;
        };

        Assert.Equal("unchanged from the game", vm.ChangeSummary);
        Assert.False(vm.HasAnyChanges);

        // Typing into a line.
        vm.Nodes.First(n => n.Model.Id == vm.Model.RootNodeIds[0]).Text = "Sunscreen?";
        Assert.Equal("1 line changed", vm.ChangeSummary);
        Assert.True(vm.HasAnyChanges);
        Assert.True(told > 0, "nothing told the header the summary had moved");

        // Adding a line of the pack's own, counted as an addition rather than
        // lumped in with the line that was edited.
        vm.AddNode(parentId: null);
        Assert.Equal("1 added, 1 line changed", vm.ChangeSummary);

        // And removing one of the game's.
        vm.Model.Nodes.RemoveAt(4);
        vm.Nodes.RemoveAt(4);
        Assert.Contains("removed", vm.ChangeSummary);
    }

    /// <summary>
    /// Adding a line says so, rather than counting as two changes.
    /// <para/>
    /// Adding one under another really does change that other line - its list
    /// of what follows it is different now - so "2 lines changed" was true and
    /// read as a miscount. Said apart, both facts are visible.
    /// </summary>
    [Fact]
    public void AddingALineIsCountedAsAnAddition()
    {
        var vm = Extension();

        vm.AddNode(parentId: null);
        Assert.Equal("1 added", vm.ChangeSummary);

        // Under an existing line, the parent genuinely changed too.
        vm.AddNode(parentId: vm.Model.RootNodeIds[0]);
        Assert.Equal("2 added, 1 line changed", vm.ChangeSummary);
    }

    /// <summary>
    /// Resetting a line the pack added removes it, because the game has no
    /// version of it to go back to.
    /// </summary>
    [Fact]
    public void ResettingAnAddedLineTakesItOut()
    {
        var vm = Extension();
        var added = vm.AddNode(parentId: null);

        Assert.True(added.IsAddedLine);
        Assert.True(added.IsChangedFromVanilla);
        Assert.Contains("Remove this line", added.ResetTooltip);
        Assert.Empty(added.ResettableFields);      // nothing underneath to put back

        Assert.Equal(119, vm.Model.Nodes.Count);
        added.ResetToVanillaCommand.Execute(null);

        Assert.Equal(118, vm.Model.Nodes.Count);
        Assert.DoesNotContain(vm.Model.RootNodeIds, r => r == added.Model.Id);
        Assert.Equal("unchanged from the game", vm.ChangeSummary);
    }

    /// <summary>Each changed field offers its own way back, and only the
    /// changed ones.</summary>
    [Fact]
    public void EachChangedFieldCanBePutBackOnItsOwn()
    {
        var vm = Extension();
        var row = vm.Nodes.First(n => n.Model.Id == vm.Model.RootNodeIds[0]);
        Assert.Empty(row.ResettableFields);

        string line = row.Text;
        row.Text = "Sunscreen?";
        row.Model.Tag = "marked";
        row.RefreshAll();

        var offered = row.ResettableFields;
        Assert.Equal(new[] { "line", "tag" }, offered.Select(f => f.Label).OrderBy(l => l).ToArray());

        offered.First(f => f.Field == "text").Reset.Execute(null);
        Assert.Equal(line, row.Model.Text);
        Assert.Equal("marked", row.Model.Tag);

        row.RefreshAll();
        Assert.Equal(new[] { "tag" }, row.ResettableFields.Select(f => f.Label).ToArray());
    }

    /// <summary>The game's conversation keeps the game's name.</summary>
    [Fact]
    public void AVanillaConversationCannotBeRenamed()
    {
        var vm = Extension();
        Assert.False(vm.NameIsEditable);
        Assert.True(vm.NameIsReadOnly);

        vm.DisplayName = "Something Else";
        Assert.Equal("AnnaBeachDefault", vm.DisplayName);

        // And from the moment it is going to be one, before a conversation has
        // been picked - which is the state a just-added extension sits in, and
        // where a typed name would be thrown away by the picker anyway.
        var pending = new DialogueViewModel(new DialogueDef { DisplayName = "New Dialogue" });
        pending.WantsVanilla = true;
        Assert.True(pending.NameIsReadOnly);
        pending.DisplayName = "Typed anyway";
        Assert.Equal("New Dialogue", pending.DisplayName);

        // A dialogue of the pack's own is still the author's to name.
        var own = new DialogueViewModel(new DialogueDef());
        Assert.True(own.NameIsEditable);
        own.DisplayName = "Mine";
        Assert.Equal("Mine", own.DisplayName);
    }

    /// <summary>A dialogue of the pack's own is not offered any of this — there
    /// is nothing underneath it to compare against or reset to.</summary>
    [Fact]
    public void APacksOwnDialogueHasNoBaseline()
    {
        var vm = new DialogueViewModel(new DialogueDef());
        var own = new DialogueNodeDef { Id = 1, Text = "Mine." };
        vm.Model.Nodes.Add(own);

        Assert.False(vm.IsVanillaBased);
        Assert.False(vm.HasChanges(own));
        Assert.Empty(vm.VanillaGates);
        Assert.Equal("", vm.ChangeSummary);

        // And resetting is a no-op rather than a blank-out.
        vm.ResetNode(own);
        vm.ResetAll();
        Assert.Equal("Mine.", own.Text);
    }
}
