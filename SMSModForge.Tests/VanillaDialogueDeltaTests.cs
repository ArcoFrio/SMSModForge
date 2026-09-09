using System.Linq;
using Newtonsoft.Json;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Seeding a vanilla conversation and pruning it back to what an author
/// actually changed.
/// <para/>
/// The property that matters is the round trip: seed a dialogue, change
/// nothing, and the manifest should hold nothing. Anything less and a pack
/// asserts hundreds of lines it merely copied — which reads as authored intent,
/// and pins the conversation to the shape it had when the pack was written.
/// </summary>
public sealed class VanillaDialogueDeltaTests
{
    private readonly ITestOutputHelper _out;
    public VanillaDialogueDeltaTests(ITestOutputHelper o) => _out = o;

    private const string Anna = "8_Room_Talk/Beach/AnnaBeachDefault";

    [Fact]
    public void SeedingBringsInTheWholeConversation()
    {
        var seeded = VanillaDialogueSeed.Seed(Anna);
        Assert.NotNull(seeded);

        Assert.True(seeded!.IsVanillaBased);
        Assert.Equal(VanillaDialogueCatalog.TokenPrefix + Anna, seeded.Source);
        Assert.Equal("AnnaBeachDefault", seeded.DisplayName);
        Assert.Equal("8-room-talk-beach-annabeachdefault", seeded.Key);

        Assert.Equal(118, seeded.Nodes.Count);
        Assert.Equal(4, seeded.RootNodeIds.Count);

        // The line, the speaker and the gate all come across.
        var first = seeded.Nodes.Single(n => n.Id == seeded.RootNodeIds[0]);
        Assert.Equal("Anna", first.Actor);
        Assert.Equal("Come on, don’t pout. This is supposed to be fun!", first.Text);
        Assert.Equal(DialogueNodeKind.Text, first.Kind);
        Assert.Equal("first-anna-adrian-beach-talk", first.Conditions.Single().Params["name"]);

        // And what the game checks before playing it at all.
        // The gate is the room's, so it is readable but never stored: a pack
        // that wrote it down would assert something it does not control.
        Assert.Empty(seeded.StartConditions);

        var gate = Assert.Single(VanillaDialogueSeed.StartGates(Anna));
        Assert.Equal(NodeConditionTypes.GroupAll, gate.Type);

        // The room comes first. It is the half the game never writes down -
        // the component that plays this lives ON the Beach - and without it
        // the gate read as two variables that could be true anywhere.
        Assert.Equal(NodeConditionTypes.LevelActive, gate.Conditions![0].Type);
        Assert.Equal("vanilla:14_Beach", gate.Conditions[0].Params["level"]);

        Assert.Equal(new[] { "Day", "anna-beach" },
                     gate.Conditions.Skip(1).Select(c => c.Params["name"]).ToArray());
    }

    /// <summary>
    /// A seeded line shows what the game actually does after it, not the
    /// default.
    /// <para/>
    /// 666 lines in the game exit or jump rather than continuing, and 381 time
    /// out rather than waiting. Seeding those wrong showed an author the
    /// opposite of the truth, and the delta could not catch it because it
    /// compared a seed against a seed.
    /// </summary>
    [Fact]
    public void SeedingKeepsWhatHappensAfterALine()
    {
        var seeded = VanillaDialogueSeed.Seed(Anna)!;

        Assert.Equal(4, seeded.Nodes.Count(n => n.Jump?.Mode == JumpMode.Exit));

        var jump = Assert.Single(seeded.Nodes.Where(n => n.Jump?.Mode == JumpMode.Jump));
        Assert.Equal("root", jump.Jump!.TargetTag);

        var timed = Assert.Single(seeded.Nodes.Where(n => n.Duration == NodeDurationMode.Timeout));
        Assert.Equal(5f, timed.Timeout);

        // And the rest are left as the game leaves them.
        Assert.Equal(113, seeded.Nodes.Count(n => n.Jump == null));
    }

