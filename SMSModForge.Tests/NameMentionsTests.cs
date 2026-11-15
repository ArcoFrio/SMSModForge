using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.Validation;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Validate on the game's characters' names in a pack's own words (1.9.0): each typed
/// out is listed and can be switched to the name token; a token naming nobody, and a
/// new name somebody already has, are warned about.
/// </summary>
public sealed class NameMentionsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public NameMentionsTests(ITestOutputHelper o)
    {
        _out = o;
        _root = Path.Combine(Path.GetTempPath(), "smsmodforge-mentions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static ModPack PackSaying(params string[] lines)
    {
        var pack = PackRepository.CreateEmpty("mentions.pack");
        var d = new DialogueDef { Key = "talk", DisplayName = "Talk" };
        for (int i = 0; i < lines.Length; i++)
            d.Nodes.Add(new DialogueNodeDef { Id = i + 1, Actor = "player", Text = lines[i] });
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);
        return pack;
    }

    private List<ValidationIssue> Issues(ModPack pack, string code)
    {
        var all = PackValidator.Validate(pack, _root).Where(i => i.Code == code).ToList();
        foreach (var i in all) _out.WriteLine($"{i.Severity} {i.Where}: {i.Message}");
        return all;
    }

    [Fact]
    public void EachNameOfTheGamesTypedOutIsListed_WithTheChangeThatWouldMakeItATokenForThem()
    {
        var pack = PackSaying("Hello Anna. Is Master Zhen in?", "Anna's room, Annabelle.");
        var found = Issues(pack, NameMentions.PlainMention);

        // Anna twice (her possessive too), Master Zhen once; Annabelle is somebody's
        // own word, not Anna's.
        Assert.Equal(3, found.Count);
        Assert.All(found, i => Assert.Equal(Severity.Info, i.Severity));
        var first = found[0].Fix!;
        Assert.Equal(new TextFix("talk", 1, 6, "Anna", "{name:anna}"), first);
        Assert.Equal(new TextFix("talk", 1, 15, "Master Zhen", "{name:masterzhen}"), found[1].Fix);
        Assert.Equal(new TextFix("talk", 2, 0, "Anna", "{name:anna}"), found[2].Fix);
    }

    [Fact]
    public void WhatIsAlreadyATokenATagOrAVariableIsNotAMention()
    {
        var pack = PackSaying("Ask {name:anna}, <color=#f00>later</color>, [PV:Anna]. Fine.");
        Assert.Empty(Issues(pack, NameMentions.PlainMention));
    }

    [Fact]
    public void ANameTwoOfThePacksCharactersAnswerTo_IsNobodysInParticular()
    {
        // The control: the pack's own Sakura and the game's are both here, so a
        // "Sakura" in a line is not tied to either - and gets no token offered.
        var pack = PackSaying("Sakura waves. Kate waves.");
        pack.Characters.Add(new CharacterDef { Key = "OwnSakura", Name = "OwnSakura", DisplayName = "Sakura", Pronouns = Pronouns.Female });
        var found = Issues(pack, NameMentions.PlainMention);
        Assert.DoesNotContain(found, i => i.Fix!.Was == "Sakura");
        Assert.Contains(found, i => i.Fix!.Was == "Kate");
    }

    [Fact]
    public void APacksOwnLongerName_HoldsTheGamesNameInsideIt()
    {
        // The pack's own "Anna Smith" is not the game's Anna: a Fix there wrote
        // "{name:anna} Smith", which a rename of Anna turned into "Annie Smith".
        // Control: Anna on her own, in the next line, is listed.
        var pack = PackSaying("Anna Smith waves.", "Anna waves back.");
        pack.Characters.Add(new CharacterDef { Key = "annasmith", Name = "AnnaSmith", DisplayName = "Anna Smith", Pronouns = Pronouns.Female });
        var found = Issues(pack, NameMentions.PlainMention);
        var only = Assert.Single(found);
        Assert.Equal(new TextFix("talk", 2, 0, "Anna", "{name:anna}"), only.Fix);
    }

    [Fact]
    public void ADescriptionStandingInForAName_IsNeverListed()
    {
        // "Cop" is one of the game's characters, by a word: "the Cop arrived" is no
        // place for {name:cop}, which a rename would make "the Officer Dan arrived".
        // Control: Kirby, a name, is.
        var pack = PackSaying("Then the Cop arrived, and Kirby ran.");
        Assert.Contains(pack.Characters, c => c.VanillaCharacter == "Cop");
        var found = Issues(pack, NameMentions.PlainMention);
        Assert.DoesNotContain(found, i => i.Fix!.Was == "Cop");
        Assert.Contains(found, i => i.Fix!.Was == "Kirby");
    }

    [Fact]
    public void AStutteredName_IsListedWithNoFix_SinceTheTokenCannotCarryTheStutter()
    {
        var pack = PackSaying("A-Anna, wait.", "Anna, wait.");
        var found = Issues(pack, NameMentions.PlainMention);
        Assert.Equal(2, found.Count);
        Assert.Null(found[0].Fix);                                     // "A-{name:anna}" would read "A-Bella"
        Assert.Equal(new TextFix("talk", 2, 0, "Anna", "{name:anna}"), found[1].Fix);   // the control
    }

    [Fact]
    public void ANewNameTheyAreGivenCountsAsTheirsToo()
    {
        var pack = PackSaying("Annie and Anna are one person here.");
        pack.Characters.Single(c => c.VanillaCharacter == "Anna").NewName = "Annie";
        var found = Issues(pack, NameMentions.PlainMention);
        Assert.Equal(new[] { "Annie", "Anna" }, found.Select(i => i.Fix!.Was));
        Assert.All(found, i => Assert.Equal("{name:anna}", i.Fix!.Becomes));
    }

    [Fact]
    public void TheGamesOwnLinesAreNotListed_ALineThePackAddedToTheirConversationIs()
    {
        // The game's conversation, as the editor adopts it: its lines say "Anna" in
        // the game's words, which are not the pack's to change (that is another
        // stage). A line the pack adds to it is the pack's.
        const string path = "8_Room_Talk/Kitchen/Pre_events/AnnaJosefOutfitWhat";
        var pack = PackRepository.CreateEmpty("mentions.pack");
        var talk = new DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
        talk.VanillaSource = VanillaDialogueCatalog.Find(path);
        Assert.Contains(talk.Model.Nodes, n => (n.Text ?? "").Contains("Anna"));
        pack.Dialogues.Add(talk.Model);
        Assert.Empty(Issues(pack, NameMentions.PlainMention));

        var first = talk.Model.Nodes.Single(n => n.Id == talk.Model.RootNodeIds[0]);
        var added = new DialogueNodeDef { Id = 900001, Actor = first.Actor, Text = "Anna, sit down." };
        added.Children.AddRange(first.Children);
        first.Children.Clear();
        first.Children.Add(added.Id);
        talk.Model.Nodes.Add(added);
        var found = Issues(pack, NameMentions.PlainMention);
        Assert.Equal(900001, Assert.Single(found).Fix!.Node);
    }

    [Fact]
    public void ATokenNamingNobodyIsWarnedAbout_OneNamingSomebodyIsNot()
    {
        var pack = PackSaying("{name:anna} and {name:nobodyatall}.");
        var found = Issues(pack, NameMentions.UnknownToken);
        Assert.Contains("{name:nobodyatall}", Assert.Single(found).Message);
    }

    [Fact]
    public void ANewNameSomebodyElseAlreadyHasIsWarnedAbout()
    {
        var pack = PackSaying("Hi.");
        var anna = pack.Characters.Single(c => c.VanillaCharacter == "Anna");
        anna.NewName = "Emma";
        Assert.Single(Issues(pack, NameMentions.NewNameTaken));

        anna.NewName = "Annie";                      // the control: nobody's
        Assert.Empty(Issues(pack, NameMentions.NewNameTaken));
    }

    [Fact]
    public void TwoInstalledPacksGivingOneCharacterDifferentNewNamesClash_OnTheMenusList()
    {
        // What the game's main menu warns about (PackConflicts): every field a pack
        // sets on one of the game's characters, the new name with them.
        JObject Saved(string id, string? newName)
        {
            var pack = PackRepository.CreateEmpty(id);
            pack.PackId = id;
            if (newName != null) pack.Characters.Single(c => c.VanillaCharacter == "Anna").NewName = newName;
            return JObject.Parse(PackRepository.SerializeAsSaved(pack));
        }
        List<PackConflicts.Clash> Between(JObject a, JObject b) => PackConflicts.Find(new List<KeyValuePair<string, List<PackConflicts.Change>>>
        {
            new("first", PackConflicts.Of(a)), new("second", PackConflicts.Of(b)),
        });

        var clash = Assert.Single(Between(Saved("first", "Annie"), Saved("second", "Ann")));
        _out.WriteLine($"{clash.LaterChange.Thing} {clash.LaterChange.Field}: {clash.EarlierChange.Value} / {clash.LaterChange.Value}");
        Assert.Equal("newName", clash.LaterChange.Field);
        Assert.Equal("second", clash.Later);

        // Controls: the same new name agrees; only one pack renaming her is no clash.
        Assert.Empty(Between(Saved("first", "Annie"), Saved("second", "Annie")));
        Assert.Empty(Between(Saved("first", "Annie"), Saved("second", null)));
    }
}

