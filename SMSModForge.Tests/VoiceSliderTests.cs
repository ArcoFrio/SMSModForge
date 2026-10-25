using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A character's typing voice as sliders with the number beside them, and a
/// Reset by each back to the game's numbers (the author, 1.8.0: "make sure the
/// change has a good range, and a button to reset to the game's default pitch
/// settings for each field").
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class VoiceSliderTests
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
    public void TheSlidersHoldEveryVoiceTheGameHas()
    {
        WindowHarness.Run(window =>
        {
            ShowCharacters(window);
            var vm = (MainViewModel)window.DataContext;
            vm.AddVoiceCharacterCommand.Execute(null);
            WindowHarness.Pump();
            window.UpdateLayout();

            var frequency = (Slider)window.FindName("VoiceFrequencySlider");
            var low = (Slider)window.FindName("VoicePitchMinSlider");
            var high = (Slider)window.FindName("VoicePitchMaxSlider");
            Assert.True(frequency.IsVisible, "the frequency slider is not on screen");

            var game = VanillaSpeech.All;
            Assert.True(frequency.Minimum < game.Min(s => s.Frequency) && frequency.Maximum > game.Max(s => s.Frequency),
                $"{frequency.Minimum}-{frequency.Maximum} does not hold the game's {game.Min(s => s.Frequency)}-{game.Max(s => s.Frequency)}");
            Assert.True(low.Minimum < game.Min(s => s.PitchMin) && low.Maximum >= game.Max(s => s.PitchMax));
            Assert.True(high.Minimum < game.Min(s => s.PitchMin) && high.Maximum >= game.Max(s => s.PitchMax));
        });
    }

    [Fact]
    public void ASliderSetsTheNumber_ATypedOnePastItsEndStays_AndResetPutsTheDefaultBack()
    {
        WindowHarness.Run(window =>
        {
            ShowCharacters(window);
            var vm = (MainViewModel)window.DataContext;
            vm.AddVoiceCharacterCommand.Execute(null);
            WindowHarness.Pump();
            window.UpdateLayout();
            var voice = vm.SelectedCharacter!;

            var frequency = (Slider)window.FindName("VoiceFrequencySlider");
            var pitch = (Slider)window.FindName("VoicePitchMinSlider");
            Assert.Equal(45, frequency.Value);

            frequency.Value = 30;
            pitch.Value = 0.65;
            WindowHarness.Pump();
            Assert.Equal(30, voice.Model.Typewriter!.Frequency);
            Assert.Equal("30", voice.TypewriterFrequencyText);
            Assert.Equal(0.65f, voice.Model.Typewriter.PitchMin, 3);

            // Typed past the slider's end: the slider shows its end, the
            // number stays what was typed.
            voice.TypewriterFrequencyText = "120";
            WindowHarness.Pump();
            window.UpdateLayout();
            Assert.Equal(frequency.Maximum, frequency.Value);
            Assert.Equal(120, voice.Model.Typewriter.Frequency);

            // Reset is there for a pack's own character too, and puts the
            // default back.
            var reset = Find<Button>(window, b => b.CommandParameter as string == nameof(CharacterViewModel.TypewriterFrequencyText))!;
            Assert.True(reset.IsVisible, "no Reset beside a pack character's frequency");
            Assert.True(reset.IsEnabled);
            Assert.Contains("45", reset.ToolTip as string);
            reset.Command.Execute(reset.CommandParameter);
            WindowHarness.Pump();
            Assert.Equal(45, frequency.Value);
            Assert.Equal("45", voice.TypewriterFrequencyText);
        });
    }

    [Fact]
    public void OneOfTheGamesCharactersStartsAtTheirOwnVoice()
    {
        WindowHarness.Run(window =>
        {
            ShowCharacters(window);
            var vm = (MainViewModel)window.DataContext;
            var anna = vm.Characters.First(c => c.Speaker?.Key == "anna");
            vm.SelectedCharacter = anna;
            WindowHarness.Pump();
            window.UpdateLayout();

            var speaker = anna.Speaker!;
            Assert.Equal(speaker.Frequency, ((Slider)window.FindName("VoiceFrequencySlider")).Value);
            Assert.Equal(speaker.PitchMin, ((Slider)window.FindName("VoicePitchMinSlider")).Value, 3);
            Assert.Equal(speaker.PitchMax, ((Slider)window.FindName("VoicePitchMaxSlider")).Value, 3);
            Assert.Null(anna.Model.Typewriter);   // looking changed nothing
        });
    }
}
