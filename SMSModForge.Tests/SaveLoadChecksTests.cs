using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;
using C = SMSModForge.Shared.SaveLoadChecks;

namespace SMSModForge.Tests;

/// <summary>
/// What the game checks when a save is loaded, shared with the plugin: which of
/// the game's own things a pack changes in a way that can break a save already
/// under way, which packs a save has data for that are not running, and what
/// the player is told.
/// <para/>
/// The kinds are read off the manifest the editor actually writes - the same
/// text the plugin reads - so a change to how a section is saved cannot quietly
/// stop the game from warning about it.
/// </summary>
public sealed class SaveLoadChecksTests
{
    private readonly ITestOutputHelper _out;
    public SaveLoadChecksTests(ITestOutputHelper o) => _out = o;

    private const string Anna = "8_Room_Talk/Beach/AnnaBeachDefault";

    private static List<string> KindsOf(ModPack pack)
        => C.Of(JObject.Parse(PackRepository.SerializeAsSaved(pack)));

    // ── What a pack changes ───────────────────────────────────────────

    [Fact]
    public void APackThatChangesNothingOfTheGamesIsNotRisky()
    {
        var pack = new ModPack();
        pack.Quests.Add(new QuestDef { Key = "mine", Title = "Mine" });
        pack.Places.Add(new PlaceDef { Key = "cave" });
        Assert.Empty(KindsOf(pack));
        Assert.Empty(C.Of(null!));
    }

    [Fact]
    public void AddingOrTakingOutTasksChangesTheGamesQuests()
    {
        var game = VanillaQuests.All.First(q => q.Tasks.Count >= 2);
        var pack = new ModPack();
        var def = new QuestDef { Key = "ext", Source = game.Name, Description = "Only words." };
        def.VanillaTasks.Add(new VanillaTaskHookDef { Task = game.Tasks[0].Id.ToString(), Visibility = QuestTreeEdits.Hidden });
        pack.Quests.Add(def);

        // Words and hiding change nothing about how the quest runs.
        Assert.Empty(KindsOf(pack));

        def.VanillaTasks[0].Removed = true;
        Assert.Equal(new[] { C.Quests }, KindsOf(pack));

        def.VanillaTasks[0].Removed = false;
        def.AddedTasks.Add(new AddedTaskDef { Key = "Ask", Name = "Ask" });
        Assert.Equal(new[] { C.Quests }, KindsOf(pack));
    }

    [Fact]
    public void ChangingALineChangesTheGamesConversations()
    {
        var vm = new DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
        vm.VanillaSource = VanillaDialogueCatalog.Find(Anna);
        var pack = new ModPack();
        pack.Dialogues.Add(vm.Model);

        // Seeded but untouched: it saves as nothing to apply.
        string untouched = PackRepository.SerializeAsSaved(pack);
        _out.WriteLine(JObject.Parse(untouched)["dialogues"]?.ToString() ?? "(no dialogues)");
        Assert.Empty(C.Of(JObject.Parse(untouched)));

        vm.Model.Nodes.First(n => n.Id == vm.Model.RootNodeIds[0]).Text = "Come on, I brought sunscreen.";
        Assert.Equal(new[] { C.Dialogues }, KindsOf(pack));
    }

    [Fact]
    public void ChangingAPlaceIsNotAskedAbout()
    {
        // The user's call (2026-09-17): buttons and objects added to one of
        // the game's places, or its own objects changed, do not warn.
        var vm = new MainViewModel();
        vm.AddVanillaExtensionCommand.Execute(null);
        var extension = vm.VanillaExtensions.Single();
        Assert.True(extension.HasCatalogEntry, "the default place has no extracted hierarchy to mirror");
        extension.SeedFromCatalog();
        extension.AddNavigatorButton();

        string saved = PackRepository.SerializeAsSaved(vm.Pack);
        Assert.Contains("navigatorButtons", saved);
        Assert.Empty(KindsOf(vm.Pack));
    }

