using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Choosing the language on the game's main menu: what is offered, what each
/// language is called, and the warning about packs mostly not in it. The menu
/// itself is drawn in the game; everything it decides is decided here.
/// </summary>
public sealed class LanguageChoiceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    public LanguageChoiceTests(ITestOutputHelper o) => _out = o;

    public void Dispose() => GameTexts.Use(null!, null!);

    // ── What is offered ──────────────────────────────────────────────

    [Fact]
    public void EnglishComesFirst_ThenByName_EachOnce_AndPackOnlyLanguagesToo()
    {
        var offered = LanguageChoice.Offered(
            new[] { "en", "zh-Hans", "es", "pt-BR" },
            new[] { "ES", "it", "", "not a code!", "en", "ja" });
        _out.WriteLine(string.Join(", ", offered));

        Assert.Equal("en", offered[0]);
        // Spelled as the flag is, whichever way a pack spelled it.
        Assert.Contains("es", offered);
        Assert.DoesNotContain("ES", offered);
        // A language only a pack is in is still offered: that pack can be
        // played in it.
        Assert.Contains("it", offered);
        Assert.Equal(offered.Count, offered.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("not a code!", offered);
        Assert.DoesNotContain("", offered);

        var rest = offered.Skip(1).Select(c => LanguageChoice.NativeName(c) ?? c).ToList();
        Assert.Equal(rest.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), rest);
    }

    [Fact]
    public void EnglishIsOfferedEvenWhenNothingSaysIt()
    {
        Assert.Equal(new[] { "en" }, LanguageChoice.Offered(null!, null!));
        Assert.Contains("en", LanguageChoice.Offered(new[] { "es" }, null!));
    }

    // ── Names and flags ──────────────────────────────────────────────

    /// <summary>
    /// The game names each language as the editor's Language menu does, so a
    /// player and an author see one name for it. The editor's come from
    /// Windows; the game's runtime has an older list of its own, so the
    /// plugin's are written out - and checked here against Windows'.
    /// </summary>
    [Fact]
    public void EachLanguageIsNamedAsTheEditorNamesIt()
    {
        foreach (string code in LanguageChoice.Flagged)
        {
            string? windows = TranslationFiles.NativeName(code);
            _out.WriteLine($"{code}: {LanguageChoice.NativeName(code)} / {windows}");
            Assert.Equal(windows, LanguageChoice.NativeName(code));
        }
        Assert.Null(LanguageChoice.NativeName("it"));
    }

    [Fact]
    public void EveryFlagIsBuiltIn_AndEveryLanguageModForgeShipsHasOne()
    {
        string? folder = Beside("SMSModForge.PackPlugin", "Flags");
        Assert.NotNull(folder);
        var files = Directory.GetFiles(folder!, "*.png").Select(Path.GetFileNameWithoutExtension).ToList();
        _out.WriteLine(string.Join(", ", files));

        // Exactly the flagged ones: a picture nothing names would ship for nothing.
        Assert.Equal(LanguageChoice.Flagged.OrderBy(c => c, StringComparer.Ordinal),
                     files.OrderBy(c => c, StringComparer.Ordinal));
        foreach (string path in Directory.GetFiles(folder!, "*.png"))
        {
            var head = File.ReadAllBytes(path).Take(8).ToArray();
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, head);
        }

        string? languages = Beside("SMSModForge", "Languages");
        Assert.NotNull(languages);
        foreach (string code in Directory.GetFiles(languages!, "*.txt").Select(Path.GetFileNameWithoutExtension))
        {
            Assert.NotNull(LanguageChoice.FlagOf(code!));
            Assert.NotNull(LanguageChoice.NativeName(code!));
        }
        // The control: a language with no picture has no flag.
        Assert.Null(LanguageChoice.FlagOf("it"));
        Assert.Equal("pt-BR", LanguageChoice.FlagOf("PT-br"));
    }

    private static string? Beside(string project, string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, project, relative);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    // ── How much of a pack is in a language ──────────────────────────

    private const string FourLines = @"{
        ""packId"": ""beach"",
        ""dialogues"": [ { ""key"": ""beach"", ""nodes"": [
            { ""id"": 1, ""text"": ""Hello there!"" },
            { ""id"": 2, ""text"": ""The water is warm."" },
            { ""id"": 3, ""text"": ""See you tomorrow."" },
            { ""id"": 4, ""text"": ""Bye."" } ] } ]
    }";

    private static TextFile File_(params (string Key, string Text, string English)[] entries)
    {
        var file = new TextFile();
        foreach (var (key, text, english) in entries)
            file.Add(new TextFile.Entry { Key = key, Text = text, English = english });
        return file;
    }

    private static Dictionary<string, Func<TextFile>> Files(params (string Code, TextFile File)[] files)
        => files.ToDictionary(f => f.Code, f => (Func<TextFile>)(() => f.File), StringComparer.OrdinalIgnoreCase);

    /// <summary>One line translated, one translated from words the pack no
    /// longer says, one the same as the pack's.</summary>
    private static TextFile Spanish() => File_(
        ("dialogue.beach.1", "¡Hola!", "Hello there!"),
        ("dialogue.beach.2", "El agua está caliente.", "The water was warm."),
        ("dialogue.beach.4", "Bye.", "Bye."));

    [Fact]
    public void ItCountsAsTheLogCounts_AndLeavesThePackAlone()
    {
        var manifest = JObject.Parse(FourLines);
        string before = manifest.ToString();

        var coverage = LanguageChoice.Of("beach", manifest, "es", Files(("es", Spanish())));
        _out.WriteLine($"{coverage.Translated} of {coverage.Total}, {coverage.Percent}%");

        Assert.Equal(before, manifest.ToString());
        Assert.True(coverage.HasTranslation);
        Assert.False(coverage.Own);

        // What loading the pack does with the same file, which is what the
        // plugin logs: the warning and the log must agree.
        var applied = PackTexts.Apply((JObject)manifest.DeepClone(), Spanish());
        Assert.Equal(applied.Translated, coverage.Translated);
        Assert.Equal(applied.Total, coverage.Total);
        Assert.Equal(1, coverage.Translated);
        Assert.Equal(4, coverage.Total);
        Assert.Equal(25, coverage.Percent);
        Assert.False(coverage.IsEnough);
    }

    [Fact]
    public void APackWrittenInTheLanguageIsAllInIt()
    {
        var manifest = JObject.Parse(FourLines);
        manifest[PackTexts.LanguageField] = "pt-BR";

        var mine = LanguageChoice.Of("beach", manifest, "pt-BR", Files(("es", Spanish())));
        Assert.True(mine.Own);
        Assert.True(mine.IsEnough);
        Assert.Equal(100, mine.Percent);

        // The control: the same pack chosen in another language is not.
        var other = LanguageChoice.Of("beach", manifest, "ru", Files(("es", Spanish())));
        Assert.False(other.Own);
        Assert.False(other.HasTranslation);
        Assert.Equal(0, other.Translated);
        Assert.Equal(4, other.Total);
    }

    [Fact]
    public void OnlyTheTranslationThePackWillBePlayedInIsRead()
    {
        bool readFrench = false;
        var files = new Dictionary<string, Func<TextFile>>(StringComparer.OrdinalIgnoreCase)
        {
            ["es"] = Spanish,
            ["fr"] = () => { readFrench = true; return new TextFile(); },
        };
        LanguageChoice.Of("beach", JObject.Parse(FourLines), "es-MX", files);
        Assert.False(readFrench);
    }

    // ── The warning ──────────────────────────────────────────────────

    private static LanguageChoice.Coverage Pack(string name, int translated, int total, bool hasTranslation = true)
        => new() { Pack = name, Translated = translated, Total = total, HasTranslation = hasTranslation };

    [Fact]
    public void NoWarningWhenEveryPackIsAtLeastHalfInIt()
    {
        Assert.Null(LanguageChoice.Warning("Español", new[]
        {
            Pack("Half", 2, 4),
            Pack("Empty", 0, 0, hasTranslation: false),
            new LanguageChoice.Coverage { Pack = "Own", Own = true },
        }));
        Assert.Null(LanguageChoice.Warning("Español", new List<LanguageChoice.Coverage>()));
    }

    [Fact]
    public void TheWarningListsOnlyThePacksShortOfHalf()
    {
        var text = LanguageChoice.Warning("Español", new[]
        {
            Pack("Alpha", 999, 2000),
            Pack("Beta", 3, 4),
            Pack("Gamma", 0, 10, hasTranslation: false),
        });
        Assert.NotNull(text);
        _out.WriteLine(text!.Plain());

        Assert.Contains("Español", text.Title);
        var items = Assert.Single(text.Details).Items;
        Assert.Equal(2, items.Count);
        // 49.95% is short of half, and says so rather than rounding to 50.
        Assert.Equal("Alpha: 49%", items[0]);
        Assert.Equal("Gamma: not translated", items[1]);
        Assert.DoesNotContain(items, i => i.StartsWith("Beta", StringComparison.Ordinal));
        Assert.StartsWith("These packs", Assert.Single(text.Paragraphs));

        var one = LanguageChoice.Warning("Español", new[] { Pack("Beta", 1, 4) });
        Assert.StartsWith("This pack", Assert.Single(one!.Paragraphs));
    }
}
