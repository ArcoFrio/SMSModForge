using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What each vanilla line is worn in, replayed from what the conversation
/// stages.
/// <para/>
/// The numbers here are facts about the shipped catalog, so a change to the
/// derivation that quietly answers fewer lines — or more of them, wrongly —
/// shows up as a failure rather than as a field that got emptier.
/// </summary>
public sealed class VanillaDialogueStagingTests
{
    private readonly ITestOutputHelper _out;
    public VanillaDialogueStagingTests(ITestOutputHelper o) => _out = o;

    private const string Anna = "8_Room_Talk/Beach/AnnaBeachDefault";
    private const string Cow =
        "5_Levels/125_BarnBackInterior/Dialogue_CowPhotoshootTalk/CowPhotoShootTalk_Anna";

    [Fact]
    public void ABeachSceneIsSpokenInBeachwear()
    {
        var beach = VanillaDialogueCatalog.Open(Anna);
        Assert.NotNull(beach);

        var by = new Dictionary<string, HashSet<string>>();
        foreach (var pair in beach!.InOrder())
        {
            string? who = VanillaDialogueStaging.Character(pair.Value.Actor);
            if (who == null || pair.Value.Outfit == null) continue;
            if (!by.TryGetValue(who, out var busts)) by[who] = busts = new HashSet<string>();
            busts.Add(pair.Value.Outfit!);
        }

        // Nobody at this beach is dressed for anywhere else, and each of them
        // wears one thing throughout.
        Assert.Equal("Anna_Swimwear", Assert.Single(by["Anna"]));
        Assert.Equal("Adrian_Sport", Assert.Single(by["Adrian"]));
        Assert.Equal("Samantha_Swimsuit", Assert.Single(by["Samantha"]));

        // "Continue" drives the dialogue box; it is not somebody who dresses.
        Assert.Null(VanillaDialogueStaging.Character("Continue"));
        Assert.DoesNotContain("Continue", by.Keys);
    }

    [Fact]
    public void ASetupRootDressesTheRootsThatFollowIt()
    {
        // This conversation has twenty roots. The first is a wordless
        // "Continue" whose only job is to switch the cow bust on; the other
        // nineteen are the scene. Read as a tree, those nineteen are not that
        // node's children and would be left undressed — which is what an
        // ancestors-only reading did, answering none of its 35 Anna lines.
        var cow = VanillaDialogueCatalog.Open(Cow);
        Assert.NotNull(cow);
        Assert.Equal(20, cow!.Roots.Count);

        var spoken = cow.InOrder()
            .Where(p => VanillaDialogueStaging.Character(p.Value.Actor) == "Anna")
            .ToList();

        Assert.Equal(35, spoken.Count);
        Assert.All(spoken, p => Assert.Equal("Anna_CowBust", p.Value.Outfit));
    }

    [Fact]
    public void AnActorIsMatchedToTheCastByWhatTheGameCallsThem()
    {
        // A node names the asset, a bust is filed under the artwork's
        // character, and the two are different words for one person. Without
        // the catalog's spoken names this join fails silently and the outfit
        // simply never appears.
        Assert.Equal("Doctor Frost", VanillaDialogueCatalog.SpokenActorName("DrFrost"));
        Assert.Equal("Mrs. Kimura", VanillaDialogueCatalog.SpokenActorName("HimariMom"));
        Assert.Equal("Mei", VanillaDialogueCatalog.SpokenActorName("V_Mei"));

        // Unchanged for the ones the game files under their own name.
        Assert.Equal("Anna", VanillaDialogueCatalog.SpokenActorName("Anna"));

        Assert.Equal("Doctor Frost", VanillaDialogueStaging.Character("DrFrost"));
        Assert.Equal("Mrs. Kimura", VanillaDialogueStaging.Character("HimariMom"));
    }

    [Fact]
    public void TheDerivationAnswersMostLinesAndDeclinesTheRest()
    {
        int spoken = 0, dressed = 0, certain = 0;
        var changing = new List<string>();

        foreach (var entry in VanillaDialogueCatalog.All)
        {
            var dialogue = VanillaDialogueCatalog.Open(entry.Token);
            if (dialogue == null) continue;

            var wardrobe = VanillaDialogueStaging.Wardrobe(dialogue);
            foreach (var who in wardrobe.Where(w => w.Value.Count > 1))
                changing.Add(entry.Id + " / " + who.Key);

            foreach (var pair in dialogue.InOrder())
            {
                if (string.IsNullOrEmpty(pair.Value.Actor)) continue;
                spoken++;
                if (pair.Value.Outfit == null) continue;
                dressed++;

                string who = VanillaDialogueStaging.Character(pair.Value.Actor)!;
                if (wardrobe.TryGetValue(who, out var busts) && busts.Count == 1) certain++;
            }
        }

        _out.WriteLine("{0} lines with an actor, {1} dressed ({2:F1}%), {3} of those certain",
                       spoken, dressed, 100.0 * dressed / spoken, certain);

        Assert.Equal(14866, spoken);
        Assert.Equal(10537, dressed);

        // Nine in ten need no ordering at all: the speaker wears one bust for
        // the whole conversation, whichever way the branches go.
        Assert.Equal(9689, certain);

        // The rest are scenes that undress as they go, and there are few
        // enough to have been read individually.
        Assert.Equal(39, changing.Count);
        Assert.Contains("Anna_Call_Core/CallAnna_Scene_Club / Anna", changing);
    }

