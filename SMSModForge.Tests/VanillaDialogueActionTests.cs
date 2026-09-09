using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Vanilla instructions, read as the editor's own actions.
/// <para/>
/// The beach conversation is the worked example: its branch fades the UI,
/// switches on two busts, waits, plays, then sets the cooldown that stops it
/// happening twice in a day. Every one of those is an action the editor already
/// has, which is what makes the staging editable rather than merely visible.
/// </summary>
public sealed class VanillaDialogueActionTests
{
    private readonly ITestOutputHelper _out;
    public VanillaDialogueActionTests(ITestOutputHelper o) => _out = o;

    private const string Anna = "8_Room_Talk/Beach/AnnaBeachDefault";

    /// <summary>Every instruction the game runs around a conversation, paired
    /// with the object it runs on where that is known.</summary>
    private static IEnumerable<(VanillaDialogueCatalog.Step Step, string? Self)> EveryInstruction()
    {
        foreach (var entry in VanillaDialogueCatalog.All)
        {
            var dialogue = VanillaDialogueCatalog.Open(entry.Id)!;
            foreach (var node in dialogue.Nodes.Values)
                foreach (var step in node.OnStart.Concat(node.OnFinish))
                    yield return (step, null);
            foreach (var start in dialogue.Starts)
                foreach (var step in start.Before.Concat(start.After))
                    yield return (step, start.By);
        }
    }

    [Fact]
    public void TheBeachStagingReadsAsTheEditorsOwnActions()
    {
        var start = VanillaDialogueCatalog.Open(Anna)!.Starts.Single();

        var before = VanillaDialogueActions.TranslateAll(
            start.Before, out int missedBefore, start.By);
        Assert.Equal(0, missedBefore);
        Assert.Equal(4, before.Count);

        Assert.Equal(NodeActionTypes.EmitSignal, before[0].Type);
        Assert.Equal("FadeUI", before[0].Params["signal"]);

        Assert.Equal(NodeActionTypes.SetGameObjectActive, before[1].Type);
        Assert.Equal("Direct Path", before[1].Params["kind"]);
        Assert.Equal("true", before[1].Params["active"]);
        Assert.EndsWith("Anna_Swimwear", before[1].Params["target"]);

        Assert.Equal(NodeActionTypes.Wait, before[3].Type);
        Assert.Equal("1", before[3].Params["seconds"]);

        var after = VanillaDialogueActions.TranslateAll(
            start.After, out int missedAfter, start.By);
        Assert.Equal(0, missedAfter);

        // The cooldown that stops the beach repeating, written to the game's
        // own store rather than the pack's.
        var cooldown = after.Single(a => a.Type == NodeActionTypes.SetVariable);
        Assert.Equal("anna-beach", cooldown.Params["name"]);
        Assert.Equal("true", cooldown.Params["value"]);
        Assert.Equal("vanilla", cooldown.Params["source"]);
    }