    [Fact]
    public void LooksAloneAreNotRisky()
    {
        // A screen of the game's and one of its characters restyled: nothing
        // about a save changes.
        var pack = new ModPack();
        pack.Uis.Add(new UiDef { Id = "hud", Source = "9_MainCanvas" });
        pack.Characters.Add(new CharacterDef { Name = "Amelia", BustSource = BustSource.Vanilla, VanillaCharacter = "Amelia" });
        var kinds = KindsOf(pack);
        _out.WriteLine(string.Join(",", kinds));
        Assert.Empty(kinds);
    }

    [Fact]
    public void BothKindsAreNamedInOrderAndPlacesAreNot()
    {
        var manifest = JObject.Parse(@"{
            ""vanillaExtensions"": [ { ""source"": ""vanilla:14_Beach"", ""gameObjects"": [ { ""name"": ""x"" } ],
                                     ""navigatorButtons"": [ { ""label"": ""Go"" } ] } ],
            ""dialogues"": [ { ""source"": ""vanilladialogue:a"", ""removedNodes"": [ 3 ] } ],
            ""quests"": [ { ""key"": ""e"", ""source"": ""Q"", ""addedTasks"": [ { ""key"": ""t"" } ] } ]
        }");
        Assert.Equal(new[] { C.Quests, C.Dialogues }, C.Of(manifest));
        Assert.Equal(new[] { C.Quests, C.Dialogues }, C.All);
    }

    // ── What a save has been played with ──────────────────────────────

    [Fact]
    public void ASaveIsAskedOnlyAboutWhatItHasNotSeen()
    {
        Assert.Equal(new[] { C.Quests }, C.NotYetSeen(new[] { C.Quests }, ""));

        string mark = C.Merge("", new[] { C.Quests });
        Assert.Equal("quests", mark);
        Assert.Empty(C.NotYetSeen(new[] { C.Quests }, mark));

        // A pack update that starts changing conversations asks again - about those.
        Assert.Equal(new[] { C.Dialogues }, C.NotYetSeen(new[] { C.Quests, C.Dialogues }, mark));
        Assert.Equal("quests,dialogues", C.Merge(mark, new[] { C.Dialogues }));

        // A kind a later version adds is kept, not lost.
        Assert.Equal("quests,future", C.Merge("future", new[] { C.Quests }));

        // And one an earlier version wrote is kept and asks nothing: a save
        // marked "places" before places stopped being asked about.
        Assert.Equal("quests,dialogues,places", C.Merge("quests,places", new[] { C.Dialogues }));
        Assert.Equal(new[] { C.Dialogues }, C.NotYetSeen(new[] { C.Dialogues }, "places"));
    }

    // ── Packs a save has data for ─────────────────────────────────────

    [Fact]
    public void ThePacksASaveHasDataForAreReadOffItsFileNames()
    {
        var files = new[] { "SAVE.GZ", "game_data.meta", "SMSModForge_Alpha.json", "SMSModForge_beta.json",
                            "GalleryModSave.txt", "SMSModForge_.json", "SMSModForge_Gamma.json.bak" };
        Assert.Equal("Alpha", C.PackIdOfSaveFile("SMSModForge_Alpha.json"));
        Assert.Null(C.PackIdOfSaveFile("SMSModForge_.json"));

        var missing = C.MissingPacks(files, new[] { "BETA" });
        _out.WriteLine(string.Join(", ", missing));
        Assert.Equal(new[] { "Alpha" }, missing);

        Assert.Empty(C.MissingPacks(files, new[] { "Alpha", "Beta" }));
    }

    // ── What the player is told ───────────────────────────────────────

    [Fact]
    public void NothingToSayIsNoWindow()
    {
        Assert.Null(C.Warning(new List<C.PackChanges>(), Array.Empty<string>()));
        Assert.Null(C.Warning(null!, null!));
        Assert.Null(C.Warning(new[] { new C.PackChanges("Alpha", new List<string>()) }, null!));
    }

