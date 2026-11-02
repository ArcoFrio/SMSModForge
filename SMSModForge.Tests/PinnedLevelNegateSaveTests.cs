using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A dialogue's pinned level negated - "anywhere but this level" (the author,
/// 2026-11-01) - saves as the plugin reads it, and has no room talk of that
/// level to take priority over.
/// </summary>
public sealed class PinnedLevelNegateSaveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SMSModForge-negatedlevel-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static DialogueDef Dialogue(string key, bool negate)
    {
        var d = new DialogueDef { Key = key, DisplayName = key, DisableVanillaTrigger = true };
        d.StartConditions.Add(new NodeConditionDef
        {
            Type = NodeConditionTypes.LevelActive, Negate = negate, Params = { ["level"] = "vanilla:59_Gasstation" },
        });
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Hi." });
        d.RootNodeIds.Add(1);
        return d;
    }

    [Fact]
    public void A_negated_level_saves_negated_and_with_no_room_talk()
    {
        var pack = PackRepository.CreateEmpty("negated");
        pack.Dialogues.Add(Dialogue("elsewhere", negate: true));
        pack.Dialogues.Add(Dialogue("there", negate: false));   // control
        PackRepository.Save(pack, _root);

        var saved = JObject.Parse(File.ReadAllText(Path.Combine(_root, PackRepository.ManifestFileName)));
        JObject Saved(string key) => (JObject)((JArray)saved["dialogues"]!).Single(d => (string?)d["key"] == key);

        var elsewhere = Saved("elsewhere");
        var level = (JObject)((JArray)elsewhere["startConditions"]!)[0];
        Assert.Equal("LevelActive", (string?)level["type"]);
        Assert.True((bool?)level["negate"]);
        Assert.Null(elsewhere["roomTalk"]);

        var there = Saved("there");
        Assert.Null(((JObject)((JArray)there["startConditions"]!)[0])["negate"]);
        Assert.Equal("vanilla:Gasstation", (string?)there["roomTalk"]);

        // Read back, as the editor opens it.
        var back = PackRepository.Load(_root).Dialogues.Single(d => d.Key == "elsewhere");
        Assert.True(back.LevelNegated);
        Assert.False(back.VanillaRoomTalkAvailable);
        Assert.Equal("vanilla:59_Gasstation", back.LevelToken);
    }
}
