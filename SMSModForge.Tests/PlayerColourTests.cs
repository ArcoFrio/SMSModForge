using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The player's name is the game's grey, #B0B0B0, in every pack, and no pack
/// changes it (the author, 1.7.0): the game colours the label "You" by its
/// whole text, so a pack's colour for the player would repaint it in every
/// scene for as long as the pack is loaded.
/// </summary>
public sealed class PlayerColourTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-playercolour-" + Guid.NewGuid().ToString("N"));

    public PlayerColourTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Fact]
    public void TheEditorShowsTheGamesGrey_AndCannotChangeIt()
    {
        var vm = new CharacterViewModel(CharacterDef.NewPlayer());
        Assert.False(vm.CanEditNameColor);
        Assert.Equal("#B0B0B0", vm.NameColor);
        // Shown, not stored: nothing about the player's colour is written down.
        Assert.Null(vm.Model.NameColor);
    }

    [Fact]
    public void AColourAPackSetForThePlayerIsRemovedOnLoad_Reported_AndKeptInTheBackup()
    {
        PackRepository.Save(PackRepository.CreateEmpty("player.colour"), _root);

        // Written by hand, or by an editor from before the player was fixed.
        string path = Path.Combine(_root, PackRepository.ManifestFileName);
        var json = JObject.Parse(File.ReadAllText(path));
        if (json["characters"] is not JArray chars) json["characters"] = chars = new JArray();
        var player = chars.Children<JObject>().FirstOrDefault(c => (string?)c["key"] == "player");
        if (player == null) chars.Add(player = new JObject { ["key"] = "player", ["name"] = "You", ["displayName"] = "You", ["bustSource"] = "None" });
        player["nameColor"] = "#FF0000";
        File.WriteAllText(path, json.ToString());

        // Told, asked of the migration itself (Load's report is one per process).
        string said = ReportFor(path);
        Assert.Contains("name colour", said);

        var loaded = PackRepository.Load(_root);
        Assert.Null(loaded.Characters.Single(c => c.IsPlayer).NameColor);
        // The file is untouched until the author saves.
        Assert.Contains("#FF0000", File.ReadAllText(path));

        PackRepository.Save(loaded, _root);
        Assert.DoesNotContain("#FF0000", File.ReadAllText(path));
        // The original is kept beside it, once.
        var backups = Directory.GetFiles(_root).Where(f => PackMigration.IsBackup(f)).ToList();
        Assert.Single(backups);
        Assert.Contains("#FF0000", File.ReadAllText(backups[0]));

        // And nothing to say the next time.
        Assert.DoesNotContain("name colour", ReportFor(path));
    }

    [Fact]
    public void APackWithoutOneHasNothingToReport()
    {
        PackRepository.Save(PackRepository.CreateEmpty("player.colour"), _root);
        Assert.DoesNotContain("name colour", ReportFor(Path.Combine(_root, PackRepository.ManifestFileName)));
    }

    private static string ReportFor(string manifest)
    {
        var pack = Newtonsoft.Json.JsonConvert.DeserializeObject<ModPack>(File.ReadAllText(manifest))!;
        return PackMigration.Apply(pack).Describe();
    }

    // ── The game's side: what the plugin will put into the game's colours ──

    [Theory]
    [InlineData("player", "You", false)]        // the player
    [InlineData("PLAYER", "Somebody", false)]   // the player, whatever it is called in the file
    [InlineData("hope", "You", false)]          // shown as the player is: the game matches the label's text
    [InlineData("hope", " you ", false)]
    [InlineData("hope", "Hope", true)]          // anybody else is the pack's to colour
    [InlineData("anna", "Anna", true)]
    public void NoPackColourReachesThePlayersLabel(string key, string shown, bool applies)
        => Assert.Equal(applies, PlayerLabel.MayRecolour(key, shown));

    [Fact]
    public void ThePluginAsksBeforeItColoursAName()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "SMSModForge.PackPlugin", "Plugin.cs")))
            root = Path.GetDirectoryName(root)!;
        string plugin = File.ReadAllText(Path.Combine(root, "SMSModForge.PackPlugin", "Plugin.cs"));
        int at = plugin.IndexOf("RegisterColor(displayName", StringComparison.Ordinal);
        Assert.True(at > 0);
        // The registration is behind the check, in the same condition.
        string before = plugin.Substring(Math.Max(0, at - 400), 400);
        Assert.Contains("PlayerLabel.MayRecolour(", before);
    }
}
