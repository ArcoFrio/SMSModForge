using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The shipped vanilla dialogue catalog, read the way the editor reads it.
/// <para/>
/// Built by Tools/Dialogue/BuildDialogueCatalog.py from a runtime extraction
/// and committed, so these numbers are facts about the game rather than about
/// whoever ran the extractor. The test subject is AnnaBeachDefault, surveyed
/// against the raw dump before the catalog existed: 118 nodes, 113 spoken and
/// 5 choices, four roots, four roles, nine conditioned lines.
/// <para/>
/// The point of most of these is that a construct the editor cannot EDIT is
/// still one it can SHOW. Losing that distinction is how an extension screen
/// silently drops half a conversation.
/// </summary>
public sealed class VanillaDialogueCatalogTests
{
    private readonly ITestOutputHelper _out;
    public VanillaDialogueCatalogTests(ITestOutputHelper o) => _out = o;

    private const string Anna = "8_Room_Talk/Beach/AnnaBeachDefault";

    [Fact]
    public void CatalogShipsEveryDialogue()
    {
        Assert.True(VanillaDialogueCatalog.IsAvailable,
                    "the catalog did not load - is VanillaDialogues/ being copied to the output?");

        Assert.Equal(722, VanillaDialogueCatalog.All.Count);
        Assert.Equal(19653, VanillaDialogueCatalog.All.Sum(e => e.Nodes));

        // Ids address a conversation, so two conversations may not share one.
        var repeated = VanillaDialogueCatalog.All
            .GroupBy(e => e.Id, System.StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(repeated);
    }

    /// <summary>
    /// The control for every lookup below.
    /// <para/>
    /// A catalog that answered everything would pass the rest of this file
    /// while holding nothing, because a miss and a hit would read the same.
    /// </summary>
    [Fact]
    public void ADialogueTheGameDoesNotHaveIsNotFound()
    {
        Assert.Null(VanillaDialogueCatalog.Find("8_Room_Talk/Beach/NoSuchDialogue"));
        Assert.Null(VanillaDialogueCatalog.Open("8_Room_Talk/Beach/NoSuchDialogue"));
        Assert.Null(VanillaDialogueCatalog.Find(""));
        Assert.Null(VanillaDialogueCatalog.Find(null));

        // And the one that does exist is found, by path and by token alike.
        Assert.NotNull(VanillaDialogueCatalog.Find(Anna));
        Assert.NotNull(VanillaDialogueCatalog.Find(
            VanillaDialogueCatalog.TokenPrefix + Anna));
    }

    [Fact]
    public void ADialogueOpensWithWhatTheGameHas()
    {
        var dialogue = VanillaDialogueCatalog.Open(Anna);
        Assert.NotNull(dialogue);

        Assert.Equal("AnnaBeachDefault", dialogue!.Name);
        Assert.Equal("Default_Dialogue", dialogue.Skin);
        Assert.Equal(new[] { "Adrian", "Anna", "Continue", "Samantha" }, dialogue.Roles);
        Assert.Equal(118, dialogue.Nodes.Count);
        Assert.Equal(4, dialogue.Roots.Count);

        Assert.Equal(113, dialogue.Nodes.Values.Count(n => n.Kind == "text"));
        Assert.Equal(5, dialogue.Nodes.Values.Count(n => n.Kind == "choice"));

        Assert.Equal(9, dialogue.Nodes.Values.Count(n => n.Conditions.Count > 0));
        Assert.Equal(21, dialogue.Nodes.Values.Count(n => n.OnStart.Count > 0));
        Assert.Equal(7, dialogue.Nodes.Values.Count(n => n.OnFinish.Count > 0));

        var first = dialogue.Node(dialogue.Roots[0]);
        Assert.NotNull(first);
        Assert.Equal("Anna", first!.Actor);
        Assert.Equal("Come on, don’t pout. This is supposed to be fun!", first.PlainText);
    }

    /// <summary>Every child and root names a node the catalog actually holds.
    /// A hole here is a tree the editor would draw with a line missing.</summary>
    [Fact]
    public void EveryNodeReferencePointsSomewhere()
    {
        int checkedRefs = 0;
        foreach (var entry in VanillaDialogueCatalog.All)
        {
            var dialogue = VanillaDialogueCatalog.Open(entry.Id);
            Assert.NotNull(dialogue);

            foreach (var root in dialogue!.Roots)
            {
                Assert.True(dialogue.Node(root) != null,
                            entry.Id + " starts at a node it does not have: " + root);
                checkedRefs++;
            }
            foreach (var node in dialogue.Nodes.Values)
                foreach (var child in node.Children)
                {
                    Assert.True(dialogue.Node(child) != null,
                                entry.Id + " names a child it does not have: " + child);
                    checkedRefs++;
                }
        }

        _out.WriteLine($"{checkedRefs} node references, all resolved");
        Assert.True(checkedRefs > 19000);
    }

    /// <summary>
    /// A condition the editor models arrives as a variable it can address.
    /// <para/>
    /// This one gates the whole beach conversation, and it is the shape the
    /// Variable condition already speaks: a global name variable, addressed by
    /// name, with its list kept for display.
    /// </summary>
    [Fact]
    public void AModelledConditionNamesItsVariable()
    {
        var dialogue = VanillaDialogueCatalog.Open(Anna)!;
        var step = dialogue.Node(dialogue.Roots[0])!.Conditions.Single();

        Assert.Equal("compareBool", step.Kind);
        Assert.True(step.IsModelled);
        Assert.Equal("ConditionMathCompareBooleans", step.Type);

        var variable = step.Fields!["m_Value"]!["variable"]!;
        Assert.Equal("global", (string?)variable["scope"]);
        Assert.Equal("first-anna-adrian-beach-talk", (string?)variable["name"]);
        Assert.Equal("Events_D_3", (string?)variable["list"]);
        Assert.Equal("boolean", (string?)variable["type"]);
    }

    /// <summary>
    /// The requirement this whole catalog exists to meet: a construct nothing
    /// models is still shown, in the game's own words.
    /// <para/>
    /// Game Creator writes a Title for each of these because its own editor
    /// draws it on the block - "Activate Explore The City", "Scale BathB =
    /// (1.50, 1.50, 1.00)". Without it an unmodelled step would be a blank row.
    /// </summary>
    [Fact]
    public void AnUnmodelledStepStillSaysWhatItDoes()
    {
        var unmodelled = new Dictionary<string, VanillaDialogueCatalog.Step>();
        int total = 0, titled = 0;

        foreach (var entry in VanillaDialogueCatalog.All)
        {
            var dialogue = VanillaDialogueCatalog.Open(entry.Id)!;
            foreach (var node in dialogue.Nodes.Values)
                foreach (var step in node.Conditions.Concat(node.OnStart).Concat(node.OnFinish))
                {
                    total++;
                    if (!string.IsNullOrEmpty(step.Title)) titled++;
                    if (!step.IsModelled) unmodelled[step.Type] = step;
                }
        }

        _out.WriteLine($"{total} steps, {titled} with a title, "
                       + $"{unmodelled.Count} types the editor does not model");
        foreach (var pair in unmodelled.OrderBy(p => p.Key))
            _out.WriteLine($"  {pair.Key} -> {pair.Value.Describe()}");

        // Every step says what it does, modelled or not.
        Assert.Equal(total, titled);
        Assert.All(unmodelled.Values, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Describe()));
            Assert.NotEqual(step.Type, step.Describe());   // a sentence, not a type name
        });

        // And there really are some, or the assertion above proves nothing.
        Assert.NotEmpty(unmodelled);
    }

    /// <summary>
    /// A conversation says where it is played from and what has to be true
    /// first.
    /// <para/>
    /// The beach conversation is gated on the day and on its own cooldown,
    /// from the room's Conditions component. That is the whole point of the
    /// second extraction pass: none of it is anywhere in the dialogue itself.
    /// </summary>
    [Fact]
    public void ADialogueSaysWhatHasToBeTrueBeforeItPlays()
    {
        var dialogue = VanillaDialogueCatalog.Open(Anna)!;
        var start = Assert.Single(dialogue.Starts);

        Assert.Equal("8_Room_Talk/Beach", start.By);
        Assert.Equal("Conditions", start.Script);
        Assert.Equal("InstructionDialoguePlay", start.How);
        Assert.Equal("Play AnnaBeachDefault and wait", start.Title);
        Assert.False(dialogue.IsChosenAtRuntime);

        Assert.Equal(2, start.When.Count);
        Assert.Equal(new[] { "If Core[Day] = 2", "If Cooldown[anna-beach] = False" },
                     start.When.Select(c => c.Title).ToArray());

        var day = start.When[0].Fields!["m_Value"]!["variable"]!;
        Assert.Equal("Day", (string?)day["name"]);
        Assert.Equal("Core", (string?)day["list"]);

        var cooldown = start.When[1].Fields!["m_Value"]!["variable"]!;
        Assert.Equal("anna-beach", (string?)cooldown["name"]);
        Assert.Equal("Cooldown", (string?)cooldown["list"]);

        // And how it is staged, either side of the conversation itself.
        Assert.Equal(new[] { "Signal 'FadeUI'", "Set Active Anna_Swimwear to True",
                             "Set Active Adrian_Sport to True", "Wait 1 second" },
                     start.Before.Select(i => i.Title).ToArray());
        Assert.Equal(new[] { "Set Active Leave to True", "Set Active Leave to True",
                             "Set Cooldown[anna-beach] = True", "Signal 'FadeUI'" },
                     start.After.Select(i => i.Title).ToArray());
    }

    /// <summary>
    /// Start sites are found for most conversations and honestly absent for the
    /// rest.
    /// <para/>
    /// The control here is the second half. 55 conversations are picked at
    /// runtime through a variable — "Play Self/Events_Only_Done_2[fan-encounter]"
    /// — and hold no reference for anything to find. A catalog that claimed a
    /// start for all 722 would be inventing them.
    /// </summary>
    [Fact]
    public void StartsAreFoundWhereTheyExistAndAbsentWhereTheyDoNot()
    {
        int withStarts = 0, without = 0;
        foreach (var entry in VanillaDialogueCatalog.All)
        {
            var dialogue = VanillaDialogueCatalog.Open(entry.Id)!;
            Assert.Equal(entry.Starts, dialogue.Starts.Count);   // index agrees with the file

            if (dialogue.Starts.Count > 0) withStarts++;
            else without++;

            foreach (var start in dialogue.Starts)
            {
                Assert.False(string.IsNullOrWhiteSpace(start.By));
                Assert.False(string.IsNullOrWhiteSpace(start.Script));
            }
        }

        _out.WriteLine($"{withStarts} started from somewhere, {without} unreachable");

        // Seven conversations in the game are reached by nothing this can see -
        // one of them has no lines at all. Everything else is attributed: named
        // outright, picked from among a container's children by a variable, or
        // stored by one conversation and played by another.
        Assert.Equal(715, withStarts);
        Assert.Equal(7, without);
    }

    /// <summary>Reading order terminates even though this conversation jumps
    /// back to its own root.</summary>
    [Fact]
    public void ReadingOrderSurvivesAJumpBackwards()
    {
        var dialogue = VanillaDialogueCatalog.Open(Anna)!;
        Assert.Contains(dialogue.Nodes.Values, n => n.Tag == "root");

        var order = dialogue.InOrder().ToList();
        Assert.Equal(order.Count, order.Select(p => p.Key).Distinct().Count());
        Assert.All(dialogue.Roots, root => Assert.Contains(order, p => p.Key == root));
    }
}
