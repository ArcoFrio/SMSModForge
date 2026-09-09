using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What the game checks before it plays one of its own conversations, as an
/// author reads it beside the lines.
/// <para/>
/// Display only — the gate belongs to the room and an extension has no say in
/// it — which is exactly why it has to be RIGHT: it is the only thing telling
/// an author when the line they are editing is even reached.
/// </summary>
public sealed class VanillaDialogueGateTests
{
    private readonly ITestOutputHelper _out;
    public VanillaDialogueGateTests(ITestOutputHelper o) => _out = o;

    private const string Anna = "8_Room_Talk/Beach/AnnaBeachDefault";

    private static IEnumerable<NodeConditionDef> Flat(IEnumerable<NodeConditionDef> from)
    {
        foreach (var c in from)
        {
            yield return c;
            if (c.Conditions != null)
                foreach (var inner in Flat(c.Conditions)) yield return inner;
        }
    }

    [Fact]
    public void ARoomTalkIsGatedOnItsRoomBeingTheOneOnScreen()
    {
        var gates = VanillaDialogueSeed.StartGates(Anna);
        _out.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(gates));

        var flat = Flat(gates).ToList();

        // The room is not written down by the game: the component that plays
        // this lives ON the room, so being there is the unstated half of the
        // gate. Said out loud, it is the half an author most needs.
        var level = flat.Single(c => c.Type == NodeConditionTypes.LevelActive);
        Assert.Equal("vanilla:14_Beach", level.Params["level"]);

        // And it comes first, because it is the outermost thing that has to be
        // true before any of the rest is even asked.
        var all = Assert.Single(gates);
        Assert.Equal(NodeConditionTypes.GroupAll, all.Type);
        Assert.Equal(NodeConditionTypes.LevelActive, all.Conditions[0].Type);

        // The written half is still there and still translated.
        Assert.Contains(flat, c => c.Params != null
                                   && c.Params.TryGetValue("name", out var n) && n == "Day");
        Assert.Contains(flat, c => c.Params != null
                                   && c.Params.TryGetValue("name", out var n) && n == "anna-beach");
    }

    [Fact]
    public void AConversationReachedFromAnywhereIsNotGivenALevel()
    {
        // The control. An ending is played from 17_ending, not from a room, so
        // naming a level for it would be an invention rather than a reading.
        var gates = VanillaDialogueSeed.StartGates("17_ending/Ending_1_Dialogue");
        _out.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(gates));

        Assert.DoesNotContain(Flat(gates), c => c.Type == NodeConditionTypes.LevelActive);
    }

    [Fact]
    public void TheGameHasAGateForMostOfItsConversations()
    {
        int gated = 0, levelled = 0;
        foreach (var entry in VanillaDialogueCatalog.All)
        {
            var gates = VanillaDialogueSeed.StartGates(entry.Token);
            if (gates.Count == 0) continue;
            gated++;
            if (Flat(gates).Any(c => c.Type == NodeConditionTypes.LevelActive)) levelled++;
        }
        _out.WriteLine($"{gated} of {VanillaDialogueCatalog.All.Count} gated, {levelled} naming a level");

        // 487 have a condition the game wrote down; the other 50 are room
        // talks whose only gate is the room, and which read as ungated until
        // the room was said out loud.
        Assert.Equal(537, gated);
        Assert.Equal(424, levelled);
    }

    [Fact]
    public void EveryTranslatableConditionSurvivesIntoAGate()
    {
        // A gate that silently drops what it cannot translate would be worse
        // than one that is missing: the author would read a shorter list and
        // believe it.
        int withWritten = 0;
        foreach (var entry in VanillaDialogueCatalog.All)
        {
            var dialogue = VanillaDialogueCatalog.Open(entry.Token);
            if (dialogue == null || !dialogue.Starts.Any(s => s.When.Count > 0)) continue;

            withWritten++;
            Assert.NotEmpty(VanillaDialogueSeed.StartGates(entry.Token));
        }
        Assert.Equal(487, withWritten);
    }

    [Fact]
    public void TheGateReachesTheScreenAsRowsRatherThanAsBlanks()
    {
        // The bug this guards: the rows are drawn by a template chosen on the
        // item's TYPE, and a bare model matches nothing - so the panel drew
        // empty rows and the gate looked like it did not exist at all.
        var vm = new DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
        vm.VanillaSource = VanillaDialogueCatalog.Find(Anna);

        var shown = Assert.Single(vm.VanillaGates);
        Assert.IsType<NodeConditionViewModel>(shown);
        Assert.True(shown.IsGroup);
        Assert.Equal(3, shown.Children.Count);

        // Not the pack's to edit, so not removable.
        Assert.All(vm.VanillaGates, g => Assert.True(g.IsLocked));

        // And none of it is asserted by the pack.
        Assert.Empty(vm.Model.StartConditions);
    }
}
