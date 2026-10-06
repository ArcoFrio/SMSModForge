using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The main menu's pack list as a load order the player arranges, and the
/// warning when two packs change the same thing of the game's (the author,
/// 1.7.0). The plugin cannot be run here, so its rules - shared code the
/// plugin compiles too - are checked here instead.
/// </summary>
public sealed class PackOrderAndClashTests
{
    private readonly ITestOutputHelper _out;
    public PackOrderAndClashTests(ITestOutputHelper o) => _out = o;

    // ── Order ────────────────────────────────────────────────────────────

    [Fact]
    public void UnarrangedTheOrderIsAlphabetical_AsPacksAlwaysLoaded()
    {
        Assert.Equal(new[] { "alpha", "Beta", "zed" }, PackOrder.Arrange(new[] { "zed", "alpha", "Beta" }, ""));
    }

    [Fact]
    public void ArrangedTheSettingDecides_AndANewPackGoesToTheBottom()
    {
        string setting = PackOrder.Write(new[] { "zed", "alpha" });
        var order = PackOrder.Arrange(new[] { "alpha", "zed", "new", "Another" }, setting);
        Assert.Equal(new[] { "zed", "alpha", "Another", "new" }, order);
    }

    [Fact]
    public void APackTakenOutComesBackToItsPlace()
    {
        string setting = PackOrder.Write(new[] { "c", "a", "b" });
        Assert.Equal(new[] { "c", "b" }, PackOrder.Arrange(new[] { "b", "c" }, setting));   // a uninstalled
        Assert.Equal(new[] { "c", "a", "b" }, PackOrder.Arrange(new[] { "a", "b", "c" }, setting));
    }

    [Theory]
    // Dragging a down: dropped in the gap under c.
    [InlineData("a", 3, "b,c,a")]
    // Dragging c up to the top.
    [InlineData("c", 0, "c,a,b")]
    // Dragging b just under a - which is where it already is.
    [InlineData("b", 1, "a,b,c")]
    [InlineData("b", 2, "a,b,c")]
    public void DroppingInAGapPutsThePackThere(string pack, int gap, string expected)
    {
        var shown = new List<string> { "a", "b", "c" };
        string setting = PackOrder.Move("", shown, pack, gap);
        Assert.Equal(expected.Split(','), PackOrder.Arrange(shown, setting));
    }

    [Fact]
    public void AMoveKeepsWhatTheSettingRemembersOfPacksNotInstalled()
    {
        string setting = PackOrder.Move(PackOrder.Write(new[] { "gone", "a", "b" }), new List<string> { "a", "b" }, "b", 0);
        Assert.Contains("gone", PackOrder.Read(setting));
        Assert.Equal(new[] { "b", "a" }, PackOrder.Arrange(new[] { "a", "b" }, setting));
    }

    [Fact]
    public void DialogueTiesStayAlphabeticalWhateverTheOrder()
    {
        Assert.True(PackOrder.DialogueTie("Apples", "zebra") < 0);
        Assert.True(PackOrder.DialogueTie("zebra", "Apples") > 0);
    }