    private void Show(SaveWarningText t)
    {
        _out.WriteLine(t.Title);
        foreach (var p in t.Paragraphs) _out.WriteLine("  " + p);
        foreach (var section in t.Details)
        {
            _out.WriteLine("    [box] " + section.Heading);
            foreach (var item in section.Items) _out.WriteLine("    [box]   " + SaveWarningText.Bullet + item);
        }
        foreach (var p in t.After) _out.WriteLine("  " + p);
        _out.WriteLine("");
    }

    [Fact]
    public void OnePackIsOneSentenceInTheSingular()
    {
        var one = C.Warning(new[] { new C.PackChanges("Alpha", new[] { C.Quests, C.Dialogues }) }, Array.Empty<string>())!;
        Show(one);
        Assert.Equal(new[]
        {
            "'Alpha' changes the game's own quests and conversations, and this save hasn't been played with that "
            + "yet. Your progress could break - now, or if you remove the pack later.",
        }, one.Paragraphs);
        // One pack says what it changes in the sentence, so there is no list.
        Assert.Empty(one.Details);
        Assert.Equal(new[] { "Starting a new game is safest." }, one.After);
    }

    [Fact]
    public void SeveralPacksAreAListInThePlural()
    {
        var several = C.Warning(new[]
        {
            new C.PackChanges("Alpha", new[] { C.Quests, C.Dialogues }),
            new C.PackChanges("Beta", new[] { C.Dialogues }),
        }, Array.Empty<string>())!;
        Show(several);
        // The sentence stays one sentence however many packs there are; what
        // each changes goes in the list, which is the part that scrolls.
        Assert.Equal(new[]
        {
            "These packs change the game's own content, and this save hasn't been played with that yet. "
            + "Your progress could break - now, or if you remove them later.",
        }, several.Paragraphs);
        var section = Assert.Single(several.Details);
        Assert.Equal("What each pack changes:", section.Heading);
        Assert.Equal(new[] { "'Alpha': quests and conversations", "'Beta': conversations" }, section.Items);
        Assert.Equal(new[] { "Starting a new game is safest." }, several.After);

        // Nothing in the singular is left in a message about several.
        string all = several.Plain();
        Assert.DoesNotContain("the pack ", all);
        Assert.DoesNotContain("'Alpha' changes", all);
    }

    [Fact]
    public void WhatAPackChangesIsSaidAsWhatItIsRatherThanHowItIsDone()
    {
        // The reported case: a quest entry whose only change to the game is
        // four conditions taken out of the bar's script, where that quest
        // starts. It is a quest change, and saying "it changes the game's own
        // conversations" tells its author they rewrote dialogue they never
        // opened. The same conditions taken out anywhere else are exactly what
        // that sentence is for.
        var (pack, quest) = BarPack();
        var manifest = JObject.Parse(PackRepository.SerializeAsSaved(pack));
        Assert.Equal(new[] { C.Dialogues }, C.Of(manifest));
        Assert.Equal(new[] { "when quests start" }, C.WordsOf(manifest)[C.Dialogues]);

        var text = C.Warning(new[] { new C.PackChanges("TestPack", C.Of(manifest), C.WordsOf(manifest)) }, null!)!;
        Show(text);
        Assert.StartsWith("'TestPack' changes when the game's own quests start, and this save hasn't been played "
                          + "with that yet.", text.Paragraphs[0]);
        Assert.DoesNotContain("conversations", text.Paragraphs[0]);

        // The same removal with no quest of the pack's at that place is what it
        // looks like from the Dialogues tab: when a conversation plays.
        quest.SiteConditions.Clear();
        quest.Source = "";
        pack.Quests.Clear();
        var loose = JObject.Parse(PackRepository.SerializeAsSaved(pack));
        Assert.Equal(new[] { "when conversations play" }, C.WordsOf(loose)[C.Dialogues]);
        Assert.StartsWith("'TestPack' changes when the game's own conversations play,",
            C.Warning(new[] { new C.PackChanges("TestPack", C.Of(loose), C.WordsOf(loose)) }, null!)!.Paragraphs[0]);
    }

