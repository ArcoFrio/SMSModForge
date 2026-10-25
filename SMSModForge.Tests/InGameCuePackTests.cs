using System;
using System.IO;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The pack the in-game check of line sounds plays (1.8.0): off unless asked
/// for - <c>SMSMODFORGE_INGAME_MODS=&lt;the test copy's Mods folder&gt;</c> - and
/// then exported there as <c>cuecheck.smspack</c>, the way an author's pack
/// is. Played with Tools/Game (launch.ps1, then bridge.ps1 "dialogue cuecheck
/// chat"), the Debug plugin logs each sound's start and volume.
/// <list type="bullet">
///   <item>1: <c>*clink*</c> (0.5 s) then <c>*buzz*</c> set 0.5 s after it ends - expected at 0 and 1.0 s.</item>
///   <item>2: a <c>*clink*</c> that waits for the typewriter, 40 letters in, typed at 20 a second - about 2 s.</item>
///   <item>3: a hidden <c>*clink*</c> - the line shows "Hidden: gone".</item>
///   <item>4: a <c>*buzz*</c> at 150 %.</item>
/// </list>
/// </summary>
public sealed class InGameCuePackTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-cuecheck-" + Guid.NewGuid().ToString("N"));

    public InGameCuePackTests(ITestOutputHelper o) => _out = o;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void ExportTheCheckPack()
    {
        string? mods = Environment.GetEnvironmentVariable("SMSMODFORGE_INGAME_MODS");
        if (string.IsNullOrWhiteSpace(mods)) return;

        Directory.CreateDirectory(Path.Combine(_dir, "Audio"));
        SoundCueMigrationTests.Tone(Path.Combine(_dir, "Audio", "clink.wav"), 0.5);
        SoundCueMigrationTests.Tone(Path.Combine(_dir, "Audio", "buzz.wav"), 0.3);

        var pack = PackRepository.CreateEmpty("cuecheck");
        pack.PackId = "cuecheck";
        pack.Sfx.Add(new SfxDef { Key = "Clink", DisplayName = "Clink", AudioPath = "Audio/clink.wav", TextPatterns = new() { "*clink*" } });
        pack.Sfx.Add(new SfxDef { Key = "Buzz", DisplayName = "Buzz", AudioPath = "Audio/buzz.wav", TextPatterns = new() { "*buzz*" } });
        pack.Characters.Add(new CharacterDef
        {
            Key = "tester", DisplayName = "Tester", Pronouns = Pronouns.Neutral,
            Typewriter = new TypewriterDef { Enabled = true, Frequency = 20 },
        });

        var d = new DialogueDef { Key = "chat", DisplayName = "Cue check" };
        DialogueNodeDef Line(int id, string text, params SoundCueDef[] cues)
        {
            var n = new DialogueNodeDef { Id = id, Actor = "tester", Text = text, Duration = NodeDurationMode.Timeout, Timeout = 1.5f };
            n.Cues.AddRange(cues);
            return n;
        }
        d.Nodes.Add(Line(1, "Two: *clink* and *buzz*", new SoundCueDef { At = 2, Pattern = "*buzz*", Delay = 0.5f }));
        d.Nodes.Add(Line(2, "Typed: aaaa bbbb cccc dddd eeee ffff ggg *clink*", new SoundCueDef { At = 1, Pattern = "*clink*", Typed = true }));
        d.Nodes.Add(Line(3, "Hidden: *clink* gone", new SoundCueDef { At = 1, Pattern = "*clink*", Hidden = true }));
        d.Nodes.Add(Line(4, "Loud: *buzz*", new SoundCueDef { At = 1, Pattern = "*buzz*", Volume = 1.5f }));
        for (int i = 0; i < 3; i++) d.Nodes[i].Children.Add(i + 2);
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _dir);

        Directory.CreateDirectory(mods);
        string target = Path.Combine(mods, "cuecheck.smspack");
        var result = PackExporter.Export(_dir, target);
        _out.WriteLine($"exported to {target}: {result}");
        Assert.True(File.Exists(target));
    }
}