    [Fact]
    public void TheGameTakesDialogueTiesAlphabeticallyAndLoadsInTheListsOrder()
    {
        // The plugin cannot be loaded here; its source is what can be read.
        string root = System.AppContext.BaseDirectory;
        while (!System.IO.File.Exists(System.IO.Path.Combine(root, "SMSModForge.PackPlugin", "Plugin.cs")))
            root = System.IO.Path.GetDirectoryName(root)!;
        string plugin = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "SMSModForge.PackPlugin", "Plugin.cs"));
        Assert.Contains("PackOrder.DialogueTie(_dispatchers[i].PackId, fireFrom.PackId) < 0", plugin);
        Assert.Contains("PackOrderSetting.Arrange(byId.Keys)", plugin);
    }

    // ── Clashes ──────────────────────────────────────────────────────────

    /// <summary>The manifest as saved, which is what the game reads: a new
    /// pack starts with the game's whole cast in it, and saving drops the
    /// characters it never changed.</summary>
    private static JObject Manifest(ModPack pack) => JObject.Parse(PackRepository.SerializeAsSaved(pack));

    private static ModPack WithAnna(string colour, string displayName = "")
    {
        var pack = PackRepository.CreateEmpty("p");
        var anna = new CharacterDef { Key = "anna", Name = "Anna", VanillaCharacter = "Anna", NameColor = colour };
        if (displayName.Length > 0) anna.DisplayName = displayName;
        pack.Characters.Add(anna);
        return pack;
    }

    private static List<PackConflicts.Clash> Clashes(params (string Id, ModPack Pack)[] packs)
        => PackConflicts.Find(packs.Select(p => new KeyValuePair<string, List<PackConflicts.Change>>(
               p.Id, PackConflicts.Of(Manifest(p.Pack)))).ToList());

    [Fact]
    public void TwoPacksGivingOneOfTheGamesCharactersDifferentColoursClash()
    {
        var clashes = Clashes(("first", WithAnna("#FF0000")), ("second", WithAnna("#00FF00")));
        foreach (var c in clashes) _out.WriteLine($"{c.Earlier} / {c.Later}: {c.LaterChange.Thing} {c.LaterChange.Field}");
        var clash = Assert.Single(clashes, c => c.LaterChange.Field == "nameColor");
        Assert.Equal("first", clash.Earlier);
        Assert.Equal("second", clash.Later);   // lower in the list: its colour shows
        Assert.Equal("character Anna", clash.LaterChange.Thing);
        Assert.Equal(new[] { "second" }, PackConflicts.With(clashes, "first"));
    }

    [Fact]
    public void TwoPacksSayingTheSameThingAgree()
    {
        Assert.DoesNotContain(Clashes(("first", WithAnna("#FF0000")), ("second", WithAnna("#FF0000"))),
                              c => c.LaterChange.Field == "nameColor");
    }

    [Fact]
    public void APacksOwnThingsNeverClash()
    {
        // Each has a scene and a place called beach: they are each pack's own.
        ModPack Own()
        {
            var p = PackRepository.CreateEmpty("p");
            p.Scenes.Add(new SceneDef { Key = "beach" });
            p.Places.Add(new PlaceDef { Key = "beach" });
            p.Characters.Add(new CharacterDef { Key = "sarah", Name = "sarah", NameColor = "#123456" });
            return p;
        }
        var other = Own();
        other.Characters[^1].NameColor = "#654321";
        Assert.Empty(Clashes(("a", Own()), ("b", other)));
    }

    private static ModPack Binding(string level, string objectName, bool overrideTransform, float x, bool overrideActive = false)
    {
        var pack = PackRepository.CreateEmpty("p");
        var node = new GameObjectDef
        {
            Name = objectName, Bind = true, OverrideTransform = overrideTransform, X = x,
            OverrideActive = overrideActive, StartActive = false,
        };
        pack.VanillaExtensions.Add(new VanillaPlaceExtensionDef { Source = level, GameObjects = { node } });
        return pack;
    }

    [Fact]
    public void TwoPacksMovingTheSameObjectOfALevelClash_ButOnlyThere()
    {
        var clashes = Clashes(("a", Binding("vanilla:14_Beach", "Lamp", true, 1)),
                              ("b", Binding("vanilla:14_Beach", "Lamp", true, 5)),
                              ("c", Binding("vanilla:20_Park", "Lamp", true, 9)));
        var clash = Assert.Single(clashes);
        _out.WriteLine(clash.LaterChange.Thing + " / " + clash.LaterChange.Field);
        Assert.Equal("Lamp in 14_Beach", clash.LaterChange.Thing);
        Assert.Equal("transform", clash.LaterChange.Field);
    }

    [Fact]
    public void ListingAnObjectOnlyToHangSomethingOffItIsNoChange()
    {
        // Bound, but neither its transform nor its being there overridden.
        Assert.Empty(Clashes(("a", Binding("vanilla:14_Beach", "Lamp", false, 1)),
                             ("b", Binding("vanilla:14_Beach", "Lamp", false, 5))));
    }

    [Fact]
    public void SwitchingTheSameObjectAndDrivingItByConditionsClash()
    {
        var conditions = Binding("vanilla:14_Beach", "Lamp", false, 0);
        conditions.VanillaExtensions[0].GameObjects[0].ActiveConditions.Add(
            new NodeConditionDef { Type = NodeConditionTypes.AlwaysTrue });
        var clash = Assert.Single(Clashes(("a", Binding("vanilla:14_Beach", "Lamp", false, 0, overrideActive: true)),
                                          ("b", conditions)));
        Assert.Equal("active", clash.LaterChange.Field);
    }

    [Fact]
    public void TheSameArtPathInTwoPacksIsTwoDifferentPictures()
    {
        ModPack Outfit()
        {
            var pack = PackRepository.CreateEmpty("p");
            var anna = new CharacterDef { Key = "anna", Name = "Anna", VanillaCharacter = "Anna" };
            anna.Outfits.Add(new OutfitDef { Key = "anna", GameObjectName = "Anna_Bust", BaseSprite = "Busts/Anna.png" });
            pack.Characters.Add(anna);
            return pack;
        }
        var clash = Assert.Single(Clashes(("a", Outfit()), ("b", Outfit())), c => c.LaterChange.Field.Contains("baseSprite"));
        _out.WriteLine(clash.LaterChange.Field);
        Assert.StartsWith("outfit Anna_Bust", clash.LaterChange.Field);
    }

    [Fact]
    public void TwoPacksRewritingOneLineOfTheGamesClash_AndSoDoesOneTakingItOut()
    {
        ModPack Rewrite(string text)
        {
            var pack = PackRepository.CreateEmpty("p");
            var d = new DialogueDef { Key = "talk", Source = "Dialogues/Anna/Hello" };
            d.Nodes.Add(new DialogueNodeDef { Id = 12, Text = text, Overrides = new List<string> { "text" } });
            pack.Dialogues.Add(d);
            return pack;
        }
        var removed = PackRepository.CreateEmpty("p");
        removed.Dialogues.Add(new DialogueDef { Key = "talk", Source = "Dialogues/Anna/Hello", RemovedNodes = { 12 } });

        var clashes = Clashes(("a", Rewrite("Hi!")), ("b", Rewrite("Hey!")), ("c", removed));
        foreach (var c in clashes) _out.WriteLine($"{c.Earlier} / {c.Later}: {c.LaterChange.Thing} {c.LaterChange.Field}");
        Assert.Equal(3, clashes.Count);   // a-b on the text, and c against each
        Assert.All(clashes, c => Assert.Equal("line 12 of Dialogues/Anna/Hello", c.LaterChange.Thing));
    }

    [Fact]
    public void ThePackListSaysWhoAPackClashesWith()
    {
        var report = PackStatus.Of(new PackStatus.Facts
        {
            GameVersion = "1.8E", RunningGameVersion = "1.8E",
            ForgeVersion = ForgeVersion.Current, RuntimeForgeVersion = ForgeVersion.Current,
            ClashesWith = new List<string> { "Other Pack" },
        });
        _out.WriteLine(string.Join(" | ", report.Tags));
        Assert.Equal(PackStatus.Level.Warning, report.Level);
        Assert.Contains(report.Tags, t => t.Contains("Other Pack"));
    }

    [Fact]
    public void FindingThemIsCheapEnoughForTheMenu()
    {
        // Twenty packs, each changing a character of the game's and forty
        // objects of a level: what the menu does every time it draws.
        var packs = new List<KeyValuePair<string, List<PackConflicts.Change>>>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 20; i++)
        {
            var pack = WithAnna("#" + i.ToString("X6"));
            var ext = new VanillaPlaceExtensionDef { Source = "vanilla:14_Beach" };
            for (int o = 0; o < 40; o++)
                ext.GameObjects.Add(new GameObjectDef { Name = "Thing" + o, Bind = true, OverrideTransform = true, X = i });
            pack.VanillaExtensions.Add(ext);
            var json = Manifest(pack);
            packs.Add(new KeyValuePair<string, List<PackConflicts.Change>>("pack" + i, PackConflicts.Of(json)));
        }
        long read = sw.ElapsedMilliseconds;
        var clashes = PackConflicts.Find(packs);
        sw.Stop();
        _out.WriteLine($"{clashes.Count} clashes; walking 20 manifests {read} ms, finding {sw.ElapsedMilliseconds - read} ms");
        Assert.True(sw.ElapsedMilliseconds < 2000);
    }
}