    [Fact]
    public void AConditionTakenOffTheLineAQuestStartsAtIsAQuestChangeToo()
    {
        // From the Quests tab, a line's condition is taken out of the pack's
        // version of the conversation. That line is where the quest starts,
        // so it is the quest's start that changed - not a rewritten line.
        var vm = new MainViewModel();
        vm.AddVanillaQuestCommand.Execute(null);
        var entry = vm.SelectedQuest!;
        entry.Source = "Vanessa Quest";
        var line = entry.GameSites.Single(g => g.Heading == "Started by").Sites
                        .Single(s => s.Site.Node == -1608623911)
                        .ConditionGroups.Single(g => g.Title == "This line");
        line.Rows.First(r => r.CanRemove).RemoveCommand.Execute(null);

        var manifest = JObject.Parse(PackRepository.SerializeAsSaved(vm.Pack));
        Assert.Equal(new[] { C.Dialogues }, C.Of(manifest));
        Assert.Equal(new[] { "when quests start" }, C.WordsOf(manifest)[C.Dialogues]);
        Assert.Equal("Vanessa Quest", Assert.Single(C.QuestsChanged(manifest)).Key);

        // The same line rewritten as well is a conversation change on top.
        var talk = vm.VanillaDialogues.Single();
        talk.Model.Nodes.Single(n => n.Id == unchecked((int)-1608623911)).Text = "Different words.";
        var rewritten = JObject.Parse(PackRepository.SerializeAsSaved(vm.Pack));
        Assert.Equal(new[] { "conversations" }, C.WordsOf(rewritten)[C.Dialogues]);
    }

    [Fact]
    public void RewritingALineAndTakingConditionsOutAreSaidTogether()
    {
        var (pack, _) = BarPack();
        var conversation = new DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
        conversation.VanillaSource = VanillaDialogueCatalog.Find(Anna);
        pack.Dialogues.Add(conversation.Model);
        conversation.Model.Nodes.First(n => n.Id == conversation.Model.RootNodeIds[0]).Text = "Suit yourself.";

        var manifest = JObject.Parse(PackRepository.SerializeAsSaved(pack));
        Assert.Equal(new[] { "conversations", "when quests start" }, C.WordsOf(manifest)[C.Dialogues]);
        var text = C.Warning(new[] { new C.PackChanges("TestPack", C.Of(manifest), C.WordsOf(manifest)) }, null!)!;
        Show(text);
        Assert.StartsWith("'TestPack' changes the game's own conversations and when its quests start,",
                          text.Paragraphs[0]);

        // With tasks changed as well, the quests are named once.
        var words = C.WordsOf(manifest);
        Assert.StartsWith("'TestPack' changes the game's own quests, conversations and when its quests start,",
            C.Warning(new[] { new C.PackChanges("TestPack", new[] { C.Quests, C.Dialogues }, words) }, null!)!
                .Paragraphs[0]);
    }

    [Fact]
    public void AQuestWhosePlacesThePackTookOverIsListedToo()
    {
        // TestPack adds and takes out no tasks at all, and the quest it changes
        // is still the one the player wants to read about.
        var (pack, _) = BarPack();
        var manifest = JObject.Parse(PackRepository.SerializeAsSaved(pack));
        var changed = Assert.Single(C.QuestsChanged(manifest));
        Assert.Equal(Drink, changed.Key);
        Assert.Empty(changed.Value);        // nothing added, so nothing can get stuck

        var text = C.Warning(new[] { new C.PackChanges("TestPack", C.Of(manifest), C.WordsOf(manifest)) }, null!,
                             new[] { new C.ChangedQuest(Drink, QuestTreeEdits.SaveProgress.InProgress) })!;
        Show(text);
        var section = Assert.Single(text.Details);
        Assert.Equal("Quest changed:", section.Heading);
        Assert.Equal(new[] { Drink + ": in progress, should be fine" }, section.Items);
    }

    private const string Drink = "Just A Drink (Liz)";

