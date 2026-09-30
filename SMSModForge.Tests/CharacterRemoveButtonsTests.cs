using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// - Character and - Outfit greyed for what the game owns (the author, 1.6.3):
/// its own characters, the player, and its own busts - never an outfit the
/// pack added to one of them. Measured on the buttons as drawn.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class CharacterRemoveButtonsTests
{
    private readonly ITestOutputHelper _out;
    public CharacterRemoveButtonsTests(ITestOutputHelper o) => _out = o;

    private static Button ButtonSaying(MainWindow window, string key)
    {
        var bar = (ToolBar)window.FindName("CharacterToolBar");
        return bar.Items.OfType<Button>().Single(b => (b.Content as string) == Loc.T(key));
    }

    [Fact]
    public void OnlyWhatThePackOwnsCanBeRemoved()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            for (int i = 0; i < tabs.Items.Count; i++)
                if (tabs.Items[i] is TabItem t && (t.Header as string) == "Characters") { tabs.SelectedIndex = i; break; }
            WindowHarness.Pump();

            var removeCharacter = ButtonSaying(window, "characters.removeCharacter");
            var removeOutfit = ButtonSaying(window, "characters.removeOutfit");
            void Settle() { CommandManager.InvalidateRequerySuggested(); WindowHarness.Pump(); }

            // One of the game's characters, on one of its own busts.
            var theirs = vm.Characters.First(c => c.Model.IsVanillaCharacter && c.Outfits.Any(o => o.IsVanillaBust));
            vm.SelectedCharacter = theirs;
            vm.SelectedOutfit = theirs.Outfits.First(o => o.IsVanillaBust);
            Settle();
            _out.WriteLine($"{theirs.Key}, the game's bust: - Character {removeCharacter.IsEnabled}, - Outfit {removeOutfit.IsEnabled}");
            Assert.False(removeCharacter.IsEnabled);
            Assert.False(removeOutfit.IsEnabled);

            // An outfit the pack added to them: that one is the pack's.
            var added = theirs.AddOutfit();
            vm.SelectedOutfit = added;
            Settle();
            _out.WriteLine($"{theirs.Key}, an added bust: - Character {removeCharacter.IsEnabled}, - Outfit {removeOutfit.IsEnabled}");
            Assert.False(removeCharacter.IsEnabled);
            Assert.True(removeOutfit.IsEnabled);

            // The player.
            var player = vm.Characters.First(c => c.IsPlayer);
            vm.SelectedCharacter = player;
            Settle();
            Assert.False(removeCharacter.IsEnabled);

            // A character of the pack's own: both.
            vm.AddCharacterCommand.Execute(null);
            var mine = vm.Characters.Last(c => !c.Model.IsVanillaCharacter && !c.IsPlayer);
            vm.SelectedCharacter = mine;
            vm.SelectedOutfit = mine.Outfits.FirstOrDefault() ?? mine.AddOutfit();
            Settle();
            _out.WriteLine($"{mine.Key}: - Character {removeCharacter.IsEnabled}, - Outfit {removeOutfit.IsEnabled}");
            Assert.True(removeCharacter.IsEnabled);
            Assert.True(removeOutfit.IsEnabled);
        });
    }
}
