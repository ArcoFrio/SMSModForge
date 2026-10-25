using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The packs the in-game checks of 1.8.0 play: off unless asked for -
/// <c>SMSMODFORGE_INGAME_MODS=&lt;the test copy's Mods folder&gt;</c> - and then
/// exported there the way an author's packs are. Played with Tools/Game
/// (launch.ps1, then bridge.ps1); what each should do is written beside it.
/// <list type="bullet">
///   <item><c>gamecheck</c>, dialogue <c>sounds</c>: a 440 Hz clink at 100 %,
///   150 % and 50 %, a line whose sound has no clip (nothing must be heard),
///   and an 880 Hz buzz at 200 %. A sound's loudness goes by the square of
///   its volume, so 150 % is 2.25 times 100 %, 50 % a quarter, 200 % four.</item>
///   <item><c>gamecheck</c>, dialogue <c>music</c>: a 330 Hz tone as three
///   tracks at 50 %, 100 % and 200 %, switched to one after another. A track's
///   volume is plain gain: half, the same, twice.</item>
///   <item><c>gamecheck</c>, dialogue <c>timing</c>: step 1, a 1 s Wait, step 2,
///   a fade to black held 1 s, step 3 once covered (1 s later), a 1.5 s Wait,
///   then black with words, step 4 once covered (0.75 s later).</item>
///   <item><c>gamecheck</c> in the game's own conversations: a line of the
///   player's added to <c>Annanotthere</c> with a buzz in it; Anna's line in
///   <c>Anna_DishesDecline</c> timed so it ends by itself.</item>
///   <item>Name colours: the pack's Tester in #FF8800; Anna in #FF00FF here
///   and #00C000 in <c>gameorder</c>, which loads after <c>gamecheck</c>
///   alphabetically - so Anna is green until the order is changed, and both
///   packs carry the clash warning on the main menu.</item>
/// </list>
/// </summary>
public sealed class InGameCheckPacksTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-gamecheck-" + Guid.NewGuid().ToString("N"));

    public InGameCheckPacksTests(ITestOutputHelper o) => _out = o;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const string NobodyHere = "8_Room_Talk/Kitchen/Annanotthere";
    private const string Dishes = "8_Room_Talk/Kitchen/Anna_DishesDecline";

    [Fact]
    public void ExportTheCheckPacks()
    {
        string? mods = Environment.GetEnvironmentVariable("SMSMODFORGE_INGAME_MODS");
        if (string.IsNullOrWhiteSpace(mods)) return;
        Directory.CreateDirectory(mods);
        Export(GameCheck(Path.Combine(_dir, "gamecheck")), Path.Combine(_dir, "gamecheck"), Path.Combine(mods, "gamecheck.smspack"));
        Export(GameOrder(), Path.Combine(_dir, "gameorder"), Path.Combine(mods, "gameorder.smspack"));
    }

    private void Export(ModPack pack, string folder, string target)
    {
        PackRepository.Save(pack, folder);
        var result = PackExporter.Export(folder, target);
        _out.WriteLine($"exported to {target}: {result}");
        Assert.True(File.Exists(target));
    }

    /// <summary>The sounds, music, timing and game-conversation pack; the
    /// in-game suite plays it too.</summary>
    internal static ModPack GameCheck(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "Audio"));
        SoundCueMigrationTests.Tone(Path.Combine(folder, "Audio", "clink.wav"), 0.5, hz: 440);
        SoundCueMigrationTests.Tone(Path.Combine(folder, "Audio", "buzz.wav"), 0.5, hz: 880);

        var pack = PackRepository.CreateEmpty("gamecheck");
        pack.PackId = "gamecheck";
        pack.Sfx.Add(new SfxDef { Key = "Clink", DisplayName = "Clink", AudioPath = "Audio/clink.wav", TextPatterns = new() { "*clink*" } });
        pack.Sfx.Add(new SfxDef { Key = "Buzz", DisplayName = "Buzz", AudioPath = "Audio/buzz.wav", TextPatterns = new() { "*buzz*" } });
        pack.Characters.Add(new CharacterDef { Key = "tester", DisplayName = "Tester", Pronouns = Pronouns.Neutral, NameColor = "#FF8800" });
        pack.Characters.Add(Anna("#FF00FF"));
        pack.Variables.Add(new PackVariableDef { Name = "step", Type = PackVariableType.Int, DefaultValue = "0", Persisted = false });

        static DialogueNodeDef Line(int id, string text, float seconds, params SoundCueDef[] cues)
        {
            var n = new DialogueNodeDef { Id = id, Actor = "tester", Text = text, Duration = NodeDurationMode.Timeout, Timeout = seconds };
            n.Cues.AddRange(cues);
            return n;
        }
        static void Chain(DialogueDef d)
        {
            for (int i = 0; i + 1 < d.Nodes.Count; i++) d.Nodes[i].Children.Add(d.Nodes[i + 1].Id);
            d.RootNodeIds.Add(d.Nodes[0].Id);
        }

        var sounds = new DialogueDef { Key = "sounds", DisplayName = "Sounds" };
        sounds.Nodes.Add(Line(1, "Plain *clink*", 1.5f));
        sounds.Nodes.Add(Line(2, "Loud *clink*", 1.5f, new SoundCueDef { At = 1, Pattern = "*clink*", Volume = 1.5f }));
        sounds.Nodes.Add(Line(3, "Quiet *clink*", 1.5f, new SoundCueDef { At = 1, Pattern = "*clink*", Volume = 0.5f }));
        sounds.Nodes.Add(Line(4, "No sound *zzz*", 1.5f));
        sounds.Nodes.Add(Line(5, "Very loud *buzz*", 1.5f, new SoundCueDef { At = 1, Pattern = "*buzz*", Volume = 2f }));
        Chain(sounds);
        pack.Dialogues.Add(sounds);

        static NodeActionDef Act(string type, params (string Key, string Value)[] p)
        {
            var a = new NodeActionDef { Type = type };
            foreach (var (k, v) in p) a.Params[k] = v;
            return a;
        }
        static NodeActionDef Step(int n) => Act(NodeActionTypes.SetVariable, ("name", "step"), ("value", n.ToString()));
        var timing = new DialogueDef { Key = "timing", DisplayName = "Timing" };
        var held = Line(1, "The line under the transitions.", 8f);
        held.ActionsOnStart.AddRange(new[]
        {
            Step(1),
            Act("Wait", ("seconds", "1")),
            Step(2),
            Act(Transitions.ActionType, (Transitions.StyleParam, Transitions.FadeToBlack), (Transitions.SecondsParam, "1")),
            Step(3),
            Act("Wait", ("seconds", "1.5")),
            Act(Transitions.ActionType, (Transitions.StyleParam, Transitions.TextScreen), (Transitions.SecondsParam, "1"),
                (Transitions.TextParam, "Later that day")),
            Step(4),
        });
        timing.Nodes.Add(held);
        Chain(timing);
        pack.Dialogues.Add(timing);

        // Music over 100% (a gain on the samples as they play): one 330 Hz
        // tone as three tracks, at half, the game's level and twice it, each
        // switched to by a line of its own.
        SoundCueMigrationTests.Tone(Path.Combine(folder, "Audio", "tone.wav"), 2.0, hz: 330);
        var music = new DialogueDef { Key = "music", DisplayName = "Music" };
        foreach (var (key, volume) in new[] { ("ToneHalf", 0.5f), ("ToneOne", 1f), ("ToneTwo", 2f) })
        {
            pack.Music.Add(new MusicDef { Key = key, DisplayName = key, AudioPath = "Audio/tone.wav", Loop = true, Volume = volume });
            var line = Line(music.Nodes.Count + 1, key, 2.5f);
            line.ActionsOnStart.Add(Act("SwitchMusic", ("music", key)));
            music.Nodes.Add(line);
        }
        Chain(music);
        pack.Dialogues.Add(music);

        // Only ever started by the bridge: a conversation with nothing to wait
        // for starts itself, and took over another check's.
        foreach (var d in new[] { sounds, timing, music })
            d.StartConditions.Add(new NodeConditionDef { Type = "VariableEquals", Params = { ["name"] = "step", ["value"] = "99" } });

        // One of the game's conversations, with a line of the pack's in it.
        var nobody = new DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
        nobody.VanillaSource = VanillaDialogueCatalog.Find(NobodyHere);
        var first = nobody.Model.Nodes.Single(n => n.Id == nobody.Model.RootNodeIds[0]);
        first.Duration = NodeDurationMode.Timeout;
        first.Timeout = 1.5f;
        var added = new DialogueNodeDef { Id = 900001, Actor = first.Actor, Text = "Then a *buzz* from upstairs.",
                                          Duration = NodeDurationMode.Timeout, Timeout = 2f };
        added.Children.AddRange(first.Children);
        first.Children.Clear();
        first.Children.Add(added.Id);
        nobody.Model.Nodes.Add(added);
        pack.Dialogues.Add(nobody.Model);

        var dishes = new DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
        dishes.VanillaSource = VanillaDialogueCatalog.Find(Dishes);
        var annas = dishes.Model.Nodes.Single(n => n.Id == dishes.Model.RootNodeIds[0]);
        annas.Duration = NodeDurationMode.Timeout;
        annas.Timeout = 3f;
        pack.Dialogues.Add(dishes.Model);
        return pack;
    }

    internal static ModPack GameOrder()
    {
        var pack = PackRepository.CreateEmpty("gameorder");
        pack.PackId = "gameorder";
        pack.Characters.Add(Anna("#00C000"));
        return pack;
    }

    /// <summary>The game's Anna as the editor adopts her, in a colour of
    /// the pack's.</summary>
    private static CharacterDef Anna(string colour)
    {
        var anna = VanillaCastSeed.Make(VanillaCharacters.Find("Anna")!);
        anna.NameColor = colour;
        return anna;
    }
}
