using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Services;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>What is said to edit a pack is counted once for the whole process (UndoService.Touch), so the tests
/// that count looks run with nothing else running beside them.</summary>
[CollectionDefinition("UndoSteps", DisableParallelization = true)]
public sealed class UndoStepsCollection { }

/// <summary>
/// The undo steps themselves (Services.UndoService, reworked in 1.10.0), on a
/// real pack taken in pieces and put back by its pieces, as the editor does:
/// <list type="bullet">
/// <item>a box left or a button pressed with nothing said to have edited the
/// pack looks at nothing - control: once something is said, it looks;</item>
/// <item>a checkpoint that knows an edit ended looks whatever was said;</item>
/// <item>an edit nobody announced is a step of its own before undo moves, so
/// one press takes back that edit and not the one before it too;</item>
/// <item>the steps ahead are kept until the pack is edited, and gone then;</item>
/// <item>what only the history holds is packed small, and reads back exactly.</item>
/// </list>
/// </summary>
[Collection("UndoSteps")]
public sealed class UndoServiceTests
{
    private sealed class Host
    {
        public readonly ModPack Pack = PackRepository.CreateEmpty("steps");
        public readonly UndoService Undo;
        public string? Language;
        public int Restores, StateChanges;

        public Host()
        {
            Pack.Variables.Add(new PackVariableDef { Name = "a", Type = PackVariableType.Int, DefaultValue = "0" });
            Pack.Variables.Add(new PackVariableDef { Name = "b", Type = PackVariableType.Int, DefaultValue = "0" });
            // An entry long enough to be worth packing.
            var long1 = new DialogueDef { Key = "long" };
            for (int i = 0; i < 60; i++) long1.Nodes.Add(new DialogueNodeDef { Id = i + 1, Text = "A line of the long conversation, number " + i + "." });
            Pack.Dialogues.Add(long1);
            Undo = new UndoService(like => PackPieces.Take(Pack, like, Language));
            // As the editor does: the step asked for is put into the pack, by its pieces.
            Undo.RestoreRequested += r =>
            {
                Restores++;
                Assert.True(PackPieces.Apply(Pack, r.Changes, out _));
            };
            Undo.Reset();
            Undo.StateChanged += () => StateChanges++;
        }

        public string Text => PackRepository.Serialize(Pack);
        public void Set(string value) => Pack.Variables[0].DefaultValue = value;
    }

    [Fact]
    public void WithNothingSaidToHaveEditedThePack_NothingIsLookedAt()
    {
        var h = new Host();
        int looks = h.Undo.Looks;
        h.Undo.CheckpointIfTouched();
        h.Undo.CheckpointIfTouched();
        Assert.Equal(looks, h.Undo.Looks);

        // Control: said, it looks - and finds the pack as it was, so no step.
        UndoService.Touch();
        h.Undo.CheckpointIfTouched();
        Assert.Equal(looks + 1, h.Undo.Looks);
        Assert.Equal(0, h.Undo.Depth);
        // ... once: the next one has nothing new to go on.
        h.Undo.CheckpointIfTouched();
        Assert.Equal(looks + 1, h.Undo.Looks);

        // Said and edited: a step.
        h.Set("1");
        UndoService.Touch();
        h.Undo.CheckpointIfTouched();
        Assert.Equal(looks + 2, h.Undo.Looks);
        Assert.Equal(1, h.Undo.Depth);
    }

    [Fact]
    public void ACheckpointThatKnowsAnEditEndedLooksWhateverWasSaid()
    {
        var h = new Host();
        int looks = h.Undo.Looks;
        h.Set("1");                       // nobody says
        h.Undo.CheckpointIfTouched();
        Assert.Equal(0, h.Undo.Depth);    // the lazy one cannot know
        h.Undo.Checkpoint();
        Assert.Equal(looks + 1, h.Undo.Looks);
        Assert.Equal(1, h.Undo.Depth);
        // And with nothing changed it looks, and takes no step.
        h.Undo.Checkpoint();
        Assert.Equal(looks + 2, h.Undo.Looks);
        Assert.Equal(1, h.Undo.Depth);
    }

