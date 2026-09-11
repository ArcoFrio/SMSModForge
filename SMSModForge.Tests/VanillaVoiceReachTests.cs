using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A change an author makes to one of the GAME's characters has to reach the
/// game's own scenes, not only the pack's conversations.
/// <para/>
/// Reported as "the typewriter pitch changes still sound the same and his name
/// is still the original blue" while the texture replacements on the same
/// character worked. They worked because they are painted onto the busts
/// standing in the scene; the voice and the colour were only ever attached to
/// the Actor the plugin synthesises for its own lines, and the game speaks its
/// characters through its own Actor assets and its own speech UI. So both
/// settings did exactly nothing anywhere except inside a pack-built dialogue —
/// which is indistinguishable, from the author's chair, from the setting being
/// ignored.
/// <para/>
/// The suite cannot start the game, so this holds the two ends it can reach:
/// that the editor writes precisely the fields the runtime looks for, and that
/// the runtime asks for them from its own load and frame loop rather than only
/// from the dialogue path. The engine call itself is settled by a compile probe
/// against the real assemblies (see CLAUDE.md) — the one that matters is that
/// GC2's <c>Typewriter</c> is a class, so a write through the Actor lands on
/// the Actor rather than on a boxed copy of it.
/// </summary>
public sealed class VanillaVoiceReachTests
{
    private readonly ITestOutputHelper _out;
    public VanillaVoiceReachTests(ITestOutputHelper o) => _out = o;

    /// <summary>One of the game's characters that also has a voice of their
    /// own, since those are the ones a pack can re-voice meaningfully.</summary>
    private static (ModPack Pack, CharacterViewModel Them) Seeded()
    {
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);

