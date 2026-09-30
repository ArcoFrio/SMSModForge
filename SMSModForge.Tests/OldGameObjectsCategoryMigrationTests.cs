using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// "Extra GameObjects" - what the GameObjects category was called before July
/// 2026 - taken to GameObjects on load (the author, 1.6.3). The rename taught
/// neither the editor nor the game the old name, so rows still carrying it
/// showed an empty Category and were looked up anywhere instead of in their
/// level. Found in dialogues and rules; renamed wherever a row can be.
/// </summary>
public sealed class OldGameObjectsCategoryMigrationTests
{
    private const string Old = "Extra GameObjects";
    private readonly ITestOutputHelper _out;
    public OldGameObjectsCategoryMigrationTests(ITestOutputHelper o) => _out = o;

    private static NodeActionDef Action(string type, string kind, string target = "Portal") => new()
    {
        Type = type,
        Params = { ["kind"] = kind, ["overlayLevel"] = "place:beach", ["target"] = target },
    };

    private static NodeConditionDef Condition(string kind = Old) => new()
    {
        Type = NodeConditionTypes.GameObjectActive,
        Params = { ["kind"] = kind, ["overlayLevel"] = "place:beach", ["target"] = "Portal" },
    };

    /// <summary>The old name in seven places a row can be, and two rows that
    /// must be left alone: one already current, and one whose TARGET happens
    /// to read "Extra GameObjects".</summary>
    private static ModPack PackWithTheOldName()
    {
        var pack = PackRepository.CreateEmpty("oldcategory.pack");

        var node = new DialogueNodeDef { Id = 1, Text = "hi" };
        node.ActionsOnFinish.Add(Action(NodeActionTypes.SetGameObjectActive, Old));
        node.ActionsOnFinish.Add(new NodeActionDef
        {
            Type = NodeActionTypes.DiceRoll,
            Branches = { new DiceBranchDef { Chance = 100, Action = Action(NodeActionTypes.SetSprite, Old) } },
        });
        node.ActionsOnFinish.Add(Action(NodeActionTypes.SetGameObjectActive, "GameObjects"));
        node.ActionsOnFinish.Add(Action(NodeActionTypes.SetGameObjectActive, "Direct Path", target: Old));
        pack.Dialogues.Add(new DialogueDef { Key = "d", Nodes = { node } });

        pack.IntegrationRules.Add(new UpdateRuleDef
        {
            Key = "r",
            Conditions = { new NodeConditionDef { Type = NodeConditionTypes.GroupAny, Conditions = new() { Condition() } } },
        });

        var placement = new NpcPlacementDef { Npc = "anis", ActiveConditions = { Condition() } };
        var lamp = new GameObjectDef { Name = "Lamp", ActiveConditions = { Condition() } };
        var npcs = new GameObjectDef { Name = "NPCs", Role = GameObjectDef.RoleNpcRoot, Npcs = { placement } };
        pack.Places.Add(new PlaceDef { Key = "beach", GameObjects = { new GameObjectDef { Name = "Props", Children = { lamp } }, npcs } });

        var button = new UiNodeDef { OnClick = { Action(NodeActionTypes.SetGameObjectActive, Old) } };
        pack.Uis.Add(new UiDef { Id = "shop", Nodes = { new UiNodeDef { Children = { button } } } });

        pack.Quests.Add(new QuestDef { StartConditions = { Condition() } });
        return pack;
    }

    private static string[] KindsIn(ModPack pack)
    {
        var node = pack.Dialogues[0].Nodes[0];
        return new[]
        {
            node.ActionsOnFinish[0].Params["kind"],
            node.ActionsOnFinish[1].Branches[0].Action.Params["kind"],
            pack.IntegrationRules[0].Conditions[0].Conditions![0].Params["kind"],
            pack.Places[0].GameObjects[0].Children[0].ActiveConditions[0].Params["kind"],
            pack.Places[0].GameObjects[1].Npcs[0].ActiveConditions[0].Params["kind"],
            pack.Uis[0].Nodes[0].Children[0].OnClick[0].Params["kind"],
            pack.Quests[0].StartConditions[0].Params["kind"],
        };
    }

    [Fact]
    public void EveryRowCarryingTheOldNameGetsTheCurrentOne_WhereverItIs()
    {
        var pack = PackWithTheOldName();
        Assert.All(KindsIn(pack), k => Assert.Equal(Old, k));   // or this proves nothing

        var report = PackMigration.Apply(pack);
        _out.WriteLine(report.Describe());

        Assert.All(KindsIn(pack), k => Assert.Equal("GameObjects", k));
        var line = Assert.Single(report.Changes, c => c.What == SMSModForge.Localization.Loc.T("migration.oldGameObjectsCategory"));
        Assert.Equal(7, line.Count);

        // Left alone: a row already current, and a target that merely reads
        // like the old name.
        var node = pack.Dialogues[0].Nodes[0];
        Assert.Equal("GameObjects", node.ActionsOnFinish[2].Params["kind"]);
        Assert.Equal("Direct Path", node.ActionsOnFinish[3].Params["kind"]);
        Assert.Equal(Old, node.ActionsOnFinish[3].Params["target"]);

        // Nothing else about the rows moves.
        Assert.Equal("place:beach", node.ActionsOnFinish[0].Params["overlayLevel"]);
        Assert.Equal("Portal", node.ActionsOnFinish[0].Params["target"]);
    }

    [Fact]
    public void RunningItAgainReportsNothing()
    {
        var pack = PackWithTheOldName();
        PackMigration.Apply(pack);
        var again = PackMigration.Apply(pack);
        Assert.DoesNotContain(again.Changes, c => c.What == SMSModForge.Localization.Loc.T("migration.oldGameObjectsCategory"));
    }

    [Fact]
    public void ThroughTheRealSave_OpeningChangesNothing_AndTheFirstSaveKeepsTheOriginal()
    {
        string dir = Path.Combine(Path.GetTempPath(), "smsmodforge-oldcat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            PackRepository.Save(PackWithTheOldName(), dir);
            string manifest = Path.Combine(dir, "modpack.json");
            string original = File.ReadAllText(manifest);
            Assert.Contains(Old, original);

            var pack = PackRepository.Load(dir);
            Assert.Equal(original, File.ReadAllText(manifest));        // opening writes nothing
            Assert.All(KindsIn(pack), k => Assert.Equal("GameObjects", k));
            Assert.Contains(PackRepository.LastMigration!.Changes,
                c => c.What == SMSModForge.Localization.Loc.T("migration.oldGameObjectsCategory"));

            PackRepository.Save(pack, dir);
            string saved = File.ReadAllText(manifest);
            // Only the target that reads like the old name still says it.
            Assert.Equal(1, saved.Split(Old).Length - 1);
            string kept = Assert.Single(Directory.GetFiles(dir, "modpack.pre-migration-*.json"));
            Assert.Equal(original, File.ReadAllText(kept));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
