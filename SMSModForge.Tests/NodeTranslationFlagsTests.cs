using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Two ticks on a line (the author, 1.6.3): "same in every language", and
/// "keep the words, change only the letters" - for a made-up language that a
/// translator would read as real words, which instead is only spelled out in
/// the letters of a language with an alphabet of its own.
/// </summary>
public sealed class NodeTranslationFlagsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-nodeflags-" + Guid.NewGuid().ToString("N"));

    public NodeTranslationFlagsTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private const string Plain = "Hello there.";
    private const string Growl = "Grrr!";
    private const string Chant = "Vel'nar ithil, <b>ahn</b> {PC}.";

    private ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("flags.pack");
        var d = new DialogueDef { Key = "chat" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = Plain });
        d.Nodes.Add(new DialogueNodeDef { Id = 2, Text = Growl, TextSameEverywhere = true });
        d.Nodes.Add(new DialogueNodeDef { Id = 3, Text = Chant, TextLettersOnly = true });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _root);
        return pack;
    }

    private static string Key(int id) => PackTexts.LineKey("chat", id.ToString());

    // ── A translator that shows what it was sent ─────────────────────────

    private readonly List<string> _sent = new();

    private static readonly Dictionary<char, string> Cyrillic = new()
    {
        ['a'] = "а", ['b'] = "б", ['c'] = "к", ['d'] = "д", ['e'] = "е", ['f'] = "ф", ['g'] = "г", ['h'] = "х",
        ['i'] = "и", ['j'] = "й", ['k'] = "к", ['l'] = "л", ['m'] = "м", ['n'] = "н", ['o'] = "о", ['p'] = "п",
        ['r'] = "р", ['s'] = "с", ['t'] = "т", ['u'] = "у", ['v'] = "в", ['w'] = "в", ['y'] = "ы", ['z'] = "з",
    };

    private static string Spell(string word)
    {
        var sb = new StringBuilder();
        foreach (char c in word)
        {
            if (Cyrillic.TryGetValue(char.ToLowerInvariant(c), out var s))
                sb.Append(char.IsUpper(c) ? s.ToUpperInvariant() : s);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Russian, of a sort: a name framed as one is spelled letter for
    /// letter; anything else comes back marked as translated.</summary>
    private Task<IReadOnlyList<string>> Translator(IReadOnlyList<string> texts, string from, string to, CancellationToken c)
    {
        var answers = new List<string>();
        foreach (string text in texts)
        {
            _sent.Add(text);
            var name = Regex.Match(text, @"^My name is (.+)\.$");
            var hello = Regex.Match(text, @"^Hello, (.+)!$");
            if (to == "ru" && name.Success) answers.Add("Меня зовут " + Spell(name.Groups[1].Value) + ".");
            else if (to == "ru" && hello.Success) answers.Add("Привет, " + Spell(hello.Groups[1].Value) + "!");
            else answers.Add("[" + to + "] " + text);
        }
        return Task.FromResult<IReadOnlyList<string>>(answers);
    }

    private static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private string? Line(string code, int id) => Loc.Read(PackTranslations.PathOf(_root, code))?.Find(Key(id))?.Text;

    // ── What is a text to translate ──────────────────────────────────────

    [Fact]
    public void ALineSameEverywhereIsNoTextToTranslate_AndOnlyLettersIsMarked()
    {
        var sites = PackTexts.Of(JObject.Parse(PackRepository.SerializeAsSaved(Pack())));
        var lines = sites.Where(s => s.Kind == PackTexts.Kind.Line).ToDictionary(s => s.Key);
        Assert.True(lines.ContainsKey(Key(1)));
        Assert.False(lines.ContainsKey(Key(2)), "a line meant to read the same everywhere was offered for translating");
        Assert.True(lines[Key(3)].LettersOnly);
        Assert.False(lines[Key(1)].LettersOnly);
    }

    [Fact]
    public void TheGameNeverPutsATranslationOverALineSameEverywhere_EvenOneLeftInAnOlderFile()
    {
        var manifest = JObject.Parse(PackRepository.SerializeAsSaved(Pack()));
        var older = new TextFile();
        older.Add(new TextFile.Entry { Key = Key(1), Text = "Hallo.", English = Plain });
        older.Add(new TextFile.Entry { Key = Key(2), Text = "Knurr!", English = Growl });

        PackTexts.Apply(manifest, older);
        var nodes = manifest["dialogues"]![0]!["nodes"]!.ToDictionary(n => (int)n["id"]!, n => (string)n["text"]!);
        _out.WriteLine($"line 1: {nodes[1]}, line 2: {nodes[2]}");
        Assert.Equal("Hallo.", nodes[1]);
        Assert.Equal(Growl, nodes[2]);
    }

    [Fact]
    public void OffIsNotWrittenToThePack_OnIs()
    {
        var pack = Pack();
        var json = JObject.Parse(PackRepository.SerializeAsSaved(pack));
        var nodes = json["dialogues"]![0]!["nodes"]!.ToDictionary(n => (int)n["id"]!, n => (JObject)n);
        Assert.Null(nodes[1][PackTexts.SameEverywhereKey]);
        Assert.Null(nodes[1][PackTexts.LettersOnlyKey]);
        Assert.True((bool)nodes[2][PackTexts.SameEverywhereKey]!);
        Assert.True((bool)nodes[3][PackTexts.LettersOnlyKey]!);
    }

    // ── What the translating does with them ──────────────────────────────

    [Fact]
    public async Task InTheSameAlphabet_TheWordsAreLeftAsTheyAre_AndNeitherLineIsSent()
    {
        var pack = Pack();
        await PackTranslationJob.Run(pack, _root, new[] { "de" }, Translator, NoWait);
        foreach (var s in _sent) _out.WriteLine("sent: " + s);

        Assert.Equal("[de] " + Plain, Line("de", 1));
        Assert.Null(Line("de", 2));             // not a text of the translation at all
        Assert.Equal(Chant, Line("de", 3));     // as written
        Assert.DoesNotContain(_sent, s => s.Contains("Grrr") || s.Contains("ithil"));

        var de = PackTranslations.Summaries(pack, _root).Single(s => s.Code == "de");
        Assert.Equal(100, de.Percent);
        Assert.DoesNotContain(PackTranslations.CheckAll(pack, _root).Single(c => c.Code == "de").Result.Findings,
                              f => f.Kind == TextCheck.Kind.Untranslated);
    }

    [Fact]
    public async Task InAnotherAlphabet_EachWordIsSpelledOut_AndNeverTranslated()
    {
        var pack = Pack();
        await PackTranslationJob.Run(pack, _root, new[] { "ru" }, Translator, NoWait);
        foreach (var s in _sent) _out.WriteLine("sent: " + s);
        _out.WriteLine("ru line 3: " + Line("ru", 3));

        Assert.Equal("[ru] " + Plain, Line("ru", 1));
        Assert.Null(Line("ru", 2));
        // Word for word, in Cyrillic, with the tag, the gap and the
        // punctuation where they were.
        Assert.Equal("Вел'нар итхил, <b>ахн</b> {PC}.", Line("ru", 3));
        // Never sent as a sentence to translate: only its words, as names.
        Assert.DoesNotContain(_sent, s => s.Contains("Vel'nar ithil"));
        Assert.DoesNotContain(_sent, s => s.Contains("Grrr"));

        Assert.Equal(100, PackTranslations.Summaries(pack, _root).Single(s => s.Code == "ru").Percent);

        // Done: a second run has nothing to send.
        _sent.Clear();
        await PackTranslationJob.Run(pack, _root, new[] { "ru" }, Translator, NoWait);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task ALineNothingCouldBeSpelledFor_WaitsRatherThanReadingAsDone()
    {
        var pack = Pack();
        // A translator that spells nothing: every name comes back as it went.
        Task<IReadOnlyList<string>> Stubborn(IReadOnlyList<string> texts, string from, string to, CancellationToken c)
            => Task.FromResult<IReadOnlyList<string>>(texts.Select(t => t.StartsWith("My name is") || t.StartsWith("Hello,") ? t : "[ru] " + t).ToList());

        await PackTranslationJob.Run(pack, _root, new[] { "ru" }, Stubborn, NoWait);
        _out.WriteLine("ru line 3: " + Line("ru", 3));
        Assert.Equal(1, PackTranslationJob.StillToTranslate(pack, _root).Single(w => w.Code == "ru").Missing);
    }

    [Fact]
    public void AHandTranslatorIsToldToKeepTheWords()
    {
        var pack = Pack();
        var (path, _) = PackTranslations.CreateOrUpdate(pack, _root, "fr");
        string text = File.ReadAllText(path);
        _out.WriteLine(text);
        Assert.Contains(Loc.T("packText.note.lettersOnly"), text);
        Assert.DoesNotContain("Grrr", text);
    }

    [Fact]
    public void LeftAsWrittenByHand_InTheSameAlphabetItIsDone()
    {
        // A person translating the file does what its note says and leaves the
        // line alone - with nothing to mark it as meant, it is done all the same.
        var pack = Pack();
        var (path, _) = PackTranslations.CreateOrUpdate(pack, _root, "fr");
        var file = Loc.Read(path)!;
        // Everything else translated - the player's "You" included.
        foreach (var entry in file.Entries.Where(e => e.Key != Key(3))) entry.Text = "FR " + entry.Text;
        PackTranslations.Write(pack, _root, "fr", PackTranslations.Source(pack, file), file);
        Assert.False(Loc.Read(path)!.Find(Key(3))!.Same);

        var fr = PackTranslations.Summaries(pack, _root).Single(s => s.Code == "fr");
        _out.WriteLine($"fr: {fr.Translated} of {fr.Total}");
        Assert.Equal(100, fr.Percent);
        Assert.DoesNotContain(PackTranslations.CheckAll(pack, _root).Single(c => c.Code == "fr").Result.Findings,
                              f => f.Kind == TextCheck.Kind.Untranslated);
    }

    [Theory]
    [InlineData("Vel'nar ithil, <b>ahn</b> {PC}.", "Vel'nar|ithil|ahn")]
    [InlineData("[PV:Money] zar-khan zar", "zar-khan|zar")]
    [InlineData("Уже кириллица and latin", "and|latin")]
    public void TheWordsOfALine_NotWhatItsMarkupHolds(string line, string expected)
    {
        Assert.Equal(expected.Split('|'), LettersOnly.Words(new[] { line }));
    }

    // ── The ticks ────────────────────────────────────────────────────────

    [Fact]
    public void OfferedOnlyForWordsThatAreThePacks()
    {
        // A line of the game's left as it wrote it: its words are the game's,
        // and a tick there would not even be saved.
        var vm = new DialogueViewModel(new DialogueDef());
        vm.WantsVanilla = true;
        vm.VanillaSource = VanillaDialogueCatalog.Find("8_Room_Talk/Beach/AnnaBeachDefault");
        var row = vm.Nodes.First(n => n.Id == vm.Model.RootNodeIds[0]);
        Assert.False(row.TextIsThePacks);

        // Its words changed: now they are the pack's.
        row.Text = "Different words, the pack's own.";
        Assert.True(row.TextIsThePacks);

        // And a tick on it survives the save, which keeps only what changed.
        row.TextLettersOnly = true;
        var baseline = VanillaDialogueCatalog.Open(vm.Model.Source)!;
        var saved = VanillaDialogueDelta.Prune(vm.Model.Nodes, baseline).Single(n => n.Id == row.Id);
        Assert.True(saved.TextLettersOnly);

        // A conversation of the pack's own: every line.
        var own = new DialogueViewModel(new DialogueDef { Key = "mine", Nodes = { new DialogueNodeDef { Id = 1, Text = "Hi." } } });
        Assert.True(own.Nodes.Single().TextIsThePacks);
    }

    [Fact]
    public void TheTwoTicksAreOneAtATime()
    {
        var node = new DialogueNodeViewModel(new DialogueNodeDef { Id = 1, Text = "x" });
        node.TextLettersOnly = true;
        node.TextSameEverywhere = true;
        Assert.True(node.TextSameEverywhere);
        Assert.False(node.TextLettersOnly);
        node.TextLettersOnly = true;
        Assert.False(node.TextSameEverywhere);
        Assert.True(node.Model.TextLettersOnly);
    }
}
