using System.Linq;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Renaming a record updates every list that names it, at once.
/// <para/>
/// Reported from a tutorial recording: rename a Place, go to a dialogue's start
/// conditions, and the dropdown still offers "place1". The option lists were
/// rebuilt when a record was ADDED or REMOVED and never when one was renamed,
/// so the editor showed a name that no longer existed until something unrelated
/// happened to refresh it.
/// <para/>
/// Swept across every kind rather than fixed where it was reported, because the
/// same omission is invisible until somebody happens to look at the right
/// dropdown after the right edit.
/// </summary>
public sealed class CrossTabRenameSyncTests
{
    private readonly ITestOutputHelper _out;
    public CrossTabRenameSyncTests(ITestOutputHelper o) => _out = o;

    private static bool Offers(System.Collections.IEnumerable options, string text)
        => options.Cast<object>().Any(o => (o?.ToString() ?? "").Contains(text)
                                        || Label(o).Contains(text));

    private static string Label(object? option)
        => option is NavigatorTargetOption nav ? nav.DisplayLabel : option?.ToString() ?? "";

    [Fact]
    public void RenamingAPlaceUpdatesEveryListThatNamesIt()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddPlaceCommand.Execute(null);
            WindowHarness.Pump();

            var place = Assert.Single(vm.Places);
            place.DisplayName = "Rooftop Garden";
            WindowHarness.Pump();

            _out.WriteLine("levels: " + string.Join(" | ",
                vm.LevelOptions.Select(o => o.DisplayLabel)));

            // The dropdown a dialogue's start condition reads.
            Assert.True(Offers(vm.LevelOptions, "Rooftop Garden"),
                        "the level list still offers the old name");
            Assert.True(Offers(vm.AllTargetOptions, "Rooftop Garden"),
                        "the target list still offers the old name");
        });
    }

    [Fact]
    public void RenamingASceneUpdatesTheSceneLists()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddSceneCommand.Execute(null);
            WindowHarness.Pump();

            var scene = Assert.Single(vm.Scenes);
            scene.DisplayName = "Rooftop Kiss";
            WindowHarness.Pump();

            _out.WriteLine("scenes: " + string.Join(" | ",
                vm.SceneOptions.Select(o => o.DisplayLabel)));

            Assert.True(Offers(vm.SceneOptions, "Rooftop Kiss"),
                        "the scene list still offers the old name");
        });
    }

    [Fact]
    public void RenamingAnNpcUpdatesTheNpcList()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddNpcCommand.Execute(null);
            WindowHarness.Pump();

            var npc = Assert.Single(vm.Npcs);
            npc.DisplayName = "Rooftop Guard";
            WindowHarness.Pump();

            _out.WriteLine("npcs: " + string.Join(" | ",
                vm.NpcKeyOptions.Select(o => o.DisplayLabel)));

            Assert.True(Offers(vm.NpcKeyOptions, "Rooftop Guard"),
                        "the NPC list still offers the old name");
        });
    }

    [Fact]
    public void RenamingADialogueUpdatesTheListsThatNameIt()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();

            var dialogue = Assert.Single(vm.Dialogues);
            dialogue.DisplayName = "Rooftop Talk";
            WindowHarness.Pump();

            // The sidebar row is the most visible one, and it is a rename the
            // author is looking straight at.
            Assert.Contains("Rooftop Talk", dialogue.Display);
        });
    }

    [Fact]
    public void TheListsAlreadyKeptInStepStayThatWay()
    {
        // The control: these kinds were already watched, so they must keep
        // working. A fix that rewires notification could silently break them.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            vm.AddMusicCommand.Execute(null);
            vm.AddSfxCommand.Execute(null);
            WindowHarness.Pump();

            var music = Assert.Single(vm.Music);
            music.Key = "rooftop_theme";
            var sfx = Assert.Single(vm.Sfx);
            sfx.Key = "rooftop_thud";
            WindowHarness.Pump();

            Assert.True(Offers(vm.MusicKeyOptions, "rooftop_theme"));
            Assert.True(Offers(vm.SfxKeyOptions, "rooftop_thud"));
        });
    }
}
