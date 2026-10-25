using System.Linq;
using System.Windows.Controls;
using SMSModForge.Localization;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The main tabs come in <see cref="MainTab"/>'s order, which everything that
/// picks a tab by its place reads. Variables and Integration are the last two
/// (the author, 1.8.0: "considering they affect everything else"). Run in EX
/// too by -WithEx, where the same tabs stand in the rail.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class MainTabOrderTests
{
    private static readonly (int Place, string Header)[] Expected =
    {
        (MainTab.ModForge, "home.modforge"), (MainTab.Characters, "characters.characters"),
        (MainTab.Npcs, "npcs.npcs"), (MainTab.Places, "places.places"),
        (MainTab.MapButtons, "mapButtons.mapButtons"), (MainTab.Dialogues, "dialogues.dialogues"),
        (MainTab.Scenes, "scenes.scenes"), (MainTab.Music, "music.music"), (MainTab.Sfx, "sfx.sfx"),
        (MainTab.Wallpapers, "wallpapers.wallpapers"), (MainTab.Ui, "ui.ui"), (MainTab.Quests, "quests.quests"),
        (MainTab.Variables, "variables.variables"), (MainTab.Integration, "integration.integration"),
    };

    [Fact]
    public void TheWindowsTabsAreInMainTabsOrderWithVariablesAndIntegrationLast()
    {
        WindowHarness.Run(window =>
        {
            var tabs = (TabControl)window.FindName("MainTabs");
            var headers = tabs.Items.Cast<TabItem>().Select(t => t.Header?.ToString()).ToList();
            Assert.Equal(Expected.Length, headers.Count);
            foreach (var (place, key) in Expected)
                Assert.Equal(Loc.T(key), headers[place]);

            Assert.Equal(Loc.T("variables.variables"), headers[^2]);
            Assert.Equal(Loc.T("integration.integration"), headers[^1]);

            // Picking a tab by its place opens that tab.
            var vm = (MainViewModel)window.DataContext;
            vm.SelectedTabIndex = MainTab.Variables;
            WindowHarness.Pump();
            Assert.Equal(Loc.T("variables.variables"), ((TabItem)tabs.SelectedItem).Header?.ToString());
        });
    }
}
