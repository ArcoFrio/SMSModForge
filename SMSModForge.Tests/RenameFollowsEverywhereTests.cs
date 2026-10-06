using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.Services.Translation;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Renames have to reach everywhere a name is used (the author, 1.7.0: "make
/// sure this is working around the entire ModForge correctly ... I don't know
/// how seamless/perfect this really is").
/// <para/>
/// It was not. Typed params were followed, and a few structural fields; the
/// target picker that switches things on and off, swaps sprites and fades them
/// was not - it is drawn by the row rather than declared in a schema - so a
/// scene, a bust or a place renamed under a Set-Active row left it aimed at
/// nothing. Nor were the music a button changes to, the sound a screen's
/// buttons make, an object inside a level, or an expression only one character
/// has. And the walk every rename shares missed a dice roll's branches, a
/// screen's buttons and an object's own conditions.
/// </summary>
public sealed class RenameFollowsEverywhereTests
{
    private readonly ITestOutputHelper _out;
    public RenameFollowsEverywhereTests(ITestOutputHelper o) => _out = o;

    private static ModPack Pack() => PackRepository.CreateEmpty("follow.pack");

    private static NodeActionDef Aimed(string type, string kind, string target, string level = "")
    {
        var a = new NodeActionDef { Type = type };
        a.Params["kind"] = kind;
        a.Params["target"] = target;
        if (level.Length > 0) a.Params["overlayLevel"] = level;
        return a;
    }

    private static NodeConditionDef Checked(string kind, string target, string level = "")
    {
        var c = new NodeConditionDef { Type = NodeConditionTypes.GameObjectActive };
        c.Params["kind"] = kind;
        c.Params["target"] = target;
        if (level.Length > 0) c.Params["overlayLevel"] = level;
        return c;
    }

    /// <summary>A rule to hang rows on: one of the plainest places a row can be.</summary>
    private static UpdateRuleDef Rule(ModPack pack)
    {
        var rule = new UpdateRuleDef { Key = "rule" };
        pack.IntegrationRules.Add(rule);
        return rule;
    }

    // ── The target picker ────────────────────────────────────────────────

    [Fact]
    public void ARenamedSceneTakesTheRowsThatSwitchItOn()
    {
        var pack = Pack();
        var rule = Rule(pack);
        var on = Aimed(NodeActionTypes.SetGameObjectActive, "Scene", "beach");
        var sprite = Aimed(NodeActionTypes.SetSprite, "Scene", "beach");
        var other = Aimed(NodeActionTypes.SetGameObjectActive, "GameObjects", "beach");   // an object that happens to share the name
        rule.Actions.AddRange(new[] { on, sprite, other });
        var asked = Checked("Scene", "beach");
        rule.Conditions.Add(asked);

        int n = ReferenceRenamer.Rename(pack, RefKind.Scene, "beach", "beachNight");

        _out.WriteLine($"{n} followed");
        Assert.Equal("beachNight", on.Params["target"]);
        Assert.Equal("beachNight", sprite.Params["target"]);
        Assert.Equal("beachNight", asked.Params["target"]);
        Assert.Equal("beach", other.Params["target"]);
        Assert.Equal(3, n);
    }

    [Fact]
    public void ARenamedOutfitTakesTheRowsAimedAtTheBust()
    {
        var pack = Pack();
        var rule = Rule(pack);
        var fade = Aimed(NodeActionTypes.FadeSprite, "Bust", "Sarah_Swim");
        var scene = Aimed(NodeActionTypes.SetGameObjectActive, "Scene", "Sarah_Swim");
        rule.Actions.AddRange(new[] { fade, scene });

        ReferenceRenamer.Rename(pack, RefKind.Outfit, "Sarah_Swim", "Sarah_Bikini");

        Assert.Equal("Sarah_Bikini", fade.Params["target"]);
        Assert.Equal("Sarah_Swim", scene.Params["target"]);   // a scene is not a bust
    }

    [Fact]
    public void ARenamedPlaceTakesEveryLevelFieldThatNamesIt()
    {
        var pack = Pack();
        pack.Places.Add(new PlaceDef { Key = "cave" });
        var rule = Rule(pack);

        var paint = Aimed(NodeActionTypes.SetSprite, "Places", "place:cave");
        var inside = Aimed(NodeActionTypes.SetGameObjectActive, "GameObjects", "Torch", "place:cave");
        var travel = new NodeActionDef { Type = NodeActionTypes.TransitionLevels };
        travel.Params["fromLevel"] = "place:cave";
        travel.Params["toLevel"] = "vanilla:Beach";
        var there = new NodeConditionDef { Type = NodeConditionTypes.LevelActive };
        there.Params["level"] = "place:cave";
        rule.Actions.AddRange(new[] { paint, inside, travel });
        rule.Conditions.Add(there);

        int n = ReferenceRenamer.Rename(pack, RefKind.Place, "cave", "grotto");

        _out.WriteLine($"{n} followed");
        Assert.Equal("place:grotto", paint.Params["target"]);
        Assert.Equal("place:grotto", inside.Params["overlayLevel"]);
        Assert.Equal("Torch", inside.Params["target"]);
        Assert.Equal("place:grotto", travel.Params["fromLevel"]);
        Assert.Equal("vanilla:Beach", travel.Params["toLevel"]);
        Assert.Equal("place:grotto", there.Params["level"]);
    }

    [Fact]
    public void RenamedMusicTakesTheButtonsThatPlayIt()
    {
        var pack = Pack();
        var place = new PlaceDef { Key = "cave" };
        var nav = new NavigatorButtonDef { Target = "beach", Music = "drips" };
        place.NavigatorButtons.Add(nav);
        pack.Places.Add(place);
        var map = new MapButtonDef { Target = "cave", Music = "drips" };
        pack.MapButtons.Add(map);

        int n = ReferenceRenamer.Rename(pack, RefKind.Music, "drips", "echoes");

        Assert.Equal("echoes", nav.Music);
        Assert.Equal("echoes", map.Music);
        Assert.Equal(2, n);
    }

    [Fact]
    public void ARenamedSoundTakesTheScreensThatClickWithIt()
    {
        var pack = Pack();
        var button = new UiNodeDef { ClickSound = "pop" };
        var root = new UiNodeDef();
        root.Children.Add(button);
        var ui = new UiDef { Id = "menu", Name = "Menu", ButtonSound = "pop" };
        ui.Nodes.Add(root);
        pack.Uis.Add(ui);

        ReferenceRenamer.Rename(pack, RefKind.Sfx, "pop", "plop");

        Assert.Equal("plop", ui.ButtonSound);
        Assert.Equal("plop", button.ClickSound);
    }

    [Fact]
    public void AFaceRenamedOnOneCharacterLeavesEveryoneElsesAlone()
    {
        // Everybody has a Happy. Sarah's Smirk renamed is Sarah's lines.
        var pack = Pack();
        var d = new DialogueDef { Key = "chat" };
        var hers = new DialogueNodeDef { Id = 1, Actor = "sarah", Expression = "Smirk" };
        var his = new DialogueNodeDef { Id = 2, Actor = "adam", Expression = "Smirk" };
        d.Nodes.AddRange(new[] { hers, his });
        pack.Dialogues.Add(d);

        int n = ReferenceRenamer.RenameExpression(pack, "sarah", "Smirk", "Grin");

        Assert.Equal(1, n);
        Assert.Equal("Grin", hers.Expression);
        Assert.Equal("Smirk", his.Expression);
    }

    // ── Objects inside a level ───────────────────────────────────────────

    /// <summary>A cave whose NPCs group holds a Shower with Anis in it, and a
    /// prop elsewhere that is ALSO called Shower.</summary>
    private static (ModPack Pack, GameObjectDef Shower, NpcPlacementDef Anis, GameObjectDef OtherShower) Cave()
    {
        var pack = Pack();
        var place = new PlaceDef { Key = "cave" };
        var npcs = new GameObjectDef { Name = "NPCs" };
        var shower = new GameObjectDef { Name = "Shower" };
        var anis = new NpcPlacementDef { Npc = "anis" };
        shower.Npcs.Add(anis);
        npcs.Children.Add(shower);
        var props = new GameObjectDef { Name = "Props" };
        var otherShower = new GameObjectDef { Name = "Shower" };
        props.Children.Add(otherShower);
        place.GameObjects.Add(npcs);
        place.GameObjects.Add(props);
        pack.Places.Add(place);
        pack.Npcs.Add(new NpcDef { Key = "anis" });
        return (pack, shower, anis, otherShower);
    }

    [Fact]
    public void AnObjectRenamedInALevelTakesThePathsThroughIt()
    {
        var (pack, shower, _, _) = Cave();
        var rule = Rule(pack);
        var path = Aimed(NodeActionTypes.SetGameObjectActive, "GameObjects", "NPCs > Shower", "place:cave");
        var slashes = Aimed(NodeActionTypes.FadeSprite, "GameObjects", "NPCs/Shower", "place:cave");
        var deeper = Aimed(NodeActionTypes.SetGameObjectActive, "NPCs", "NPCs > Shower > anis", "place:cave");
        var props = Aimed(NodeActionTypes.SetGameObjectActive, "GameObjects", "Props > Shower", "place:cave");
        var elsewhere = Aimed(NodeActionTypes.SetGameObjectActive, "GameObjects", "NPCs > Shower", "vanilla:Beach");
        rule.Actions.AddRange(new[] { path, slashes, deeper, props, elsewhere });

        // The box already says the new name when the rename is followed.
        shower.Name = "Bath";
        int n = ReferenceRenamer.RenameLevelObject(pack, shower, "Shower", "Bath");

        _out.WriteLine($"{n} followed: {path.Params["target"]} | {slashes.Params["target"]} | {deeper.Params["target"]}");
        Assert.Equal("NPCs > Bath", path.Params["target"]);
        Assert.Equal("NPCs/Bath", slashes.Params["target"]);
        Assert.Equal("NPCs > Bath > anis", deeper.Params["target"]);
        Assert.Equal("Props > Shower", props.Params["target"]);     // another object of the same name
        Assert.Equal("NPCs > Shower", elsewhere.Params["target"]);  // another level
        Assert.Equal(3, n);
    }

    [Fact]
    public void ABareNameFollowsOnlyWhenItLeadsToTheRenamedObject()
    {
        // The game finds a bare name as the first object of that name in the
        // level - here the NPCs group's Shower, which comes first.
        var (pack, shower, _, otherShower) = Cave();
        var rule = Rule(pack);
        var bare = Aimed(NodeActionTypes.SetGameObjectActive, "GameObjects", "Shower", "place:cave");
        rule.Actions.Add(bare);

        otherShower.Name = "Tub";
        Assert.Equal(0, ReferenceRenamer.RenameLevelObject(pack, otherShower, "Shower", "Tub"));
        Assert.Equal("Shower", bare.Params["target"]);

        shower.Name = "Bath";
        Assert.Equal(1, ReferenceRenamer.RenameLevelObject(pack, shower, "Shower", "Bath"));
        Assert.Equal("Bath", bare.Params["target"]);
    }

    [Fact]
    public void ANamedPlacementAndAnUnnamedOnesNpcBothFollow()
    {
        var (pack, _, anis, _) = Cave();
        var rule = Rule(pack);
        var row = Aimed(NodeActionTypes.SetGameObjectActive, "NPCs", "anis", "place:cave");
        rule.Actions.Add(row);

        // Unnamed, the placement is named after the NPC: renaming the NPC
        // renames the object.
        ReferenceRenamer.Rename(pack, RefKind.Npc, "anis", "anisa");
        Assert.Equal("anisa", anis.Npc);
        Assert.Equal("anisa", row.Params["target"]);

        // Given a name of its own, it is known by that.
        anis.Name = "AnisBathing";
        ReferenceRenamer.RenameLevelObject(pack, anis, "anisa", "AnisBathing");
        Assert.Equal("AnisBathing", row.Params["target"]);
    }

    // ── The walk reaches everywhere ──────────────────────────────────────

    [Fact]
    public void TheWalkReachesDiceBranchesScreensAndObjectsOwnConditions()
    {
        var pack = Pack();
        NodeActionDef Sound()
        {
            var a = new NodeActionDef { Type = NodeActionTypes.PlaySFX };
            a.Params["clip"] = "pop";
            return a;
        }
        NodeConditionDef Var()
        {
            var c = new NodeConditionDef { Type = NodeConditionTypes.VariableCompare };
            c.Params["name"] = "coins";
            return c;
        }

        // A dice roll's branch.
        var roll = new NodeActionDef { Type = NodeActionTypes.DiceRoll };
        var inBranch = Sound();
        roll.Branches = new List<DiceBranchDef> { new() { Action = inBranch } };
        Rule(pack).Actions.Add(roll);

        // A screen's button.
        var clicked = Sound();
        var shown = Var();
        var node = new UiNodeDef();
        node.OnClick.Add(clicked);
        node.ActiveConditions.Add(shown);
        var ui = new UiDef { Id = "menu" };
        ui.Nodes.Add(node);
        pack.Uis.Add(ui);

        // An object's own condition.
        var there = Var();
        var torch = new GameObjectDef { Name = "Torch" };
        torch.ActiveConditions.Add(there);
        var place = new PlaceDef { Key = "cave" };
        place.GameObjects.Add(torch);
        pack.Places.Add(place);

        ReferenceRenamer.Rename(pack, RefKind.Sfx, "pop", "plop");
        VariableRenamer.RenameReferences(pack, "coins", "gold");

        Assert.Equal("plop", inBranch.Params["clip"]);
        Assert.Equal("plop", clicked.Params["clip"]);
        Assert.Equal("gold", shown.Params["name"]);
        Assert.Equal("gold", there.Params["name"]);
    }

    [Fact]
    public void EverythingThePackSavesIsWalked()
    {
        // The backstop, asserted rather than trusted: every action and every
        // condition the pack would save is one the walk hands over.
        var pack = Pack();
        var roll = new NodeActionDef { Type = NodeActionTypes.DiceRoll };
        roll.Branches = new List<DiceBranchDef> { new() { Action = new NodeActionDef { Type = NodeActionTypes.Wait } } };
        Rule(pack).Actions.Add(roll);
        var ui = new UiDef { Id = "menu" };
        var n = new UiNodeDef();
        n.ClickConditions.Add(new NodeConditionDef { Type = NodeConditionTypes.AlwaysTrue });
        ui.Nodes.Add(n);
        ui.OpenConditions.Add(new NodeConditionDef { Type = NodeConditionTypes.AlwaysTrue });
        pack.Uis.Add(ui);

        var walkedActions = PackWalk.Actions(pack).Select(a => a.Action).ToHashSet();
        var walkedConditions = PackWalk.Conditions(pack).Select(c => c.Condition).ToHashSet();
        var savedActions = SavedObjects.All<NodeActionDef>(pack);
        var savedConditions = SavedObjects.All<NodeConditionDef>(pack)
            .Where(c => !NodeConditionTypes.IsGroup(c.Type)).ToList();

        _out.WriteLine($"{savedActions.Count} actions, {savedConditions.Count} conditions saved");
        Assert.All(savedActions, a => Assert.Contains(a, walkedActions));
        Assert.All(savedConditions, c => Assert.Contains(c, walkedConditions));
    }

    // ── Translations ─────────────────────────────────────────────────────

    private static TextFile File(params (string Key, string Text)[] lines)
    {
        var f = new TextFile();
        foreach (var (k, t) in lines) f.Add(new TextFile.Entry { Key = k, Text = t });
        return f;
    }

    private static HashSet<string> Made(params string[] keys)
        => new(keys, System.StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ATranslatedLineMovesToTheKeyItsDialogueHasNow()
    {
        var moves = new TextKeyMoves();
        moves.Link("dialogue.chat.1", "dialogue.talk.1");

        var es = File(("dialogue.chat.1", "hola"));
        Assert.True(moves.Apply(es, Made("dialogue.talk.1")));
        Assert.Equal("hola", es.Get("dialogue.talk.1"));
        Assert.False(es.Has("dialogue.chat.1"));
    }

    [Fact]
    public void ARenameUndoneLeavesTheLineWhereItWas()
    {
        // The moves are only ever applied against what the pack makes NOW, so
        // a rename taken back is a line left alone rather than one stranded
        // under a key nothing uses.
        var moves = new TextKeyMoves();
        moves.Link("dialogue.chat.1", "dialogue.talk.1");

        var es = File(("dialogue.chat.1", "hola"));
        Assert.False(moves.Apply(es, Made("dialogue.chat.1")));
        Assert.Equal("hola", es.Get("dialogue.chat.1"));
    }

    [Fact]
    public void RenamedTwiceTheLineEndsUpUnderTheLastName()
    {
        var moves = new TextKeyMoves();
        moves.Link("dialogue.chat.1", "dialogue.talk.1");
        moves.Link("dialogue.talk.1", "dialogue.gossip.1");

        var es = File(("dialogue.chat.1", "hola"));
        moves.Apply(es, Made("dialogue.gossip.1"));
        Assert.Equal("hola", es.Get("dialogue.gossip.1"));
    }

    [Fact]
    public void ALineTheFileAlreadyHasUnderTheNewKeyIsNotOverwritten()
    {
        var moves = new TextKeyMoves();
        moves.Link("dialogue.chat.1", "dialogue.talk.1");

        var es = File(("dialogue.chat.1", "hola"), ("dialogue.talk.1", "buenas"));
        Assert.False(moves.Apply(es, Made("dialogue.talk.1")));
        Assert.Equal("buenas", es.Get("dialogue.talk.1"));
        Assert.Equal("hola", es.Get("dialogue.chat.1"));   // left for the author, under "not used"
    }
}
