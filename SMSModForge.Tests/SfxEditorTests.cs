using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Services.Audio;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The SFX tab's sound editor, through the real window and the real save (the
/// author, 1.7.0): pick a spot, cut there, take a piece out, pitch it, save -
/// and the game gets a file made from the edit while the recording stays as
/// it was.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class SfxEditorTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;
    private const int TabSfx = 8;
    private const int Rate = 22050;

    public SfxEditorTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-sfxedit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>A second of tone, then a second of silence, then a second of
    /// a higher tone: three parts a cut can tell apart.</summary>
    private static AudioData Recording(double first = 300, double last = 900)
    {
        var x = new float[Rate * 3];
        for (int i = 0; i < Rate; i++) x[i] = 0.5f * (float)Math.Sin(2 * Math.PI * first * i / Rate);
        for (int i = 2 * Rate; i < 3 * Rate; i++) x[i] = 0.5f * (float)Math.Sin(2 * Math.PI * last * i / Rate);
        return new AudioData(new[] { x, (float[])x.Clone() }, Rate);
    }

    private string Hash(string rel) => Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(Path.Combine(_dir, rel))));

    private void Prepare(bool withVariant = false)
    {
        Recording().WriteWav(Path.Combine(_dir, "Audio", "Plap.wav"));
        if (withVariant) Recording(500, 700).WriteWav(Path.Combine(_dir, "Audio", "Plap_1.wav"));
        var pack = PackRepository.CreateEmpty("sfxedit.pack");
        pack.Sfx.Add(new SfxDef { Key = "plap", DisplayName = "Plap", AudioPath = "Audio/Plap.wav" });
        PackRepository.Save(pack, _dir);
    }

    private static SfxEditorViewModel Open(MainViewModel vm, string dir)
    {
        vm.OpenPackFromPath(dir);
        vm.SelectedTabIndex = TabSfx;
        vm.SelectedSfx = vm.Sfx.Single();
        WindowHarness.Pump();
        var editor = vm.SfxEditor!;
        for (int i = 0; i < 200 && editor.Current == null; i++) WindowHarness.Wait(TimeSpan.FromMilliseconds(20));
        Assert.NotNull(editor.Current);
        return editor;
    }

    private static System.Collections.Generic.IEnumerable<SfxStrip> Strips(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is SfxStrip s) yield return s;
            foreach (var inner in Strips(child)) yield return inner;
        }
    }

    private JObject Saved() => JObject.Parse(File.ReadAllText(Path.Combine(_dir, "modpack.json")));

    [Fact]
    public void CutPitchAndSave_TheEditIsWrittenOverTheFile_AndTheRecordingIsKeptAside()
    {
        Prepare();
        string before = Hash("Audio/Plap.wav");

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var editor = Open(vm, _dir);
            Assert.Equal(3.0, editor.Length, 2);
            Assert.Single(editor.Pieces);

            // Cut either side of the silence, and take it out.
            editor.Cursor = 1.0;
            editor.SplitCommand.Execute(null);
            editor.Cursor = 2.0;
            editor.SplitCommand.Execute(null);
            Assert.Equal(3, editor.Pieces.Count);
            editor.Cursor = 1.5;
            Assert.Equal(1, editor.Highlighted);
            editor.DeleteCommand.Execute(null);
            Assert.Equal(2, editor.Pieces.Count);
            Assert.Equal(2.0, editor.Length, 2);

            editor.Pitch = 12;
            Assert.True(vm.SavePack());
        });

        var sfx = (JObject)Saved()["sfx"]![0]!;
        _out.WriteLine(sfx.ToString());
        string made = (string)sfx["audioPath"]!;
        // The sound still plays its own file - which is the edit now (1.7.0):
        // one file for the sound, not the recording and a copy beside it.
        Assert.Equal("Audio/Plap.wav", made);
        Assert.Equal("Audio/Plap.wav", (string)sfx["edit"]!["source"]!);
        Assert.False(Directory.Exists(Path.Combine(_dir, "Audio", SfxEdits.Folder)));
        // The recording itself, untouched, kept aside where the export does
        // not look.
        Assert.Equal(before, Hash(".originals/Audio/Plap.wav"));
        Assert.NotEqual(before, Hash("Audio/Plap.wav"));

        var file = AudioData.Load(Path.Combine(_dir, made));
        _out.WriteLine($"made: {file.Seconds:0.000} s, {file.ChannelCount} ch");
        Assert.Equal(2.0, file.Seconds, 1);      // the silence is gone
        Assert.Equal(2, file.ChannelCount);      // still stereo
        // The first half is the first tone an octave up: 600 Hz. Measured
        // short of the join, which the stretch may move by a few milliseconds
        // to line the waves up.
        var x = file.Channels[0];
        int crossings = 0;
        for (int i = 1; i < (int)(Rate * 0.9); i++) if (x[i - 1] < 0 && x[i] >= 0) crossings++;
        _out.WriteLine($"first 0.9 s crosses zero {crossings} times");
        Assert.InRange(crossings, 535, 545);
    }

    [Fact]
    public void ItsVariantsAreCutOnTheirOwnAndMadeWhereTheGameLooksForThem()
    {
        Prepare(withVariant: true);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var editor = Open(vm, _dir);
            Assert.True(editor.HasVariants);
            Assert.Equal(new[] { "Plap.wav", "Plap_1.wav" }, editor.Recordings.Select(r => r.Name));

            editor.Current = editor.Recordings[1];
            editor.Cursor = 1.5;
            editor.SplitCommand.Execute(null);
            editor.Cursor = 2.5;
            editor.DeleteCommand.Execute(null);   // the second piece of the variant, its last 1.5 s
            Assert.Equal(1.5, editor.Length, 2);

            editor.Current = editor.Recordings[0];
            Assert.Single(editor.Pieces);          // the main recording was not cut
            Assert.True(vm.SavePack());
        });

        // Each over its own file, which is where the game looks for it.
        var variant = AudioData.Load(Path.Combine(_dir, "Audio", "Plap_1.wav"));
        var main = AudioData.Load(Path.Combine(_dir, "Audio", "Plap.wav"));
        _out.WriteLine($"main {main.Seconds:0.00} s, variant {variant.Seconds:0.00} s");
        Assert.Equal(3.0, main.Seconds, 1);
        Assert.Equal(1.5, variant.Seconds, 1);
        Assert.True(File.Exists(Path.Combine(_dir, ".originals", "Audio", "Plap.wav")));
        Assert.Equal(3.0, AudioData.Load(Path.Combine(_dir, ".originals", "Audio", "Plap_1.wav")).Seconds, 1);
    }

    [Fact]
    public void ResetPutsTheRecordingBack_AndNothingIsLeftAside()
    {
        Prepare();
        string before = Hash("Audio/Plap.wav");
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var editor = Open(vm, _dir);
            editor.Speed = 1.5;
            Assert.True(vm.SavePack());
            Assert.NotEqual(before, Hash("Audio/Plap.wav"));

            editor.ResetCommand.Execute(null);
            Assert.False(editor.IsEdited);
            Assert.True(vm.SavePack());
        });

        var sfx = (JObject)Saved()["sfx"]![0]!;
        Assert.Equal("Audio/Plap.wav", (string)sfx["audioPath"]!);
        Assert.Null(sfx["edit"]);
        Assert.Equal(before, Hash("Audio/Plap.wav"));
        Assert.False(Directory.Exists(Path.Combine(_dir, ".originals")));
    }

    [Fact]
    public void ASecondEditIsMadeFromTheRecording_NotFromTheFirstEdit()
    {
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var editor = Open(vm, _dir);
            editor.Cursor = 1.0;
            editor.SplitCommand.Execute(null);
            editor.Cursor = 0.5;
            editor.DeleteCommand.Execute(null);          // 2 s left
            Assert.True(vm.SavePack());
            Assert.Equal(2.0, AudioData.Load(Path.Combine(_dir, "Audio", "Plap.wav")).Seconds, 1);

            // Changed again and saved again: still the recording cut once.
            editor.Pitch = 3;
            Assert.True(vm.SavePack());
            Assert.True(vm.SavePack());
        });
        Assert.Equal(2.0, AudioData.Load(Path.Combine(_dir, "Audio", "Plap.wav")).Seconds, 1);
        Assert.Equal(3.0, AudioData.Load(Path.Combine(_dir, ".originals", "Audio", "Plap.wav")).Seconds, 1);
    }

    [Fact]
    public void TheExportShipsTheEdit_AndNotTheRecordingItWasCutFrom()
    {
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var editor = Open(vm, _dir);
            editor.Speed = 2;
            Assert.True(vm.SavePack());
        });

        string archive = Path.Combine(_dir + "-out", "pack.smspack");
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        try
        {
            PackExporter.Export(_dir, archive);
            using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
            var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            _out.WriteLine(string.Join(", ", names));
            Assert.Contains("Audio/Plap.wav", names);
            Assert.DoesNotContain(names, n => n.Contains(".originals"));
            Assert.Equal(1, names.Count(n => n.EndsWith(".wav") || n.EndsWith(".ogg")));
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(archive)!, true); } catch (IOException) { } }
    }

    [Fact]
    public void ARecordingAnotherSoundAlsoPlaysIsNotReplaced()
    {
        // Replacing it would change the other sound too, so this one keeps a
        // file of its own beside it, as every edit did before.
        Recording().WriteWav(Path.Combine(_dir, "Audio", "Plap.wav"));
        string before = Hash("Audio/Plap.wav");
        var pack = PackRepository.CreateEmpty("sfxedit.pack");
        pack.Sfx.Add(new SfxDef { Key = "plap", DisplayName = "Plap", AudioPath = "Audio/Plap.wav" });
        pack.Sfx.Add(new SfxDef { Key = "slap", DisplayName = "Slap", AudioPath = "Audio/Plap.wav" });
        PackRepository.Save(pack, _dir);

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = TabSfx;
            vm.SelectedSfx = vm.Sfx.Single(s => s.Key == "plap");
            WindowHarness.Pump();
            var editor = vm.SfxEditor!;
            for (int i = 0; i < 200 && editor.Current == null; i++) WindowHarness.Wait(TimeSpan.FromMilliseconds(20));
            editor.Speed = 2;
            Assert.True(vm.SavePack());
        });

        var sfx = (JArray)Saved()["sfx"]!;
        Assert.Equal("Audio/edited/plap.ogg", (string)sfx[0]!["audioPath"]!);
        Assert.Equal("Audio/Plap.wav", (string)sfx[1]!["audioPath"]!);
        Assert.Equal(before, Hash("Audio/Plap.wav"));
        Assert.False(Directory.Exists(Path.Combine(_dir, ".originals")));
    }

    [Fact]
    public void AnEditSavedByAnEarlierBuild_MovesOverItsRecordingOnTheNextSave()
    {
        // 1.7.0's first builds wrote the edit into an "edited" folder beside
        // the recording. The next save puts it where it belongs now.
        Prepare();
        string before = Hash("Audio/Plap.wav");
        var pack = PackRepository.Load(_dir);
        pack.Sfx[0].Edit = new SfxEditDef { Source = "Audio/Plap.wav", Speed = 2, Rendered = "made-before" };
        pack.Sfx[0].AudioPath = "Audio/edited/plap.ogg";
        PackRepository.Save(pack, _dir);
        Recording().WriteOgg(Path.Combine(_dir, "Audio", "edited", "plap.ogg"));

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            Assert.True(vm.SavePack());
        });

        var sfx = (JObject)Saved()["sfx"]![0]!;
        Assert.Equal("Audio/Plap.wav", (string)sfx["audioPath"]!);
        Assert.False(File.Exists(Path.Combine(_dir, "Audio", "edited", "plap.ogg")));
        Assert.Equal(before, Hash(".originals/Audio/Plap.wav"));
        Assert.Equal(1.5, AudioData.Load(Path.Combine(_dir, "Audio", "Plap.wav")).Seconds, 1);   // twice as fast
    }

    [Fact]
    public void AnEditComesBackWithThePack_AndUndoTakesACutBack()
    {
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var editor = Open(vm, _dir);
            editor.Cursor = 1.0;
            editor.SplitCommand.Execute(null);
            Assert.True(vm.SavePack());
        });

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var editor = Open(vm, _dir);
            Assert.Equal(2, editor.Pieces.Count);
            // The picker still names the recording, not the file made from it.
            Assert.Equal("Audio/Plap.wav", vm.SelectedSfx!.AudioPath);

            editor.Cursor = 0.5;
            editor.SplitCommand.Execute(null);
            Assert.Equal(3, editor.Pieces.Count);
            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();
            var after = vm.SfxEditor!;
            for (int i = 0; i < 200 && after.Current == null; i++) WindowHarness.Wait(TimeSpan.FromMilliseconds(20));
            Assert.Equal(2, after.Pieces.Count);
        });
    }

    [Fact]
    public void TheWheelZoomsTheStrip_AndAClickPicksASpotAndHighlightsItsPiece()
    {
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var editor = Open(vm, _dir);
            editor.Cursor = 1.0;
            editor.SplitCommand.Execute(null);
            WindowHarness.Pump();

            var strip = Strips(window).Single(x => x.IsVisible && ReferenceEquals(x.Editor, editor));
            Assert.True(strip.ActualWidth > 100, "the strip was not laid out");

            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = UIElement.MouseWheelEvent };
            strip.RaiseEvent(wheel);
            _out.WriteLine($"zoom after one notch: {editor.Zoom:0.00}");
            Assert.True(wheel.Handled, "the page under the strip would scroll too");
            Assert.Equal(1.25, editor.Zoom, 2);

            // Zooming keeps the time under the pointer where it was.
            editor.ZoomAround(4, 2.0);
            double x = strip.XOf(2.0);
            editor.ZoomAround(8, 2.0);
            Assert.Equal(x, strip.XOf(2.0), 1);
            editor.FitCommand.Execute(null);

            // A click at three quarters of the way picks 2.25 s, in the second piece.
            var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
            double at = strip.TimeAt(strip.ActualWidth * 0.75);
            editor.Cursor = at;   // what the click handler does with the pointer's position
            _out.WriteLine($"picked {editor.Cursor:0.000} s, piece {editor.Highlighted}");
            Assert.Equal(2.25, editor.Cursor, 2);
            Assert.Equal(1, editor.Highlighted);
        });
    }

    [Fact]
    public void PlayPlaysTheEditFromTheSpotPicked()
    {
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var editor = Open(vm, _dir);
            editor.Speed = 2;
            editor.Cursor = 1.0;
            editor.PlayCommand.Execute(null);

            var player = (Services.SfxPreviewPlayer)typeof(MainViewModel)
                .GetField("_sfxPreview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(vm)!;
            var played = player.LastPlayed!.Value;
            _out.WriteLine($"played {played.Sound.Seconds:0.00} s from {played.From:0.00} s");
            Assert.Equal(1.5, played.Sound.Seconds, 1);   // twice as fast
            Assert.Equal(0.5, played.From, 2);            // the spot, in the faster sound
        });
    }

    [Fact]
    public void AnEditedSoundIsHeardAsEdited_EvenWhenAnotherIsOpenInTheTab()
    {
        // The list of sound cues over a dialogue line plays any of the pack's
        // sounds, and one that was edited has to sound as the game will play
        // it - not as the recording it was cut from (1.7.0).
        Prepare();
        Recording().WriteWav(Path.Combine(_dir, "Audio", "Door.wav"));
        var pack = PackRepository.Load(_dir);
        pack.Sfx.Add(new SfxDef { Key = "door", DisplayName = "Door", AudioPath = "Audio/Door.wav" });
        PackRepository.Save(pack, _dir);

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = TabSfx;
            var plap = vm.Sfx.Single(s => s.Key == "plap");
            vm.SelectedSfx = plap;
            WindowHarness.Pump();
            var editor = vm.SfxEditor!;
            for (int i = 0; i < 200 && editor.Current == null; i++) WindowHarness.Wait(TimeSpan.FromMilliseconds(20));

            // Its middle second out: two seconds left of three.
            editor.Cursor = 1.0;
            editor.SplitCommand.Execute(null);
            editor.Cursor = 2.0;
            editor.SplitCommand.Execute(null);
            editor.Cursor = 1.5;
            editor.DeleteCommand.Execute(null);

            vm.SelectedSfx = vm.Sfx.Single(s => s.Key == "door");
            WindowHarness.Pump();

            var player = (Services.SfxPreviewPlayer)typeof(MainViewModel)
                .GetField("_sfxPreview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(vm)!;
            player.Stop();
            vm.PlaySfxCommand.Execute(plap);
            for (int i = 0; i < 200 && player.LastPlayed == null; i++) WindowHarness.Wait(TimeSpan.FromMilliseconds(20));

            // Not the recording it was cut from.
            Assert.Null(player.LastPlayedFile);
            Assert.NotNull(player.LastPlayed);
            var played = player.LastPlayed!.Value;
            _out.WriteLine($"played {played.Sound.Seconds:0.00} s");
            Assert.Equal(2.0, played.Sound.Seconds, 1);
            player.Stop();
        });
    }

    [Fact]
    public void AMusicTrackIsEditedTheSameWay()
    {
        Recording().WriteWav(Path.Combine(_dir, "Music", "Theme.wav"));
        // A file beside it named like a variant: music has none, so it is not one.
        Recording().WriteWav(Path.Combine(_dir, "Music", "Theme_1.wav"));
        string theme1 = Hash("Music/Theme_1.wav");
        var pack = PackRepository.CreateEmpty("musicedit.pack");
        pack.Music.Add(new MusicDef { Key = "theme", DisplayName = "Theme", AudioPath = "Music/Theme.wav" });
        PackRepository.Save(pack, _dir);

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = 7;   // Music
            vm.SelectedMusic = vm.Music.Single();
            WindowHarness.Pump();
            var editor = vm.MusicEditor!;
            for (int i = 0; i < 200 && editor.Current == null; i++) WindowHarness.Wait(TimeSpan.FromMilliseconds(20));
            Assert.NotNull(editor.Current);
            Assert.False(editor.HasVariants);

            // The tab shows it, in the Music tab's own box.
            Assert.Contains(Strips(window), x => x.IsVisible && ReferenceEquals(x.Editor, editor));

            editor.Cursor = 1.0;
            editor.SplitCommand.Execute(null);
            editor.Cursor = 0.5;
            editor.DeleteCommand.Execute(null);
            Assert.True(vm.SavePack());
        });

        var music = (JObject)Saved()["music"]![0]!;
        Assert.Equal("Music/Theme.wav", (string)music["audioPath"]!);
        var made = AudioData.Load(Path.Combine(_dir, "Music", "Theme.wav"));
        _out.WriteLine($"made {made.Seconds:0.000} s");
        Assert.Equal(2.0, made.Seconds, 2);
        Assert.True(File.Exists(Path.Combine(_dir, ".originals", "Music", "Theme.wav")));
        // Music has no variants: the file named like one is left alone.
        Assert.Equal(theme1, Hash("Music/Theme_1.wav"));
        Assert.False(File.Exists(Path.Combine(_dir, ".originals", "Music", "Theme_1.wav")));
    }

    /// <summary>A picture of the tab for a person to look at, when
    /// <c>SMSMODFORGE_SHOT</c> names a file. Not a test of anything.</summary>
    [Fact]
    public void Picture()
    {
        string? shot = Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT");
        if (string.IsNullOrEmpty(shot)) return;
        Prepare(withVariant: true);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            window.Width = 1400;
            window.Height = 1000;
            var editor = Open(vm, _dir);
            editor.Cursor = 1.0;
            editor.SplitCommand.Execute(null);
            editor.Cursor = 2.0;
            editor.SplitCommand.Execute(null);
            editor.Cursor = 1.4;
            editor.Pitch = 2;
            editor.EchoAmount = 0.3;
            WindowHarness.Pump();
            var bmp = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(window);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(shot);
            png.Save(fs);
        });
    }
}
