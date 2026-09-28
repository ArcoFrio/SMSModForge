using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The game's own lines inside a conversation a pack extends, as texts its
/// translations can have (the author's decision, 2026-09-24): translatable
/// while a translation is up, filled by the machine like anything else, and
/// never written into the pack - whose own words for them stay the game's.
/// </summary>
public sealed class GameLinesTests : IDisposable
{
    private const string Anna = "8_Room_Talk/Beach/AnnaBeachDefault";

    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-gamelines-" + Guid.NewGuid().ToString("N"));

    public GameLinesTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>A pack extending the beach conversation, saved.</summary>
    private (ModPack Pack, DialogueDef Beach) Pack()
    {
        Assert.True(VanillaDialogueCatalog.IsAvailable, "the game's extracted conversations should be beside the tests");
        var pack = PackRepository.CreateEmpty("gamelines.pack");
        var beach = VanillaDialogueSeed.Seed(Anna)!;
        beach.Key = "beach";
        pack.Dialogues.Add(beach);
        PackRepository.Save(pack, _dir);
        return (pack, beach);
    }

    /// <summary>The first of the conversation's game lines with plain words.</summary>
    private static DialogueNodeDef FirstLine(ModPack pack) => GameLines.Of(pack).First().Node;

    [Fact]
    public void TheGamesUntouchedLinesAreInTheTranslationFile_UnderAHeadingOfTheirOwn()
    {
        var (pack, beach) = Pack();
        var lines = GameLines.Of(pack);
        _out.WriteLine($"{lines.Count} game lines of {beach.Nodes.Count} nodes");
        Assert.NotEmpty(lines);

        var source = PackTranslations.Source(pack);
        var line = lines[0];
        var entry = source.Find(line.Key);
        Assert.NotNull(entry);
        Assert.Equal(line.Text, entry!.Text);
        Assert.Contains(Loc.F("packText.heading.gameLines", "name", beach.DisplayName), entry.Heading);

        // None of them is a text of the pack's: the pack saved nothing of the
        // conversation, because it changed nothing.
        var saved = JObject.Parse(PackRepository.SerializeAsSaved(pack));
        Assert.Empty(PackTexts.Of(saved, withEmpty: true).Where(s => s.Kind == PackTexts.Kind.Line));
    }

    [Fact]
    public void ALineThePackRewroteIsItsOwn_UnderTheSameKey()
    {
        var (pack, beach) = Pack();
        var node = FirstLine(pack);
        string key = PackTexts.LineKey(beach.Key, node.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string gameWords = node.Text;

        node.Text = "Nice day for the beach, isn't it?";
        Assert.DoesNotContain(GameLines.Of(pack), l => l.Key == key);

        var entry = PackTranslations.Source(pack).Find(key);
        Assert.NotNull(entry);
        Assert.Equal("Nice day for the beach, isn't it?", entry!.Text);
        // So a translation made from the game's words is now out of date.
        Assert.NotEqual(gameWords, entry.Text);
    }

    [Fact]
    public void ALineChangedInOtherWaysKeepsTheGamesWords()
    {
        var (pack, beach) = Pack();
        var node = FirstLine(pack);
        node.Tag = "changed";   // anything but its words
        PackRepository.Save(pack, _dir);

        var saved = JObject.Parse(File.ReadAllText(Path.Combine(_dir, "modpack.json")));
        var stored = ((JArray)((JObject)((JArray)saved["dialogues"]!)[0])["nodes"]!).OfType<JObject>()
                         .Single(n => (int)n["id"]! == node.Id);
        _out.WriteLine(stored.ToString(Newtonsoft.Json.Formatting.None));
        Assert.True(PackTexts.KeepsTheGamesText(stored));

        // Not an empty text of the pack's...
        Assert.DoesNotContain(PackTexts.Of(saved, withEmpty: true), s => s.Kind == PackTexts.Kind.Line);
        // ...but still one of the game's lines.
        Assert.Contains(GameLines.Of(pack),
                        l => l.Node == node && l.Key == PackTexts.LineKey("beach", node.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void TypingOverAGameLineInATranslation_WritesTheTranslation_NeverThePack()
    {
        var (pack, beach) = Pack();
        var node = FirstLine(pack);
        string gameWords = node.Text;
        string before = PackRepository.SerializeAsSaved(pack);

        var session = LanguageSession.Enter(pack, "es", null);
        node.Text = "¡Qué buen día para la playa!";

        using (session.OwnWords(pack))
            Assert.Equal(before, PackRepository.SerializeAsSaved(pack));

        var file = session.Translation(pack, null);
        var entry = file.Find(PackTexts.LineKey("beach", node.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.NotNull(entry);
        Assert.Equal("¡Qué buen día para la playa!", entry!.Text);
        Assert.Equal(gameWords, entry.English);

        session.Leave(pack, file);
        Assert.Equal(gameWords, node.Text);

        // The control: the same edit with no translation up IS the pack's -
        // so the check above could have failed.
        node.Text = "¡Qué buen día para la playa!";
        Assert.NotEqual(before, PackRepository.SerializeAsSaved(pack));
    }

    // ── What the player is told ──────────────────────────────────────

    /// <summary>They are always shown now; the player is told so, once, and
    /// asked nothing (the author, 2026-09-27).</summary>
    [Fact]
    public void TheNoticeNamesThePacksAndTheLanguage_AndAsksNothing()
    {
        var one = GameLineNotice.Notice(new[] { "Beach Days" }, "Español");
        _out.WriteLine(one.Plain());
        Assert.Equal(GameTexts.T("game.gameLines.title"), one.Title);
        string body = Assert.Single(one.Paragraphs);
        Assert.Contains(GameTexts.Quoted("Beach Days"), body);
        Assert.Contains("Español", body);
        Assert.Contains("it changes", body);

        var two = GameLineNotice.Notice(new[] { "Beach Days", "Night Market" }, "Español");
        Assert.Contains("they change", Assert.Single(two.Paragraphs));
        Assert.Empty(two.After);
    }

    [Fact]
    public async Task TheMachineTranslatesThemToo()
    {
        var (pack, _) = Pack();
        var lines = GameLines.Of(pack);
        string manifest = File.ReadAllText(Path.Combine(_dir, "modpack.json"));

        Task<IReadOnlyList<string>> Honest(IReadOnlyList<string> texts, string a, string b, CancellationToken c)
            => Task.FromResult<IReadOnlyList<string>>(texts.Select(t => "[es] " + t).ToList());

        var done = await PackTranslationJob.Run(pack, _dir, new[] { "es" }, Honest, (_, _) => Task.CompletedTask);
        _out.WriteLine($"{done[0].Translated} translated");

        var file = Loc.Read(PackTranslations.PathOf(_dir, "es"))!;
        // Every line with words in it came back from the machine; the ones
        // without ("...", "{PC}?") are never sent, as for the pack's own lines.
        var worded = lines.Where(l => TranslationRun.HasWords(ProtectedText.Protect(l.Text).Text)).ToList();
        Assert.NotEmpty(worded);
        Assert.All(worded, l => Assert.StartsWith("[es]", (file.Translated(l.Key) ?? "").TrimStart()));
        // And the pack itself did not change.
        Assert.Equal(manifest, File.ReadAllText(Path.Combine(_dir, "modpack.json")));
    }
}
