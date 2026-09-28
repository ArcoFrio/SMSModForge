using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Google Translate web page as a second way in, used when Google's free
/// translation service fails its test sentence - and never used to get round a
/// block (the author's condition, 2026-09-24).
/// <para/>
/// Everything the page decides is tested here without a browser; the browser
/// itself is <see cref="LiveBrowserTranslationTests"/>, opt-in.
/// </summary>
public sealed class TranslationFallbackTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public TranslationFallbackTests(ITestOutputHelper o)
    {
        _out = o;
        _root = Path.Combine(Path.GetTempPath(), "smsmodforge-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    // ── The page ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("zh-Hant", "zh-TW")]
    [InlineData("pt-BR", "pt")]
    [InlineData("PT-br", "pt")]
    [InlineData("pt-PT", "pt-PT")]
    [InlineData("es", "es")]
    [InlineData("ja", "ja")]
    [InlineData("", "auto")]
    [InlineData(null, "auto")]
    [InlineData("auto", "auto")]
    public void ThePageIsAskedInItsOwnCodes(string? ours, string its)
        => Assert.Equal(its, GoogleWebPage.CodeFor(ours));

    [Fact]
    public void TheTextGoesInTheAddress_Whole()
    {
        string url = GoogleWebPage.Url("%%8000%% Hello, you & me?\n%%8001%% Bye", "en", "zh-Hans");
        _out.WriteLine(url);
        Assert.StartsWith("https://translate.google.com/?sl=en&tl=zh-CN&text=", url);
        // Nothing in the text can end it early or be read as another parameter.
        Assert.Contains("%25%258000%25%25", url);
        Assert.Contains("%26", url);
        Assert.Contains("%0A", url);
        Assert.EndsWith("&op=translate", url);
    }

    private static string FromBrowser(string host, string tl, bool blocked, bool consent, string? result)
        => JsonConvert.SerializeObject(JsonConvert.SerializeObject(new { host, tl, blocked, consent, result }));

    [Fact]
    public void WhatThePageSaysIsRead_AndWhatIsNotItIsNot()
    {
        var reading = GoogleWebPage.Read(FromBrowser("translate.google.com", "zh-CN", false, false, "%%8000%% 你好"));
        Assert.NotNull(reading);
        Assert.Equal("zh-CN", reading!.Tl);
        Assert.Equal("%%8000%% 你好", reading.Result);

        Assert.Null(GoogleWebPage.Read(null));
        Assert.Null(GoogleWebPage.Read("null"));
        Assert.Null(GoogleWebPage.Read("not json at all"));
    }

    [Fact]
    public void ATranslationIsTakenOnlyOnceItHasStoppedChanging()
    {
        var half = new GoogleWebPage.Reading("translate.google.com", "zh-CN", false, false, "%%8000%% 你");
        var whole = half with { Result = "%%8000%% 你好" };

        Assert.Equal(GoogleWebPage.State.Waiting, GoogleWebPage.Judge(null, "zh-Hans", null));
        Assert.Equal(GoogleWebPage.State.Waiting, GoogleWebPage.Judge(half with { Result = null }, "zh-Hans", null));
        Assert.Equal(GoogleWebPage.State.Waiting, GoogleWebPage.Judge(half, "zh-Hans", null));
        Assert.Equal(GoogleWebPage.State.Waiting, GoogleWebPage.Judge(whole, "zh-Hans", half.Result));
        Assert.Equal(GoogleWebPage.State.Done, GoogleWebPage.Judge(whole, "zh-Hans", whole.Result));
    }

    [Fact]
    public void AnotherLanguageABlockOrAQuestionIsNeverTakenForATranslation()
    {
        // The page does not refuse a code it does not know: it translates into
        // the last language used and says so in its address.
        var spanish = new GoogleWebPage.Reading("translate.google.com", "es", false, false, "%%8000%% Hola");
        Assert.Equal(GoogleWebPage.State.OtherLanguage, GoogleWebPage.Judge(spanish, "zh-Hans", spanish.Result));
        // The control: the same page, asked for Spanish, is done.
        Assert.Equal(GoogleWebPage.State.Done, GoogleWebPage.Judge(spanish, "es", spanish.Result));

        Assert.Equal(GoogleWebPage.State.Blocked,
                     GoogleWebPage.Judge(spanish with { Blocked = true }, "es", spanish.Result));
        Assert.Equal(GoogleWebPage.State.Consent,
                     GoogleWebPage.Judge(new GoogleWebPage.Reading("consent.google.com", "", false, true, null), "es", null));
    }

    [Fact]
    public async Task ThePageIsAskedInPiecesItTakes_AndEachLineComesBackToItsOwnKey()
    {
        var asked = new List<string>();
        Task<string> Page(string text, string? from, string to, CancellationToken cancel)
        {
            asked.Add(text);
            return Task.FromResult(text.Replace("Line", "Línea"));
        }

        var lines = Enumerable.Range(0, 20).Select(i => $"Line {i} is long enough to be measured like a sentence.").ToList();
        var translator = new GoogleTranslator(Page, 400);
        var got = await translator.Send(lines, "en", "es", CancellationToken.None);

        _out.WriteLine($"{asked.Count} page loads, longest {asked.Max(a => a.Length)} characters");
        Assert.True(asked.Count > 1, "twenty long lines should not fit one page");
        Assert.All(asked, a => Assert.True(a.Length <= 400, a.Length + " characters"));
        Assert.Equal(lines.Select(l => l.Replace("Line", "Línea")), got);
    }

    // ── When it is used ──────────────────────────────────────────────

    private ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("fallback.pack");
        var dialogue = new DialogueDef { Key = "chat" };
        for (int i = 0; i < 4; i++)
            dialogue.Nodes.Add(new DialogueNodeDef { Text = "Line number " + i + ", spoken aloud." });
        pack.Dialogues.Add(dialogue);
        PackRepository.Save(pack, _root);
        return pack;
    }

    private static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static Task<IReadOnlyList<string>> Working(IReadOnlyList<string> texts, string a, string b, CancellationToken c)
        => Task.FromResult<IReadOnlyList<string>>(texts.Select(t => "[es] " + t).ToList());

    /// <summary>Answers, but hands the text back untranslated - a service that
    /// has changed under ModForge.</summary>
    private static Task<IReadOnlyList<string>> Changed(IReadOnlyList<string> texts, string a, string b, CancellationToken c)
        => Task.FromResult<IReadOnlyList<string>>(texts.ToList());

    private static Task<IReadOnlyList<string>> Blocking(IReadOnlyList<string> texts, string a, string b, CancellationToken c)
        => throw new TranslationRun.ServiceRefused(429, "blocked for now", blocked: true);

    [Fact]
    public async Task WhenTheFreeServiceFailsItsTest_ThePageIsTriedAndUsed()
    {
        bool opened = false;
        var said = new List<string>();
        var pack = Pack();
        int texts = PackTranslations.Source(pack).Entries.Count(e => !string.IsNullOrWhiteSpace(e.Text));
        var done = await PackTranslationJob.Run(pack, _root, new[] { "es" }, Changed, NoWait,
            probeFirst: true,
            fallback: _ => { opened = true; return Task.FromResult<(TranslationRun.Send?, string?)>((Working, null)); },
            said: said.Add);

        var one = Assert.Single(done);
        _out.WriteLine($"{one.Translated} translated, by fallback {one.ByFallback}, stopped: {one.StoppedBecause}");
        Assert.True(opened);
        Assert.True(one.Finished);
        Assert.True(one.ByFallback);
        Assert.Equal(texts, one.Translated);
        Assert.Contains(Loc.T("packText.web.switching"), said);
        Assert.All(Loc.Read(PackTranslations.PathOf(_root, "es"))!.Entries.Where(e => e.Key.StartsWith("dialogue.")),
                   e => Assert.StartsWith("[es] ", e.Text));
    }

    [Fact]
    public async Task ABlockIsNeverWorkedAround()
    {
        bool opened = false;
        var done = await PackTranslationJob.Run(Pack(), _root, new[] { "es" }, Blocking, NoWait,
            probeFirst: true,
            fallback: _ => { opened = true; return Task.FromResult<(TranslationRun.Send?, string?)>((Working, null)); });

        var one = Assert.Single(done);
        Assert.False(opened, "a block must stop the run, not send the pack another way");
        Assert.Equal("blocked for now", one.StoppedBecause);
        Assert.Equal(0, one.Translated);
    }

    [Fact]
    public async Task APassingServiceNeverOpensThePage()
    {
        bool opened = false;
        var done = await PackTranslationJob.Run(Pack(), _root, new[] { "es" }, Working, NoWait,
            probeFirst: true,
            fallback: _ => { opened = true; return Task.FromResult<(TranslationRun.Send?, string?)>((Working, null)); });

        Assert.False(opened);
        Assert.False(done[0].ByFallback);
        Assert.True(done[0].Finished);
    }

    [Fact]
    public async Task WhenThePageFailsToo_BothReasonsAreGiven_AndNothingIsSent()
    {
        var done = await PackTranslationJob.Run(Pack(), _root, new[] { "es", "fr" }, Changed, NoWait,
            probeFirst: true,
            fallback: _ => Task.FromResult<(TranslationRun.Send?, string?)>((Changed, null)));
        _out.WriteLine(done[0].StoppedBecause);
        Assert.All(done, d =>
        {
            Assert.StartsWith(Loc.T("packText.mt.probeFailed"), d.StoppedBecause);
            Assert.Contains(Loc.F("packText.web.alsoFailed", "why", Loc.T("packText.mt.probeFailed")), d.StoppedBecause);
            Assert.Equal(0, d.Translated);
        });

        // And when the page could not even be opened, that is the reason given.
        var none = await PackTranslationJob.Run(Pack(), _root, new[] { "es" }, Changed, NoWait,
            probeFirst: true,
            fallback: _ => Task.FromResult<(TranslationRun.Send?, string?)>((null, Loc.T("packText.web.noRuntime"))));
        Assert.Contains(Loc.T("packText.web.noRuntime"), none[0].StoppedBecause);
    }
}