    [Fact]
    public void AnEditNobodyAnnouncedIsAStepOfItsOwnBeforeUndoMoves()
    {
        var h = new Host();
        string s0 = h.Text;
        h.Set("1");
        h.Undo.Checkpoint();
        string s1 = h.Text;
        h.Set("2");                       // nobody says, and no checkpoint
        string s2 = h.Text;

        h.Undo.Undo();
        Assert.Equal(s1, h.Text);         // that edit only - not back to the start
        Assert.Equal(1, h.Undo.Depth);
        Assert.Equal(1, h.Undo.RedoDepth);
        h.Undo.Undo();
        Assert.Equal(s0, h.Text);
        Assert.False(h.Undo.CanUndo);
        h.Undo.Undo();                    // nothing further back: nothing happens
        Assert.Equal(s0, h.Text);
        Assert.Equal(2, h.Restores);

        h.Undo.Redo();
        Assert.Equal(s1, h.Text);
        h.Undo.Redo();
        Assert.Equal(s2, h.Text);
        Assert.False(h.Undo.CanRedo);
        h.Undo.Redo();
        Assert.Equal(s2, h.Text);
        Assert.Equal(4, h.Restores);
    }

    [Fact]
    public void TheStepsAheadAreKeptUntilThePackIsEdited_AndGoneThen()
    {
        var h = new Host();
        h.Set("1"); h.Undo.Checkpoint();
        h.Set("2"); h.Undo.Checkpoint();
        h.Undo.Undo();
        h.Undo.Undo();
        Assert.Equal(2, h.Undo.RedoDepth);

        // Boxes left, buttons pressed, things said - and the pack as it is: still there.
        h.Undo.CheckpointIfTouched();
        UndoService.Touch();
        h.Undo.CheckpointIfTouched();
        h.Undo.Checkpoint();
        Assert.Equal(2, h.Undo.RedoDepth);
        h.Undo.Redo();
        Assert.Equal(1, h.Undo.RedoDepth);
        Assert.Equal("1", h.Pack.Variables[0].DefaultValue);

        // An edit: what was ahead is gone, whoever finds the edit first - here Redo itself, which then has
        // nowhere to go and leaves the edit alone.
        h.Set("7");
        string edited = h.Text;
        h.Undo.Redo();
        Assert.Equal(edited, h.Text);
        Assert.Equal(0, h.Undo.RedoDepth);
        Assert.Equal(2, h.Undo.Depth);
        h.Undo.Undo();
        Assert.Equal("1", h.Pack.Variables[0].DefaultValue);
    }

    [Fact]
    public void WhileARestoreIsBeingPutBack_NothingIsTaken()
    {
        var h = new Host();
        h.Set("1"); h.Undo.Checkpoint();
        int looks = h.Undo.Looks;
        h.Undo.Suspended = true;
        h.Set("half put back");
        UndoService.Touch();
        h.Undo.CheckpointIfTouched();
        h.Undo.Checkpoint();
        Assert.Equal(looks, h.Undo.Looks);
        Assert.Equal(1, h.Undo.Depth);
        h.Undo.Suspended = false;
        h.Undo.CheckpointIfTouched();
        Assert.Equal(2, h.Undo.Depth);
    }

    [Fact]
    public void WhatTheEditorDidToThePackItselfIsNotAStep()
    {
        // A pack opened or put back is tidied by the editor (a list made, a default filled in): taken in as where
        // the editor is, it is not an edit of the author's.
        var h = new Host();
        h.Set("1"); h.Undo.Checkpoint();
        h.Pack.Variables.Add(new PackVariableDef { Name = "tidied", Type = PackVariableType.Bool, DefaultValue = "false" });
        UndoService.Touch();
        h.Undo.AbsorbCurrentAsBaseline();
        h.Undo.CheckpointIfTouched();
        h.Undo.Checkpoint();
        Assert.Equal(1, h.Undo.Depth);
        // Control: without it, the same change is a step.
        h.Pack.Variables.Add(new PackVariableDef { Name = "edited", Type = PackVariableType.Bool, DefaultValue = "false" });
        h.Undo.Checkpoint();
        Assert.Equal(2, h.Undo.Depth);

        h.Undo.Reset();
        Assert.False(h.Undo.CanUndo);
        Assert.False(h.Undo.CanRedo);
    }

    [Fact]
    public void TheButtonsAreToldWhenThereIsSomethingToUndoOrRedo_AndOnlyThen()
    {
        var h = new Host();
        h.Undo.CheckpointIfTouched();
        h.Undo.Checkpoint();
        Assert.Equal(0, h.StateChanges);
        h.Set("1"); h.Undo.Checkpoint();
        Assert.Equal(1, h.StateChanges);
        h.Undo.Undo();
        Assert.Equal(2, h.StateChanges);
        h.Undo.Redo();
        Assert.Equal(3, h.StateChanges);
        h.Undo.Reset();
        Assert.Equal(4, h.StateChanges);
    }