/// <summary>The issue's offer, taken from the list in the real window.</summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NameMentionsFixTests
{
    private readonly ITestOutputHelper _out;
    public NameMentionsFixTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void TakingTheOfferWritesTheTokenInThatLineOnly_AndUndoTakesItBack()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();
            if (vm.SelectedDialogue!.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
            var node = vm.SelectedDialogue.Nodes.First();
            node.Text = "Hello Anna, and Anna again.";
            WindowHarness.Pump();

            // Validate wants a pack that has a folder.
            string dir = Path.Combine(Path.GetTempPath(), "smsmodforge-mentionfix-" + Guid.NewGuid().ToString("N"));
            PackRepository.Save(vm.Pack, dir);
            vm.PackRoot = dir;
            vm.ValidateCommand.Execute(null);
            WindowHarness.Pump();
            var offers = vm.Issues.Where(i => i.Code == NameMentions.PlainMention).ToList();
            Assert.Equal(2, offers.Count);
            Assert.False(string.IsNullOrEmpty(offers[0].Place), "the issue says nowhere an author recognises");

            // The menu says what it will write.
            var list = (ListBox)window.FindName("IssueList");
            list.SelectedItem = vm.Issues.First(i => i.Fix != null && i.Fix.Start == 16);
            // What opening the list's menu runs (its event args cannot be made outside WPF).
            typeof(MainWindow).GetMethod("IssueList_ContextMenuOpening",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(window, new object?[] { list, null });
            var item = (MenuItem)window.FindName("MenuFixIssue");
            _out.WriteLine($"{item.Header} (visible {item.Visibility}, enabled {item.IsEnabled})");
            Assert.Equal("Write {name:anna} there instead", item.Header);
            Assert.True(item.IsEnabled);

            Assert.True(vm.FixIssue((ValidationIssue)list.SelectedItem));
            WindowHarness.Pump();
            _out.WriteLine(node.Text);
            Assert.Equal("Hello Anna, and {name:anna} again.", node.Text);
            Assert.Single(vm.Issues, i => i.Code == NameMentions.PlainMention);

            // A line edited since Validate ran is left alone: the offer now points
            // at words that moved.
            var stale = vm.Issues.Single(i => i.Code == NameMentions.PlainMention);
            node.Text = "Changed. Hello Anna, and {name:anna} again.";
            Assert.False(vm.FixIssue(stale));
            Assert.Equal("Changed. Hello Anna, and {name:anna} again.", node.Text);

            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();
            var back = vm.SelectedDialogue!.Nodes.First();
            _out.WriteLine("after undo: " + back.Text);
            Assert.Equal("Hello Anna, and Anna again.", back.Text);
            try { Directory.Delete(dir, true); } catch (IOException) { }
        });
    }
}
