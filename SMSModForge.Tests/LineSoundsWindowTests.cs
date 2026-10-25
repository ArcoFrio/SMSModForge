using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A line's sounds in the node editor (the author, 1.8.0): a box under the
/// Text only while the line has a sound's pattern, a row per sound - volume,
/// start said in words, words shown or not, waiting for the typewriter - and
/// the whole line played as the game will play it.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class LineSoundsWindowTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-linesounds-" + Guid.NewGuid().ToString("N"));

    public LineSoundsWindowTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Audio"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private MainViewModel Opened(MainWindow window)
    {
        SoundCueMigrationTests.Tone(Path.Combine(_dir, "Audio", "clink.wav"), 0.5);
        SoundCueMigrationTests.Tone(Path.Combine(_dir, "Audio", "buzz.wav"), 0.3);
        var pack = PackRepository.CreateEmpty("linesounds.pack");
        pack.Sfx.Add(new SfxDef { Key = "Clink", DisplayName = "Clink", AudioPath = "Audio/clink.wav", TextPatterns = new() { "*clink*" } });
        pack.Sfx.Add(new SfxDef { Key = "Buzz", DisplayName = "Buzz", AudioPath = "Audio/buzz.wav", TextPatterns = new() { "*buzz*" } });
        var d = new DialogueDef { Key = "chat", DisplayName = "Chat" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Oh *clink* and *buzz* there", Children = { 2 } });
        d.Nodes.Add(new DialogueNodeDef { Id = 2, Text = "No sounds here." });
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _dir);

        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        vm.SelectedTabIndex = 5;
        vm.SelectedDialogue = vm.Dialogues.Single();
        vm.SelectedNode = vm.SelectedDialogue.Nodes.Single(n => n.Id == 1);
        WindowHarness.Pump();
        window.UpdateLayout();
        return vm;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    [Fact]
    public void TheBoxShowsOnlyWhileTheLineHasASound()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var box = (FrameworkElement)window.FindName("NodeSoundsBox");
            Assert.True(box.IsVisible);
            Assert.Equal(new[] { "1 · *clink*", "2 · *buzz*" }, vm.SelectedNodeCueRows.Select(r => r.Title));

            vm.SelectedNode = vm.SelectedDialogue!.Nodes.Single(n => n.Id == 2);   // the control
            WindowHarness.Pump();
            Assert.False(box.IsVisible);

            // Typed in, the box comes: it follows the text as it changes.
            vm.SelectedNode!.Text = "Now *clink*.";
            WindowHarness.Pump();
            Assert.True(box.IsVisible);
            Assert.Single(vm.SelectedNodeCueRows);
        });
    }

    /// <summary>
    /// The box follows the SFX tab as well as the line: a pattern changed
    /// there, or a sound added with one the line holds, shows here on coming
    /// back (the author, 1.8.0: "make sure this syncs to new SFX live").
    /// </summary>
    [Fact]
    public void TheBoxFollowsPatternsChangedOnTheSfxTab()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            Assert.Equal(new[] { "*clink*", "*buzz*" }, vm.SelectedNodeCueRows.Select(r => r.Match.Pattern));

            // On the SFX tab, Buzz answers to another word now.
            vm.SelectedTabIndex = MainTab.Sfx;
            WindowHarness.Pump();
            vm.Sfx.Single(s => s.Key == "Buzz").TextPatternsCsv = "*bzzt*";
            WindowHarness.Pump();
            vm.SelectedTabIndex = MainTab.Dialogues;
            WindowHarness.Pump();
            Assert.Equal(new[] { "*clink*" }, vm.SelectedNodeCueRows.Select(r => r.Match.Pattern));

            // A new sound whose pattern the line holds joins it.
            vm.SelectedTabIndex = MainTab.Sfx;
            WindowHarness.Pump();
            vm.AddSfxCommand.Execute(null);
            WindowHarness.Pump();
            vm.SelectedSfx!.TextPatternsCsv = "there";
            WindowHarness.Pump();
            vm.SelectedTabIndex = MainTab.Dialogues;
            WindowHarness.Pump();
            Assert.Equal(new[] { "*clink*", "there" }, vm.SelectedNodeCueRows.Select(r => r.Match.Pattern));
            Assert.True(((FrameworkElement)window.FindName("NodeSoundsBox")).IsVisible);
        });
    }

    [Fact]
    public void ARowWritesOnlyWhatDiffersAndSaysWhatItsStartMeans()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var node = vm.SelectedNode!.Model;
            var (first, second) = (vm.SelectedNodeCueRows[0], vm.SelectedNodeCueRows[1]);

            Assert.Equal(SMSModForge.Localization.Loc.T("cues.startsWithLine"), first.DelayMeaning);
            Assert.Equal(-3, second.DelayMin);   // a sound after another can overlap it
            Assert.Equal(0, first.DelayMin);     // the first cannot start before the line

            first.VolumePercent = 150;
            second.Delay = -0.2;
            WindowHarness.Pump();
            Assert.Equal(1.5f, node.Cues.Single(c => c.At == 1).Volume);
            Assert.Equal(-0.2f, node.Cues.Single(c => c.At == 2).Delay);
            _out.WriteLine(second.DelayMeaning);
            Assert.Contains("*clink*", second.DelayMeaning);
            Assert.Equal(SMSModForge.Localization.Loc.F("cues.startsBeforePreviousEnds", "seconds", 0.2.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "pattern", "*clink*"),
                         second.DelayMeaning);

            // Back to the defaults, nothing left in the file.
            first.VolumePercent = 100;
            second.Delay = 0;
            Assert.Empty(node.Cues);
        });
    }

    /// <summary>
    /// A start or a volume beyond what the sliders reach - an older pack's
    /// migration gives two long sounds that started together a start well
    /// under -3 s, and a pattern written at 300% a volume of 300% - is shown
    /// and kept: looking at the line must not change it.
    /// </summary>
    [Fact]
    public void AValueBeyondTheSlidersIsKeptWhenTheLineIsShown()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            Opened(window);
            var node = vm.SelectedNode!.Model;
            node.Cues.Clear();   // whatever opening the pack gave it
            node.Cues.Add(new SoundCueDef { At = 1, Pattern = "*clink*", Volume = 3f, Delay = 6.5f });
            node.Cues.Add(new SoundCueDef { At = 2, Pattern = "*buzz*", Delay = -5f });
            vm.SelectedNode = vm.SelectedDialogue!.Nodes.Single(n => n.Id == 2);
            WindowHarness.Pump();
            string before = PackRepository.SerializeAsSaved(vm.Pack);
            vm.SelectedNode = vm.SelectedDialogue.Nodes.Single(n => n.Id == 1);   // shown again, with them
            WindowHarness.Pump();
            window.UpdateLayout();

            var sliders = Descendants<Slider>(window).Where(s => s.Name is "CueVolume" or "CueDelay" && s.IsVisible).ToList();
            Assert.Equal(4, sliders.Count);   // the control: the rows were built
            Assert.Equal(3f, node.Cues[0].Volume);
            Assert.Equal(6.5f, node.Cues[0].Delay);
            Assert.Equal(-5f, node.Cues[1].Delay);
            Assert.Equal(before, PackRepository.SerializeAsSaved(vm.Pack));
            // What the sliders show is the value, not their end.
            Assert.Equal(new[] { 300.0, 6.5, -5.0 }, new[] { sliders[0].Value, sliders[1].Value, sliders[3].Value });
        });
    }

    /// <summary>
    /// A start to the millisecond - what the migration writes - reads the
    /// same in the box and in the sentence beside it (the author's screenshot,
    /// 1.8.0: the box showed "0.05700(", the float read back as a double and
    /// cut off, while the sentence said "0,06").
    /// </summary>
    [Fact]
    public void TheBoxAndTheSentenceSayTheSameStart()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            Opened(window);
            var node = vm.SelectedNode!.Model;
            node.Cues.Clear();
            node.Cues.Add(new SoundCueDef { At = 2, Pattern = "*buzz*", Delay = 0.057f });
            vm.SelectedNode = vm.SelectedDialogue!.Nodes.Single(n => n.Id == 2);
            WindowHarness.Pump();
            vm.SelectedNode = vm.SelectedDialogue.Nodes.Single(n => n.Id == 1);
            WindowHarness.Pump();
            window.UpdateLayout();

            var box = Descendants<TextBox>(window).Where(t => t.Name == "CueDelayBox" && t.IsVisible).ElementAt(1);
            _out.WriteLine($"box '{box.Text}', sentence '{vm.SelectedNodeCueRows[1].DelayMeaning}'");
            Assert.Equal("0.057", box.Text);
            Assert.Contains("0.057", vm.SelectedNodeCueRows[1].DelayMeaning);
            // The whole number is in sight: the box is wide enough for it.
            var typeface = new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch);
            double wide = new FormattedText("-0.057", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                            typeface, box.FontSize, Brushes.Black, 1.0).Width;
            Assert.True(box.ViewportWidth >= wide, $"the box shows {box.ViewportWidth:0} px of {wide:0}");
        });
    }

    [Fact]
    public void HiddenWordsLeaveTheGameLook()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            vm.SelectedNodeCueRows[1].ShowText = false;
            WindowHarness.Pump();
            Assert.Equal("Oh *clink* and there", vm.SelectedNode!.ShownPreview);
            Assert.Equal("Oh *clink* and *buzz* there", vm.SelectedNode.Text);   // the pattern stays: it is what plays
        });
    }

    /// <summary>Where sounds begin in a mix: the starts of stretches of
    /// sound after silence, in seconds.</summary>
    private static List<double> Onsets(SMSModForge.Services.Audio.AudioData mix)
    {
        var onsets = new List<double>();
        var l = mix.Channels[0];
        int quiet = 0;
        for (int i = 0; i < l.Length; i++)
        {
            if (Math.Abs(l[i]) > 0.001)
            {
                if (quiet > 200 || onsets.Count == 0 && i < 200) onsets.Add(i / (double)mix.SampleRate);
                quiet = 0;
            }
            else quiet++;
        }
        return onsets;
    }

    [Fact]
    public void PlayingTheLinePutsEachSoundWhereTheGameWill()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            Assert.True(vm.PlayLineSoundsCommand.CanExecute(null));

            // *buzz* half a second after *clink* (0.5 s long) ends: at 1.0 s.
            vm.SelectedNodeCueRows[1].Delay = 0.5;
            var mix = vm.PlayLineSounds()!;
            var onsets = Onsets(mix);
            _out.WriteLine("onsets: " + string.Join(", ", onsets.Select(o => o.ToString("0.000"))));
            Assert.Equal(2, onsets.Count);
            Assert.Equal(0.0, onsets[0], 2);
            Assert.Equal(1.0, onsets[1], 2);
            Assert.Same(mix, vm.LinePlayer.LastPlayed!.Value.Sound);   // what was handed to the player

            // Louder: the first sound twice its own volume is four times as
            // strong, as the game squares it.
            double Peak(SMSModForge.Services.Audio.AudioData a, double from, double to)
                => a.Channels[0].Skip((int)(from * a.SampleRate)).Take((int)((to - from) * a.SampleRate)).Max(Math.Abs);
            double before = Peak(mix, 0, 0.4);
            vm.SelectedNodeCueRows[0].VolumePercent = 200;
            var louder = vm.PlayLineSounds()!;
            _out.WriteLine($"peak {before:0.000} -> {Peak(louder, 0, 0.4):0.000}");
            Assert.Equal(4.0, Peak(louder, 0, 0.4) / before, 1);
        });
    }
}
