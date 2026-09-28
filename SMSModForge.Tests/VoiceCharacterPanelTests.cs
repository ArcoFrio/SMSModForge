using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A character with no bust at all - a voice, like John Dick - has no outfit to
/// select, and the pane with its name, pronouns, colour and voice was tied to
/// the selected outfit: it never showed, so none of those could be set
/// (author, 2026-09-27).
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class VoiceCharacterPanelTests
{
    private static void ShowCharacters(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Characters")
            { tabs.SelectedIndex = i; WindowHarness.Pump(); return; }
        throw new Xunit.Sdk.XunitException("no Characters tab");
    }

    private static T? Find<T>(DependencyObject from, System.Func<T, bool> wanted) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(from); i++)
        {
            var child = VisualTreeHelper.GetChild(from, i);
            if (child is T t && wanted(t)) return t;
            var deeper = Find(child, wanted);
            if (deeper != null) return deeper;
        }
        return null;
    }

    [Fact]
    public void AVoiceOnlyCharacterShowsItsOwnSettings_ItsPronounsCanBeChosen_AndNoOutfitPanelShows()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);

            vm.AddVoiceCharacterCommand.Execute(null);
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var voice = vm.SelectedCharacter;
            Assert.NotNull(voice);
            Assert.True(voice!.HasNoBust, "the new character is not the voice-only one just added");
            Assert.Null(vm.SelectedOutfit);

            var picker = (ComboBox)window.FindName("PronounsPicker");
            Assert.True(picker.IsVisible, "the pronouns box is not on screen for a voice-only character");
            Assert.True(picker.IsEnabled, "the pronouns box is greyed for a character of the pack's own");

            // Chosen through the box itself.
            picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, Pronouns.Female));
            WindowHarness.Pump();
            Assert.Equal(Pronouns.Female, voice.Model.Pronouns);

            // Nothing of an outfit's: there is none.
            var sprites = Find<GroupBox>(window, g => View.TutorialAnchor.GetId(g) == "panel:outfitSprites");
            Assert.True(sprites == null || !sprites.IsVisible, "an outfit's sprites panel shows with no outfit");

            // Nor what only a bust uses: faces to pick, and an outfit to start in.
            var faces = (GroupBox)window.FindName("SpeechExpressionsBox");
            var startIn = (ComboBox)window.FindName("DefaultOutfitPicker");
            Assert.False(faces.IsVisible, "speech expressions show for a voice");
            Assert.False(startIn.IsVisible, "the default outfit shows for a voice");

            // Given a bust, they are back.
            voice.BustSource = BustSource.Pack;
            WindowHarness.Pump();
            window.UpdateLayout();
            Assert.True(faces.IsVisible, "speech expressions stay hidden once the character has a bust");
            Assert.True(startIn.IsVisible, "the default outfit stays hidden once the character has a bust");
        });
    }

    [Fact]
    public void ThePlayerShowsTheirSettings_AsAVoice_WithTheGamesPronounsGreyed()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            vm.SelectedCharacter = vm.Characters.First(c => c.IsPlayer);
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var picker = (ComboBox)window.FindName("PronounsPicker");
            Assert.True(picker.IsVisible, "the player's settings do not show");
            Assert.False(picker.IsEnabled, "the player's pronouns can be changed - they are the game's");
            Assert.Equal(Pronouns.Male, (Pronouns)picker.SelectedValue);

            Assert.False(((GroupBox)window.FindName("SpeechExpressionsBox")).IsVisible, "speech expressions show for the player");
            Assert.False(((ComboBox)window.FindName("DefaultOutfitPicker")).IsVisible, "the default outfit shows for the player");

            // The typing voice: shown, greyed.
            var voice = (Grid)window.FindName("TypewriterFields");
            Assert.True(voice.IsVisible, "the player's typing voice is not shown");
            Assert.False(voice.IsEnabled, "the player's typing voice can be changed");
        });
    }
}