    /// <summary>A pack shaped like the reported one: a quest entry about one of
    /// the game's quests, changed only by conditions taken out of the script
    /// that plays the conversation it starts at.</summary>
    private static (ModPack Pack, QuestDef Quest) BarPack()
    {
        var vm = new MainViewModel();
        vm.AddVanillaQuestCommand.Execute(null);
        var entry = vm.SelectedQuest!;
        entry.Source = Drink;
        var site = entry.GameSites.Single(g => g.Heading == "Started by").Sites
                        .Single(s => s.Site.Dialogue == "8_Room_Talk/Bar/AnnaAndLiz1");
        foreach (var row in site.ConditionGroups.SelectMany(g => g.Rows).Where(r => r.CanRemove && !r.IsRoom).ToList())
            row.RemoveCommand.Execute(null);
        vm.Pack.PackId = "TestPack";
        return (vm.Pack, entry.Model);
    }

    [Fact]
    public void ASaveFromBeforeTheMarksIsNotToldItHasNeverSeenWhatItMayHave()
    {
        // A save last written before 1.5.0 was played with the pack, but
        // nothing wrote down with which of its changes. Rewritten conversations
        // are the one risky change a pack could already make then.
        var lines = new Dictionary<string, List<string>> { [C.Dialogues] = new List<string> { "conversations" } };
        var old = C.Warning(new[]
        {
            new C.PackChanges("Alpha", new[] { C.Dialogues }, lines) { SaveBeforeMarks = true },
        }, null!)!;
        Show(old);
        Assert.Equal("'Alpha' changes the game's own conversations, and ModForge can't tell whether this save has "
                     + "been played with that. Your progress could break - now, or if you remove the pack later.",
                     Assert.Single(old.Paragraphs));

        // A pack whose words are not known is taken the careful way.
        Assert.Contains("can't tell", Assert.Single(C.Warning(new[]
        {
            new C.PackChanges("Alpha", new[] { C.Dialogues }) { SaveBeforeMarks = true },
        }, null!)!.Paragraphs));

        // Everything else a pack can change is newer than the marks, so an older
        // save has certainly never been played with it.
        const string never = "this save hasn't been played with that yet.";
        var starts = new Dictionary<string, List<string>> { [C.Dialogues] = new List<string> { "when quests start" } };
        var plays = new Dictionary<string, List<string>> { [C.Dialogues] = new List<string> { "when conversations play" } };
        foreach (var pack in new[]
                 {
                     new C.PackChanges("Alpha", new[] { C.Dialogues }, starts) { SaveBeforeMarks = true },
                     new C.PackChanges("Alpha", new[] { C.Dialogues }, plays) { SaveBeforeMarks = true },
                     new C.PackChanges("Alpha", new[] { C.Quests }) { SaveBeforeMarks = true },
                 })
            Assert.Contains(never, Assert.Single(C.Warning(new[] { pack }, null!)!.Paragraphs));

        // A save marked since is not in doubt either.
        Assert.Contains(never, Assert.Single(C.Warning(new[]
        {
            new C.PackChanges("Alpha", new[] { C.Dialogues }, lines),
        }, null!)!.Paragraphs));

        // Several packs, one of them in doubt: the sentence says so for all of it.
        var several = C.Warning(new[]
        {
            new C.PackChanges("Alpha", new[] { C.Dialogues }, lines) { SaveBeforeMarks = true },
            new C.PackChanges("Beta", new[] { C.Quests }),
        }, null!)!;
        Show(several);
        Assert.Equal("These packs change the game's own content, and ModForge can't tell whether this save has "
                     + "been played with all of it. Your progress could break - now, or if you remove them later.",
                     Assert.Single(several.Paragraphs));
    }

