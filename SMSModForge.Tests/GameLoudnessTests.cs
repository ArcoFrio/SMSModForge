using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Services.Audio;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Play buttons sound as the game will (the author, 1.7.0): a sound effect
/// at its volume squared, as Game Creator 2 plays it, music at its own volume
/// or the game's track it is copied from, and both through the game's mixer -
/// numbers read from 1.8E's files (see <see cref="GameLoudness"/>).
/// </summary>
public sealed class GameLoudnessTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-loud-" + Guid.NewGuid().ToString("N"));

    public GameLoudnessTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Audio"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void TheGamesOwnLevels()
    {
        // CoreAudio's Master group sits at -10.41 dB: a third of full, near enough.
        Assert.InRange(GameLoudness.Mixer, 0.301f, 0.303f);
        // Game Creator squares what it is given: half is a quarter.
        Assert.Equal(0.25f * GameLoudness.Mixer, GameLoudness.Sfx(0.5f), 5);
        Assert.Equal(GameLoudness.Mixer, GameLoudness.Sfx(null), 5);
        // A track with no volume is as loud as the game's Beach, which it is copied from.
        Assert.Equal(0.5f * GameLoudness.Mixer, GameLoudness.Music(null), 5);
        Assert.Equal(0.8f * GameLoudness.Mixer, GameLoudness.Music(0.8f), 5);
        // And past full, up to five times the game's level (1.7.0): the same
        // curve all the way up, and stopped at the top.
        Assert.Equal(4f * GameLoudness.Mixer, GameLoudness.Sfx(2f), 5);
        Assert.Equal(25f * GameLoudness.Mixer, GameLoudness.Sfx(9f), 5);
        Assert.Equal(2.5f * GameLoudness.Mixer, GameLoudness.Music(9f), 5);
    }

    [Fact]
    public void ALouderSoundComesOutLouder_PastFullScaleToo()
    {
        // The author, 1.7.0: changing the default volume made no difference to
        // Play above 1, because the player stopped every gain at 1. Measured on
        // the samples it hands the sound card.
        var tone = new AudioData(new[] { Enumerable.Range(0, 2205).Select(i => 0.1f * (float)Math.Sin(i * 0.1)).ToArray() }, 22050);
        float Peak(float volume)
        {
            var provider = Services.SfxPreviewPlayer.Provider(tone, volume, 0, out _);
            var buffer = new float[2205];
            int n = provider.Read(buffer, 0, buffer.Length);
            return buffer.Take(n).Max(Math.Abs);
        }
        float atOne = Peak(GameLoudness.Sfx(1f)), atTwo = Peak(GameLoudness.Sfx(2f)), atHalf = Peak(GameLoudness.Sfx(0.5f));
        _out.WriteLine($"peak at 0.5: {atHalf:0.0000}, 1: {atOne:0.0000}, 2: {atTwo:0.0000}");
        Assert.Equal(4.0, atTwo / atOne, 2);
        Assert.Equal(0.25, atHalf / atOne, 2);
    }

    [Fact]
    public void TheSoundsVolumeBoxAndSliderAgree_AndStartAtTheGamesLevel()
    {
        var sfx = new SfxViewModel(new SfxDef());
        Assert.Equal("1.0", sfx.DefaultVolumeText);
        Assert.Equal(1.0, sfx.DefaultVolumeValue);

        sfx.DefaultVolumeValue = 2.5;
        Assert.Equal(2.5f, sfx.Model.DefaultVolume);
        Assert.Equal("2.5", sfx.DefaultVolumeText);

        sfx.DefaultVolumeText = "0.5";
        Assert.Equal(0.5, sfx.DefaultVolumeValue, 3);
        Assert.Equal(0.5f, sfx.Model.DefaultVolume);

        // The game's own level is not a choice: nothing is stored for it.
        sfx.DefaultVolumeText = "1.0";
        Assert.Null(sfx.Model.DefaultVolume);
        sfx.DefaultVolumeText = "";
        Assert.Null(sfx.Model.DefaultVolume);
        // A box mid-edit is left as typed.
        sfx.DefaultVolumeText = "0.";
        Assert.Equal("0.", sfx.DefaultVolumeText);
    }

    [Fact]
    public void TheTracksVolumeReadsOneForTheGamesLevel_AndStoresWhatTheGameIsGiven()
    {
        var music = new MusicViewModel(new MusicDef());
        Assert.Equal("1.0", music.VolumeText);
        Assert.Equal(1.0, music.VolumeValue);

        // Twice the game's level for a track is its AudioSource at 1.0.
        music.VolumeValue = 2.0;
        Assert.Equal(1.0f, music.Model.Volume);
        Assert.Equal("2.0", music.VolumeText);

        music.VolumeText = "5";
        Assert.Equal(2.5f, music.Model.Volume);

        music.VolumeText = "1";
        Assert.Null(music.Model.Volume);

        // A pack saved before keeps its loudness and shows it the new way.
        Assert.Equal("1.6", new MusicViewModel(new MusicDef { Volume = 0.8f }).VolumeText);
    }

    [Fact]
    public void TheGamePlaysPastFullToo()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "SMSModForge.PackPlugin", "GameAudio.cs")))
            root = Path.GetDirectoryName(root)!;
        string Read(string f) => File.ReadAllText(Path.Combine(root, "SMSModForge.PackPlugin", f));

        // A sound effect above 1 is played from a louder copy, by the square.
        string audio = Read("GameAudio.cs");
        Assert.Contains("clip = Louder(clip, volume * volume);", audio);
        Assert.DoesNotContain("Mathf.Clamp01(volume)", audio);
        // A track above what its AudioSource goes to takes the rest from a gain
        // on its samples - and that file is built into the plugin.
        Assert.Contains("go.AddComponent<SoundGain>().Gain = volume;", Read("MusicFactory.cs"));
        Assert.Contains("<Compile Include=\"SoundGain.cs\" />", Read("SMSModForge.PackPlugin.csproj"));
    }

    private (MainViewModel Vm, Services.SfxPreviewPlayer Player) Open(ModPack pack)
    {
        new AudioData(new[] { new float[22050] }, 22050).WriteWav(Path.Combine(_dir, "Audio", "Plap.wav"));
        PackRepository.Save(pack, _dir);
        var vm = new MainViewModel();
        vm.OpenPackFromPath(_dir);
        var player = (Services.SfxPreviewPlayer)typeof(MainViewModel)
            .GetField("_sfxPreview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(vm)!;
        return (vm, player);
    }

    [Fact]
    public void ASoundEffectPreviewsAsLoudAsTheGamePlaysIt()
    {
        var pack = PackRepository.CreateEmpty("loud.pack");
        pack.Sfx.Add(new SfxDef { Key = "plap", DisplayName = "Plap", AudioPath = "Audio/Plap.wav", DefaultVolume = 0.5f });
        var (vm, player) = Open(pack);

        vm.PlaySfxCommand.Execute(vm.Sfx.Single());
        _out.WriteLine($"played at {player.LastVolume}");
        Assert.Equal(GameLoudness.Sfx(0.5f), player.LastVolume!.Value, 5);
        // The control: what it used to play at, which the game never did.
        Assert.NotEqual(0.5f, player.LastVolume!.Value, 2);
    }

    [Fact]
    public void AMusicTrackWithNoVolumePreviewsAtTheGamesTemplate()
    {
        var pack = PackRepository.CreateEmpty("loud.pack");
        pack.Music.Add(new MusicDef { Key = "theme", DisplayName = "Theme", AudioPath = "Audio/Plap.wav" });
        var (vm, player) = Open(pack);

        vm.PlayMusicCommand.Execute(vm.Music.Single());
        _out.WriteLine($"played at {player.LastVolume}");
        Assert.Equal(GameLoudness.Music(null), player.LastVolume!.Value, 5);
    }

    [Fact]
    public void TheSoundEditorPlaysAtTheSameLevel()
    {
        var sfx = new SfxDef { DefaultVolume = 0.7f };
        Assert.Equal(GameLoudness.Sfx(0.7f), EditableSound.Of(sfx).Volume, 5);
        var music = new MusicDef();
        Assert.Equal(GameLoudness.Music(null), EditableSound.Of(music).Volume, 5);
    }

    [Theory]
    [InlineData("Audio/Plap.wav", "Audio/Plap.wav")]
    [InlineData("Audio/Plap.OGG", "Audio/Plap.OGG")]
    [InlineData("Audio/Plap.mp3", "Audio/Plap.ogg")]   // nothing here writes an MP3
    public void AnEditReplacesItsFile_InTheSameFormatWhereItCan(string recording, string replacement)
        => Assert.Equal(replacement, SfxEdits.ReplacementFor(recording));

    [Fact]
    public void TheKeptRecordingsAreKnownByTheirFolder()
    {
        Assert.True(SfxEdits.IsKept(".originals/Audio/Plap.wav"));
        Assert.True(SfxEdits.IsKept(@".originals\Audio\Plap.wav"));
        Assert.False(SfxEdits.IsKept("Audio/Plap.wav"));
        Assert.False(SfxEdits.IsKept("Audio/.originals.wav"));
    }
}
