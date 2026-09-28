using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SMSModForge.Localization;

namespace SMSModForge.Services.Translation;

/// <summary>
/// Sends text to the free translation endpoint.
/// <para/>
/// <b>What this is, said plainly.</b> It is the endpoint the translate web page
/// uses, not a supported API. It costs nothing and needs no key, which is why
/// tools have used it for years, and it comes with three things an author
/// should be told rather than discover:
/// <list type="bullet">
/// <item>It is rate limited by ADDRESS. A block lands on the person's own
/// connection, not on ModForge. Everything about how this sends — batching,
/// pausing, backing off, giving up — exists to stay well under that, and it is
/// in <see cref="Batches"/> and <see cref="Pacing"/> where it can be checked.</item>
/// <item>It can change or stop without notice, because nobody promised it
/// would not. When it does, this fails cleanly and the pack keeps whatever was
/// already translated.</item>
/// <item>It is not something Google supports being used this way.</item>
/// </list>
/// <para/>
/// <b>Which endpoint, and why - checked against the real service in September
/// 2026.</b> The batch endpoint, <c>translate_a/t</c>, answers with Google's
/// "unusual traffic" page whatever it is asked, and the client identifier most
/// tools send, <c>gtx</c>, is refused on every endpoint. Both of those are this
/// month's changes, seen by other projects as well as here. What works is
/// <c>translate_a/single</c> with <c>client=at</c>, which takes ONE text - so a
/// batch is sent as one text and divided again on the way back.
/// <para/>
/// <b>Dividing it is the fragile part, and nothing about it is documented.</b>
/// The service translates a batch as one document: it may merge two short
/// lines, break a long one, or move a word across the join. So a line is never
/// found by its position. Each goes out behind a numbered marker of its own
/// (<see cref="LineMarker"/>) - the same kind of marker <see cref="ProtectedText"/>
/// uses, which the real service was seen to keep intact even while reordering a
/// Spanish sentence around it - and each is claimed by that marker on the way
/// back. Three layers catch what can still go wrong:
/// <list type="number">
/// <item>A marker missing or doubled: the batch is halved and each half tried
/// again, down to one line, which cannot be misaligned because it is the whole
/// of its own answer.</item>
/// <item>A line whose translation is far out of proportion to the rest of its
/// batch, the sign of words moved across a marker: that line is retranslated
/// on its own (<see cref="Doubtful"/>).</item>
/// <item>A line that lost a piece of markup is not written at all -
/// <see cref="TranslationRun"/>'s check, which holds every engine to it.</item>
/// </list>
/// <para/>
/// What batching cannot rule out is context: a line translated among its
/// neighbours can read differently from the same line alone. Batches keep a
/// pack's lines in order, so the neighbours are usually the conversation the
/// line belongs to - which tends to help, with pronouns and tone - but it is a
/// difference, and it is why an author can always write over a line.
/// </summary>
public sealed class GoogleTranslator : IDisposable
{
    /// <summary>The single-text endpoint. See the type doc for why not the
    /// batch one.</summary>
    private const string Endpoint = "https://translate.googleapis.com/translate_a/single";

    /// <summary>The client identifier the endpoint still answers. <c>gtx</c>,
    /// the one most tools send, is refused as of September 2026.</summary>
    private const string Client = "at";

    /// <summary>
    /// The longest request URL this will send.
    /// <para/>
    /// A batch is joined into one text and that text goes in the query string,
    /// escaped - an accented letter becomes six characters and a Chinese one
    /// nine. Measured on the escaped URL rather than guessed from the words,
    /// because the words are no guide to it: the same batch in Chinese is
    /// several times the length it is in English.
    /// </summary>
    public const int LongestUrl = 6000;

    /// <summary>What stands in for a line break INSIDE a line while a batch is
    /// joined by line breaks. Digits between percent signs, the shape
    /// <see cref="ProtectedText"/>'s markers use and a real translator was seen
    /// to leave alone - and far past the numbers those markers take, so the two
    /// cannot be confused.</summary>
    public const string BreakMarker = "%%9000%%";