    /// <summary>
    /// An expression arrives as a word, not a number.
    /// <para/>
    /// A node picks one by index into its actor's list, and the actors are
    /// assets rather than anything in a scene - so the number only becomes a
    /// name once those are extracted too. Seeded as the index, the editor
    /// showed "4" in a field whose contract is a name like "Flirty".
    /// </summary>
    [Fact]
    public void AnExpressionIsSeededByName()
    {
        var vanilla = VanillaDialogueCatalog.Open(Anna)!;

        var expressive = vanilla.Nodes.Values.First(n => n.Expression != 0);
        Assert.False(string.IsNullOrEmpty(expressive.ExpressionName));
        Assert.Contains(expressive.ExpressionName,
                        new[] { "neutral", "Happy", "Angry", "Sad", "Flirty" });

        var seeded = VanillaDialogueSeed.Seed(Anna)!;
        var line = seeded.Nodes.First(n => n.Id == unchecked((int)
            long.Parse(vanilla.Nodes.First(p => ReferenceEquals(p.Value, expressive)).Key,
                       System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(expressive.ExpressionName, line.Expression);

        // Nought is a face like any other - the actor's first, which for this
        // cast is "neutral". Read as "no expression" it left 11,429 lines
        // showing an empty box where the game plainly has one.
        var first = vanilla.Nodes.First(p => p.Value.Expression == 0
                                             && p.Value.Actor == "Anna");
        var firstLine = seeded.Nodes.First(n => n.Id == unchecked((int)
            long.Parse(first.Key, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal("neutral", firstLine.Expression);

        // A line with nobody speaking has no face to name.
        var silent = vanilla.Nodes.First(p => string.IsNullOrEmpty(p.Value.Actor));
        var silentLine = seeded.Nodes.First(n => n.Id == unchecked((int)
            long.Parse(silent.Key, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal("", silentLine.Expression);
    }

    /// <summary>
    /// The one that keeps a manifest honest: seeded and untouched prunes to
    /// nothing at all.
    /// </summary>
    [Fact]
    public void AnUntouchedExtensionStoresNothing()
    {
        var seeded = VanillaDialogueSeed.Seed(Anna)!;
        var vanilla = VanillaDialogueCatalog.Open(Anna)!;

        var pruned = VanillaDialogueDelta.Prune(seeded.Nodes, vanilla);

        _out.WriteLine($"{seeded.Nodes.Count} seeded, {pruned.Count} worth storing");
        Assert.Empty(pruned);
        Assert.Empty(VanillaDialogueDelta.RemovedNodes(seeded.Nodes, vanilla));
    }

    /// <summary>And it holds for every conversation in the game, not just the
    /// one worked through by hand.</summary>
    [Fact]
    public void NoConversationSeedsIntoSomethingWorthStoring()
    {
        int checkedDialogues = 0, leaked = 0;

        foreach (var entry in VanillaDialogueCatalog.All)
        {
            var seeded = VanillaDialogueSeed.Seed(entry.Id);
            if (seeded == null) continue;

            var vanilla = VanillaDialogueCatalog.Open(entry.Id)!;
            var pruned = VanillaDialogueDelta.Prune(seeded.Nodes, vanilla);
            checkedDialogues++;

            if (pruned.Count == 0) continue;
            leaked++;
            if (leaked <= 3)
                _out.WriteLine($"{entry.Id}: {pruned.Count} node(s) would be stored, "
                               + $"e.g. {JsonConvert.SerializeObject(pruned[0])[..160]}");
        }

        _out.WriteLine($"{checkedDialogues} conversations seeded");
        Assert.Equal(722, checkedDialogues);
        Assert.Equal(0, leaked);
    }

    /// <summary>
    /// The control: a change IS stored, and only the node that carries it.
    /// <para/>
    /// Without this the test above passes just as well on a pruner that throws
    /// everything away.
    /// </summary>
    [Fact]
    public void AChangedLineIsTheOnlyThingStored()
    {
        var seeded = VanillaDialogueSeed.Seed(Anna)!;
        var vanilla = VanillaDialogueCatalog.Open(Anna)!;

        var edited = seeded.Nodes.Single(n => n.Id == seeded.RootNodeIds[0]);
        edited.Text = "Come on, don't pout. I brought sunscreen.";

        var stored = Assert.Single(VanillaDialogueDelta.Prune(seeded.Nodes, vanilla));
        Assert.Equal(edited.Id, stored.Id);
        Assert.Equal(edited.Text, stored.Text);

        // Only the field that changed is named, and only it survives - the
        // speaker and the gate are the game's, so the pack does not restate
        // them.
        Assert.Equal(new[] { "text" }, stored.Overrides);
        Assert.Equal("", stored.Actor);
        Assert.Empty(stored.Conditions);

        // A changed action counts too, not just changed words.
        var second = VanillaDialogueSeed.Seed(Anna)!;
        var withAction = second.Nodes.Single(n => n.Id == second.RootNodeIds[0]);
        withAction.ActionsOnStart.Add(new NodeActionDef
        {
            Type = NodeActionTypes.EmitSignal,
            Params = new System.Collections.Generic.Dictionary<string, string>
            {
                ["signal"] = "Blink",
            },
        });
        var storedAction = Assert.Single(VanillaDialogueDelta.Prune(second.Nodes, vanilla));
        Assert.Equal(new[] { "actionsOnStart" }, storedAction.Overrides);
        Assert.Equal("", storedAction.Text);
    }

    /// <summary>A line the author deleted is recorded, because "absent" already
    /// means "unchanged".</summary>
    [Fact]
    public void ADeletedLineIsRecordedRatherThanInferred()
    {
        var seeded = VanillaDialogueSeed.Seed(Anna)!;
        var vanilla = VanillaDialogueCatalog.Open(Anna)!;

        int removedId = seeded.Nodes[7].Id;
        seeded.Nodes.RemoveAt(7);

        var gone = VanillaDialogueDelta.RemovedNodes(seeded.Nodes, vanilla);
        Assert.Equal(new[] { removedId }, gone);

        // Deleting a line does not make the surviving ones worth storing.
        Assert.Empty(VanillaDialogueDelta.Prune(seeded.Nodes, vanilla));
    }

    /// <summary>A node the pack adds has no vanilla twin, so it is always
    /// kept.</summary>
    [Fact]
    public void ALineThePackAddsIsAlwaysKept()
    {
        var seeded = VanillaDialogueSeed.Seed(Anna)!;
        var vanilla = VanillaDialogueCatalog.Open(Anna)!;

        var added = new DialogueNodeDef { Id = 12345, Text = "And a new line." };
        seeded.Nodes.Add(added);

        var stored = Assert.Single(VanillaDialogueDelta.Prune(seeded.Nodes, vanilla));
        Assert.Same(added, stored);              // kept whole, not reduced
        Assert.Empty(stored.Overrides);          // nothing underneath to override
    }

    /// <summary>
    /// Game Creator's node ids run the full width of a 32-bit integer, and the
    /// pack model stores them in one. This is closer to the edge than it looks
    /// — the game's lowest is within 84,000 of int.MinValue — so it is worth
    /// asserting rather than assuming.
    /// </summary>
    [Fact]
    public void EveryVanillaNodeIdFitsThePackModel()
    {
        long lowest = long.MaxValue, highest = long.MinValue;
        int counted = 0;

        foreach (var entry in VanillaDialogueCatalog.All)
        {
            var vanilla = VanillaDialogueCatalog.Open(entry.Id)!;
            foreach (string key in vanilla.Nodes.Keys)
            {
                long id = long.Parse(key, System.Globalization.CultureInfo.InvariantCulture);
                lowest = System.Math.Min(lowest, id);
                highest = System.Math.Max(highest, id);
                counted++;

                Assert.InRange(id, int.MinValue, int.MaxValue);
                Assert.Equal(id, unchecked((int)id));   // narrowing loses nothing
            }
        }

        _out.WriteLine($"{counted} ids, from {lowest} to {highest}");
        Assert.Equal(19653, counted);
    }

    /// <summary>Saving never rewrites what is on screen — the pass builds
    /// copies and hands back a way to put the originals back.</summary>
    [Fact]
    public void PreparingToSaveLeavesTheLiveModelAlone()
    {
        var pack = new ModPack();
        var extension = VanillaDialogueSeed.Seed(Anna)!;
        pack.Dialogues.Add(extension);

        var live = extension.Nodes;
        Assert.Equal(118, live.Count);

        var restore = VanillaDialogueDelta.PrepareForSave(pack);
        Assert.Empty(extension.Nodes);          // what would be written

        restore();
        Assert.Same(live, extension.Nodes);     // what is on screen
        Assert.Equal(118, extension.Nodes.Count);
    }

    /// <summary>
    /// End to end: what actually reaches the manifest.
    /// <para/>
    /// The pass above is only worth having if the writer uses it, and an
    /// extension that seeded 118 lines must not put any of them on disk until
    /// one is changed.
    /// </summary>
    [Fact]
    public void TheManifestHoldsOnlyWhatTheAuthorChanged()
    {
        var pack = new ModPack();
        var extension = VanillaDialogueSeed.Seed(Anna)!;
        pack.Dialogues.Add(extension);

        string untouched = PackRepository.SerializeAsSaved(pack);
        Assert.DoesNotContain("This is supposed to be fun", untouched);
        Assert.Contains("vanilladialogue:" + Anna, untouched);

        // Not one line, and not the room's gate either. The key survives as an
        // empty list, which asserts nothing - what must not appear is a gate
        // inside it.
        Assert.DoesNotContain("anna-beach", untouched);
        Assert.Contains("\"startConditions\": []", untouched);

        // Change one line, and exactly that line appears.
        extension.Nodes.Single(n => n.Id == extension.RootNodeIds[0]).Text =
            "Come on, I brought sunscreen.";

        string edited = PackRepository.SerializeAsSaved(pack);
        Assert.Contains("Come on, I brought sunscreen.", edited);
        Assert.DoesNotContain("This is supposed to be fun", edited);

        _out.WriteLine($"untouched {untouched.Length} chars, edited {edited.Length} chars");
        Assert.True(edited.Length > untouched.Length);

        // And the live model still has the whole conversation to edit.
        Assert.Equal(118, extension.Nodes.Count);
    }

    /// <summary>
    /// The whole round trip: seed, change, save, re-open.
    /// <para/>
    /// This is the property an author actually depends on. Saving keeps two
    /// lines of a hundred and eighteen, so re-opening has to put the rest back
    /// or the conversation they were editing is gone — and the two lines they
    /// changed have to still be changed.
    /// </summary>
    [Fact]
    public void ASavedExtensionOpensAsTheWholeConversationAgain()
    {
        var pack = new ModPack();
        var extension = VanillaDialogueSeed.Seed(Anna)!;
        pack.Dialogues.Add(extension);

        extension.Nodes.Single(n => n.Id == extension.RootNodeIds[0]).Text = "Sunscreen?";
        int deletedId = extension.Nodes[11].Id;
        extension.Nodes.RemoveAt(11);

        // Through the writer and back, exactly as a saved pack would go.
        string json = PackRepository.SerializeAsSaved(pack);
        var reopened = PackRepository.Deserialize(json)!.Dialogues.Single();

        Assert.Equal(1, reopened.Nodes.Count);              // only the change was stored
        Assert.Equal(new[] { deletedId }, reopened.RemovedNodes);

        var whole = VanillaDialogueSeed.Merge(reopened.Source, reopened.Nodes,
                                              reopened.RemovedNodes, out int removed);

        Assert.Equal(117, whole.Count);                     // 118 less the deleted one
        Assert.Equal(1, removed);
        Assert.DoesNotContain(whole, n => n.Id == deletedId);

        // The edit survived, and everything else came back as the game has it.
        Assert.Equal("Sunscreen?", whole.Single(n => n.Id == extension.RootNodeIds[0]).Text);
        Assert.Equal("Anna", whole.Single(n => n.Id == extension.RootNodeIds[0]).Actor);

        // And re-saving the re-opened conversation stores the same thing, so a
        // pack does not grow every time it is opened.
        var again = new ModPack();
        var restored = VanillaDialogueSeed.Seed(Anna)!;
        restored.Nodes = whole;
        restored.RemovedNodes = reopened.RemovedNodes;
        again.Dialogues.Add(restored);

        var pruned = VanillaDialogueDelta.Prune(whole, VanillaDialogueCatalog.Open(Anna)!);
        Assert.Single(pruned);
        Assert.Equal(new[] { "text" }, pruned[0].Overrides);
    }

    /// <summary>
    /// The fields an extension can change, pinned.
    /// <para/>
    /// This list is a contract with the runtime: VanillaDialogueInjector has a
    /// case for each of these, and anything it does not recognise is reported
    /// as an authored change it could not apply. The two live in different
    /// assemblies, so nothing but this stops a field being added here and
    /// silently doing nothing in game.
    /// </summary>
    [Fact]
    public void TheChangeableFieldsAreTheOnesTheRuntimeKnows()
    {
        Assert.Equal(
            new[]
            {
                "kind", "actor", "expression", "outfit", "text", "tag", "children",
                "conditions", "actionsOnStart", "actionsOnFinish", "jump", "duration",
                "timeout",
            },
            VanillaDialogueDelta.Fieldnames.ToArray());
    }

    /// <summary>A dialogue the pack wrote itself is not touched, whatever it
    /// holds.</summary>
    [Fact]
    public void APacksOwnDialogueIsLeftAlone()
    {
        var pack = new ModPack();
        var own = new DialogueDef { Key = "mine" };
        own.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Hello." });
        pack.Dialogues.Add(own);

        var live = own.Nodes;
        VanillaDialogueDelta.PrepareForSave(pack);

        Assert.Same(live, own.Nodes);
        Assert.Single(own.Nodes);
    }
}
