using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What one of the game's start or reset places comes to once a pack changes
/// it: the rule the runtime asks continuously, written into the manifest as the
/// pack is saved.
/// <para/>
/// The rule names the game's own lists and lines rather than copying what is in
/// them, so these check the NAMES - a path the runtime follows through the live
/// script, and the lines it has to be able to be on - and that a place nobody
/// has touched writes nothing at all.
/// </summary>
public sealed class PlaceRuleTests
{
    private readonly ITestOutputHelper _out;
    public PlaceRuleTests(ITestOutputHelper o) => _out = o;

    private const string JustADrink = "Just A Drink (Liz)";
    private const string Bar = "8_Room_Talk/Bar";
    private const string AnnaAndLiz = "8_Room_Talk/Bar/AnnaAndLiz1";
    private const long AnnaLine = -436121678;

    private static (MainViewModel Vm, QuestViewModel Quest) Extension(string quest)
    {
        var vm = new MainViewModel();
        vm.AddVanillaQuestCommand.Execute(null);
        var entry = vm.SelectedQuest!;
        entry.Source = quest;
        return (vm, entry);
    }

    private static GameQuestSiteViewModel StartSite(QuestViewModel quest, string dialogue)
        => quest.GameSites.Single(g => g.Heading == "Started by").Sites.Single(s => s.Site.Dialogue == dialogue);

    private static JObject? Saved(ModPack pack, string quest, string dialogue)
    {
        var json = JObject.Parse(PackRepository.SerializeAsSaved(pack));
        var entry = (json["quests"] as JArray)?.OfType<JObject>()
                    .FirstOrDefault(q => (string?)q["source"] == quest);
        return (entry?[GameConditionEdits.SiteConditionsKey] as JArray)?.OfType<JObject>()
               .FirstOrDefault(p => (string?)p[GameConditionEdits.DialogueKey] == dialogue);
    }

    /// <summary>
    /// Through the editor's real Save and Export, reading back the file the
    /// game loads. The first round of this work was tested only through
    /// SerializeAsSaved, which the save did not use: every test passed and not
    /// one rule reached a pack - the game said so in its log, and the warning
    /// fell back to talking about conversations.
    /// </summary>
    [Fact]
    public void TheRuleReachesTheFileTheGameLoads()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "smsforge-rule-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        try
        {
            var (vm, quest) = Extension(JustADrink);
            vm.Pack.PackId = "TestPack";
            var site = StartSite(quest, AnnaAndLiz);
            foreach (var row in site.ConditionGroups.SelectMany(g => g.Rows).Where(r => r.CanRemove).ToList())
                row.RemoveCommand.Execute(null);
            site.ExtraConditions!.Add();

            var folder = System.IO.Path.Combine(root, "TestPack");
            PackRepository.Save(vm.Pack, folder);
            var archive = System.IO.Path.Combine(root, "TestPack.smspack");
            PackExporter.Export(folder, archive);

            JObject Loaded()
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
                using var reader = new System.IO.StreamReader(zip.GetEntry(PackExporter.ManifestEntryName)!.Open());
                return JObject.Parse(reader.ReadToEnd());
            }