    /// <summary>What the eight translated types cover, across the whole game.</summary>
    [Fact]
    public void TheInstructionsTheGameLeansOnAllTranslate()
    {
        var made = new Dictionary<string, int>();
        var missed = new Dictionary<string, int>();
        int total = 0;

        foreach (var (step, self) in EveryInstruction())
        {
            total++;
            var one = VanillaDialogueActions.Translate(step, self);
            if (one == null)
            {
                missed[step.Type] = missed.TryGetValue(step.Type, out int had) ? had + 1 : 1;
                continue;
            }
            made[one.Type] = made.TryGetValue(one.Type, out int seen) ? seen + 1 : 1;
        }

        int translated = made.Values.Sum();
        _out.WriteLine($"{translated} of {total} instructions translated");
        foreach (var pair in made.OrderByDescending(p => p.Value))
            _out.WriteLine($"  {pair.Key,-30} {pair.Value}");
        _out.WriteLine("without an equivalent:");
        foreach (var pair in missed.OrderByDescending(p => p.Value))
            _out.WriteLine($"  {pair.Key,-46} {pair.Value}");

        // All eight land, and between them they are the bulk of what the game
        // actually does in a conversation.
        Assert.Equal(6, made.Count);   // three Set* instructions share one type
        Assert.Contains(NodeActionTypes.SetGameObjectActive, made.Keys);
        Assert.Contains(NodeActionTypes.SetVariable, made.Keys);
        Assert.Contains(NodeActionTypes.IncrementVariable, made.Keys);
        Assert.Contains(NodeActionTypes.Wait, made.Keys);
        Assert.Contains(NodeActionTypes.EmitSignal, made.Keys);
        Assert.Contains(NodeActionTypes.PlaySFX, made.Keys);

        // The eight types this translates are the bulk of what a conversation
        // does. The rest is asserted BY NAME rather than by a percentage: a
        // ratio would drift silently, while a new type appearing in the game
        // should fail here and be looked at.
        var expected = new[]
        {
            "InstructionGameObjectSetGameObject", "InstructionGameObjectSetActive",
            "InstructionDialoguePlay", "InstructionLogicRunConditions",
            "InstructionQuestsTaskComplete", "InstructionLogicRunActions",
            "InstructionLogicCheckConditions", "InstructionLogicCallMethod",
            "InstructionQuestsActivate", "InstructionUICanvasGroupAlpha",
            "InstructionArithmeticSetNumber", "InstructionUICanvasGroupInteractable",
            "InstructionCommonDebugText", "InstructionCommonAudioSourceVolume",
            "InstructionTransformChangePosition", "InstructionBooleanAND",
            "InstructionCursorTexture", "InstructionQuestTaskValue",
            "InstructionQuestsDeactivate", "InstructionParticleSystemStopParticleSystem",
            "InstructionTransformChangeScale", "InstructionGameObjectInstantiate",
            "InstructionShadingLerpColor", "InstructionQuestsTaskFail",
            "InstructionArithmeticSubtractNumbers", "InstructionStatsChangeAttribute",
            "InstructionCommonAudioSourcePause", "InstructionCommonAudioSourcePlay",
            "InstructionRoundNumber",

            // These two ARE translated, nearly always. Four instances in the
            // game are not: two write into a UI text element rather than a
            // variable, and two have no target at all ("Set (none) = True").
            // Refusing those is right - there is nothing to aim them at.
            "InstructionBooleanSetBool", "InstructionTextSetString",
        };
        var surprises = missed.Keys.Where(k => !expected.Contains(k)).ToList();
        Assert.Empty(surprises);

        Assert.True(translated >= 10_700,
                    $"only {translated} of {total} instructions translated");
    }

    /// <summary>
    /// Nothing is dropped: a step with no equivalent still arrives as an
    /// action, saying what it is and what it does.
    /// <para/>
    /// This is the difference between a list an author can trust and one they
    /// cannot. If the untranslatable steps were simply missing, an extension
    /// would look as though it had removed them, and reordering or deleting
    /// around them would be working blind.
    /// </summary>
    [Fact]
    public void EveryStepIsRepresented()
    {
        int steps = 0, kept = 0;
        var kinds = new HashSet<string>();

        foreach (var (step, self) in EveryInstruction())
        {
            steps++;
            var shown = VanillaDialogueActions.Represent(step, self);
            Assert.NotNull(shown);

            if (shown!.Type != NodeActionTypes.VanillaStep) continue;
            kept++;
            kinds.Add(shown.Params["vanilla"]);

            // It says what it is, and what it does.
            Assert.Equal(step.Type, shown.Params["vanilla"]);
            Assert.Equal(step.Title, shown.Params["title"]);
        }

        _out.WriteLine($"{steps} steps, {kept} kept as-is across {kinds.Count} kinds");
        Assert.True(kept > 1000, "the untranslatable steps should all be here");
        Assert.Equal(steps, EveryInstruction().Count());

        // And it is not something an author can pick from the type list, since
        // it stands for a step that is already there.
        Assert.DoesNotContain(NodeActionTypes.VanillaStep, NodeActionTypes.All);
    }

