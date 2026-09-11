using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Renaming as a refactor rather than a silent break.
/// <para/>
/// Only variables were ever followed. Renaming a character, an outfit, a scene,
/// a place — anything else — changed the declaration and left every reference
/// pointing at something that no longer existed, with nothing to say so until
/// the pack ran in the game and a character stopped speaking.
/// </summary>
public sealed class ReferenceRenameTests
{
    private readonly ITestOutputHelper _out;
    public ReferenceRenameTests(ITestOutputHelper o) => _out = o;

    /// <summary>A pack with one of everything, all cross-referenced.</summary>
    private static ModPack Woven()
    {
        var pack = PackRepository.CreateEmpty("weave.pack");

        var sarah = new CharacterDef { Key = "sarah", Name = "sarah", DisplayName = "Sarah" };
        sarah.Outfits.Add(new OutfitDef { Key = "swim", GameObjectName = "Sarah_Swim" });
        sarah.DefaultOutfit = "Sarah_Swim";
        pack.Characters.Add(sarah);

        var d = new DialogueDef { Key = "chat" };
        var node = new DialogueNodeDef
        {
            Id = 1, Text = "hi", Actor = "sarah", Outfit = "Sarah_Swim", Expression = "Smirk",
        };
        d.Nodes.Add(node);
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);

        pack.Places.Add(new PlaceDef { Key = "cave" });
        pack.MapButtons.Add(new MapButtonDef { Label = "To the cave", Target = "cave" });
        pack.CustomRoomTalks.Add("chat");