            foreach (var manifest in new[]
                     {
                         JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, PackRepository.ManifestFileName))),
                         Loaded(),
                     })
            {
                var entry = ((JArray)manifest["quests"]!).OfType<JObject>().Single(q => (string?)q["source"] == JustADrink);
                var place = ((JArray)entry[GameConditionEdits.SiteConditionsKey]!).OfType<JObject>().Single();
                Assert.IsType<JObject>(place[GameConditionEdits.RuleKey]);

                // ...and so the player is told about the quest, not about
                // conversations.
                Assert.Equal(new[] { "when quests start" }, SaveLoadChecks.WordsOf(manifest)[SaveLoadChecks.Dialogues]);
                Assert.Equal(JustADrink, Assert.Single(SaveLoadChecks.QuestsChanged(manifest)).Key);
            }
        }
        finally
        {
            try { System.IO.Directory.Delete(root, true); } catch (System.IO.IOException) { }
        }
    }

    [Fact]
    public void APlaceNobodyHasTouchedIsTheGamesAndWritesNothing()
    {
        var (vm, quest) = Extension(JustADrink);
        quest.Description = "Words only.";
        string json = PackRepository.SerializeAsSaved(vm.Pack);
        Assert.DoesNotContain(GameConditionEdits.SiteConditionsKey, json);
        Assert.DoesNotContain(GameConditionEdits.RuleKey + "\"", json);

        // ...and the pack in hand is left exactly as it was: the rule is
        // written for the file and taken out again.
        Assert.Empty(quest.Model.SiteConditions);
    }

    [Fact]
    public void TakingTheGamesConditionsOutMakesThePlaceThePacksOwnRule()
    {
        var (vm, quest) = Extension(JustADrink);
        var site = StartSite(quest, AnnaAndLiz);
        var room = site.ConditionGroups.SelectMany(g => g.Rows)
                       .First(r => r.Condition?.Model.Type == NodeConditionTypes.LevelActive);
        var bar = site.ConditionGroups.Single(g => g.Title.StartsWith("The conversation plays from " + Bar + " "));

        // The room is one of the rows now, and it can be taken out.
        Assert.True(room.CanRemove);
        Assert.False(site.IsChanged);
        Assert.Equal("", site.RuleNote);

        foreach (var row in bar.Rows.Where(r => r.CanRemove && r != room)) row.RemoveCommand.Execute(null);
        Assert.True(site.IsChanged);
        _out.WriteLine(site.RuleNote);
        Assert.StartsWith("Your pack decides this place now: the quest starts as soon as everything above passes",
                          site.RuleNote);

        // Nothing of the entry says so yet - the removals live with the script -
        // so the entry that carries the rule is made for the save.
        Assert.Empty(quest.Model.SiteConditions);
        var place = Saved(vm.Pack, JustADrink, AnnaAndLiz);
        Assert.NotNull(place);
        _out.WriteLine(place!.ToString());

        var rule = Assert.IsType<JObject>(place[GameConditionEdits.RuleKey]);
        var scope = Assert.IsType<JObject>(Assert.Single((JArray)rule[GameConditionEdits.AnyKey]!));
        Assert.Equal(Bar, (string?)scope[GameConditionEdits.ByKey]);
        Assert.Equal("Conditions", (string?)scope[GameConditionEdits.ScriptKey]);
        Assert.Equal("m_Branches/m_Branches/5/m_ConditionList",
                     GameConditionEdits.PathText((JArray)((JArray)scope[GameConditionEdits.ListsKey]!)[0]));

        // The room it is in is one of the rule's conditions, and the line the
        // step is on is asked too.
        var roomWritten = Assert.IsType<JArray>(scope[GameConditionEdits.RoomKey]);
        Assert.Equal(NodeConditionTypes.LevelActive, (string?)roomWritten[0]!["type"]);
        var lines = Assert.IsType<JArray>(rule[GameConditionEdits.LinesKey]);
        Assert.Equal(new[] { AnnaLine }, lines.Select(l => (long)l[GameConditionEdits.NodeKey]!));
        Assert.Equal(AnnaAndLiz, (string?)lines[0]![GameConditionEdits.DialogueKey]);

        // The pack in hand is as it was: the entry and its rule were made for
        // the file, and saving twice writes the same thing.
        Assert.Empty(quest.Model.SiteConditions);
        Assert.Equal(PackRepository.SerializeAsSaved(vm.Pack), PackRepository.SerializeAsSaved(vm.Pack));
    }

    [Fact]
    public void TakingTheRoomOutLeavesItOutOfTheRule()
    {
        var (vm, quest) = Extension(JustADrink);
        var site = StartSite(quest, AnnaAndLiz);
        var room = site.ConditionGroups.SelectMany(g => g.Rows)
                       .First(r => r.Condition?.Model.Type == NodeConditionTypes.LevelActive);

        room.RemoveCommand.Execute(null);
        Assert.True(room.IsRemoved);
        Assert.True(site.IsChanged);
        Assert.Contains("it can happen anywhere", site.ChangedText);

        // Nothing of the game's own lists changed, so no player is warned
        // about their save over it.
        Assert.DoesNotContain("the game's conditions taken out", site.ChangedText);
        Assert.False(quest.WarnsOnLoad);
        Assert.Empty(vm.Pack.VanillaGates);

        // The room taken out is the pack's own doing, so it is in the entry.
        var kept = Assert.Single(quest.Model.SiteConditions);
        Assert.Equal(new[] { Bar }, kept.RoomsOut);

        var place = Saved(vm.Pack, JustADrink, AnnaAndLiz)!;
        var scope = (JObject)((JArray)place[GameConditionEdits.RuleKey]![GameConditionEdits.AnyKey]!)[0];
        Assert.Null(scope[GameConditionEdits.RoomKey]);
        Assert.NotNull(scope[GameConditionEdits.ListsKey]);

        // Put it back and the place is the game's again.
        room.UndoCommand.Execute(null);
        Assert.False(site.IsChanged);
        Assert.Empty(quest.Model.SiteConditions);
        Assert.DoesNotContain(GameConditionEdits.SiteConditionsKey, PackRepository.SerializeAsSaved(vm.Pack));
    }

    [Fact]
    public void ConditionsOfYourOwnAloneAlsoMakeItARule()
    {
        var (vm, quest) = Extension(JustADrink);
        var site = StartSite(quest, AnnaAndLiz);
        site.ExtraConditions!.Add();
        var mine = site.ExtraConditions.Items[0].Model;
        mine.Type = NodeConditionTypes.InputKey;
        mine.Params = new Dictionary<string, string> { ["key"] = "Insert", ["phase"] = InputPhases.Pressed };

        Assert.True(site.IsChanged);
        var place = Saved(vm.Pack, JustADrink, AnnaAndLiz)!;
        Assert.Equal("Insert", (string?)place[GameConditionEdits.ConditionsKey]![0]!["params"]!["key"]);

        // Everything the game asks is still in the rule: taking nothing out
        // leaves the game's own conditions in the lists it names.
        var scope = (JObject)((JArray)place[GameConditionEdits.RuleKey]![GameConditionEdits.AnyKey]!)[0];
        Assert.NotNull(scope[GameConditionEdits.ListsKey]);
        Assert.NotNull(scope[GameConditionEdits.RoomKey]);
    }

    [Fact]
    public void TakingEveryConditionOffALineIsSavedAsAnEmptyList()
    {
        // The runtime answers a changed line from the pack's own list. A line
        // left with none has to reach it as an empty list and not as "nothing
        // said about conditions", or the game's own list gates it again and
        // the change looks applied while doing nothing.
        const string library = "8_Room_Talk/C_Library/LibraryTalk_Default";
        const long line = -1608623911;
        var (vm, quest) = Extension("Vanessa Quest");
        var site = quest.GameSites.Single(g => g.Heading == "Started by").Sites.Single(s => s.Site.Node == line);
        var own = site.ConditionGroups.Single(g => g.Title == "This line");
        foreach (var row in own.Rows.Where(r => r.CanRemove).ToList()) row.RemoveCommand.Execute(null);

        var saved = JObject.Parse(PackRepository.SerializeAsSaved(vm.Pack));
        var conversation = ((JArray)saved["dialogues"]!).OfType<JObject>()
            .Single(d => (string?)d[VanillaDialogueKeys.Source] == VanillaDialogueCatalog.TokenPrefix + library);
        var node = ((JArray)conversation["nodes"]!).OfType<JObject>()
            .Single(n => (int?)n["id"] == unchecked((int)line));
        _out.WriteLine(node.ToString());
        Assert.Contains("conditions", ((JArray)node[VanillaDialogueKeys.Overrides]!).Select(o => (string?)o));
        Assert.Empty((JArray)node["conditions"]!);
    }

    [Fact]
    public void AScriptPlaceNamesItsOwnScript()
    {
        // "Old Friends (Charlotte)" is started by the script that plays a
        // conversation rather than by a line of one.
        var quest = VanillaQuestReferences.Quests
            .First(q => VanillaQuestReferences.For(q)!.Starts.Any(s => !s.IsDialogue));
        var (vm, entry) = Extension(quest);
        var site = entry.GameSites.Single(g => g.Heading == "Started by").Sites.First(s => !s.Site.IsDialogue);
        _out.WriteLine(quest + " - " + site.Site.By);

        var row = site.ConditionGroups.SelectMany(g => g.Rows).First(r => r.CanRemove);
        row.RemoveCommand.Execute(null);

        var json = JObject.Parse(PackRepository.SerializeAsSaved(vm.Pack));
        var saved = ((JArray)json["quests"]!).OfType<JObject>().First(q => (string?)q["source"] == quest);
        var place = ((JArray)saved[GameConditionEdits.SiteConditionsKey]!).OfType<JObject>()
                    .First(p => (string?)p[GameConditionEdits.ByKey] == site.Site.By);
        var rule = (JObject)place[GameConditionEdits.RuleKey]!;
        var scope = (JObject)((JArray)rule[GameConditionEdits.AnyKey]!)[0];
        Assert.Equal(site.Site.By, (string?)scope[GameConditionEdits.ByKey]);
        Assert.Equal(site.Site.Script, (string?)scope[GameConditionEdits.ScriptKey]);
        // A script's place is not on a line, so the rule asks about none.
        Assert.Null(rule[GameConditionEdits.LinesKey]);
    }

    [Fact]
    public void TheRuleIsWrittenForEveryPlaceThePackHasChangedAndNoOther()
    {
        var (vm, quest) = Extension(JustADrink);
        var sites = quest.GameSites.SelectMany(g => g.Sites).Where(s => s.IsEditable).ToList();
        Assert.True(sites.Count >= 1);

        // Change one of them.
        var row = sites[0].ConditionGroups.SelectMany(g => g.Rows).First(r => r.CanRemove);
        row.RemoveCommand.Execute(null);

        var json = JObject.Parse(PackRepository.SerializeAsSaved(vm.Pack));
        var entry = ((JArray)json["quests"]!).OfType<JObject>().Single(q => (string?)q["source"] == JustADrink);
        var places = Assert.IsType<JArray>(entry[GameConditionEdits.SiteConditionsKey]);
        var written = Assert.Single(places.OfType<JObject>());
        Assert.NotNull(written[GameConditionEdits.RuleKey]);
        Assert.Equal(sites[0].Site.Dialogue ?? sites[0].Site.By,
                     (string?)(written[GameConditionEdits.DialogueKey] ?? written[GameConditionEdits.ByKey]));
    }
}
