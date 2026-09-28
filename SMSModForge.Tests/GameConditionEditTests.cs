using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.Validation;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Changing the conditions around the game's own places that start or reset a
/// quest: the game's conditions taken out and put back (on the script, or on
/// a line of a conversation), the pack's conditions the quest step waits for,
/// the pack's own reset conditions, and the marks and resets that say what an
/// entry changes.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class GameConditionEditTests
{
    private readonly ITestOutputHelper _out;
    public GameConditionEditTests(ITestOutputHelper o) => _out = o;

    private const int TabQuests = 13, TabDialogues = 5;

    private const string Himari = "Dinner Boyfriend (Himari)";
    private const string HimariTalk = "8_Room_Talk/Entrance/HiramiQuestStart";
    private const string Entrance = "8_Room_Talk/Entrance";

    private const string Vanessa = "Vanessa Quest";
    private const string LibraryTalk = "8_Room_Talk/C_Library/LibraryTalk_Default";
    private const long LibraryLine = -1608623911;

    private static (MainViewModel Vm, QuestViewModel Quest) Extension(string quest)
    {
        var vm = new MainViewModel();
        vm.AddVanillaQuestCommand.Execute(null);
        var entry = vm.SelectedQuest!;
        entry.Source = quest;
        return (vm, entry);
    }

    private static GameQuestSiteViewModel StartSite(QuestViewModel quest, System.Func<GameQuestSiteViewModel, bool> which)
        => quest.GameSites.Single(g => g.Heading == "Started by").Sites.Single(which);

    private static GameConditionGroupViewModel RoomGroup(GameQuestSiteViewModel site)
        => site.ConditionGroups.Single(g => g.Title.StartsWith("The conversation plays from " + Entrance + " "));

    /// <summary>A row for one of the game's own conditions in the script's
    /// list - past the row for the room the script sits on, which is a row of
    /// its own and can be taken out too.</summary>
    private static GameConditionRowViewModel GameRow(GameConditionGroupViewModel group)
        => group.Rows.First(r => r.CanRemove && r.Condition?.Model.Type != NodeConditionTypes.LevelActive);

    // -- Where the game keeps its conditions ---------------------------

    [Fact]
    public void TheCatalogueSaysWhereEachListIs()
    {
        var start = VanillaDialogueCatalog.Open(HimariTalk)!.Starts.Single(s => s.By == Entrance);
        var gate = Assert.Single(start.Gates);
        Assert.Equal("m_Branches/m_Branches/2/m_ConditionList", GameConditionEdits.PathText(gate.At));
        Assert.Equal(2, gate.Count);
        Assert.Equal(start.When.Count, gate.Count);

        // Everywhere: the lists never claim more conditions than there are.
        foreach (var entry in VanillaDialogueCatalog.All)
            foreach (var s in VanillaDialogueCatalog.Open(entry.Id)!.Starts)
                Assert.True(s.Gates.Sum(g => g.Count) <= s.When.Count, entry.Id + " claims more than it has");
    }

    [Fact]
    public void ARemovalIsOnlyTakenWhereTheGameStillHasIt()
    {
        var refused = new List<KeyValuePair<int, string>>();
        var kept = GameConditionEdits.Kept(
            new[] { "A", "B", "C" },
            new[] { new KeyValuePair<int, string>(1, "B"), new KeyValuePair<int, string>(2, "X"),
                    new KeyValuePair<int, string>(5, "C") },
            refused);
        Assert.Equal(new[] { 0, 2 }, kept);
        Assert.Equal(new[] { 2, 5 }, refused.Select(r => r.Key));
    }

    // -- A room's conditions, from either tab --------------------------

    [Fact]
    public void TakingARoomConditionOutShowsOnBothTabsAndComesBack()
    {
        var (vm, quest) = Extension(Himari);
        var site = StartSite(quest, s => s.Site.Dialogue == HimariTalk);
        Assert.True(site.IsEditable);
        var group = RoomGroup(site);
        var row = GameRow(group);
        _out.WriteLine(string.Join(" | ", group.Rows.Select(r => (r.CanRemove ? "x " : "- ") + (r.Condition?.Model.Type ?? r.Text))));
        Assert.False(row.IsRemoved);
        Assert.Equal("unchanged", quest.ChangeSummary);

        row.RemoveCommand.Execute(null);

        var gate = Assert.Single(vm.Pack.VanillaGates);
        Assert.Equal(Entrance, gate.By);
        Assert.Equal("Conditions", gate.Script);
        Assert.Equal("m_Branches/m_Branches/2/m_ConditionList", GameConditionEdits.PathText(gate.At));
        var removed = Assert.Single(gate.Removed);
        Assert.Equal(0, removed.Index);
        Assert.Equal("ConditionMathCompareBooleans", removed.Type);
        Assert.Contains("himari-first-dialogue-when-rich", removed.Title);

        Assert.True(row.IsRemoved);
        Assert.True(site.IsChanged);
        Assert.Equal("Changed: 1 of the game's conditions taken out.", site.ChangedText);
        Assert.Equal("1 other change", quest.ChangeSummary);
        Assert.True(quest.WarnsOnLoad);
        Assert.Contains("takes conditions out of the game's own conversations or scripts", quest.LoadWarningText);

        // The Dialogues tab shows the same thing.
        Assert.True(vm.OpenVanillaConversation(site));
        var talk = vm.SelectedDialogue!;
        var there = talk.GateGroups.Single(g => g.Title.StartsWith("Played from " + Entrance + " "));
        Assert.True(there.Rows.Single(r => r.IsRemoved).CanRemove);
        Assert.Equal(1, talk.GateRemovals);
        Assert.Contains("1 condition to play it taken out", talk.ChangeSummary);

        // Put back from there, and the quest hears of it.
        there.Rows.Single(r => r.IsRemoved).UndoCommand.Execute(null);
        Assert.Empty(vm.Pack.VanillaGates);
        Assert.False(row.IsRemoved);
        Assert.False(site.IsChanged);
        Assert.Equal("unchanged", quest.ChangeSummary);
        Assert.DoesNotContain(GameConditionEdits.GatesKey, PackRepository.SerializeAsSaved(vm.Pack));

        // The conversation's own Reset all puts the room's conditions back too.
        row.RemoveCommand.Execute(null);
        Assert.True(talk.HasAnyChanges);
        talk.ResetAll();
        Assert.Empty(vm.Pack.VanillaGates);
        Assert.False(row.IsRemoved);
        Assert.Equal("unchanged from the game", talk.ChangeSummary);
    }

    [Fact]
    public void ItIsSavedTheWayThePluginReadsIt()
    {
        var (vm, quest) = Extension(Himari);
        GameRow(RoomGroup(StartSite(quest, s => s.Site.Dialogue == HimariTalk))).RemoveCommand.Execute(null);

        var saved = JObject.Parse(PackRepository.SerializeAsSaved(vm.Pack));
        var gate = (JObject)((JArray)saved[GameConditionEdits.GatesKey]!)[0];
        _out.WriteLine(gate.ToString());
        Assert.Equal(GameConditionEdits.GateKey(Entrance, "Conditions", "m_Branches/m_Branches/2/m_ConditionList"),
                     GameConditionEdits.GateKey(gate));
        Assert.Equal(new[] { new KeyValuePair<int, string>(0, "ConditionMathCompareBooleans") },
                     GameConditionEdits.RemovalsOf(gate));
        Assert.Contains(SaveLoadChecks.Dialogues, SaveLoadChecks.Of(saved));

        // And read back.
        var again = PackRepository.Deserialize(PackRepository.Serialize(vm.Pack))!;
        Assert.Equal(vm.Pack.VanillaGates.Single().Key, again.VanillaGates.Single().Key);
    }

    [Fact]
    public void AnUndoPutsItBack()
    {
        var (vm, quest) = Extension(Himari);
        vm.Undo.Reset();
        GameRow(RoomGroup(StartSite(quest, s => s.Site.Dialogue == HimariTalk))).RemoveCommand.Execute(null);
        Assert.Single(vm.Pack.VanillaGates);

        vm.Undo.Checkpoint();
        vm.Undo.Undo();
        Assert.Empty(vm.Pack.VanillaGates);
        var reopened = vm.Quests.Single(q => q.Model.Source == Himari);
        Assert.DoesNotContain(RoomGroup(StartSite(reopened, s => s.Site.Dialogue == HimariTalk)).Rows, r => r.IsRemoved);
    }

    [Fact]
    public void AQuestPlaceListsItsRoomAsSomethingItCanTakeOut()
    {
        // Where the game's script sits is a condition of the quest starting
        // there, and the only one an author cannot see in the game's own lists.
        // On a quest's place it can be taken out; on a conversation of the
        // game's it is only shown, because a conversation is always played
        // somewhere.
        var (vm, quest) = Extension(Himari);
        var site = StartSite(quest, s => s.Site.Dialogue == HimariTalk);
        var group = RoomGroup(site);
        _out.WriteLine(group.Title);
        Assert.EndsWith(", in Entrance", group.Title);
        var room = group.Rows.First();
        Assert.Equal(NodeConditionTypes.LevelActive, room.Condition!.Model.Type);
        Assert.True(room.CanRemove);
        Assert.All(group.Rows, r => Assert.True(r.CanRemove));

        Assert.True(vm.OpenVanillaConversation(site));
        var there = vm.SelectedDialogue!.GateGroups.Single(g => g.Title.StartsWith("Played from " + Entrance + " "));
        var level = there.Rows.First();
        Assert.Equal(NodeConditionTypes.LevelActive, level.Condition!.Model.Type);
        Assert.False(level.CanRemove);
        Assert.False(level.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public void AScriptPlaceSaysWhichRoomItIsIn()
    {
        var (_, quest) = Extension("A Trained Eye (Gabriel)");
        var site = StartSite(quest, s => s.Site.By == "8_Room_Talk/Mansion");
        _out.WriteLine(site.Where);
        Assert.StartsWith("The Trigger script on 8_Room_Talk/Mansion (in ", site.Where);

        // Its script checks nothing, so the room it sits on is all there is -
        // and that can be taken out, which is what makes the place the pack's.
        var group = Assert.Single(site.ConditionGroups);
        var room = Assert.Single(group.Rows);
        Assert.Equal(NodeConditionTypes.LevelActive, room.Condition!.Model.Type);
        Assert.True(room.CanRemove);
        Assert.False(site.IsChanged);
        room.RemoveCommand.Execute(null);
        Assert.True(site.IsChanged);
    }

    [Fact]
    public void APlaceThatOnlyCompletesATaskIsShownNotChanged()
    {
        var (vm, quest) = Extension(Himari);
        var task = quest.VanillaTaskRows.First(r => r.GameSites.Count > 0);
        var site = task.GameSites.SelectMany(g => g.Sites).First();
        Assert.False(site.IsEditable);
        Assert.Null(site.ExtraConditions);
        Assert.All(site.ConditionGroups.SelectMany(g => g.Rows), r => Assert.False(r.CanRemove));
    }

    // -- A line's own conditions ---------------------------------------

    [Fact]
    public void TakingALineConditionOutChangesTheConversation()
    {
        var (vm, quest) = Extension(Vanessa);
        var site = StartSite(quest, s => s.Site.Node == LibraryLine);
        var line = site.ConditionGroups.Single(g => g.Title == "This line");
        Assert.Equal(2, line.Rows.Count);
        Assert.Empty(vm.VanillaDialogues);

        line.Rows[1].RemoveCommand.Execute(null);

        // The pack's version of the conversation now exists, with that line
        // one condition shorter - and nothing moved the Dialogues tab.
        var talk = Assert.Single(vm.VanillaDialogues);
        Assert.Equal(VanillaDialogueCatalog.TokenPrefix + LibraryTalk, talk.Model.Source);
        Assert.NotSame(talk, vm.SelectedDialogue);
        var node = talk.Model.Nodes.Single(n => n.Id == unchecked((int)LibraryLine));
        var kept = Assert.Single(node.Conditions);
        Assert.Contains("Vanessamet-AtLibrary", kept.Params.Values);
        Assert.Contains("conditions", talk.ChangedFields(node));
        var row = talk.Nodes.Single(n => n.Model == node);
        Assert.True(row.IsChangedFromVanilla);
        Assert.Single(row.Conditions);   // the rows followed the model
        Assert.True(line.Rows[1].IsRemoved);
        Assert.False(line.Rows[0].IsRemoved);
        Assert.True(quest.WarnsOnLoad);

        // Put back: the line is the game's again.
        line.Rows[1].UndoCommand.Execute(null);
        Assert.Empty(talk.ChangedFields(node));
        Assert.Equal(2, row.Conditions.Count);
        Assert.False(line.Rows[1].IsRemoved);
        Assert.False(site.IsChanged);
    }

    [Fact]
    public void AConditionAddedToTheLineIsShownAsYours()
    {
        var (vm, quest) = Extension(Vanessa);
        var site = StartSite(quest, s => s.Site.Node == LibraryLine);
        site.ConditionGroups.Single(g => g.Title == "This line").Rows[0].RemoveCommand.Execute(null);
        var talk = vm.VanillaDialogues.Single();
        var node = talk.Model.Nodes.Single(n => n.Id == unchecked((int)LibraryLine));
        node.Conditions.Add(new NodeConditionDef { Type = NodeConditionTypes.GameObjectActive,
                                                   Params = new Dictionary<string, string> { ["target"] = "Somewhere" } });

        var fresh = StartSite(Extension2(vm, quest), s => s.Site.Node == LibraryLine);
        var rows = fresh.ConditionGroups.Single(g => g.Title == "This line").Rows;
        Assert.Equal(3, rows.Count);
        Assert.True(rows[0].IsRemoved);
        Assert.True(rows[2].IsYours);
        Assert.False(rows[2].CanRemove);

        static QuestViewModel Extension2(MainViewModel vm, QuestViewModel quest)
        {
            quest.RefreshGameConditions();
            return new QuestViewModel(quest.Model);
        }
    }

    // -- The pack's conditions for the quest step -----------------------

    [Fact]
    public void ConditionsForTheStepAreKeptOnlyWhileThereAreAny()
    {
        var (vm, quest) = Extension(Himari);
        var site = StartSite(quest, s => s.Site.Dialogue == HimariTalk);
        Assert.NotNull(site.ExtraConditions);
        Assert.Empty(quest.Model.SiteConditions);

        site.ExtraConditions!.Add();
        // Asked continuously once the game gets there, so a key press is as
        // good here as in Starts when.
        Assert.Equal(ConditionContext.Polled, site.ExtraConditions.Items[0].Context);
        Assert.Contains("Asked along with everything left above, over and over.", site.ExtraNote);
        var place = Assert.Single(quest.Model.SiteConditions);
        Assert.Equal(GameConditionEdits.Starts, place.Does);
        Assert.Equal(HimariTalk, place.Dialogue);
        Assert.Equal(site.Site.Node, place.Node);
        Assert.Equal(site.Site.Moment, place.Moment);
        Assert.True(site.IsChanged);
        Assert.Equal("1 other change", quest.ChangeSummary);
        Assert.False(quest.WarnsOnLoad, "conditions the step waits for are not a change a save has to be warned about");

        var saved = JObject.Parse(PackRepository.SerializeAsSaved(vm.Pack));
        var entry = (JObject)((JArray)saved["quests"]!)[0];
        var written = (JObject)((JArray)entry[GameConditionEdits.SiteConditionsKey]!)[0];
        Assert.Equal(place.Key, GameConditionEdits.SiteKey(written));

        // A new view of the same entry finds the list again.
        var reopened = StartSite(new QuestViewModel(quest.Model), s => s.Site.Dialogue == HimariTalk);
        Assert.Equal(1, reopened.ExtraConditions!.Count);

        site.ResetCommand.Execute(null);
        Assert.Empty(quest.Model.SiteConditions);
        Assert.False(site.IsChanged);
        Assert.DoesNotContain(GameConditionEdits.SiteConditionsKey, PackRepository.SerializeAsSaved(vm.Pack));
    }

    [Fact]
    public void AScriptPlaceTakesConditionsToo()
    {
        var (vm, quest) = Extension("Cryptids (Emma)");
        var site = StartSite(quest, s => !s.Site.IsDialogue);
        var group = site.ConditionGroups.Single(g => g.Title == "The script's conditions");
        Assert.Equal(3, group.Rows.Count(r => r.CanRemove));

        group.Rows[1].RemoveCommand.Execute(null);
        var gate = Assert.Single(vm.Pack.VanillaGates);
        Assert.Equal("8_Room_Talk/Garage", gate.By);
        Assert.Equal(1, gate.Removed.Single().Index);

        site.ExtraConditions!.Add();
        var place = quest.Model.SiteConditions.Single();
        Assert.Equal("8_Room_Talk/Garage", place.By);
        Assert.Equal("Conditions", place.Script);
        Assert.Equal(GameConditionEdits.ScriptSiteKey("8_Room_Talk/Garage", "Conditions", GameConditionEdits.Starts),
                     place.Key);
    }

    // -- Resets when ------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AQuestCanBeResetByItsOwnConditions(bool vanilla)
    {
        var vm = new MainViewModel();
        QuestViewModel quest;
        if (vanilla) quest = Extension(Himari).Quest;
        else
        {
            vm.AddQuestCommand.Execute(null);
            quest = vm.SelectedQuest!;
        }

        Assert.StartsWith("No reset conditions", quest.ResetNote);
        quest.ResetConditions.Add();
        Assert.Single(quest.Model.ResetConditions);
        Assert.StartsWith("Once the quest has started", quest.ResetNote);

        quest.StartConditions.Add();
        Assert.EndsWith("While its reset conditions pass, these wait.", quest.StartNote);

        var saved = JObject.Parse(PackRepository.SerializeAsSaved(new ModPack { Quests = { quest.Model } }));
        Assert.Single((JArray)((JObject)((JArray)saved["quests"]!)[0])[GameConditionEdits.ResetConditionsKey]!);
        if (vanilla) Assert.Equal("2 other changes", quest.ChangeSummary);
    }

    // -- Marks and resets on the game's tasks ---------------------------

    [Fact]
    public void AChangedTaskIsMarkedAndEachChangeCanBeUndone()
    {
        var (_, quest) = Extension("Astrid Quest");
        var row = quest.VanillaTaskRows.First();
        Assert.False(row.IsChanged);
        Assert.Empty(row.ResettableFields);

        row.QuestDescription = "Something changed.";
        row.Visibility = VanillaTaskRowViewModel.NeverShown;
        row.Actions.Add();
        Assert.True(row.IsChanged);
        Assert.Equal("Changed from the game: journal, description, actions", row.ChangedFieldsText);
        Assert.Equal(new[] { "journal", "description", "actions" }, row.ResettableFields.Select(f => f.Label));

        row.ResettableFields.Single(f => f.Label == "journal").Reset.Execute(null);
        Assert.Equal(VanillaTaskRowViewModel.ShownAsTheGameHasIt, row.Visibility);
        Assert.Equal("Changed from the game: description, actions", row.ChangedFieldsText);

        row.ResetCommand.Execute(null);
        Assert.False(row.IsChanged);
        Assert.Empty(quest.Model.VanillaTasks);

        // Taken out, then put back from the same button.
        row = quest.VanillaTaskRows.First();
        row.IsRemoved = true;
        row = quest.VanillaTaskRows.First();
        Assert.Equal("Changed from the game: taken out", row.ChangedFieldsText);
        row.ResetCommand.Execute(null);
        Assert.Empty(quest.Model.VanillaTasks);
        Assert.False(quest.VanillaTaskRows.First().IsRemoved);
    }

    [Fact]
    public void ResetAllPutsTheWholeQuestBack()
    {
        var (vm, quest) = Extension(Himari);
        quest.Description = "Mine.";
        quest.StartConditions.Add();
        quest.ResetConditions.Add();
        quest.VanillaTaskRows[0].QuestDescription = "Done.";
        quest.SelectedExtensionRow = null;
        quest.AddTaskCommand.Execute(null);
        var site = StartSite(quest, s => s.Site.Dialogue == HimariTalk);
        RoomGroup(site).Rows.First(r => r.CanRemove).RemoveCommand.Execute(null);
        site.ExtraConditions!.Add();
        _out.WriteLine(quest.ChangeSummary);
        Assert.True(quest.HasChanges);
        Assert.True(quest.ResetDescriptionCommand.CanExecute(null));

        Assert.True(quest.ResetAllCommand.CanExecute(null));
        quest.ResetAll();

        Assert.Equal("unchanged", quest.ChangeSummary);
        Assert.False(quest.HasChanges);
        Assert.Empty(vm.Pack.VanillaGates);
        Assert.Empty(quest.Model.SiteConditions);
        Assert.Empty(quest.Model.AddedTasks);
        Assert.Empty(quest.Model.VanillaTasks);
        Assert.Empty(quest.Model.StartConditions);
        Assert.Empty(quest.Model.ResetConditions);
        Assert.Equal("", quest.Model.Description);
        Assert.False(quest.ResetAllCommand.CanExecute(null));
    }

    // -- Checks ------------------------------------------------------------

    private static List<ValidationIssue> Check(ModPack pack) => PackValidator.Validate(pack, "");

    [Fact]
    public void AStaleRemovalIsSaid()
    {
        var pack = PackRepository.CreateEmpty("my.pack");
        pack.VanillaGates.Add(new GameGateEditDef
        {
            By = Entrance, Script = "Conditions", At = JArray.Parse("[\"m_Branches\",\"m_Branches\",2,\"m_ConditionList\"]"),
            Removed = { new RemovedConditionDef { Index = 0, Type = "ConditionChance", Title = "A chance" } },
        });
        pack.VanillaGates.Add(new GameGateEditDef
        {
            By = Entrance, Script = "Conditions", At = JArray.Parse("[\"m_Branches\",\"m_Branches\",99,\"m_ConditionList\"]"),
            Removed = { new RemovedConditionDef { Index = 0, Type = "ConditionChance" } },
        });
        pack.VanillaGates.Add(new GameGateEditDef
        {
            By = Entrance, Script = "Conditions", At = JArray.Parse("[\"m_Branches\",\"m_Branches\",2,\"m_ConditionList\"]"),
            Removed = { new RemovedConditionDef { Index = 1, Type = "ConditionMathCompareIntegers" } },
        });
        var issues = Check(pack);
        foreach (var i in issues.Where(i => i.Code.StartsWith("quest.gate"))) _out.WriteLine(i.Code + " " + i.Where + ": " + i.Message);
        Assert.Single(issues, i => i.Code == "quest.gateChanged" && i.Where == "vanillaGates[0]");
        Assert.Single(issues, i => i.Code == "quest.gateUnknown" && i.Where == "vanillaGates[1]");
        Assert.DoesNotContain(issues, i => i.Where == "vanillaGates[2]");
    }

    [Fact]
    public void AKeyPressAtAPlaceIsNotWarnedAbout()
    {
        // The reported case: at the bar, press Insert. The conditions are
        // asked every frame after the game gets there, so a press is fine.
        var pack = PackRepository.CreateEmpty("my.pack");
        var real = VanillaQuestReferences.For(Himari)!.Starts.First(s => s.IsDialogue);
        var quest = new QuestDef { Key = "e", Source = Himari };
        quest.SiteConditions.Add(new SiteConditionsDef
        {
            Does = GameConditionEdits.Starts, Dialogue = real.Dialogue!, Node = real.Node, Moment = real.Moment!,
            Conditions =
            {
                new NodeConditionDef
                {
                    Type = NodeConditionTypes.InputKey,
                    Params = new Dictionary<string, string> { ["key"] = "Insert", ["phase"] = InputPhases.Pressed },
                },
            },
        });
        pack.Quests.Add(quest);
        var issues = Check(pack);
        Assert.DoesNotContain(issues, i => i.Code == "input.edgeInOneShot");
        Assert.DoesNotContain(issues, i => i.Where.StartsWith("quests[e].siteConditions"));
    }

    [Fact]
    public void ConditionsForAPlaceTheGameDoesNotHaveAreSaid()
    {
        var pack = PackRepository.CreateEmpty("my.pack");
        var quest = new QuestDef { Key = "e", Source = Himari };
        quest.SiteConditions.Add(new SiteConditionsDef
        {
            Does = GameConditionEdits.Starts, Dialogue = HimariTalk, Node = 12345, Moment = GameConditionEdits.OnStart,
            Conditions = { new NodeConditionDef { Type = NodeConditionTypes.Random } },
        });
        var real = Refs(Himari);
        quest.SiteConditions.Add(new SiteConditionsDef
        {
            Does = GameConditionEdits.Starts, Dialogue = real.Dialogue!, Node = real.Node, Moment = real.Moment!,
            Conditions = { new NodeConditionDef { Type = NodeConditionTypes.Random } },
        });
        pack.Quests.Add(quest);
        var issues = Check(pack);
        Assert.Single(issues, i => i.Code == "quest.siteUnknown" && i.Where == "quests[e].siteConditions[0]");
        Assert.DoesNotContain(issues, i => i.Code == "quest.siteUnknown" && i.Where == "quests[e].siteConditions[1]");

        static VanillaQuestReferences.Site Refs(string name)
            => VanillaQuestReferences.For(name)!.Starts.First(s => s.IsDialogue);
    }

    // -- What the game tries first -------------------------------------------

    private const string JustADrink = "Just A Drink (Liz)";
    private const string Bar = "8_Room_Talk/Bar";

    [Fact]
    public void ABranchKnowsWhatTheGameTriesBeforeIt()
    {
        var start = VanillaDialogueCatalog.Open(Bar + "/AnnaAndLiz1")!.Starts.Single(s => s.By == Bar);
        Assert.Equal("m_Branches/m_Branches/5/m_ConditionList", GameConditionEdits.PathText(start.Gates.Single().At));
        Assert.Equal(new[] { "ClaudiaBar", "ZuriBar", "Katebar", "ToniBarEvent", "ToniBarEventRepeat" },
                     start.Ahead.Select(a => a.Plays!.Substring(Bar.Length + 1)));
        Assert.Equal(new[] { 3, 3, 3, 4, 4 }, start.Ahead.Select(a => a.Count));
        Assert.Empty(VanillaDialogueCatalog.Open(Bar + "/ClaudiaBar")!.Starts.Single(s => s.By == Bar).Ahead);

        // Everywhere a branch of a room's list holds a conversation, no more
        // come before it than its place in the list.
        int checkedBranches = 0;
        foreach (var entry in VanillaDialogueCatalog.All)
            foreach (var s in VanillaDialogueCatalog.Open(entry.Id)!.Starts)
            {
                if (s.Gates.Count != 1 || s.Gates[0].At.Count != 4) continue;
                if ((string?)s.Gates[0].At[0] != "m_Branches" || (string?)s.Gates[0].At[1] != "m_Branches") continue;
                Assert.True(s.Ahead.Count <= (int)s.Gates[0].At[2], entry.Id + " has more before it than its place");
                checkedBranches++;
            }
        Assert.True(checkedBranches > 100, checkedBranches + " branches checked");
    }

    [Fact]
    public void APlaceSaysWhatTheGameTriesFirstEvenWithEveryConditionTakenOut()
    {
        var (vm, quest) = Extension(JustADrink);
        var site = StartSite(quest, s => s.Site.Dialogue == Bar + "/AnnaAndLiz1");
        var group = site.ConditionGroups.Single(g => g.Title.StartsWith("The conversation plays from " + Bar + " "));
        _out.WriteLine(group.Title + ": " + group.Note);
        Assert.Equal("The game tries these first: ClaudiaBar, ZuriBar, Katebar, ToniBarEvent and ToniBarEventRepeat. "
                     + "If one of them runs, this one doesn't.", group.Note);
        Assert.Equal("", site.PlaysNote);

        foreach (var row in group.Rows.Where(r => r.CanRemove)) row.RemoveCommand.Execute(null);
        Assert.Equal(4, Assert.Single(vm.Pack.VanillaGates).Removed.Count);
        Assert.True(group.HasNote);

        // The conversation the room plays last says the same on the Dialogues tab,
        // though it has no conditions of its own.
        var last = new DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
        last.VanillaSource = VanillaDialogueCatalog.Find(Bar + "/defaultbar");
        var tried = Assert.Single(last.GateGroups);
        Assert.DoesNotContain(tried.Rows, r => r.CanRemove);
        _out.WriteLine(tried.Note);
        Assert.StartsWith("The game tries these first: ClaudiaBar, ZuriBar, Katebar, ToniBarEvent, "
                          + "ToniBarEventRepeat, AnnaAndLiz1,", tried.Note);
    }

    [Fact]
    public void WhatComesFirstIsSaidInOneLine()
    {
        Assert.Equal("", GameConditionGroups.EarlierNote(null));
        Assert.Equal("", GameConditionGroups.EarlierNote(new List<VanillaDialogueCatalog.EarlierBranch>()));
        Assert.Equal("The game tries this first: A. If one of them runs, this one doesn't.",
            GameConditionGroups.EarlierNote(new[] { new VanillaDialogueCatalog.EarlierBranch { Plays = "Room/A", Count = 1 } }));
        Assert.Equal("The game tries these first: A, B and 2 other branches. If one of them runs, this one doesn't.",
            GameConditionGroups.EarlierNote(new[]
            {
                new VanillaDialogueCatalog.EarlierBranch { Plays = "Room/A", Count = 1 },
                new VanillaDialogueCatalog.EarlierBranch { Branch = "loop", Count = 0 },
                new VanillaDialogueCatalog.EarlierBranch { Plays = "Room/B", Count = 2 },
                new VanillaDialogueCatalog.EarlierBranch { Plays = "Room/A", Count = 2 },
                new VanillaDialogueCatalog.EarlierBranch { Count = 3 },
            }));
        Assert.Equal("The game tries these first: 1 other branch. If one of them runs, this one doesn't.",
            GameConditionGroups.EarlierNote(new[] { new VanillaDialogueCatalog.EarlierBranch { Count = 1 } }));
    }

    [Fact]
    public void AnEntryThatOnlyChangesWhatStartsOrResetsTheQuestIsNotSaidToDoNothing()
    {
        static bool SaysNothing(ModPack pack) => Check(pack).Any(i => i.Code == "quest.extensionSaysNothing");

        // The reported entry: its own conditions at the bar, a key to reset
        // it, and the room's conditions taken out.
        var (vm, quest) = Extension(JustADrink);
        var pack = vm.Pack;
        Assert.True(SaysNothing(pack));

        quest.Model.ResetConditions.Add(new NodeConditionDef { Type = NodeConditionTypes.Random });
        Assert.False(SaysNothing(pack));
        quest.Model.ResetConditions.Clear();
        Assert.True(SaysNothing(pack));

        var real = VanillaQuestReferences.For(JustADrink)!.Starts.First(s => s.IsDialogue);
        var place = new SiteConditionsDef
        {
            Does = GameConditionEdits.Starts, Dialogue = real.Dialogue!, Node = real.Node, Moment = real.Moment!,
        };
        quest.Model.SiteConditions.Add(place);
        Assert.True(SaysNothing(pack));    // a place with nothing to wait for is nothing
        place.Conditions.Add(new NodeConditionDef { Type = NodeConditionTypes.Random });
        Assert.False(SaysNothing(pack));
        quest.Model.SiteConditions.Clear();
        Assert.True(SaysNothing(pack));

        var site = StartSite(quest, s => s.Site.Dialogue == Bar + "/AnnaAndLiz1");
        var row = site.ConditionGroups.SelectMany(g => g.Rows).First(r => r.CanRemove);
        row.RemoveCommand.Execute(null);
        Assert.False(SaysNothing(pack));
        row.UndoCommand.Execute(null);
        Assert.True(SaysNothing(pack));

        // Taken out of a line of the conversation, kept with the conversation.
        var (vm2, vanessa) = Extension(Vanessa);
        Assert.True(SaysNothing(vm2.Pack));
        var line = StartSite(vanessa, s => s.Site.Node == LibraryLine).ConditionGroups.Single(g => g.Title == "This line");
        line.Rows[1].RemoveCommand.Execute(null);
        Assert.False(SaysNothing(vm2.Pack));
        line.Rows[1].UndoCommand.Execute(null);
        Assert.True(SaysNothing(vm2.Pack));
    }

    // -- On screen -----------------------------------------------------------

    [Fact]
    public void TheRowsAreDrawnStruckThroughWithTheWayBack()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            tabs.SelectedIndex = TabQuests;
            vm.AddVanillaQuestCommand.Execute(null);
            var quest = vm.SelectedQuest!;
            quest.Source = Himari;
            Settle(window);

            Assert.True(((FrameworkElement)window.FindName("QuestResetPanel")).IsVisible);
            var groups = (ItemsControl)window.FindName("GameQuestSiteGroups");
            var site = StartSite(quest, s => s.Site.Dialogue == HimariTalk);
            // One of the game's own conditions, not the room: this is about
            // what both tabs draw for a list the pack has taken something out of.
            var row = GameRow(RoomGroup(site));
            var takeOut = FindAll<Button>(groups).Single(b => ReferenceEquals(b.DataContext, row) && (string)b.Content == "Take out");
            Assert.True(takeOut.IsVisible);
            Press(takeOut);
            Settle(window);

            Assert.True(row.IsRemoved);
            var container = FindAll<FrameworkElement>(groups).First(e => ReferenceEquals(e.DataContext, row) && e is DockPanel);
            Assert.True(FindAll<Button>(container).Single(b => (string)b.Content == "Put back").IsVisible);
            Assert.False(FindAll<Button>(container).Single(b => (string)b.Content == "Take out").IsVisible);
            Assert.True(FindAll<System.Windows.Shapes.Rectangle>(container).Single(r => r.Height == 1.5).IsVisible,
                        "the row is not struck through");
            Assert.True(((FrameworkElement)window.FindName("QuestTreeChangeWarning")).IsVisible);
            Assert.True(((Button)window.FindName("VanillaQuestResetAll")).IsEnabled);

            // The Dialogues tab draws the same row taken out.
            Assert.True(site.IsChanged);
            Assert.True(vm.OpenVanillaConversation(site));
            tabs.SelectedIndex = TabDialogues;
            Settle(window);
            var gates = (ItemsControl)window.FindName("DialogueGateGroups");
            Assert.True(gates.IsVisible);
            var putBack = FindAll<Button>(gates).Where(b => b.IsVisible && (string)b.Content == "Put back").ToList();
            _out.WriteLine(putBack.Count + " put-back button(s) on the Dialogues tab");
            Press(Assert.Single(putBack));
            Settle(window);
            Assert.Empty(vm.Pack.VanillaGates);
            Assert.False(row.IsRemoved);
        });
    }

    [Fact]
    public void OnlyTheRowsThatNeedItSayWhatTheyAre()
    {
        // Every locked row used to say "Required - this dialogue only starts
        // in this level": right on a dialogue's own pinned level row, wrong on
        // every condition of the game's shown on the Quests tab.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");

            // A dialogue of the pack's: required, editable, not removable.
            tabs.SelectedIndex = TabDialogues;
            vm.AddDialogueCommand.Execute(null);
            Settle(window);
            var pinned = vm.SelectedDialogue!.StartConditions[0];
            Assert.Equal(NodeConditionTypes.LevelActive, pinned.Model.Type);
            Assert.False(pinned.RemoveCommand.CanExecute(null));
            var pinnedBox = FindAll<FrameworkElement>(window)
                .First(e => ReferenceEquals(e.DataContext, pinned) && e is Border);
            var pinnedHeader = FindAll<TextBlock>(pinnedBox).Single(t => t.Name == "LockedHeaderText");
            Assert.True(pinnedHeader.IsVisible);
            Assert.Equal(NodeConditionViewModel.PinnedLevelHeader, pinnedHeader.Text);
            Assert.Contains(FindAll<ComboBox>(pinnedBox), c => c.IsVisible && c.IsEnabled);

            // The game's conditions on a quest's place say nothing.
            tabs.SelectedIndex = TabQuests;
            vm.AddVanillaQuestCommand.Execute(null);
            var quest = vm.SelectedQuest!;
            quest.Source = Himari;
            Settle(window);
            var site = StartSite(quest, s => s.Site.Dialogue == HimariTalk);
            var groups = (ItemsControl)window.FindName("GameQuestSiteGroups");
            var placeRows = RoomGroup(site).Rows;
            // The first is the room the script sits on, which says so; the
            // game's own conditions say nothing.
            Assert.Equal(GameConditionGroups.LocationHeader, HeaderOf(groups, placeRows[0]).Text);
            Assert.True(HeaderOf(groups, placeRows[0]).IsVisible);
            foreach (var row in placeRows.Skip(1))
                Assert.False(HeaderOf(groups, row).IsVisible, "a condition of the game's reads as a required level");

            // On the Dialogues tab, where the game's script is says so.
            Assert.True(vm.OpenVanillaConversation(site));
            tabs.SelectedIndex = TabDialogues;
            Settle(window);
            var gates = (ItemsControl)window.FindName("DialogueGateGroups");
            var rows = vm.SelectedDialogue!.GateGroups.Single(g => g.Title.StartsWith("Played from " + Entrance + " ")).Rows;
            var where = HeaderOf(gates, rows[0]);
            _out.WriteLine("location: " + where.Text);
            Assert.True(where.IsVisible);
            Assert.Equal(GameConditionGroups.LocationHeader, where.Text);
            foreach (var row in rows.Skip(1))
                Assert.False(HeaderOf(gates, row).IsVisible, "a condition of the game's reads as a required level");
        });

        static TextBlock HeaderOf(ItemsControl within, GameConditionRowViewModel row)
        {
            var box = FindAll<FrameworkElement>(within)
                .First(e => ReferenceEquals(e.DataContext, row.Condition) && e is Border);
            return FindAll<TextBlock>(box).Single(t => t.Name == "LockedHeaderText");
        }
    }

    [Fact]
    public void AQuestOfThePacksHasResetsWhen()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = TabQuests;
            vm.AddQuestCommand.Execute(null);
            Settle(window);
            Assert.True(((FrameworkElement)window.FindName("QuestResetPanel")).IsVisible);
            Assert.False(((FrameworkElement)window.FindName("GameQuestSitesPanel")).IsVisible);
            var note = (TextBlock)window.FindName("QuestResetNote");
            Assert.StartsWith("No reset conditions", note.Text);
        });
    }

    private static void Settle(Window window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
    }

    private static void Press(Button button)
        => ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(button)
            .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in FindAll<T>(child)) yield return deeper;
        }
    }
}