        return pack;
    }

    private static DialogueNodeDef Node(ModPack pack)
        => pack.Dialogues.Single(d => d.Key == "chat").Nodes[0];

    /// <summary>Sarah, found by key: a fresh pack already has the player in it,
    /// so index 0 is somebody else.</summary>
    private static CharacterDef Sarah(ModPack pack)
        => pack.Characters.Single(c => c.Key == "sarah");

    [Fact]
    public void RenamingACharacterCarriesEveryLineTheySpeak()
    {
        var pack = Woven();
        int refs = ReferenceRenamer.Rename(pack, RefKind.Character, "sarah", "sarahb");

        _out.WriteLine($"{refs} reference(s) followed");
        Assert.True(refs > 0);
        Assert.Equal("sarahb", Node(pack).Actor);
    }

    [Fact]
    public void RenamingAnOutfitCarriesTheNodesThatSwitchIntoIt()
    {
        var pack = Woven();
        int refs = ReferenceRenamer.Rename(pack, RefKind.Outfit, "Sarah_Swim", "Sarah_Beach");

        _out.WriteLine($"{refs} reference(s) followed");
        Assert.Equal("Sarah_Beach", Node(pack).Outfit);
        Assert.Equal("Sarah_Beach", Sarah(pack).DefaultOutfit);
    }

    [Fact]
    public void RenamingAPlaceCarriesTheButtonsThatTravelToIt()
    {
        var pack = Woven();
        ReferenceRenamer.Rename(pack, RefKind.Place, "cave", "grotto");
        Assert.Equal("grotto", pack.MapButtons[0].Target);
    }

    [Fact]
    public void RenamingADialogueCarriesTheRoomTalkThatOffersIt()
    {
        var pack = Woven();
        ReferenceRenamer.Rename(pack, RefKind.Dialogue, "chat", "smalltalk");
        Assert.Equal("smalltalk", pack.CustomRoomTalks[0]);
    }

    [Fact]
    public void RenamingAnExpressionCarriesTheLinesThatAskForIt()
    {
        var pack = Woven();
        ReferenceRenamer.Rename(pack, RefKind.Expression, "Smirk", "Smug");
        Assert.Equal("Smug", Node(pack).Expression);
    }

    [Fact]
    public void ANameThatLooksLikeAnotherKindIsNotTouched()
    {
        // The control, and the reason this is typed rather than a search and
        // replace. A place called "sarah" is not the character called "sarah",
        // and a rename that could not tell them apart would repoint the map
        // button at somebody's dialogue key.
        var pack = Woven();
        pack.Places[0].Key = "sarah";
        pack.MapButtons[0].Target = "sarah";

        ReferenceRenamer.Rename(pack, RefKind.Character, "sarah", "sarahb");

        Assert.Equal("sarahb", Node(pack).Actor);
        Assert.Equal("sarah", pack.MapButtons[0].Target);   // still the place
    }

    [Fact]
    public void RenamingToItselfDoesNothing()
    {
        var pack = Woven();
        Assert.Equal(0, ReferenceRenamer.Rename(pack, RefKind.Character, "sarah", "sarah"));
        Assert.Equal(0, ReferenceRenamer.Rename(pack, RefKind.Character, "", "x"));
        Assert.Equal(0, ReferenceRenamer.Rename(null, RefKind.Character, "a", "b"));
    }

    [Fact]
    public void ATypedParamIsFollowedWhereverItLives()
    {
        // Driven off the SCHEMAS rather than a list of action types, which is
        // what makes an action that takes a bust name covered the day somebody
        // adds it. Asserted across every action the editor offers, so this
        // cannot quietly stop being true.
        var byType = ActionSchemas.ByType
            .Where(kv => kv.Value.Any(p => p.Type == ParamType.ActorRef))
            .Select(kv => kv.Key).ToList();

        _out.WriteLine($"{byType.Count} action type(s) take a character: "
                       + string.Join(", ", byType.Take(8)));
        Assert.NotEmpty(byType);

        foreach (string type in byType)
        {
            var pack = Woven();
            var schema = ActionSchemas.For(type).First(p => p.Type == ParamType.ActorRef);
            var action = new NodeActionDef { Type = type };
            action.Params[schema.Key] = "sarah";
            Node(pack).ActionsOnStart.Add(action);

            ReferenceRenamer.Rename(pack, RefKind.Character, "sarah", "sarahb");
            Assert.Equal("sarahb", action.Params[schema.Key]);
        }
    }

    [Fact]
    public void TheWalkReachesEveryPlaceAnActionCanHide()
    {
        // A pack has more hiding places than anyone remembers, and a sweep that
        // misses one fails silently. So the walk is asserted to reach each of
        // them by putting the same action in all of them at once.
        var pack = Woven();
        string type = ActionSchemas.ByType
            .First(kv => kv.Value.Any(p => p.Type == ParamType.ActorRef)).Key;
        var schema = ActionSchemas.For(type).First(p => p.Type == ParamType.ActorRef);

        NodeActionDef Made()
        {
            var a = new NodeActionDef { Type = type };
            a.Params[schema.Key] = "sarah";
            return a;
        }

        var hiding = new List<NodeActionDef>();
        void Hide(List<NodeActionDef> list) { var a = Made(); list.Add(a); hiding.Add(a); }

        Hide(Node(pack).ActionsOnStart);
        Hide(Node(pack).ActionsOnFinish);

        var rule = new UpdateRuleDef { Key = "rule" };
        Hide(rule.Actions);
        var branch = new LevelHookDef();
        Hide(branch.Actions);
        rule.Branches.Add(branch);
        pack.IntegrationRules.Add(rule);

        var hook = new LevelHookDef();
        Hide(hook.Actions);
        pack.Places[0].OnEnter.Add(hook);
        var exit = new LevelHookDef();
        Hide(exit.Actions);
        pack.Places[0].OnExit.Add(exit);

        int refs = ReferenceRenamer.Rename(pack, RefKind.Character, "sarah", "sarahb");
        _out.WriteLine($"{hiding.Count} hiding places, {refs} references followed");

        Assert.All(hiding, a => Assert.Equal("sarahb", a.Params[schema.Key]));
    }

    // ── Through the editor ───────────────────────────────────────

    [Fact]
    public void TypingANewKeyAndLeavingCarriesTheLines()
    {
        // The key box writes through on every keystroke, so cascading per
        // character would be nonsense ("s", "sa", "sar"). The rename is
        // snapshotted on selection and reconciled when the author leaves -
        // the same bargain variables have struck all along.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            vm.AddCharacterCommand.Execute(null);
            WindowHarness.Pump();
            var mine = vm.Characters.First(c => c.IsPackBust && !c.IsPlayer);
            vm.SelectedCharacter = mine;
            WindowHarness.Pump();

            var d = new DialogueDef { Key = "chat" };
            d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "hi", Actor = mine.Key });
            d.RootNodeIds.Add(1);
            vm.Pack.Dialogues.Add(d);

            // Typed, one keystroke at a time, as a person does.
            foreach (char c in "renamed")
                mine.Key = mine.Key + c;
            WindowHarness.Pump();

            // Nothing has followed yet: the author is still typing.
            Assert.NotEqual(mine.Key, d.Nodes[0].Actor);

            // Leaving is the commit point.
            vm.SelectedCharacter = null;
            WindowHarness.Pump();

            _out.WriteLine($"node speaker is now '{d.Nodes[0].Actor}'");
            Assert.Equal(mine.Key, d.Nodes[0].Actor);
        });
    }

    [Fact]
    public void AKeyThatCollidesIsLeftForTheAuthorToResolve()
    {
        // Repointing somebody else's lines at this character would be worse
        // than leaving the two of them clashing where it can be seen.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            vm.AddCharacterCommand.Execute(null);
            WindowHarness.Pump();
            var first = vm.Characters.First(c => c.IsPackBust && !c.IsPlayer);
            string taken = first.Key;

            vm.AddCharacterCommand.Execute(null);
            WindowHarness.Pump();
            var second = vm.Characters.Last(c => c.IsPackBust && !c.IsPlayer);
            vm.SelectedCharacter = second;
            WindowHarness.Pump();

            var d = new DialogueDef { Key = "chat" };
            d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "hi", Actor = taken });
            d.RootNodeIds.Add(1);
            vm.Pack.Dialogues.Add(d);

            second.Key = taken;
            vm.SelectedCharacter = null;
            WindowHarness.Pump();

            // The first character's line still names the first character.
            Assert.Equal(taken, d.Nodes[0].Actor);
        });
    }

    [Fact]
    public void ResettingABorrowedCharactersKeyTakesTheLinesWithIt()
    {
        // The one difference on a borrowed character that could not be
        // migrated: a pack written by an older editor keyed Mobster 1
        // "mobster", and every line in the pack names them by it.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var character = vm.Characters.First(c => c.IsVanillaBust);
            vm.SelectedCharacter = character;
            WindowHarness.Pump();

            string theirs = character.TheirOwnKey;
            Assert.False(character.CanResetKey);

            character.Key = "somebodyelse";
            var d = new DialogueDef { Key = "chat" };
            d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "hi", Actor = "somebodyelse" });
            d.RootNodeIds.Add(1);
            vm.Pack.Dialogues.Add(d);
            WindowHarness.Pump();

            Assert.True(character.CanResetKey);
            Assert.Contains("dialogue key", character.ChangedFields);

            vm.ResetCharacterKeyCommand.Execute(null);
            WindowHarness.Pump();

            _out.WriteLine($"key back to '{character.Key}', line now names '{d.Nodes[0].Actor}'");
            Assert.Equal(theirs, character.Key);
            Assert.Equal(theirs, d.Nodes[0].Actor);
            Assert.False(character.CanResetKey);
        });
    }

    [Fact]
    public void RenamingAnOutfitThroughTheEditorCarriesTheNodes()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            vm.AddCharacterCommand.Execute(null);
            WindowHarness.Pump();
            var mine = vm.Characters.First(c => c.IsPackBust && !c.IsPlayer);
            var outfit = mine.Outfits.FirstOrDefault() ?? mine.AddOutfit();
            vm.SelectedCharacter = mine;
            vm.SelectedOutfit = outfit;
            WindowHarness.Pump();

            var d = new DialogueDef { Key = "chat" };
            d.Nodes.Add(new DialogueNodeDef
            {
                Id = 1, Text = "hi", Actor = mine.Key, Outfit = outfit.GameObjectName,
            });
            d.RootNodeIds.Add(1);
            vm.Pack.Dialogues.Add(d);

            outfit.GameObjectName = "Somebody_Swim";
            vm.SelectedOutfit = null;
            WindowHarness.Pump();

            _out.WriteLine($"node switches into '{d.Nodes[0].Outfit}'");
            Assert.Equal("Somebody_Swim", d.Nodes[0].Outfit);
        });
    }

    [Fact]
    public void ARenameSaysNothingWhenThereIsNobodyToSayItTo()
    {
        // Found the way these always are: a dialog appeared over the work of
        // the person running the tests.
        //
        // ShowInfo is wired straight to a real MessageBox by the window, so
        // invoking it under the harness does not fail a run - it STOPS one,
        // waiting for a click nobody is there to give. It had been reachable
        // all along and went unnoticed because only a variable rename reached
        // it and the suite rarely renamed one; following references on every
        // kind of rename reached it constantly.
        //
        // Asserted through the hook rather than by reading the source, so it
        // covers every notice rather than the two this change added.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            int announced = 0;
            vm.ShowInfo = (_, _) => announced++;

            vm.AddCharacterCommand.Execute(null);
            WindowHarness.Pump();
            var mine = vm.Characters.First(c => c.IsPackBust && !c.IsPlayer);
            vm.SelectedCharacter = mine;
            WindowHarness.Pump();

            var d = new DialogueDef { Key = "chat" };
            d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "hi", Actor = mine.Key });
            d.RootNodeIds.Add(1);
            vm.Pack.Dialogues.Add(d);

            mine.Key = "renamed";
            vm.SelectedCharacter = null;
            WindowHarness.Pump();

            // The rename happened...
            Assert.Equal("renamed", d.Nodes[0].Actor);
            // ...and said nothing, because TestMode.Active is what the harness
            // sets and there is nobody here to read it.
            Assert.True(Services.TestMode.Active, "the harness should be in test mode");
            Assert.Equal(0, announced);
        });
    }

    [Fact]
    public void AndSaysItExactlyOnceWhenThereIs()
    {
        // The other half, and not a formality: the guard above was written by
        // rewriting every ShowInfo call into Announce, which rewrote the one
        // INSIDE Announce too. It called itself. Under the harness that is
        // invisible - TestMode returns before reaching the recursion - so the
        // silent-path test passed while the editor would have overflowed its
        // stack on the author's first rename.
        //
        // TestMode is put back in a finally: leaving it off would have every
        // later test raising real dialogs over somebody's work.
        bool was = Services.TestMode.Active;
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            int announced = 0;
            vm.ShowInfo = (_, _) => announced++;

            vm.AddCharacterCommand.Execute(null);
            WindowHarness.Pump();
            var mine = vm.Characters.First(c => c.IsPackBust && !c.IsPlayer);
            vm.SelectedCharacter = mine;
            WindowHarness.Pump();

            var d = new DialogueDef { Key = "chat" };
            d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "hi", Actor = mine.Key });
            d.RootNodeIds.Add(1);
            vm.Pack.Dialogues.Add(d);

            mine.Key = "renamed";
            try
            {
                // The only thing in this path that can put a window on screen
                // is the hook, and the hook is the counter above.
                Services.TestMode.Active = false;
                vm.CommitPendingCharacterRename();
            }
            finally { Services.TestMode.Active = was; }

            _out.WriteLine($"announced {announced} time(s)");
            Assert.Equal("renamed", d.Nodes[0].Actor);
            Assert.Equal(1, announced);
        });
        Assert.True(Services.TestMode.Active, "test mode was left off");
    }

    [Fact]
    public void ClickingFromOneCharacterToAnotherRenamesNothing()
    {
        // The repro, reported as "a random renaming pop up appears when
        // clicking through characters and outfits" - and the popup was the
        // harmless half.
        //
        // Selecting a character moves the OUTFIT selection too, so the sprite
        // editor does not go on showing somebody else's art, and it did that by
        // assigning the field behind the property. The rename snapshot never
        // moved with it: it went on naming the previous character's outfit
        // while the selection pointed at the new character's first. The next
        // commit read that as a rename and repointed every node that switched
        // into Adrian_bust at Anna_Bust.
        //
        // Nothing was typed in this test. That is the whole point.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            var a = vm.Characters.First(c => c.IsVanillaBust && c.Outfits.Count > 0);
            var b = vm.Characters.Last(c => c.IsVanillaBust && c.Outfits.Count > 0 && c != a);
            string bustOfA = a.Outfits[0].GameObjectName;
            string bustOfB = b.Outfits[0].GameObjectName;
            Assert.NotEqual(bustOfA, bustOfB);

            var d = new DialogueDef { Key = "chat" };
            d.Nodes.Add(new DialogueNodeDef
            {
                Id = 1, Text = "hi", Actor = a.Key, Outfit = bustOfA,
            });
            d.RootNodeIds.Add(1);
            vm.Pack.Dialogues.Add(d);

            int announced = 0;
            vm.ShowInfo = (_, _) => announced++;

            // Click one, then the other, then somewhere else - the ordinary
            // way anybody browses a cast of a hundred and nineteen.
            vm.SelectedCharacter = a;
            vm.SelectedOutfit = a.Outfits[0];
            WindowHarness.Pump();

            // ...and leave for another character WITHOUT touching an outfit,
            // which is what makes it bite: the outfit selection follows along
            // behind the property, so nothing re-snapshots it. Coming back to A
            // first would put the snapshot right again by accident and hide it.
            vm.SelectedCharacter = b;
            WindowHarness.Pump();

            vm.CommitPendingRenames();
            WindowHarness.Pump();

            _out.WriteLine($"node still switches into '{d.Nodes[0].Outfit}', "
                           + $"{announced} announcement(s)");
            Assert.Equal(bustOfA, d.Nodes[0].Outfit);
            Assert.Equal(a.Key, d.Nodes[0].Actor);
            Assert.Equal(0, announced);
        });
    }

    [Fact]
    public void WhereItIsUsedCanBeAskedWithoutChangingAnything()
    {
        var pack = Woven();
        var hits = ReferenceRenamer.FindReferences(pack, RefKind.Character, "sarah");

        _out.WriteLine(string.Join("\n", hits));
        Assert.NotEmpty(hits);
        Assert.Equal("sarah", Node(pack).Actor);   // asking changed nothing
    }
}