    private static readonly Regex BreakBack =
        new(@"%\s*%?\s*9000\s*%?\s*%", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Long enough for a slow connection, short enough that a run does not sit
    /// on a request that is never going to be answered — the usual sign of a
    /// block is silence rather than a refusal.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>The pause between the halves of a batch that came back
    /// misaligned, so splitting one request into several does not arrive as a
    /// burst.</summary>
    private static readonly TimeSpan BetweenHalves = TimeSpan.FromMilliseconds(400);

    /// <summary>Translate one joined text: the whole of a batch, markers and all.</summary>
    public delegate Task<string> Asker(string text, string? from, string to, CancellationToken cancel);

    private readonly HttpClient? _http;
    private readonly bool _owned;
    private readonly Asker _ask;
    private readonly Func<string, string?, string, bool> _tooLong;

    /// <summary>
    /// The cookies its requests carry, when it made its own connection - which
    /// is where what Google gives a person for passing its check goes
    /// (<see cref="TakeCookies"/>), so the requests after it are theirs.
    /// </summary>
    internal CookieContainer? Cookies { get; }

    /// <summary>The free endpoint, over <paramref name="http"/>.</summary>
    public GoogleTranslator(HttpClient? http = null)
    {
        if (http == null) Cookies = new CookieContainer();
        _http = http ?? new HttpClient(new HttpClientHandler { CookieContainer = Cookies!, UseCookies = true })
        {
            Timeout = Timeout,
        };
        _owned = http == null;
        _ask = (text, from, to, cancel) => Ask(Url(text, from, to), cancel);
        _tooLong = (text, from, to) => Url(text, from, to).Length > LongestUrl;
    }

    /// <summary>
    /// Another way of asking the same translator - the Google Translate web
    /// page (<see cref="GoogleWebPage"/>) - with everything that makes a batch
    /// safe kept: the markers, the halving, the retranslating of a line out
    /// of proportion. Only the asking differs.
    /// </summary>
    /// <param name="longestText">The longest joined text it takes at once.</param>
    public GoogleTranslator(Asker ask, int longestText)
    {
        _ask = ask ?? throw new ArgumentNullException(nameof(ask));
        _tooLong = (text, from, to) => text.Length > longestText;
    }

    public void Dispose() { if (_owned) _http?.Dispose(); }

    /// <summary>
    /// Send <paramref name="cookies"/> with every request from here on: what
    /// Google set in the browser a person passed its check in. Only Google's.
    /// </summary>
    public void TakeCookies(IEnumerable<Cookie>? cookies)
    {
        if (Cookies == null || cookies == null) return;
        foreach (var cookie in cookies)
        {
            string domain = (cookie.Domain ?? "").TrimStart('.');
            if (!domain.EndsWith("google.com", StringComparison.OrdinalIgnoreCase)
                && !domain.EndsWith("googleapis.com", StringComparison.OrdinalIgnoreCase))
                continue;
            try { Cookies.Add(cookie); }
            catch (CookieException) { /* one Google would not have sent here either */ }
        }
    }

    // ── Joining a batch into one text, and splitting it back ────────────

    /// <summary>
    /// The number the first line's marker carries. Well away from the numbers
    /// <see cref="ProtectedText"/>'s markers take (0 upward, one per piece of
    /// markup in a line) and from <see cref="BreakMarker"/>'s 9000, so the three
    /// kinds cannot be read as one another.
    /// </summary>
    public const int FirstLineMarker = 8000;

    /// <summary>The marker that starts line <paramref name="index"/> of a batch.</summary>
    public static string LineMarker(int index) => "%%" + (FirstLineMarker + index) + "%%";

    /// <summary>A line marker as it may come back: padded, a sign dropped.</summary>
    private static readonly Regex LineBack =
        new(@"%\s*%?\s*(8\d{3})\s*%?\s*%", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// One text for the whole batch.
    /// <para/>
    /// Each line starts with a marker of its own - <see cref="LineMarker"/> -
    /// and the lines are joined by line breaks, which keeps the translator
    /// treating them as separate sentences. A line break a line already had is
    /// turned into <see cref="BreakMarker"/> so it cannot be taken for the end of
    /// the line.
    /// </summary>
    public static string Join(IReadOnlyList<string> texts)
    {
        var made = new StringBuilder();
        for (int i = 0; i < texts.Count; i++)
        {
            if (i > 0) made.Append('\n');
            made.Append(LineMarker(i)).Append(' ');
            made.Append((texts[i] ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", BreakMarker));
        }
        return made.ToString();
    }

    /// <summary>
    /// The batch's lines out of one translated text, each found by its OWN
    /// marker; or null when any line's marker is missing or appears twice.
    /// <para/>
    /// By marker rather than by counting line breaks, and the difference is the
    /// whole point. A batch goes out as one document, and a translator is free
    /// to merge two short lines into one sentence or break a long one in two.
    /// Counting breaks catches that only when the count changes - merge one pair
    /// and split another and the count is right while every line after is filed
    /// under its neighbour's key. A marker names its line, so a merge leaves two
    /// markers in one row and each line still gets what follows its own.
    /// <para/>
    /// Null rather than a best guess when a marker is gone, because a guess is a
    /// line's words under another line's key - and the caller has something
    /// better to do than guess: send fewer lines at once.
    /// </summary>
    public static IReadOnlyList<string>? Split(string? translated, int expected)
    {
        if (translated == null || expected <= 0) return null;

        var found = LineBack.Matches(translated);
        var start = new int[expected];
        var end = new int[expected];
        var seen = new bool[expected];

        for (int m = 0; m < found.Count; m++)
        {
            int index = int.Parse(found[m].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                        - FirstLineMarker;

            // A marker for a line this batch does not have is not ours to read,
            // and one seen twice means the translator copied it - either way the
            // text cannot be divided with certainty.
            if (index < 0 || index >= expected || seen[index]) return null;
            seen[index] = true;

            start[index] = found[m].Index + found[m].Length;
            end[index] = m + 1 < found.Count ? found[m + 1].Index : translated.Length;
        }
        for (int i = 0; i < expected; i++) if (!seen[i]) return null;

        var lines = new string[expected];
        for (int i = 0; i < expected; i++)
        {
            string text = translated.Substring(start[i], end[i] - start[i]).Trim();
            lines[i] = BreakBack.Replace(text, "\n");
        }
        return lines;
    }

    // ── Sending ──────────────────────────────────────────────────────────

    /// <summary>
    /// Translate a batch. Matches <see cref="TranslationRun.Send"/>, so the run
    /// drives this exactly as it drives the fake one its tests use.
    /// </summary>
    public async Task<IReadOnlyList<string>> Send(IReadOnlyList<string> texts,
                                                  string? from, string to,
                                                  CancellationToken cancel)
    {
        if (texts == null || texts.Count == 0) return Array.Empty<string>();

        string joined = Join(texts);

        // Too long to send as one - halve it before sending rather than after
        // a refusal, since a URL over the limit is a request the service may
        // truncate and translate wrong instead of refusing.
        if (_tooLong(joined, from, to) && texts.Count > 1) return await Halves(texts, from, to, cancel);

        string answer = HalfWidthMarkers(await _ask(joined, from, to, cancel).ConfigureAwait(false));

        // One line is the whole of its own answer, whatever the translator did
        // to its marker - there is no neighbour for its words to belong to.
        if (texts.Count == 1)
        {
            var only = Split(answer, 1);
            return new[] { only != null ? only[0] : BreakBack.Replace(LineBack.Replace(answer, "").Trim(), "\n") };
        }

        var lines = Split(answer, texts.Count);

        // A marker went missing: the translator merged two lines past telling
        // apart. Fewer at once until they line up; at one line each, they must.
        if (lines == null) return await Halves(texts, from, to, cancel);

        // Every line was claimed by its own marker - but a translator can still
        // move words ACROSS a marker, which leaves one line short and its
        // neighbour long. Those are retranslated alone rather than trusted.
        var doubtful = Doubtful(texts, lines);
        if (doubtful.Count == 0) return lines;

        var fixedLines = new List<string>(lines);
        foreach (int i in doubtful)
        {
            await Task.Delay(BetweenHalves, cancel).ConfigureAwait(false);
            var alone = await Send(new[] { texts[i] }, from, to, cancel).ConfigureAwait(false);
            fixedLines[i] = alone[0];
        }
        return fixedLines;
    }

    /// <summary>
    /// A marker as Chinese and Japanese can write it: full-width percent signs
    /// (％) and digits (０-９) in place of the ones it was sent with.
    /// </summary>
    private static readonly Regex WideMarker =
        new(@"[%％]\s*[%％]?\s*([0-9０-９]{1,4})\s*[%％]?\s*[%％]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Every marker written back in the characters it was sent in.
    /// <para/>
    /// Chinese writes a percent sign full-width, and the Google Translate page
    /// hands back "％％8000％％" for the "%%8000%%" it was given - which no
    /// line could be read out of: every line of a Chinese batch was lost, and
    /// the Chinese spellings of names never arrived. Only a marker containing
    /// a full-width character is touched, so a line's own "50%" is left alone.
    /// </summary>
    public static string HalfWidthMarkers(string? text)
    {
        if (string.IsNullOrEmpty(text) || text!.IndexOf('％') < 0) return text ?? "";
        return WideMarker.Replace(text, m =>
        {
            bool wide = false;
            foreach (char c in m.Value) if (c == '％' || (c >= '０' && c <= '９')) { wide = true; break; }
            if (!wide) return m.Value;
            var digits = new StringBuilder();
            foreach (char c in m.Groups[1].Value)
                digits.Append(c >= '０' && c <= '９' ? (char)('0' + (c - '０')) : c);
            return "%%" + digits + "%%";
        });
    }

    /// <summary>
    /// The lines of a batch whose translation is out of proportion to the rest.
    /// <para/>
    /// Languages differ in length, so no single ratio is right - Spanish runs
    /// longer than English and Chinese much shorter. What stays steady within
    /// one batch is the ratio itself: a line whose translation is three times
    /// longer or shorter than the batch's usual has most likely had words moved
    /// in or out of it. Short lines are left out of it, since "Yes." against
    /// "Sí." says nothing about anything.
    /// <para/>
    /// Plus the plain case at any length: words went in, and none came out.
    /// </summary>
    public static List<int> Doubtful(IReadOnlyList<string> sent, IReadOnlyList<string> came)
    {
        var doubtful = new List<int>();
        var ratios = new List<(int index, double ratio)>();

        for (int i = 0; i < sent.Count; i++)
        {
            bool hadWords = TranslationRun.HasWords(sent[i]);
            if (hadWords && !TranslationRun.HasWords(came[i])) { doubtful.Add(i); continue; }
            if (sent[i].Length >= ShortestMeasured && came[i].Length > 0)
                ratios.Add((i, came[i].Length / (double)sent[i].Length));
        }

        // A usual ratio needs enough lines to be usual.
        if (ratios.Count >= 4)
        {
            var sorted = ratios.Select(r => r.ratio).OrderBy(r => r).ToList();
            double median = sorted[sorted.Count / 2];
            foreach (var (index, ratio) in ratios)
                if (ratio > median * OutOfProportion || ratio < median / OutOfProportion)
                    doubtful.Add(index);
        }

        doubtful.Sort();
        return doubtful;
    }

    /// <summary>How far from the batch's usual a line's length may be before
    /// it is retranslated alone.</summary>
    public const double OutOfProportion = 3.0;

    /// <summary>Lines shorter than this are not measured: at a few letters the
    /// ratio is noise.</summary>
    public const int ShortestMeasured = 12;

    private async Task<IReadOnlyList<string>> Halves(IReadOnlyList<string> texts, string? from, string to,
                                                     CancellationToken cancel)
    {
        int middle = texts.Count / 2;
        var first = new List<string>(texts.Count);
        var second = new List<string>(texts.Count - middle);
        for (int i = 0; i < texts.Count; i++) (i < middle ? first : second).Add(texts[i]);

        var result = new List<string>(texts.Count);
        result.AddRange(await Send(first, from, to, cancel).ConfigureAwait(false));
        await Task.Delay(BetweenHalves, cancel).ConfigureAwait(false);
        result.AddRange(await Send(second, from, to, cancel).ConfigureAwait(false));
        return result;
    }

    private static string Url(string text, string? from, string to)
        => Endpoint
           + "?client=" + Client
           + "&sl=" + Uri.EscapeDataString(string.IsNullOrEmpty(from) ? "auto" : from!)
           + "&tl=" + Uri.EscapeDataString(to ?? "en")
           + "&dt=t"
           + "&q=" + Uri.EscapeDataString(text);

    /// <summary>One request, and the translated text of its reply.</summary>
    private async Task<string> Ask(string url, CancellationToken cancel)
    {
        HttpResponseMessage reply;
        try
        {
            reply = await _http!.GetAsync(url, cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // The request timed out, which is what a block usually looks like
            // from this side. Worth waiting out rather than treating as final.
            throw new TranslationRun.ServiceRefused(0, Loc.T("packText.mt.timedOut"));
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationRun.ServiceRefused(0, ex.Message);
        }

        string body = await reply.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Read before the status is acted on, because the status alone cannot
        // tell a slow-down from a block: both are 429. The page is what says
        // which, and a block must not be retried. A redirect to Google's
        // "sorry" page is the same block by another route.
        bool sentToSorry = reply.Headers.Location?.AbsoluteUri.Contains("/sorry/") == true;
        if (sentToSorry || GoogleReply.IsBlockPage(body))
        {
            // Where a person can pass the check: the "sorry" page it was sent
            // to, or else the request itself, which shows the same page to a
            // browser on this connection.
            string? sorry = reply.Headers.Location?.AbsoluteUri;
            string? landed = reply.RequestMessage?.RequestUri?.AbsoluteUri;
            string check = sorry != null && sorry.Contains("/sorry/") ? sorry
                         : landed != null && landed.Contains("/sorry/") ? landed
                         : url;
            throw new TranslationRun.ServiceRefused((int)reply.StatusCode,
                                                    Loc.T("packText.mt.captcha"), blocked: true)
            {
                CheckAddress = check,
            };
        }

        if (!reply.IsSuccessStatusCode)
            throw new TranslationRun.ServiceRefused((int)reply.StatusCode, Explain(reply.StatusCode));

        var text = GoogleReply.Parse(body, 1);
        if (text == null)
        {
            // Not a refusal to retry: a reply that cannot be read is the same
            // reply however many times it is asked for. The run stops and keeps
            // what it has.
            throw new TranslationRun.ServiceRefused(
                (int)HttpStatusCode.BadRequest, Loc.T("packText.mt.unreadable"));
        }
        return text[0];
    }

    /// <summary>
    /// What a status means to somebody who is translating a pack, rather than
    /// what it means to a protocol.
    /// </summary>
    private static string Explain(HttpStatusCode status)
    {
        switch ((int)status)
        {
            case 429: return Loc.T("packText.mt.tooMany");
            case 403: return Loc.T("packText.mt.blocked");
            default:
                return Loc.F("packText.mt.answered", "status", (int)status + " " + status);
        }
    }
}