    [Fact]
    public void ASceneThatUndressesIsFollowedThroughInOrder()
    {
        // Anna arrives in a trenchcoat and does not stay in it. Both busts are
        // hers, so the answer is not "which bust" but "which line" — and that
        // is the whole reason the walk is in reading order.
        var call = VanillaDialogueCatalog.Open("Anna_Call_Core/CallAnna_Scene_Club");
        Assert.NotNull(call);

        var wardrobe = VanillaDialogueStaging.Wardrobe(call);
        Assert.Equal(new[] { "Anna_Trenchcoat", "Anna_SexWorker" },
                     wardrobe["Anna"].ToArray());

        var order = call!.InOrder()
            .Where(p => VanillaDialogueStaging.Character(p.Value.Actor) == "Anna"
                        && p.Value.Outfit != null)
            .Select(p => p.Value.Outfit!)
            .ToList();

        Assert.NotEmpty(order);
        Assert.Equal("Anna_Trenchcoat", order[0]);
        Assert.Equal("Anna_SexWorker", order[order.Count - 1]);

        // And it changes once, rather than flickering back and forth.
        int switches = order.Where((o, i) => i > 0 && o != order[i - 1]).Count();
        Assert.Equal(1, switches);
    }

    [Fact]
    public void TheSeededLineCarriesTheOutfitAndTheDeltaAgrees()
    {
        var beach = VanillaDialogueCatalog.Open(Anna);
        Assert.NotNull(beach);

        var pair = beach!.InOrder().First(
            p => VanillaDialogueStaging.Character(p.Value.Actor) == "Anna");
        var seeded = VanillaDialogueSeed.SeedNode(unchecked((int)pair.Key), pair.Value);

        Assert.Equal("Anna_Swimwear", seeded.Outfit);

        // Seeded is not changed: an author who never touched the outfit must
        // not have one written into their pack, or every extension would ship
        // an assertion about clothes it never made.
        Assert.DoesNotContain("outfit",
                              VanillaDialogueDelta.ChangedFields(seeded, pair.Value));

        seeded.Outfit = "Anna_Nude";
        Assert.Contains("outfit", VanillaDialogueDelta.ChangedFields(seeded, pair.Value));

        // And resetting the field puts the game's own answer back, rather than
        // emptying it.
        VanillaDialogueDelta.ResetTo(seeded, pair.Value, "outfit");
        Assert.Equal("Anna_Swimwear", seeded.Outfit);
    }

    [Fact]
    public void TheOutfitPickerOffersAVanillaSpeakersWardrobe()
    {
        // The editor joins a line to its speaker by key, and a vanilla line
        // names the actor asset - so "DrFrost" found nobody and the picker was
        // empty for every one of Doctor Frost's lines. Asserted through the
        // window because the join is the view model's, not the catalog's.
        WindowHarness.Run(window =>
        {
            var vm = (SMSModForge.ViewModel.MainViewModel)window.DataContext;

            var made = new DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
            made.VanillaSource = VanillaDialogueCatalog.Find(
                "8_Room_Talk/C_EvelynSecretLab/EvelynLabDefaultTalk");
            vm.Dialogues.Add(made);
            vm.SelectedDialogue = made;
            WindowHarness.Pump();

            vm.SelectedNode = made.Nodes.First(n => n.Actor == "DrFrost");
            WindowHarness.Pump();

            _out.WriteLine("offered: " + string.Join(", ", vm.SelectedNodeOutfitOptions));

            // Her own busts, and nobody else's.
            Assert.NotEmpty(vm.SelectedNodeOutfitOptions);
            var hers = VanillaCharacters.Find("Doctor Frost")!;
            Assert.All(vm.SelectedNodeOutfitOptions,
                       o => Assert.Contains(o, hers.Outfits));
        });
    }

    [Fact]
    public void ABustSwitchedByAVariableIsNotGuessedAt()
    {
        // The derivation reads only SetActive steps whose on/off is a literal.
        // One driven by a variable is a bust this cannot know the state of, and
        // a wrong outfit is worse than none.
        var seen = VanillaDialogueCatalog.All
            .Select(e => VanillaDialogueCatalog.Open(e.Token))
            .Where(d => d != null)
            .SelectMany(d => d!.InOrder())
            .Count(p => p.Value.Outfit != null);

        Assert.Equal(10537, seen);
    }
}
