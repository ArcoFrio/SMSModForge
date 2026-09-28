using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The player types the same way in every pack: 45 a second, pitch 0.4-0.7,
/// given by the author of SMSAndroids (2026-09-27). A pack's own setting for
/// them did nothing for the game's "You" and made one person type two ways.
/// </summary>
public sealed class PlayerVoiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-playervoice-" + Guid.NewGuid().ToString("N"));

    public PlayerVoiceTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Fact]
    public void AVoiceAPackSetForThePlayerIsRemovedOnLoad_Reported_AndNotWrittenAgain()
    {
        var pack = PackRepository.CreateEmpty("player.voice");
        PackRepository.Save(pack, _root);

        // As SMSAndroids has it.
        string path = Path.Combine(_root, PackRepository.ManifestFileName);
        var json = JObject.Parse(File.ReadAllText(path));
        // Only put in when missing: assigning a token already in place makes
        // Newtonsoft store a copy, and the edits below would land on nothing.
        if (json["characters"] is not JArray chars) json["characters"] = chars = new JArray();
        var player = chars.Children<JObject>().FirstOrDefault(c => (string?)c["key"] == "player");
        if (player == null) chars.Add(player = new JObject { ["key"] = "player", ["name"] = "You", ["displayName"] = "You", ["bustSource"] = "None" });
        player["typewriter"] = new JObject { ["template"] = "Custom", ["frequency"] = 45, ["pitchMin"] = 0.4, ["pitchMax"] = 0.7 };
        File.WriteAllText(path, json.ToString());
        Assert.Contains("\"pitchMin\"", File.ReadAllText(path));

        // What loading reports - asked of the migration itself, since the
        // report Load keeps is one for the whole process, and another test
        // loading a pack at the same moment would replace it.
        Assert.Contains("typing voice", ReportFor(path));
        var loaded = PackRepository.Load(_root);
        Assert.Null(loaded.Characters.Single(c => c.IsPlayer).Typewriter);
        // The file is untouched until the author saves.
        Assert.NotNull(JObject.Parse(File.ReadAllText(path))["characters"]!
                       .Children<JObject>().Single(c => (string?)c["key"] == "player")["typewriter"]);

        PackRepository.Save(loaded, _root);
        var savedPlayer = JObject.Parse(File.ReadAllText(path))["characters"]?
            .Children<JObject>().FirstOrDefault(c => (string?)c["key"] == "player");
        Assert.Null(savedPlayer?["typewriter"]);

        // And nothing to report the next time.
        Assert.DoesNotContain("typing voice", ReportFor(path));
    }

    private static string ReportFor(string manifest)
    {
        var pack = Newtonsoft.Json.JsonConvert.DeserializeObject<ModPack>(File.ReadAllText(manifest))!;
        return PackMigration.Apply(pack).Describe();
    }

    [Fact]
    public void ThePlayersPanelShowsTheOneVoice_AndCannotChangeIt()
    {
        var vm = new CharacterViewModel(CharacterDef.NewPlayer());
        Assert.False(vm.CanEditVoice);
        Assert.Equal("45", vm.TypewriterFrequencyText);
        Assert.Equal("0.4", vm.TypewriterPitchMinText);
        Assert.Equal("0.7", vm.TypewriterPitchMaxText);

        // Anybody else is still theirs to voice.
        Assert.True(new CharacterViewModel(new CharacterDef { Key = "hope", DisplayName = "Hope" }).CanEditVoice);
    }
}
