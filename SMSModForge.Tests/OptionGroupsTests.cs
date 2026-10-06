using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Data;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The headings the editor's dropdowns are under (the author, 1.7.0): whose an
/// entry is wherever the pack's and the game's share a list, which folder for
/// a list that is all the pack's, and what the action and condition types are
/// for.
/// </summary>
public sealed class OptionGroupsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public OptionGroupsTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-groups-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>What a grouped view shows: each heading and what is under it.</summary>
    private static List<(string Heading, List<string> Items)> Groups(System.ComponentModel.ICollectionView view)
        => view.Groups!.Cast<CollectionViewGroup>()
               .Select(g => ((string)g.Name, g.Items.Select(i => i?.ToString() ?? "").ToList()))
               .ToList();

    private void Show(System.ComponentModel.ICollectionView view)
    {
        foreach (var (heading, items) in Groups(view)) _out.WriteLine($"{heading}: {string.Join(", ", items)}");
    }

    // ── Whose is it ──────────────────────────────────────────────────────

    [Fact]
    public void ABustIsTheGamesWhenTheGameHasOneOfThatName()
    {
        string games = VanillaBusts.All.First().GoName;
        Assert.Equal(OptionGroups.GamesOwn, OptionGroups.OfBust(games));
        Assert.Equal(OptionGroups.ThisPack, OptionGroups.OfBust("Elf_Default"));
        Assert.Equal(OptionGroups.GamesOwn, OptionGroups.OfBust(new NavigatorTargetOption(games, "label")));
    }

    [Fact]
    public void ALevelIsThePacksByItsToken()
    {
        Assert.Equal(OptionGroups.ThisPack, OptionGroups.OfLevel(new NavigatorTargetOption("place:home", "Home")));
        Assert.Equal(OptionGroups.ThisPack, OptionGroups.OfLevel(new NavigatorTargetOption("self:home", "Home")));
        Assert.Equal(OptionGroups.GamesOwn, OptionGroups.OfLevel(new NavigatorTargetOption("vanilla:14_Beach", "Beach")));
    }

    [Fact]
    public void TheFourFacesEveryBustHasAreTheGames()
    {
        foreach (string face in new[] { "Happy", "Angry", "Sad", "Flirty" })
            Assert.Equal(OptionGroups.GamesOwn, OptionGroups.OfExpression(face));
        Assert.Equal(OptionGroups.ThisPack, OptionGroups.OfExpression("Smug"));
    }

    // ── Which folder ─────────────────────────────────────────────────────

    [Fact]
    public void AUnitIsUnderTheFolderItIsFiledIn_ByItsWholePath()
    {
        var folders = new List<UnitFolderDef>
        {
            new() { Name = "Night", Items = { "bar" }, Folders = { new() { Name = "Late", Items = { "alley" } } } },
        };
        Assert.Equal("Night", OptionGroups.FolderOf(folders, "bar"));
        Assert.Equal("Night / Late", OptionGroups.FolderOf(folders, "alley"));
        Assert.Equal(OptionGroups.NotInFolder, OptionGroups.FolderOf(folders, "beach"));
        Assert.Equal(OptionGroups.NotInFolder, OptionGroups.FolderOf(null, "beach"));
    }

    [Fact]
    public void ScenesAreUnderTheirFolders_AndOneMovedIsFiledAgain()
    {
        var pack = PackRepository.CreateEmpty("groups.pack");
        pack.Scenes.Add(new SceneDef { Key = "bar", DisplayName = "Bar" });
        pack.Scenes.Add(new SceneDef { Key = "beach", DisplayName = "Beach" });
        pack.SceneFolders.Add(new UnitFolderDef { Name = "Night", Items = { "bar" } });
        PackRepository.Save(pack, _dir);

        var vm = new MainViewModel();
        vm.OpenPackFromPath(_dir);
        var view = vm.SceneKeyOptionsGrouped;
        Show(view);
        Assert.Contains(Groups(view), g => g.Heading == "Night" && g.Items.SequenceEqual(new[] { "bar" }));
        Assert.Contains(Groups(view), g => g.Heading == OptionGroups.NotInFolder && g.Items.SequenceEqual(new[] { "beach" }));

        // Filed into the folder in the Scenes tab: the picker follows the next
        // time one appears.
        vm.Pack.SceneFolders[0].Items.Add("beach");
        vm.RebuildGameObjectNameOptions();
        Show(view);
        var night = Assert.Single(Groups(view));
        Assert.Equal("Night", night.Heading);
        Assert.Equal(new[] { "bar", "beach" }, night.Items.OrderBy(x => x));
    }

    [Fact]
    public void TheHeadingsAreInTheEditorsLanguage_NotLeftInEnglish()
    {
        // "Ungrouped" and "Other" were English in every language before 1.7.0.
        foreach (string key in new[] { "common.group.notInFolder", "common.group.misc" })
            Assert.False(string.IsNullOrEmpty(SMSModForge.Localization.Loc.T(key)), key);
        Assert.Equal(SMSModForge.Localization.Loc.T("common.group.notInFolder"), OptionGroups.NotInFolder);
    }

    // ── What is it for ───────────────────────────────────────────────────

    [Fact]
    public void EveryActionTypeOfferedIsUnderATopic_InTheTopicsOrder()
    {
        var vm = new MainViewModel();
        string other = SMSModForge.Localization.Loc.T("common.group.misc");
        foreach (string type in vm.ActionTypes)
            Assert.True(OptionGroups.ActionTopic(type) != other, $"{type} is under no topic");

        var groups = Groups(vm.ActionTypesGrouped);
        Show(vm.ActionTypesGrouped);
        // Every offered type is in the grouped list, once.
        Assert.Equal(vm.ActionTypes.OrderBy(t => t), groups.SelectMany(g => g.Items).OrderBy(t => t));
        Assert.Equal(SMSModForge.Localization.Loc.T("typeGroup.characters"), groups[0].Heading);
        Assert.Contains(groups, g => g.Heading == SMSModForge.Localization.Loc.T("typeGroup.screen")
                                     && g.Items.Contains(NodeActionTypes.Transitions));
    }

    [Theory]
    [InlineData(ConditionContext.Polled)]
    [InlineData(ConditionContext.OneShot)]
    [InlineData(ConditionContext.Rule)]
    public void EveryConditionTypeOfferedIsUnderATopic(ConditionContext context)
    {
        var row = new NodeConditionViewModel(new NodeConditionDef(), context: context);
        string other = SMSModForge.Localization.Loc.T("common.group.misc");
        foreach (string type in row.AvailableTypes)
            Assert.True(OptionGroups.ConditionTopic(type) != other, $"{type} is under no topic");
        var groups = Groups(row.AvailableTypesGrouped);
        Show(row.AvailableTypesGrouped);
        Assert.Equal(row.AvailableTypes.OrderBy(t => t), groups.SelectMany(g => g.Items).OrderBy(t => t));
    }
}