/// <summary>
/// The Google Translate page in the real browser control, against the real
/// page. Off unless asked for, like <see cref="LiveTranslationTests"/>:
/// <code>
///   set SMSMODFORGE_LIVE_TRANSLATE=1
///   dotnet test --filter LiveBrowserTranslationTests
/// </code>
/// </summary>
[Trait("Speed", "Slow")]   // a real browser and a real service; see CLAUDE.md
public sealed class LiveBrowserTranslationTests
{
    private readonly ITestOutputHelper _out;
    public LiveBrowserTranslationTests(ITestOutputHelper o) => _out = o;

    private bool Wanted()
    {
        if (Environment.GetEnvironmentVariable("SMSMODFORGE_LIVE_TRANSLATE") == "1") return true;
        _out.WriteLine("SMSMODFORGE_LIVE_TRANSLATE not set; nothing sent.");
        return false;
    }

    /// <summary>Wait for <paramref name="task"/> with the UI thread still
    /// turning, which the page needs to do anything at all.</summary>
    private static T Await<T>(Task<T> task, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!task.IsCompleted && DateTime.UtcNow < until) WindowHarness.Wait(TimeSpan.FromMilliseconds(50));
        Assert.True(task.IsCompleted, "no answer in " + timeout);
        return task.GetAwaiter().GetResult();
    }

    [Fact]
    public void ThePageTranslates_IntoTheLanguageAsked_AndRefusesOneItDoesNotKnow()
    {
        if (!Wanted()) return;

        WindowHarness.Run(main =>
        {
            var (window, whyNot) = Await(View.BrowserTranslatorWindow.Open(main), TimeSpan.FromSeconds(60));
            Assert.True(window != null, whyNot);
            try
            {
                var translator = new GoogleTranslator(window!.Ask, GoogleWebPage.LongestText);

                // The same test sentence every run starts with.
                var check = Await(TranslationRun.Check(translator.Send, "es"), TimeSpan.FromSeconds(90));
                _out.WriteLine("test sentence, Spanish: " + (check.Wrong ?? "passed"));
                Assert.Null(check.Wrong);

                // Chinese, which the page only knows as zh-CN.
                var lines = new[] { "Good morning.", "Where is the beach?", "Thank you very much." };
                var got = Await(translator.Send(lines, "en", "zh-Hans", CancellationToken.None), TimeSpan.FromSeconds(90));
                for (int i = 0; i < lines.Length; i++) _out.WriteLine($"{lines[i]}  ->  {got[i]}");
                Assert.Equal(lines.Length, got.Count);
                Assert.All(got, g => Assert.Contains(g, c => c >= 0x4E00 && c <= 0x9FFF));

                // The control: a code the page does not know must not come back
                // as a translation into whatever it used last.
                var wrong = Await(Task.Run(async () =>
                {
                    try { await translator.Send(new[] { "Good morning." }, "en", "xx-nope", CancellationToken.None); return (Exception?)null; }
                    catch (Exception ex) { return ex; }
                }), TimeSpan.FromSeconds(90));
                _out.WriteLine("unknown code: " + wrong?.Message);
                var refused = Assert.IsType<TranslationRun.ServiceRefused>(wrong);
                Assert.Equal(400, refused.Status);
            }
            finally
            {
                window?.Finished();
            }
        });
    }
}