    /// <summary>A kept step survives being written to a pack and read back —
    /// it is a normal action on disk, not a special case.</summary>
    [Fact]
    public void AKeptStepRoundTripsThroughAPack()
    {
        var original = VanillaDialogueActions.Represent(
            EveryInstruction().First(p =>
                VanillaDialogueActions.Translate(p.Step, p.Self) == null).Step)!;

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(original);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<NodeActionDef>(json)!;

        Assert.Equal(NodeActionTypes.VanillaStep, back.Type);
        Assert.Equal(original.Params["vanilla"], back.Params["vanilla"]);
        Assert.Equal(original.Params["title"], back.Params["title"]);

        // And the editor reads it back as a row that says what it does.
        var row = new NodeActionViewModel(back);
        Assert.True(row.IsVanillaStep);
        Assert.False(string.IsNullOrWhiteSpace(row.VanillaTitle));
        Assert.DoesNotContain("Instruction", row.VanillaTypeName);
        _out.WriteLine($"{row.VanillaTypeName}: {row.VanillaTitle}");
        foreach (var detail in row.VanillaDetails)
            _out.WriteLine($"    {detail.Name} = {detail.Value}");
    }

    /// <summary>
    /// The control: what has no equivalent is refused, and still says what it
    /// does.
    /// <para/>
    /// Quests, transforms, particle systems and Game Creator's own Actions
    /// assets are not things this editor can express. Approximating them would
    /// be worse than showing them as read-only, because an action that looks
    /// editable and does something else is invisible until it is played.
    /// </summary>
    [Fact]
    public void WhatHasNoEquivalentIsRefusedButStillDescribed()
    {
        Assert.Null(VanillaDialogueActions.Translate(null));
        Assert.Null(VanillaDialogueActions.Translate(new VanillaDialogueCatalog.Step()));
        Assert.Null(VanillaDialogueActions.Translate(new VanillaDialogueCatalog.Step
        {
            Type = "InstructionQuestsActivate",
            Fields = new Newtonsoft.Json.Linq.JObject(),
        }));

        // A Set Active naming an object with no scene path is refused rather
        // than aimed at a name that may match several objects.
        Assert.Null(VanillaDialogueActions.Translate(new VanillaDialogueCatalog.Step
        {
            Type = "InstructionGameObjectSetActive",
            Fields = Newtonsoft.Json.Linq.JObject.Parse(
                "{'m_Active':{'kind':'value','value':true},"
                + "'m_GameObject':{'kind':'object','name':'Leave'}}"),
        }));

        var refused = EveryInstruction()
            .Where(p => VanillaDialogueActions.Translate(p.Step, p.Self) == null)
            .Select(p => p.Step)
            .ToList();

        Assert.NotEmpty(refused);
        foreach (var step in refused)
            Assert.False(string.IsNullOrWhiteSpace(step.Describe()));

        // "Self" is only an address where the object is known. A node
        // instruction has no such context, so the same step is refused there
        // and resolved in a start site - which is the honest answer both times.
        var onSelf = new VanillaDialogueCatalog.Step
        {
            Type = "InstructionGameObjectSetActive",
            Fields = Newtonsoft.Json.Linq.JObject.Parse(
                "{'m_Active':{'kind':'value','value':false},"
                + "'m_GameObject':{'kind':'raw','type':'GetGameObjectSelf'}}"),
        };
        Assert.Null(VanillaDialogueActions.Translate(onSelf));
        Assert.Equal("8_Room_Talk/Beach",
                     VanillaDialogueActions.Translate(onSelf, "8_Room_Talk/Beach")!
                         .Params["target"]);
    }
}
