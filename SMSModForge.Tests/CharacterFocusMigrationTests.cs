using System;
using System.IO;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// SetSpriteFocus is called CharacterFocus from 1.7.0 (the author). Packs that
/// name it the old way are given the new name on load, wherever the action is,
/// and the game answers to both, so a pack nobody has re-saved still works.
/// </summary>
public sealed class CharacterFocusMigrationTests
{
    private readonly ITestOutputHelper _out;
    public CharacterFocusMigrationTests(ITestOutputHelper o) => _out = o;

    private static NodeActionDef Focus() => new()
    {
        Type = NodeActionTypes.SetSpriteFocus,
        Params = { ["focused"] = "false" },
    };

    /// <summary>The old name in four places an action can be, one of them
    /// only the full walk reaches - a quest task's own actions.</summary>
    private static ModPack PackWithTheOldName()
    {
        var pack = PackRepository.CreateEmpty("focus.pack");
        var node = new DialogueNodeDef { Id = 1, Text = "hi" };
        node.ActionsOnStart.Add(Focus());
        node.ActionsOnFinish.Add(new NodeActionDef
        {
            Type = NodeActionTypes.DiceRoll,
            Branches = { new DiceBranchDef { Chance = 100, Action = Focus() } },
        });
        pack.Dialogues.Add(new DialogueDef { Key = "d", Nodes = { node } });
        pack.IntegrationRules.Add(new UpdateRuleDef { Key = "r", Actions = { Focus() } });
        var quest = new QuestDef { Key = "q" };
        quest.Tasks.Add(new QuestTaskDef { Key = "t", Actions = { Focus() } });
        pack.Quests.Add(quest);
        return pack;
    }

    private static string[] TypesIn(ModPack pack) => new[]
    {
        pack.Dialogues[0].Nodes[0].ActionsOnStart[0].Type,
        pack.Dialogues[0].Nodes[0].ActionsOnFinish[0].Branches[0].Action.Type,
        pack.IntegrationRules[0].Actions[0].Type,
        pack.Quests[0].Tasks[0].Actions[0].Type,
    };

    [Fact]
    public void EverySetSpriteFocusIsCalledCharacterFocus_AndKeepsItsSetting()
    {
        var pack = PackWithTheOldName();
        Assert.All(TypesIn(pack), t => Assert.Equal("SetSpriteFocus", t));   // or this proves nothing

        var report = PackMigration.Apply(pack);
        _out.WriteLine(report.Describe());

        Assert.All(TypesIn(pack), t => Assert.Equal("CharacterFocus", t));
        Assert.Equal("false", pack.IntegrationRules[0].Actions[0].Params["focused"]);
        var line = Assert.Single(report.Changes, c => c.What == Loc.T("migration.characterFocus"));
        Assert.Equal(4, line.Count);

        // Idempotent: the load right after a save reports nothing.
        Assert.DoesNotContain(PackMigration.Apply(pack).Changes, c => c.What == Loc.T("migration.characterFocus"));
    }

    [Fact]
    public void TheEditorOffersOnlyTheNewName()
    {
        Assert.Contains(NodeActionTypes.CharacterFocus, NodeActionTypes.All);
        Assert.DoesNotContain(NodeActionTypes.SetSpriteFocus, NodeActionTypes.All);
        Assert.NotEmpty(ActionSchemas.For(NodeActionTypes.CharacterFocus));
    }

    [Fact]
    public void TheGameAnswersToBothNames()
    {
        // The plugin cannot be loaded here; its source is what can be read.
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "SMSModForge.PackPlugin", "ActionRuntime.cs")))
            root = Path.GetDirectoryName(root)!;
        Assert.NotNull(root);
        string runtime = File.ReadAllText(Path.Combine(root!, "SMSModForge.PackPlugin", "ActionRuntime.cs"));
        Assert.Contains("case \"CharacterFocus\":", runtime);
        Assert.Contains("case \"SetSpriteFocus\":", runtime);
    }

    [Fact]
    public void ThroughTheRealSave_OpeningChangesNothing_AndTheFirstSaveKeepsTheOriginal()
    {
        string dir = Path.Combine(Path.GetTempPath(), "smsmodforge-focus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            PackRepository.Save(PackWithTheOldName(), dir);
            string manifest = Path.Combine(dir, "modpack.json");
            string original = File.ReadAllText(manifest);
            Assert.Contains("SetSpriteFocus", original);

            var pack = PackRepository.Load(dir);
            Assert.Equal(original, File.ReadAllText(manifest));   // opening writes nothing
            Assert.All(TypesIn(pack), t => Assert.Equal("CharacterFocus", t));

            PackRepository.Save(pack, dir);
            string saved = File.ReadAllText(manifest);
            Assert.DoesNotContain("SetSpriteFocus", saved);
            Assert.Contains(Directory.GetFiles(dir), f => PackMigration.IsBackup(Path.GetFileName(f)));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
