using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// An older pack's lines given their sounds' settings as it is opened (1.8.0,
/// the author: "Font sizes and spacing should stay as they are already set
/// for the packs, but the migrations should make the appropriate changes to
/// their SFX entries"): a pattern written bigger plays louder, and a line of
/// several sounds keeps playing each when it did.
/// </summary>
public sealed class SoundCueMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-cuemig-" + Guid.NewGuid().ToString("N"));

    public SoundCueMigrationTests() => Directory.CreateDirectory(Path.Combine(_dir, "Audio"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>A plain tone of <paramref name="seconds"/>, as a sound file.</summary>
    internal static void Tone(string file, double seconds, int rate = 44100, double hz = 440)
    {
        int frames = (int)(seconds * rate);
        using var w = new BinaryWriter(File.Create(file));
        w.Write("RIFF"u8.ToArray()); w.Write(36 + frames * 2); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(frames * 2);
        for (int i = 0; i < frames; i++) w.Write((short)(Math.Sin(i * 2 * Math.PI * hz / rate) * 12000));
    }

    private static readonly string[] Lines =
    {
        "<size=140%>*clink*",     // written bigger: louder
        "*clink* *clink*",        // the same sound twice: spaced by the characters between
        "*clink* and *buzz*",     // two sounds: they started together
        "plain *buzz*",           // one sound as set: nothing to write
    };

    /// <summary>A pack as an older ModForge wrote it.</summary>
    private ModPack Older(string stamp = "1.7.0")
    {
        Tone(Path.Combine(_dir, "Audio", "clink.wav"), 0.5);
        Tone(Path.Combine(_dir, "Audio", "buzz.wav"), 0.3);
        var pack = PackRepository.CreateEmpty("cuemig.pack");
        pack.Sfx.Add(new SfxDef { Key = "Clink", DisplayName = "Clink", AudioPath = "Audio/clink.wav", TextPatterns = new() { "*clink*" } });
        pack.Sfx.Add(new SfxDef { Key = "Buzz", DisplayName = "Buzz", AudioPath = "Audio/buzz.wav", TextPatterns = new() { "*buzz*" } });
        var d = new DialogueDef { Key = "chat", DisplayName = "Chat" };
        for (int i = 0; i < Lines.Length; i++) d.Nodes.Add(new DialogueNodeDef { Id = i + 1, Text = Lines[i] });
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _dir);

        // The stamp an older editor left, whatever this one stamps now.
        string manifest = Path.Combine(_dir, PackRepository.ManifestFileName);
        var json = JObject.Parse(File.ReadAllText(manifest));
        json["forgeVersion"] = stamp;
        File.WriteAllText(manifest, json.ToString());
        return PackRepository.Load(_dir);
    }

    private static SoundCueDef? Cue(ModPack pack, int line, int at)
        => pack.Dialogues[0].Nodes[line].Cues.FirstOrDefault(c => c.At == at);

    [Fact]
    public void ABiggerPatternPlaysLouderAndTheLinesKeepTheirTiming()
    {
        var pack = Older();   // opening the pack migrates it, as the editor's opening does
        var report = PackRepository.LastMigration!;

        Assert.Equal(1.4f, Cue(pack, 0, 1)!.Volume);
        Assert.Equal("<size=140%>*clink*", pack.Dialogues[0].Nodes[0].Text);   // the size stays

        // The second *clink* played 0.22 s after the first (one character
        // between); the first lasts 0.5 s: 0.28 s before it ends.
        Assert.Equal(-0.28f, Cue(pack, 1, 2)!.Delay!.Value, 3);
        // *buzz* played with *clink*: as it began, 0.5 s before it ended.
        Assert.Equal(-0.5f, Cue(pack, 2, 2)!.Delay!.Value, 3);
        Assert.Empty(pack.Dialogues[0].Nodes[3].Cues);                       // the control: nothing to say

        Assert.Contains(SMSModForge.Localization.Loc.T("migration.cueVolumes"), report.Describe());
        Assert.Contains(SMSModForge.Localization.Loc.T("migration.cueTiming"), report.Describe());
    }

    [Fact]
    public void WhatPlaysNowStartsWhenItDidBefore()
    {
        var pack = Older();
        PackMigration.Apply(pack, _dir);
        var patterns = SMSModForge.Services.Audio.LineSounds.Patterns(pack);
        var lengths = new Dictionary<string, double> { ["Clink"] = 0.5, ["Buzz"] = 0.3 };
        foreach (var (line, expected) in new[] { (1, new[] { 0, 0.22 }), (2, new[] { 0.0, 0.0 }) })
        {
            var node = pack.Dialogues[0].Nodes[line];
            var starts = SoundCues.Plan(SoundCues.Find(node.Text, patterns), node.Cues.Select(c => c.ToSetting()).ToList(),
                                        m => lengths[m.Sound], _ => 0);
            Assert.Equal(expected, starts.Select(s => Math.Round(s, 3)));
        }
    }

    [Fact]
    public void ItIsDoneOnceAndNotForANewerPack()
    {
        var pack = Older();
        PackMigration.Apply(pack, _dir);
        string once = PackRepository.SerializeAsSaved(pack);
        PackMigration.Apply(pack, _dir);
        Assert.Equal(once, PackRepository.SerializeAsSaved(pack));

        var newer = Older("1.8.0");
        PackMigration.Apply(newer, _dir);
        Assert.All(newer.Dialogues[0].Nodes, n => Assert.Empty(n.Cues));
    }

    [Fact]
    public void TheSettingsReachTheSavedFile()
    {
        var pack = Older();
        PackMigration.Apply(pack, _dir);
        PackRepository.Save(pack, _dir);
        var json = JObject.Parse(File.ReadAllText(Path.Combine(_dir, PackRepository.ManifestFileName)));
        var nodes = (JArray)json["dialogues"]![0]!["nodes"]!;
        Assert.Equal(1.4, (double)nodes[0]["cues"]![0]!["volume"]!, 3);
        Assert.Null(nodes[3]["cues"]);   // a line with nothing to say has no entry at all
        // What the game reads: the same settings.
        var setting = SoundCues.Read(nodes[2]["cues"]).Single();
        Assert.Equal(2, setting.At);
        Assert.Equal("*buzz*", setting.Pattern);
    }
}
