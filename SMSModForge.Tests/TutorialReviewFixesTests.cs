using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Editor bugs the skeptical reviewers of every tutorial step found (2026-11-22), each with its control:
/// a line's Outfit buttons did not show an outfit added on the Characters tab until another line was chosen;
/// a character of the pack's own wearing one of the game's busts was filed under THE GAME'S CHARACTERS; and a
/// variable's code in a line ([PV:Confidence]) was underlined as a misspelling.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class TutorialReviewFixesTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SMSModForge-reviewfixes-" + Guid.NewGuid().ToString("N"));
    public TutorialReviewFixesTests(ITestOutputHelper o) => _out = o;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    [Fact]
    public void AnOutfitAddedOnTheCharactersTabIsOnTheLinesButtonsOnComingBack()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            Assert.True(vm.MakePack(_dir, "Outfits"));
            vm.SelectedTabIndex = MainTab.Characters;
            vm.AddCharacterCommand.Execute(null);
            var her = vm.SelectedCharacter!;
            WindowHarness.Pump();

            vm.SelectedTabIndex = MainTab.Dialogues;
            vm.AddDialogueCommand.Execute(null);
            vm.AddDialogueRootNodeCommand.Execute(null);
            WindowHarness.Pump();
            var line = vm.SelectedDialogue!.Nodes[0];
            vm.SelectedNode = line;
            line.Actor = her.Key;
            WindowHarness.Pump();
            var before = vm.SelectedNodeOutfitChoices.Select(c => c.Value).ToList();
            _out.WriteLine($"{her.Key}'s outfits: {string.Join(", ", her.Outfits.Select(o => o.GameObjectName))}; the line offers: {string.Join(", ", before)}");
            Assert.Contains(her.Outfits[0].GameObjectName, before);   // the test is about something: her first outfit is offered

            vm.SelectedTabIndex = MainTab.Characters;
            vm.SelectedCharacter = her;
            vm.AddOutfitCommand.Execute(null);
            WindowHarness.Pump();
            string added = her.Outfits.Last().GameObjectName;
            // Control: on the Characters tab, the line's buttons are as they were - it is coming back that brings it.
            Assert.DoesNotContain(vm.SelectedNodeOutfitChoices, c => c.Value == added);

            vm.SelectedTabIndex = MainTab.Dialogues;
            WindowHarness.Pump();
            _out.WriteLine("after: " + string.Join(", ", vm.SelectedNodeOutfitChoices.Select(c => c.Value)));
            Assert.Contains(vm.SelectedNodeOutfitChoices, c => c.Value == added);
        });
    }

    [Fact]
    public void ACharacterOfThePacksInAGameBustStaysUnderThePacksHeading()
    {
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);
        var owner = new CharacterViewModel(new CharacterDef { Key = "GalleryOwner", Name = "GalleryOwner", DisplayName = "Gallery owner" });
        Assert.Equal(BustSource.Pack, owner.ListGroup);
        owner.BustSource = BustSource.Vanilla;
        Assert.Equal(BustSource.Pack, owner.ListGroup);       // still the pack's
        owner.BustSource = BustSource.None;
        Assert.Equal(BustSource.None, owner.ListGroup);       // a voice is filed as one
        // Control: one of the game's characters is under the game's heading.
        var cop = new CharacterViewModel(pack.Characters.First(c => c.IsVanillaCharacter && !c.IsPlayer));
        Assert.Equal(BustSource.Vanilla, cop.ListGroup);
    }

    [Theory]
    [InlineData("I'm at [PV:Confidence] out of 10 now.", "I'm at                 out of 10 now.")]
    [InlineData("Thank you, {PC}.", "Thank you,     .")]
    [InlineData("Set it to $Mood now", "Set it to       now")]
    [InlineData("A plain line, no codes.", "A plain line, no codes.")]   // the control: words are left alone
    public void ThePacksCodesAreNotSpellChecked(string line, string asChecked)
    {
        string checkedText = Speller.WithoutCodes(line);
        Assert.Equal(asChecked, checkedText);
        Assert.Equal(line.Length, checkedText.Length);   // every mark still falls on its letters
    }
}