    [Fact]
    public void MissingPacksAgreeInNumber()
    {
        var gone = C.Warning(new List<C.PackChanges>(), new[] { "Gamma", "Delta" })!;
        var alone = C.Warning(new List<C.PackChanges>(), new[] { "Gamma" })!;
        var both = C.Warning(new[] { new C.PackChanges("Alpha", new[] { C.Quests }) }, new[] { "Gamma" })!;
        foreach (var t in new[] { gone, alone, both }) Show(t);

        Assert.Equal("This save has data from 'Gamma' and 'Delta', which aren't installed. Their data won't be kept "
                     + "in the saves you make from now on.", Assert.Single(gone.After));
        Assert.Equal("This save has data from 'Gamma', which isn't installed. Its data won't be kept in the saves "
                     + "you make from now on.", Assert.Single(alone.After));
        Assert.Empty(gone.Paragraphs);
        Assert.Empty(gone.Details);

        // Both reasons in one window, the second saying it is another.
        Assert.StartsWith("'Alpha' changes the game's own quests,", Assert.Single(both.Paragraphs));
        Assert.Equal(2, both.After.Count);
        Assert.Equal("Starting a new game is safest.", both.After[0]);
        Assert.StartsWith("This save also has data from 'Gamma', which isn't installed.", both.After[1]);
    }

    [Fact]
    public void TheQuestsAPackChangesAreListedWithWhereTheSaveIs()
    {
        var packs = new[] { new C.PackChanges("Alpha", new[] { C.Quests }) };
        var quests = new[]
        {
            new C.ChangedQuest("Not Yet", QuestTreeEdits.SaveProgress.NotStarted),
            new C.ChangedQuest("All Done", QuestTreeEdits.SaveProgress.Finished),
            new C.ChangedQuest("Going", QuestTreeEdits.SaveProgress.InProgress),
            new C.ChangedQuest("Stuck", QuestTreeEdits.SaveProgress.MayGetStuck),
            // Two packs changing one quest: named once, by the worse of the two.
            new C.ChangedQuest("Going", QuestTreeEdits.SaveProgress.MayGetStuck),
        };
        var text = C.Warning(packs, null!, quests)!;
        Show(text);
        // The list is in the box, and only there: the warning's own sentences
        // are the same length however many quests there are.
        Assert.StartsWith("'Alpha' changes the game's own quests,", Assert.Single(text.Paragraphs));
        Assert.Equal(new[] { "Starting a new game is safest." }, text.After);
        var section = Assert.Single(text.Details);
        Assert.Equal("Quests changed:", section.Heading);
        Assert.Equal(new[]
        {
            "Going: in progress, but a new task was added before where you are - it may get stuck",
            "Stuck: in progress, but a new task was added before where you are - it may get stuck",
            "All Done: already finished, so you won't see the changes",
            "Not Yet: not started yet, fine",
        }, section.Items);

        var single = C.Warning(packs, null!, new[] { new C.ChangedQuest("Going", QuestTreeEdits.SaveProgress.InProgress) })!;
        var one = Assert.Single(single.Details);
        Assert.Equal("Quest changed:", one.Heading);
        Assert.Equal(new[] { "Going: in progress, should be fine" }, one.Items);

        // Only said beside a warning about the packs' changes.
        var onlyMissing = C.Warning(new List<C.PackChanges>(), new[] { "Gamma" }, quests)!;
        Assert.Empty(onlyMissing.Details);
    }

