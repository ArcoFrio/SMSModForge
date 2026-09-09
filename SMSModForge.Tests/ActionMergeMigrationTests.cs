using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Actions folded into the ones that replaced them.
/// <para/>
/// <c>EmitSignalDelayed</c> is <c>EmitSignal</c> with a delay, and
/// <c>ActivateScene</c> is <c>SetGameObjectActive</c> aimed at a scene — a
/// merge the runtime made some time ago and the editor never finished, so the
/// tool went on offering an action it already treated as an alias.
/// <para/>
/// The rewrite has to preserve behaviour exactly. An author's signal that
/// starts firing immediately instead of after a second is a bug they will find
/// in play, not in the editor.
/// </summary>
public sealed class ActionMergeMigrationTests
{
    private readonly ITestOutputHelper _out;
    public ActionMergeMigrationTests(ITestOutputHelper o) => _out = o;

    private static ModPack PackWith(params NodeActionDef[] actions)
    {
        var pack = new ModPack { PackId = "merge.pack" };
        var dialogue = new DialogueDef { Key = "d" };
        var node = new DialogueNodeDef { Id = 1, Text = "hi" };
        node.ActionsOnStart.AddRange(actions);
        dialogue.Nodes.Add(node);
        pack.Dialogues.Add(dialogue);
        return pack;
    }

    private static NodeActionDef Old(string type, params (string Key, string Value)[] ps)
    {
        var a = new NodeActionDef { Type = type };
        foreach (var (k, v) in ps) a.Params[k] = v;
        return a;
    }

    [Fact]
    public void ADelayedSignalKeepsItsDelay()
    {
        var pack = PackWith(Old(NodeActionTypes.EmitSignalDelayed,
                                ("signal", "openDoor"), ("seconds", "2.5")));

        var report = PackMigration.Apply(pack);
        var only = pack.Dialogues[0].Nodes[0].ActionsOnStart[0];
        _out.WriteLine(report.Describe());

        Assert.Equal(NodeActionTypes.EmitSignal, only.Type);
        Assert.Equal("openDoor", only.Params["signal"]);
        Assert.Equal("2.5", only.Params["seconds"]);
    }

    [Fact]
    public void ADelayedSignalWithNoDelayKeepsTheDefaultItUsedToHave()
    {
        // The old action's delay defaulted to 1 and the merged one defaults to
        // 0, so a pack that never wrote the field would start firing
        // immediately if this just changed the type and walked away.
        var pack = PackWith(Old(NodeActionTypes.EmitSignalDelayed, ("signal", "boom")));

        PackMigration.Apply(pack);
        var only = pack.Dialogues[0].Nodes[0].ActionsOnStart[0];

        Assert.Equal(NodeActionTypes.EmitSignal, only.Type);
        Assert.Equal("1", only.Params["seconds"]);
    }

    [Fact]
    public void AnImmediateSignalIsLeftAlone()
    {
        // The control. EmitSignal was already the immediate one and must not
        // acquire a delay.
        var pack = PackWith(Old(NodeActionTypes.EmitSignal, ("signal", "now")));

        var report = PackMigration.Apply(pack);
        var only = pack.Dialogues[0].Nodes[0].ActionsOnStart[0];

        Assert.Equal(NodeActionTypes.EmitSignal, only.Type);
        Assert.False(only.Params.ContainsKey("seconds"));
        Assert.DoesNotContain(report.Changes, c => c.What.Contains("merged"));
    }

    [Fact]
    public void ActivatingASceneBecomesSettingItActive()
    {
        var pack = PackWith(Old(NodeActionTypes.ActivateScene, ("scene", "kiss01")));

        PackMigration.Apply(pack);
        var only = pack.Dialogues[0].Nodes[0].ActionsOnStart[0];
        _out.WriteLine(string.Join(", ", only.Params.Select(p => $"{p.Key}={p.Value}")));

        Assert.Equal(NodeActionTypes.SetGameObjectActive, only.Type);
        Assert.Equal("Scene", only.Params["kind"]);
        Assert.Equal("kiss01", only.Params["target"]);

        // It only ever switched a scene ON.
        Assert.Equal("true", only.Params["active"]);

        // And the old param does not linger to be read by mistake.
        Assert.False(only.Params.ContainsKey("scene"));
    }

    [Fact]
    public void NeitherIsOfferedToAuthorsAnyMore()
    {
        // Finishing the merge means the picker stops offering the old ones -
        // otherwise the tool keeps creating what it just migrated away.
        Assert.DoesNotContain(NodeActionTypes.All, t => t == NodeActionTypes.EmitSignalDelayed);
        Assert.DoesNotContain(NodeActionTypes.All, t => t == NodeActionTypes.ActivateScene);

        // The ones that replaced them are offered, and DeactivateAllScenes
        // stays: it is a different verb with no target at all.
        Assert.Contains(NodeActionTypes.All, t => t == NodeActionTypes.EmitSignal);
        Assert.Contains(NodeActionTypes.All, t => t == NodeActionTypes.SetGameObjectActive);
        Assert.Contains(NodeActionTypes.All, t => t == NodeActionTypes.DeactivateAllScenes);
    }

    [Fact]
    public void TheMergedSignalStillOffersADelayField()
    {
        var schema = ActionSchemas.For(NodeActionTypes.EmitSignal);
        var names = schema.Select(p => p.Key).ToList();
        _out.WriteLine(string.Join(", ", names));

        Assert.Contains("signal", names);
        Assert.Contains("seconds", names);

        // Defaulting to none, so the common case is unchanged from what the
        // immediate action did.
        Assert.Equal("0", schema.Single(p => p.Key == "seconds").DefaultValue);
    }

    [Fact]
    public void RunningItTwiceChangesNothingTheSecondTime()
    {
        var pack = PackWith(Old(NodeActionTypes.EmitSignalDelayed, ("signal", "s"), ("seconds", "3")),
                            Old(NodeActionTypes.ActivateScene, ("scene", "sc")));

        Assert.True(PackMigration.Apply(pack).Migrated);
        var again = PackMigration.Apply(pack);
        Assert.DoesNotContain(again.Changes, c => c.What.Contains("merged"));

        var actions = pack.Dialogues[0].Nodes[0].ActionsOnStart;
        Assert.Equal("3", actions[0].Params["seconds"]);
        Assert.Equal("sc", actions[1].Params["target"]);
    }
}