        var def = pack.Characters.First(c => !string.IsNullOrEmpty(c.VanillaCharacter)
                                             && VanillaSpeech.Has(c.Key));
        return (pack, new CharacterViewModel(def));
    }

    /// <summary>The character as the file on disk has them, or null when the
    /// prune decided the pack had nothing to say about them.</summary>
    private static JObject? Saved(ModPack pack, string key)
    {
        var root = JObject.Parse(PackRepository.SerializeAsSaved(pack));
        var characters = root["characters"] as JArray;
        if (characters == null) return null;
        return characters.OfType<JObject>().FirstOrDefault(c => (string?)c["key"] == key);
    }

    [Fact]
    public void ARevoicedCharacterIsWrittenExactlyAsTheRuntimeReadsThem()
    {
        // Four fields, and the runtime keys off every one of them:
        // bustSource picks the character out, typewriter's presence is what
        // says the author changed something, and the three numbers are the
        // voice. A rename on any of these is a setting that silently stops
        // arriving.
        var (pack, them) = Seeded();

        them.TypewriterFrequencyText = "40";
        them.TypewriterPitchMinText = "0.2";
        them.TypewriterPitchMaxText = "3.0";

        var written = Saved(pack, them.Key);
        Assert.NotNull(written);
        _out.WriteLine(written!.ToString());

        Assert.Equal("Vanilla", (string?)written["bustSource"]);

        var tw = written["typewriter"] as JObject;
        Assert.NotNull(tw);
        Assert.Equal(40, (int)tw!["frequency"]!);
        Assert.Equal(0.2f, (float)tw["pitchMin"]!, 3);
        Assert.Equal(3.0f, (float)tw["pitchMax"]!, 3);
    }

    [Fact]
    public void ACharacterNobodyRevoicedIsNotWrittenAtAll()
    {
        // The control for the one above, and the rule the runtime leans on:
        // the typewriter object's PRESENCE is the whole signal. A character
        // nobody touched does not reach the file at all - and were the editor
        // to write one for every one of the game's characters, the plugin
        // would restate the voice of the entire cast with the numbers they
        // already had, overwriting whatever the game changed later.
        var (pack, them) = Seeded();

        Assert.Null(Saved(pack, them.Key));

        // ...and one touch is enough to bring them back, carrying the object.
        them.TypewriterFrequencyText = "12";
        var written = Saved(pack, them.Key);
        Assert.NotNull(written);
        Assert.NotNull(written!["typewriter"]);
    }

    [Fact]
    public void TheColourIsWrittenWithTheNameThatGetsPainted()
    {
        // The colorizer matches on the text it sees in the speaker label,
        // which is the display name — so a colour without one beside it is a
        // colour with nothing to attach to.
        var (pack, them) = Seeded();
        them.NameColor = "#FF0000";

        var written = Saved(pack, them.Key);
        _out.WriteLine(written?.ToString() ?? "(not written at all)");

        Assert.NotNull(written);
        Assert.Equal("#FF0000", (string?)written!["nameColor"]);
        Assert.False(string.IsNullOrEmpty((string?)written["displayName"]),
                     "the colour is looked up by the name on screen; without one it lands nowhere");
        Assert.Equal(VanillaSpeech.For(them.Key)!.Name, (string?)written["displayName"]);
    }

    [Fact]
    public void TheDefaultsTheEditorShowsAreTheOnesTheRuntimeFallsBackTo()
    {
        // Both ends carry their own copy of these numbers — the runtime cannot
        // reference the editor's model — and the moment they disagree the Voice
        // panel is describing a voice nobody will hear. A character with no
        // typewriter of the game's own is the case that reaches them.
        string? runtime = FindPluginFile("RuntimeActorFactory.cs");
        if (runtime == null) { _out.WriteLine("plugin source not found - skipping"); return; }

        string text = File.ReadAllText(runtime);
        var fallback = new TypewriterDef();
        _out.WriteLine($"editor: {fallback.Frequency}, {fallback.PitchMin}-{fallback.PitchMax}");

        // Invariant, or this reads the plugin's source through whatever
        // decimal separator the machine running the suite happens to use.
        var c = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Contains(string.Format(c, "DefaultFrequency = {0};", fallback.Frequency), text);
        Assert.Contains(string.Format(c, "DefaultPitchMin = {0:0.0}f;", fallback.PitchMin), text);
        Assert.Contains(string.Format(c, "DefaultPitchMax = {0:0.0}f;", fallback.PitchMax), text);
    }

    [Fact]
    public void BothSettingsAreAskedForOutsideThePacksOwnDialogue()
    {
        // The actual defect, in the one form the suite can see it. Every other
        // assertion here would have passed while the bug was live: the editor
        // was writing the right fields and the runtime was reading them
        // correctly — into a place only a pack-built dialogue ever looked.
        //
        // So what is checked is REACHABILITY. The plugin's own load pass and
        // frame loop have to be among the callers, because those run whatever
        // the game is doing; a dialogue path does not.
        string? plugin = FindPluginFile("Plugin.cs");
        string? dispatcher = FindPluginFile("DialogueDispatcher.cs");
        if (plugin == null || dispatcher == null)
        { _out.WriteLine("plugin source not found - skipping"); return; }

        string text = File.ReadAllText(plugin);

        Assert.Contains("VanillaVoiceOverrides.ApplyAll(", text);
        Assert.Contains("VanillaVoiceOverrides.Tick(", text);
        Assert.Contains("SpeechColorApplier.Tick(", text);

        // ...and the control for that, so this cannot be satisfied by the
        // dialogue path alone: the applier used to be reachable from exactly
        // one place, and that place was the dispatcher.
        string only = File.ReadAllText(dispatcher);
        _out.WriteLine("dispatcher still applies per line: "
                       + only.Contains("SpeechColorApplier."));
        Assert.True(text.Contains("SpeechColorApplier."),
                    "the plugin's own loop never asks for speaker colours");
    }

    [Fact]
    public void WhatIsBorrowedIsGivenBack()
    {
        // Both of these write into things the GAME owns rather than anything
        // the plugin built — the speech skin's colour list (a prefab, which
        // outlives every scene) and the cast's own Actor assets. A pack that
        // has been unloaded must not still be speaking through them.
        string? plugin = FindPluginFile("Plugin.cs");
        if (plugin == null) { _out.WriteLine("plugin source not found - skipping"); return; }

        string text = File.ReadAllText(plugin);
        Assert.Contains("SpeechColorApplier.Forget();", text);
        Assert.Contains("VanillaVoiceOverrides.Restore();", text);
    }

    private static string? FindPluginFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "SMSModForge.PackPlugin", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