    [Fact]
    public void TheWordsOfTheLanguageBeingEditedArePartOfAStep()
    {
        var h = new Host();
        h.Language = "de\nHallo";
        h.Undo.Reset();
        h.Language = "de\nGuten Tag";     // a translation typed: the pack itself is as it was
        h.Undo.Checkpoint();
        Assert.Equal(1, h.Undo.Depth);
        UndoService.Restore? asked = null;
        h.Undo.RestoreRequested += r => asked = r;
        h.Undo.Undo();
        Assert.NotNull(asked);
        Assert.Equal("de\nHallo", asked!.To.Language);
        Assert.Equal("de\nGuten Tag", asked.From.Language);
        Assert.Empty(asked.Changes);      // nothing of the pack to put back
    }

    [Fact]
    public void WhatOnlyTheHistoryHoldsIsPackedSmall_AndReadsBackExactly()
    {
        UndoService.PackOnThisThreadForTests = true;
        try
        {
            var h = new Host();
            string s0 = h.Text;
            var first = h.Undo.Baseline;
            int d = Array.IndexOf(first.Names, "dialogues");
            Assert.True(d >= 0);
            var longPiece = first.Entries[d][0];
            Assert.True(longPiece.Text.Length >= 2048, "the long conversation is too short to be packed: the test holds nothing");

            // The step shares the long conversation with the one before: live, not packed.
            h.Set("1"); h.Undo.Checkpoint();
            Assert.Same(longPiece, h.Undo.Baseline.Entries[d][0]);
            Assert.False(longPiece.IsPacked);

            // The conversation edited: the piece the history alone holds is packed, the one the editor is at is not.
            h.Pack.Dialogues[0].Nodes[0].Text = "Another first line.";
            h.Undo.Checkpoint();
            string s2 = h.Text;
            Assert.NotSame(longPiece, h.Undo.Baseline.Entries[d][0]);
            Assert.True(longPiece.IsPacked, "a piece only the history holds was left unpacked");
            Assert.False(h.Undo.Baseline.Entries[d][0].IsPacked);

            // Back through both, and forward again: exactly what was kept.
            h.Undo.Undo();
            h.Undo.Undo();
            Assert.Equal(s0, h.Text);
            h.Undo.Redo();
            h.Undo.Redo();
            Assert.Equal(s2, h.Text);
            Assert.Equal(0, h.Undo.RedoDepth);
        }
        finally { UndoService.PackOnThisThreadForTests = false; }
    }

    [Fact]
    public void ManyStepsBackAndForward_EveryOneThePackItWas()
    {
        var h = new Host();
        var states = new List<string> { h.Text };
        var random = new Random(20261120);
        for (int i = 0; i < 40; i++)
        {
            switch (random.Next(5))
            {
                case 0: h.Pack.Variables.Add(new PackVariableDef { Name = "v" + i, Type = PackVariableType.Int, DefaultValue = i.ToString() }); break;
                case 1: if (h.Pack.Variables.Count > 1) h.Pack.Variables.RemoveAt(random.Next(h.Pack.Variables.Count)); else h.Set("only" + i); break;
                case 2: h.Pack.Variables.Reverse(); h.Pack.Variables[0].DefaultValue = "r" + i; break;
                case 3: h.Pack.Dialogues[0].Nodes[random.Next(h.Pack.Dialogues[0].Nodes.Count)].Text = "Line changed at " + i; break;
                default: h.Pack.Dialogues.Add(new DialogueDef { Key = "d" + i }); break;
            }
            h.Undo.Checkpoint();
            states.Add(h.Text);
        }
        Assert.Equal(40, h.Undo.Depth);
        // A walk up and down the history, held at every stop to the pack that was there.
        int at = 40;
        for (int i = 0; i < 200; i++)
        {
            bool back = at == 40 || (at > 0 && random.Next(2) == 0);
            if (back) { h.Undo.Undo(); at--; } else { h.Undo.Redo(); at++; }
            Assert.True(states[at] == h.Text, $"after {i + 1} moves the pack is not the one of step {at}");
            Assert.Equal(at, h.Undo.Depth);
            Assert.Equal(40 - at, h.Undo.RedoDepth);
        }
    }
}