    [Fact]
    public void TheQuestsAManifestChangesAreFoundWithTheTasksItAdds()
    {
        var game = VanillaQuests.All.First(q => q.Tasks.Count >= 2);
        var other = VanillaQuests.All.First(q => q.Name != game.Name && q.Tasks.Count >= 1);
        var pack = new ModPack { PackId = "alpha" };
        var adds = new QuestDef { Key = "a", Source = game.Name };
        var extra = new AddedTaskDef { Key = "extra", Name = "Extra" };
        extra.Subtasks.Add(new QuestTaskDef { Key = "extra-part", Name = "Part" });
        adds.AddedTasks.Add(extra);
        var removes = new QuestDef { Key = "b", Source = other.Name };
        removes.VanillaTasks.Add(new VanillaTaskHookDef
        {
            Task = other.Tasks[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Removed = true,
        });
        var words = new QuestDef { Key = "c", Source = game.Name, Description = "Only words." };
        pack.Quests.Add(adds);
        pack.Quests.Add(removes);
        pack.Quests.Add(words);
        pack.Quests.Add(new QuestDef { Key = "own", Title = "Own" });

        var found = C.QuestsChanged(JObject.Parse(PackRepository.SerializeAsSaved(pack)));
        Assert.Equal(new[] { game.Name, other.Name }, found.Select(f => f.Key));
        Assert.Equal(new[] { "extra", "extra-part" }, found[0].Value);
        Assert.Empty(found[1].Value);
        Assert.Empty(C.QuestsChanged(null!));
    }

    // ── Where a save is in a quest ────────────────────────────────────

    private static QuestTreeEdits.Node N(int id, QuestTreeEdits.Completion how = QuestTreeEdits.Completion.InOrder,
                                         params QuestTreeEdits.Node[] children)
    {
        var node = new QuestTreeEdits.Node { Id = id, Completion = how };
        node.Children.AddRange(children);
        return node;
    }

    private static QuestTreeEdits.SaveProgress Progress(QuestTreeEdits.Node[] roots, int added,
                                                        params (int Id, QuestTreeEdits.TaskState State)[] states)
        => QuestTreeEdits.ProgressOf(QuestTreeEdits.TaskState.Active, roots, new[] { added },
            id => states.Where(s => s.Id == id).Select(s => s.State).DefaultIfEmpty(QuestTreeEdits.TaskState.Inactive).First());

    private const QuestTreeEdits.TaskState Off = QuestTreeEdits.TaskState.Inactive,
        On = QuestTreeEdits.TaskState.Active, Done = QuestTreeEdits.TaskState.Completed;

    [Fact]
    public void AQuestNotStartedOrFinishedIsSaidSoWhateverItsTasks()
    {
        var roots = new[] { N(1), N(99), N(2) };
        Assert.Equal(QuestTreeEdits.SaveProgress.NotStarted,
            QuestTreeEdits.ProgressOf(Off, roots, new[] { 99 }, _ => Off));
        foreach (var finished in new[] { Done, QuestTreeEdits.TaskState.Failed, QuestTreeEdits.TaskState.Abandoned })
            Assert.Equal(QuestTreeEdits.SaveProgress.Finished,
                QuestTreeEdits.ProgressOf(finished, roots, new[] { 99 }, _ => Done));
    }

    [Fact]
    public void ATaskAddedBeforeWhereThePlayerIsHoldsTheQuestUp()
    {
        // In order: a task can only be completed once everything before it is.
        var roots = new[] { N(1), N(99), N(2), N(3) };
        Assert.Equal(QuestTreeEdits.SaveProgress.MayGetStuck, Progress(roots, 99, (1, Done), (2, On)));
        Assert.Equal(QuestTreeEdits.SaveProgress.MayGetStuck, Progress(roots, 99, (1, Done), (2, Done), (3, On)));
        // Ahead of the player, it is started when the game gets there.
        Assert.Equal(QuestTreeEdits.SaveProgress.InProgress, Progress(roots, 99, (1, On)));
        Assert.Equal(QuestTreeEdits.SaveProgress.InProgress, Progress(roots, 99, (1, Done)));
        // Already started or done: the save has it.
        Assert.Equal(QuestTreeEdits.SaveProgress.InProgress, Progress(roots, 99, (1, Done), (99, On)));
        Assert.Equal(QuestTreeEdits.SaveProgress.InProgress, Progress(roots, 99, (1, Done), (99, Done), (2, On)));
        // Nothing added: nothing to hold anything up.
        Assert.Equal(QuestTreeEdits.SaveProgress.InProgress,
            QuestTreeEdits.ProgressOf(On, roots, new int[0], id => id == 3 ? On : Done));
    }

    [Fact]
    public void UnderATaskItDependsOnHowThatTaskRunsItsSubtasks()
    {
        QuestTreeEdits.Node[] Under(QuestTreeEdits.Completion how) => new[] { N(1, how, N(10), N(99), N(11)), N(2) };

        // Started together: one added after the start is never started, and
        // the task waits for all of them.
        Assert.Equal(QuestTreeEdits.SaveProgress.MayGetStuck,
            Progress(Under(QuestTreeEdits.Completion.AnyOrder), 99, (1, On), (10, On), (11, On)));
        // Any one of them finishes it; subtasks the game starts itself are its to start.
        Assert.Equal(QuestTreeEdits.SaveProgress.InProgress,
            Progress(Under(QuestTreeEdits.Completion.AnyOne), 99, (1, On), (10, On), (11, On)));
        Assert.Equal(QuestTreeEdits.SaveProgress.InProgress,
            Progress(Under(QuestTreeEdits.Completion.ByAction), 99, (1, On), (11, On)));
        // In order, the same rule as at the top.
        Assert.Equal(QuestTreeEdits.SaveProgress.MayGetStuck,
            Progress(Under(QuestTreeEdits.Completion.InOrder), 99, (1, On), (10, Done), (11, On)));
        Assert.Equal(QuestTreeEdits.SaveProgress.InProgress,
            Progress(Under(QuestTreeEdits.Completion.InOrder), 99, (1, On), (10, On)));
        // Under a finished task nothing waits on it.
        Assert.Equal(QuestTreeEdits.SaveProgress.InProgress,
            Progress(Under(QuestTreeEdits.Completion.AnyOrder), 99, (1, Done), (10, Done), (11, Done), (2, On)));
        // Under one not started, it is where that one sits that counts.
        Assert.Equal(QuestTreeEdits.SaveProgress.InProgress,
            Progress(new[] { N(1), N(2, QuestTreeEdits.Completion.AnyOrder, N(99)) }, 99, (1, On)));
        Assert.Equal(QuestTreeEdits.SaveProgress.MayGetStuck,
            Progress(new[] { N(5, QuestTreeEdits.Completion.AnyOrder, N(99)), N(1) }, 99, (1, On)));
        // A later task counts as started when anything under it has.
        Assert.Equal(QuestTreeEdits.SaveProgress.MayGetStuck,
            Progress(new[] { N(99), N(1, QuestTreeEdits.Completion.InOrder, N(10)) }, 99, (10, On)));
        // An id the tree does not have is nobody's to wait on.
        Assert.Equal(QuestTreeEdits.SaveProgress.InProgress, Progress(new[] { N(1), N(2) }, 99, (2, On)));
    }

    // ── On screen ─────────────────────────────────────────────────────

    [Fact]
    public void TheDialoguesTabSaysWhatThePlayerWillBeToldAndThePlacesTabDoesNot()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");

            var extension = new DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
            extension.VanillaSource = VanillaDialogueCatalog.Find(Anna);
            vm.Dialogues.Add(extension);
            vm.RefreshVanillaDialogues();
            tabs.SelectedItem = tabs.Items.Cast<TabItem>().First(t => (string)t.Header == "Dialogues");
            vm.SelectedVanillaDialogue = extension;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();
            var dialogueNote = (FrameworkElement)window.FindName("DialogueChangeWarning");
            Assert.True(dialogueNote.IsVisible, "the Dialogues tab does not warn about changing the game's conversations");

            vm.AddVanillaExtensionCommand.Execute(null);
            tabs.SelectedItem = tabs.Items.Cast<TabItem>().First(t => (string)t.Header == "Places");
            vm.SelectedVanillaExtension = vm.VanillaExtensions.Single();
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();
            // Nothing is asked about places, so nothing on the tab says it is.
            Assert.Null(window.FindName("PlaceChangeWarning"));
            var said = FindAll<TextBlock>(window).Where(t => t.IsVisible).Select(t => t.Text).ToList();
            Assert.DoesNotContain(said, t => t.Contains("recommends a new game"));
        });
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in FindAll<T>(child)) yield return deeper;
        }
    }
}
