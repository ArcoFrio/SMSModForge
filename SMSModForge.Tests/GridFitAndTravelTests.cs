using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Validation;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Two things found testing a pack (2026-11-02): a grid that wraps at N and is
/// only as wide as what it holds, and a Travel action. What they do in the
/// game is the in-game suite's F6 and B8; this is what the editor writes and
/// says.
/// </summary>
public sealed class GridFitAndTravelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SMSModForge-fitgrid-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private JObject SavedLayout(UiLayoutDef layout)
    {
        var pack = PackRepository.CreateEmpty("fitgrid");
        pack.Uis.Add(new UiDef { Id = "strip", Name = "strip", Nodes = { new UiNodeDef { Name = "Strip", Layout = layout } } });
        PackRepository.Save(pack, _root);
        var saved = JObject.Parse(File.ReadAllText(Path.Combine(_root, PackRepository.ManifestFileName)));
        return (JObject)((JArray)saved["uis"]!)[0]!["nodes"]![0]!["layout"]!;
    }

    [Fact]
    public void A_grid_fitted_to_what_it_holds_says_so_and_one_that_is_not_says_nothing()
    {
        var layout = new UiLayoutDef { Kind = UiLayoutKinds.Grid, Constraint = "FixedColumnCount", ConstraintCount = 8, FitToContent = true };
        Assert.True((bool?)SavedLayout(layout)["fitToContent"]);
        var back = PackRepository.Load(_root).Uis.Single().Nodes.Single().Layout!;
        Assert.True(back.FitToContent);

        // Control: off writes nothing, as every grid saved before it.
        layout.FitToContent = false;
        Assert.Null(SavedLayout(layout)["fitToContent"]);
    }

    [Fact]
    public void A_copied_layout_keeps_both_of_the_grids_own_settings()
    {
        var copy = new UiLayoutDef { Kind = UiLayoutKinds.Grid, CenterLastLine = true, FitToContent = true }.Copy();
        Assert.True(copy.CenterLastLine);
        Assert.True(copy.FitToContent);
    }

    [Theory]
    [InlineData(UiLayoutKinds.Grid, "FixedColumnCount", true)]
    [InlineData(UiLayoutKinds.Grid, "FixedRowCount", true)]
    [InlineData(UiLayoutKinds.Grid, "Flexible", false)]        // already follows what it holds
    [InlineData(UiLayoutKinds.Horizontal, "FixedColumnCount", false)]
    public void The_box_shows_only_for_a_grid_with_a_set_number_of_lines(string kind, string constraint, bool shown)
    {
        var vm = new UiNodeViewModel(new UiNodeDef { Name = "Strip", Layout = new UiLayoutDef { Kind = kind, Constraint = constraint } });
        Assert.Equal(shown, vm.ShowsFitToContent);
    }

    [Fact]
    public void Validate_names_a_Travel_that_goes_nowhere_and_checks_the_place_it_names()
    {
        var pack = PackRepository.CreateEmpty("travel");
        var d = new DialogueDef { Key = "go", DisplayName = "go" };
        var line = new DialogueNodeDef { Id = 1, Text = "Off we go." };
        line.ActionsOnStart.Add(new NodeActionDef { Type = NodeActionTypes.Travel });
        line.ActionsOnStart.Add(new NodeActionDef { Type = NodeActionTypes.Travel, Params = { ["target"] = "place:nowhere" } });
        line.ActionsOnStart.Add(new NodeActionDef { Type = NodeActionTypes.Travel, Params = { ["target"] = "vanilla:32_StorageRoom" } });
        d.Nodes.Add(line);
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);

        var issues = PackValidator.Validate(pack, _root);
        Assert.Single(issues, i => i.Code == "action.travelNone");
        // The level setting is checked as every one is: a place the pack does
        // not have is named...
        Assert.Contains(issues, i => i.Code == "level.unknownPlace" && i.Where.Contains("Travel"));
        // ...and the game's Storage Room is not (the control).
        Assert.DoesNotContain(issues, i => i.Message.Contains("32_StorageRoom"));
    }
}
