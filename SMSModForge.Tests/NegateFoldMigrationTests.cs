using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Folding "Negate" into True/False on boolean checks.
/// <para/>
/// A boolean check now offers two buttons instead of one tick, so Negate beside
/// it says the same thing twice — and "Negate: is false" is a double negative
/// nobody should have to unpick. The checkbox is gone from boolean checks,
/// which means a pack still carrying the flag would have it applied invisibly
/// and no way to clear it.
/// <para/>
/// The rewrite has to be EXACT. An author's gate that quietly inverts is worse
/// than one that stops working, because nothing announces it.
/// </summary>
public sealed class NegateFoldMigrationTests
{
    private readonly ITestOutputHelper _out;
    public NegateFoldMigrationTests(ITestOutputHelper o) => _out = o;

    private static NodeConditionDef Check(string name, string value, bool negate)
    {
        var c = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals, Negate = negate };
        c.Params["name"] = name;
        c.Params["value"] = value;
        return c;
    }

    private static ModPack PackWith(params NodeConditionDef[] conditions)
    {
        var pack = new ModPack { PackId = "fold.pack" };
        pack.Variables.Add(new PackVariableDef { Name = "flag", Type = PackVariableType.Bool });
        pack.Variables.Add(new PackVariableDef { Name = "count", Type = PackVariableType.Int });

        var dialogue = new DialogueDef { Key = "d" };
        var node = new DialogueNodeDef { Id = 1, Text = "hi" };
        node.Conditions.AddRange(conditions);
        dialogue.Nodes.Add(node);
        pack.Dialogues.Add(dialogue);
        return pack;
    }

    [Fact]
    public void NotTrueBecomesFalseAndNotFalseBecomesTrue()
    {
        var pack = PackWith(Check("flag", "true", negate: true),
                            Check("flag", "false", negate: true));

        var report = PackMigration.Apply(pack);
        var conditions = pack.Dialogues[0].Nodes[0].Conditions;

        _out.WriteLine(report.Describe());

        Assert.Equal("false", conditions[0].Params["value"]);
        Assert.Equal("true", conditions[1].Params["value"]);
        Assert.All(conditions, c => Assert.False(c.Negate));
        Assert.Contains(report.Changes, c => c.What.Contains("Negate") && c.Count == 2);
    }

    [Fact]
    public void AnUnnegatedBooleanIsLeftExactlyAlone()
    {
        // The control. Most boolean checks are not negated, and a migration
        // that touched them would invert half the gates in a pack.
        var pack = PackWith(Check("flag", "true", negate: false),
                            Check("flag", "false", negate: false));

        var report = PackMigration.Apply(pack);
        var conditions = pack.Dialogues[0].Nodes[0].Conditions;

        Assert.Equal("true", conditions[0].Params["value"]);
        Assert.Equal("false", conditions[1].Params["value"]);
        Assert.DoesNotContain(report.Changes, c => c.What.Contains("Negate"));
    }

    [Fact]
    public void NegateOnSomethingThatIsNotABooleanStillMeansNotEqual()
    {
        // "Negate: count = 5" is "count is not 5", and there is no second way
        // to say that - the checkbox is still shown for it and still works.
        var pack = PackWith(Check("count", "5", negate: true));

        PackMigration.Apply(pack);
        var only = pack.Dialogues[0].Nodes[0].Conditions[0];

        Assert.True(only.Negate);
        Assert.Equal("5", only.Params["value"]);
    }

    [Fact]
    public void ABoolVariableWithAnOddValueFoldsTheWayTheEditorReadsIt()
    {
        // Everywhere else in the tool, a boolean value that is not spelled
        // "true" reads as false. Folding has to agree with that, or the gate
        // means one thing before the migration and another after.
        var pack = PackWith(Check("flag", "", negate: true));

        PackMigration.Apply(pack);
        var only = pack.Dialogues[0].Nodes[0].Conditions[0];

        Assert.False(only.Negate);
        Assert.Equal("true", only.Params["value"]);
    }

    [Fact]
    public void ConditionsNestedInGroupsAreReachedToo()
    {
        // A condition two groups deep is exactly as invisible to an author as
        // one at the top, and the flag would have been applied where nobody
        // could see it.
        var inner = Check("flag", "true", negate: true);
        // A group's child list starts null - which is exactly why the walker
        // has to cope with one, and why this plants it the way the editor does.
        var group = new NodeConditionDef
        {
            Type = NodeConditionTypes.GroupAll,
            Conditions = new List<NodeConditionDef> { inner },
        };
        var outer = new NodeConditionDef
        {
            Type = NodeConditionTypes.GroupAny,
            Conditions = new List<NodeConditionDef> { group },
        };

        // And an empty group alongside, so the null/empty path is walked too.
        var barren = new NodeConditionDef { Type = NodeConditionTypes.GroupAll };

        var pack = PackWith(outer, barren);
        PackMigration.Apply(pack);

        Assert.False(inner.Negate);
        Assert.Equal("false", inner.Params["value"]);
    }

    [Fact]
    public void EveryPlaceAPackKeepsConditionsIsCovered()
    {
        // A migration that reaches dialogues but not map buttons leaves half a
        // pack behind, and the half it misses is the half nobody tests.
        var pack = new ModPack { PackId = "fold.pack" };
        pack.Variables.Add(new PackVariableDef { Name = "flag", Type = PackVariableType.Bool });

        var everywhere = new List<NodeConditionDef>();
        NodeConditionDef One() { var c = Check("flag", "true", negate: true); everywhere.Add(c); return c; }

        var dialogue = new DialogueDef { Key = "d" };
        dialogue.StartConditions.Add(One());
        var node = new DialogueNodeDef { Id = 1 };
        node.Conditions.Add(One());
        dialogue.Nodes.Add(node);
        pack.Dialogues.Add(dialogue);

        var rule = new UpdateRuleDef { Key = "r" };
        rule.Conditions.Add(One());
        var branch = new LevelHookDef();
        branch.Conditions.Add(One());
        rule.Branches.Add(branch);
        pack.IntegrationRules.Add(rule);

        var place = new PlaceDef { Key = "p" };
        var onEnter = new LevelHookDef();
        onEnter.Conditions.Add(One());
        place.OnEnter.Add(onEnter);
        var navButton = new NavigatorButtonDef();
        navButton.Conditions.Add(One());
        place.NavigatorButtons.Add(navButton);
        pack.Places.Add(place);

        var button = new MapButtonDef();
        button.Conditions.Add(One());
        pack.MapButtons.Add(button);

        var wallpaper = new WallpaperDef { Key = "w" };
        wallpaper.UnlockConditions.Add(One());
        pack.Wallpapers.Add(wallpaper);

        var report = PackMigration.Apply(pack);
        _out.WriteLine($"{everywhere.Count} conditions planted; {report.Describe()}");

        Assert.All(everywhere, c =>
        {
            Assert.False(c.Negate);
            Assert.Equal("false", c.Params["value"]);
        });
    }

    [Fact]
    public void RunningItTwiceChangesNothingTheSecondTime()
    {
        // It runs on every load, including the one straight after a save. A
        // fold that flipped again would invert the gate on every open.
        var pack = PackWith(Check("flag", "true", negate: true));

        Assert.True(PackMigration.Apply(pack).Migrated);
        var only = pack.Dialogues[0].Nodes[0].Conditions[0];
        Assert.Equal("false", only.Params["value"]);

        var again = PackMigration.Apply(pack);
        Assert.DoesNotContain(again.Changes, c => c.What.Contains("Negate"));
        Assert.Equal("false", only.Params["value"]);
    }
}
