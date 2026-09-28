using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SMSModForge.Localization;

namespace SMSModForge.Services.Translation;

/// <summary>
/// Translating a pack's text, one batch at a time, keeping what it gets.
/// <para/>
/// This is the part worth being careful about, and it is deliberately separate
/// from anything that touches a network. What it decides:
/// <list type="bullet">
/// <item>Which lines go in which request (<see cref="Batches"/>).</item>
/// <item>How long to wait, and when to stop asking (<see cref="Pacing"/>).</item>
/// <item>That every line's markup survives, and that a line whose markup did
/// not survive is thrown away rather than written (<see cref="ProtectedText"/>).</item>
/// <item>That what has been translated is handed back AS IT ARRIVES, so a run
/// that is cut off - a rate limit, a closed lid, a lost connection - keeps
/// everything up to that point.</item>
/// </list>
/// <para/>
/// <b>Resuming is not a mechanism, it is a consequence.</b> Results are written
/// to the pack's translation file as they come, and the next run is given only
/// the lines that are still empty. So a run that stops halfway is simply a
/// shorter run next time; there is no progress file to go stale, and nothing to
/// clean up when somebody gives up and translates the rest by hand.
/// <para/>
/// Sending and waiting are handed in rather than done here. Tests drive the
/// whole thing against a translator that behaves badly - drops markers, refuses
/// service, returns the wrong number of lines - without a network or a clock.
/// </summary>
public sealed class TranslationRun
{
    /// <summary>One line to translate, and where it belongs.</summary>
    public sealed record Line(string Key, string Text);

    /// <summary>What happened to one line.</summary>
    public sealed record Result(string Key, string Text, string? Trouble)
    {
        /// <summary>Whether this is fit to write. A line with trouble is not a
        /// rough translation, it is a damaged one.</summary>
        public bool Usable => Trouble == null;
    }

    /// <summary>
    /// Send one batch. Returns a line per line sent, in order. Throws to say
    /// the service refused, carrying the HTTP status in
    /// <see cref="ServiceRefused.Status"/>.
    /// </summary>
    public delegate Task<IReadOnlyList<string>> Send(IReadOnlyList<string> texts,
                                                     string from, string to,
                                                     CancellationToken cancel);

    /// <summary>Wait. Handed in so tests do not spend real seconds.</summary>
    public delegate Task Wait(TimeSpan howLong, CancellationToken cancel);

    /// <summary>The service declined to answer.</summary>
    public sealed class ServiceRefused : Exception
    {
        public int Status { get; }

        /// <summary>
        /// The service has stopped serving this connection altogether, rather
        /// than asking it to slow down. Never retried, whatever the status
        /// says: see <see cref="GoogleReply.IsBlockPage"/>.
        /// </summary>
        public bool Blocked { get; }

        /// <summary>
        /// Where a person can pass the check Google wants before it serves this
        /// connection again - its "sorry" page, with the captcha on it - or
        /// null when there is none to pass.
        /// </summary>
        public string? CheckAddress { get; init; }

        public ServiceRefused(int status, string message, bool blocked = false) : base(message)
        {
            Status = status;
            Blocked = blocked;
        }
    }

    /// <summary>How showing Google's check to a person ended.</summary>
    public enum CheckOutcome
    {
        /// <summary>They passed it, and what Google gave them for it has been
        /// handed to the translator.</summary>
        Passed,

        /// <summary>They stopped, or it could not be shown.</summary>
        NotPassed,

        /// <summary>
        /// Google's page had nothing to answer - only "try again later". Seen on
        /// the free service: its refusal page carries no captcha, so no person
        /// can lift it, and waiting is the only thing that does.
        /// </summary>
        NothingToAnswer,
    }

    /// <summary>
    /// Show Google's check to the person and wait for them. ModForge never
    /// answers a check itself - that is what this is for.
    /// </summary>
    public delegate Task<CheckOutcome> AskAPerson(string checkAddress, CancellationToken cancel);

    /// <summary>
    /// Who to ask when the service stops to check for a person in the middle of
    /// a run, or null to stop the run there, as before. Asked once per block:
    /// refused again straight after a check was passed, the run stops.
    /// </summary>
    public AskAPerson? Person { get; init; }

    /// <summary>
    /// Another way in to the same translator - the Google Translate website -
    /// for when the service refuses and gives a person nothing to answer. Null
    /// when there is none; the run then stops, as before. Once switched to, the
    /// rest of the run uses it (<see cref="SwitchedTo"/>).
    /// </summary>
    public Func<CancellationToken, Task<Send?>>? Instead { get; init; }

    /// <summary>What the run went on through after switching, or null.</summary>
    public Send? SwitchedTo { get; private set; }

    private Send _send;
    private readonly Wait _wait;
    private readonly Random _random;

    public TranslationRun(Send send, Wait wait, Random random = null)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _wait = wait ?? throw new ArgumentNullException(nameof(wait));
        _random = random ?? new Random();
    }

    /// <summary>How many batches were sent, and how many were refused. For the
    /// progress the editor shows and for the log.</summary>
    public int Sent { get; private set; }
    public int Refused { get; private set; }

    /// <summary>
    /// Why the run ended early, or null when it finished everything it was
    /// given. A run that stops has still handed back everything it managed.
    /// </summary>
    public string? StoppedBecause { get; private set; }

    /// <summary>
    /// Translate <paramref name="lines"/>, reporting each batch's results
    /// through <paramref name="done"/> as they arrive.
    /// <para/>
    /// Returns everything it managed, whether or not it finished. There is no
    /// failure return: a run that translated four thousand lines and then hit a
    /// rate limit has done four thousand lines of work, and throwing that away
    /// to report an error would be the worst of both.
    /// </summary>
    /// <param name="names">Character names to keep out of the translation
    /// (<see cref="PackNames"/>), or null to send them like any other word.</param>
    /// <param name="spellings">How each of those is written in <paramref name="to"/>;
    /// a name it does not have goes back as it was written.</param>
    /// <param name="who">Who says each line and how each kept name is spoken
    /// of, for a language whose words change with it (<see cref="GenderHints"/>);
    /// null to send the lines without.</param>
    public async Task<IReadOnlyList<Result>> Go(IReadOnlyList<Line>? lines, string from, string to,
                                                Action<IReadOnlyList<Result>>? done = null,
                                                CancellationToken cancel = default,
                                                IReadOnlyList<string>? names = null,
                                                IReadOnlyDictionary<string, string>? spellings = null,
                                                GenderHints.Who? who = null)
    {
        var all = new List<Result>();
        if (lines == null || lines.Count == 0) return all;
        var hints = who != null && GenderHints.Needed(to) ? GenderHints.WordsFor(who.WrittenIn ?? from) : null;
        // What each hinted text is without its hints, to ask again with when
        // its hints come back out of place.
        var plainOf = new Dictionary<string, string>(StringComparer.Ordinal);

        // The words only - the markup is taken out first, so nothing that has
        // to survive is ever in front of a translator.
        var masked = new ProtectedText.Masked[lines.Count];
        for (int i = 0; i < lines.Count; i++) masked[i] = ProtectedText.Protect(lines[i].Text, names);

        // Each distinct text once, whoever says it. Measured on a real pack:
        // a fifth of its lines are repeats of another, and sending a line a
        // second time buys nothing but a second chance of being blocked.
        //
        // And not sent at all when there is nothing in it to translate - "...",
        // "?!", or a line that is only a {name}. One pack had a hundred and
        // forty of the first alone. Not the same in every language, though:
        // Chinese and Japanese write them with characters of their own, and
        // those are put in here rather than asked for (see Punctuation).
        var distinct = new List<string>();
        var sharers = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var wordless = new List<Result>();
        for (int i = 0; i < lines.Count; i++)
        {
            string words = masked[i].Text;
            if (!HasWords(words))
            {
                Wordless++;
                string theirs = Punctuation(words, to);
                // A line that is only a name - "Kiki!" - is still done: in its
                // spelling where the language has one, and marked as the same
                // where it has not, so it is not waiting for ever.
                if (!string.Equals(theirs, words, StringComparison.Ordinal) || masked[i].HasNames)
                {
                    var lost = new List<string>();
                    wordless.Add(new Result(lines[i].Key, ProtectedText.Restore(theirs, masked[i], spellings, lost), null));
                }
                continue;
            }
            // With its hints, for a language that needs them: the same words
            // from a man and from a woman are two different lines there.
            string sent = words;
            if (hints != null)
            {
                who!.Speakers.TryGetValue(lines[i].Key, out var speaker);
                sent = GenderHints.Add(words, masked[i], speaker, who, hints);
                plainOf[sent] = words;
            }
            if (!sharers.TryGetValue(sent, out var sharing))
            {
                sharers[sent] = sharing = new List<int>();
                distinct.Add(sent);
            }
            sharing.Add(i);
        }

        // Those need no request, so they are handed over before any is made.
        if (wordless.Count > 0)
        {
            all.AddRange(wordless);
            done?.Invoke(wordless);
        }

        int usable = 0, damaged = 0;
        bool first = true;
        foreach (var batch in Batches.Of(distinct))
        {
            if (cancel.IsCancellationRequested) { StoppedBecause = Cancelled; break; }

            // A pause before every request but the first, so a long run does
            // not arrive as a machine-perfect pulse.
            if (!first) await _wait(Pacing.Between(_random), cancel).ConfigureAwait(false);
            first = false;

            var sending = new List<string>(batch.Count);
            foreach (int d in batch) sending.Add(distinct[d]);

            IReadOnlyList<string>? came = await Ask(sending, from, to, cancel).ConfigureAwait(false);
            if (came == null) break;            // StoppedBecause is already set

            // The hints out. A line whose hints cannot be taken out for
            // certain is asked for again without them, rather than written
            // with a hint's words in it or thrown away.
            bool stopAfter = false;
            if (hints != null)
            {
                var clean = new string?[came.Count];
                var again = new List<int>();
                for (int n = 0; n < came.Count; n++)
                {
                    if (!GenderHints.Has(sending[n])) { clean[n] = came[n]; continue; }
                    clean[n] = GenderHints.Remove(came[n], sending[n]);
                    if (clean[n] == null) again.Add(n);
                }
                if (again.Count > 0)
                {
                    await _wait(Pacing.Between(_random), cancel).ConfigureAwait(false);
                    var plain = again.Select(n => plainOf.TryGetValue(sending[n], out var p) ? p : sending[n]).ToList();
                    var retried = await Ask(plain, from, to, cancel).ConfigureAwait(false);
                    // Stopped while asking again: those lines stay to do, and
                    // what this batch did get is still kept.
                    if (retried == null) stopAfter = true;
                    else for (int k = 0; k < again.Count; k++) clean[again[k]] = retried[k];
                }
                came = clean.Select(c => c ?? "").ToList();
            }

            // Every line that shares a text gets the one answer, each put back
            // together with its own markup.
            var owners = new List<int>();
            var answers = new List<string>();
            for (int n = 0; n < batch.Count; n++)
                foreach (int i in sharers[distinct[batch[n]]])
                {
                    owners.Add(i);
                    answers.Add(came[n]);
                }

            var results = Unmask(lines, masked, owners, answers, spellings);
            // Lines whose asking again was cut off are not damaged: they are
            // left to do, like the rest of a run that stopped.
            if (stopAfter) results = results.Where(r => r.Usable).ToList();
            all.AddRange(results);
            done?.Invoke(results);
            if (stopAfter) break;

            // An engine that is answering but not translating - a changed reply
            // format, a service quietly returning something else - does not
            // refuse and does not throw. It hands back text, line after line,
            // that fails the markup check. Past a point that is not a few bad
            // lines, it is an engine that has stopped working, and going on
            // would spend the rest of the run and the address's goodwill on
            // lines that will all be thrown away.
            foreach (var r in results) { if (r.Usable) usable++; else damaged++; }
            if (NotWorking(usable, damaged))
            {
                StoppedBecause = Loc.F("packText.mt.notWorking",
                                       "damaged", damaged.ToString(), "total", (usable + damaged).ToString());
                break;
            }
        }

        return all;
    }

    /// <summary>
    /// Ask the engine something whose answer can be judged, before trusting it
    /// with a pack.
    /// <para/>
    /// Null when it answered properly; otherwise why not. "Properly" is the
    /// same test every line of the run is held to - the right number of lines
    /// back, the markup intact, words where words went in - plus one more: that
    /// it actually translated. An engine that hands the text back unchanged, or
    /// a page that happens to parse, passes every other check.
    /// <para/>
    /// One small request. It costs a pack nothing, and it turns "the service
    /// changed overnight" from five thousand damaged lines into one sentence
    /// before anything is sent.
    /// </summary>
    public static async Task<string?> Probe(Send send, string to, CancellationToken cancel = default)
        => (await Check(send, to, cancel).ConfigureAwait(false)).Wrong;

    /// <summary>
    /// <see cref="Check"/>, and when the service is refusing this connection
    /// and has a check for a person to pass, <paramref name="person"/> is shown
    /// it; passed, the test sentence is asked again. <c>Passed</c> says whether
    /// somebody did pass one - the connection is then a person's, and another
    /// way in to the same service is no longer a way round a block.
    /// </summary>
    public static async Task<(string? Wrong, bool Blocked, bool WebsiteAllowed)> CheckWithPerson(
        Send send, string to, AskAPerson? person, CancellationToken cancel = default)
    {
        var (wrong, blocked, address) = await Check(send, to, cancel).ConfigureAwait(false);
        if (wrong == null || !blocked || person == null || address == null || cancel.IsCancellationRequested)
            return (wrong, blocked, false);
        switch (await person(address, cancel).ConfigureAwait(false))
        {
            case CheckOutcome.NotPassed:
                return (wrong, blocked, false);
            case CheckOutcome.NothingToAnswer:
                // A person looked, and Google gave them nothing to do: the
                // website, which does ask a check a person can answer, may go on.
                return (wrong, blocked, true);
            default:
                (wrong, blocked, _) = await Check(send, to, cancel).ConfigureAwait(false);
                return (wrong, blocked, true);
        }
    }

    /// <summary>
    /// <see cref="Probe"/>, and whether what went wrong was a block - the
    /// service refusing this connection, which no other way in to the same
    /// service should be used to get around.
    /// </summary>
    public static async Task<(string? Wrong, bool Blocked, string? Address)> Check(Send send, string to, CancellationToken cancel = default)
    {
        var probe = new[]
        {
            "Hello {name}, how are you today?",                // English on purpose: sent from "en" to test the engine, never shown
            "The weather is <b>very</b> nice at the beach.",   // English on purpose: sent from "en" to test the engine, never shown
        };
        var masked = probe.Select(ProtectedText.Protect).ToList();

        IReadOnlyList<string> came;
        try { came = await send(masked.Select(m => m.Text).ToList(), "en", to, cancel).ConfigureAwait(false); }
        catch (ServiceRefused refused) { return (refused.Message, refused.Blocked, refused.Blocked ? refused.CheckAddress : null); }
        catch (OperationCanceledException) { return (Cancelled, false, null); }

        if (came == null || came.Count != probe.Length) return (Loc.T("packText.mt.probeFailed"), false, null);

        bool english = Shared.PluralRules.LanguageOf(to ?? "") == "en";
        for (int i = 0; i < probe.Length; i++)
        {
            var lost = new List<string>();
            string put = ProtectedText.Restore(came[i], masked[i].Codes, lost);
            if (lost.Count > 0 || !ProtectedText.KeptItsCodes(probe[i], put) || !HasWords(put))
                return (Loc.T("packText.mt.probeFailed"), false, null);
            if (!english && string.Equals(put.Trim(), probe[i], StringComparison.Ordinal))
                return (Loc.T("packText.mt.probeFailed"), false, null);
        }
        return (null, false, null);
    }

    /// <summary>How many lines a run looks at before it judges the engine, and
    /// what share of them coming back damaged means it is not working.</summary>
    public const int JudgedAfter = 20;
    public const double DamagedLimit = 0.25;

    /// <summary>
    /// Whether this many damaged lines means the engine itself has stopped
    /// working, rather than that a few lines were hard. Not before
    /// <see cref="JudgedAfter"/> lines: two bad lines out of three is bad luck,
    /// not a verdict.
    /// </summary>
    public static bool NotWorking(int usable, int damaged)
    {
        int total = usable + damaged;
        return total >= JudgedAfter && damaged > total * DamagedLimit;
    }

    /// <summary>
    /// A line with no words in it, written the way <paramref name="to"/> writes
    /// punctuation.
    /// <para/>
    /// Not asked of the translator, because there is nothing for it to
    /// translate and every request is a chance of being blocked - and not left
    /// alone either, because Chinese and Japanese do not write these the way
    /// English does. An ellipsis is two ellipsis characters, "……", and the
    /// question and exclamation marks are full-width, "？" and "！". Korean, like
    /// the European languages, writes them as English does, so gets them as
    /// they are.
    /// <para/>
    /// Kept to the marks that are certain. A tilde, a colon or a lone full stop
    /// are left alone: each has more than one accepted form, and a guess about
    /// punctuation is still a guess.
    /// </summary>
    public static string Punctuation(string text, string to)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string language = Shared.PluralRules.LanguageOf(to ?? "");
        bool full = language == "zh" || language == "ja";
        if (!full) return text;

        // Any run of full stops two or longer, or an ellipsis character, is an
        // ellipsis - "..", "...", "....", "…" all mean the same pause.
        string made = System.Text.RegularExpressions.Regex.Replace(text, @"\.{2,}|…+", "……");
        return made.Replace('?', '？').Replace('!', '！');
    }

    /// <summary>Lines left out because there was nothing in them to translate.
    /// Reported rather than silently dropped, so a count that looks short can
    /// be explained.</summary>
    public int Wordless { get; private set; }

    /// <summary>Whether a text has anything a translator would translate: a
    /// letter, in any script. What is left of a line once its markup is taken
    /// out is markers, digits and punctuation when it has none.</summary>
    public static bool HasWords(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text) if (char.IsLetter(c)) return true;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="text"/> has nothing a translation into
    /// <paramref name="to"/> would change: no words once its markup is out, and
    /// punctuation that language writes the same. <see cref="Go"/> never sends
    /// such a line, and this is the same test, for asking beforehand.
    /// </summary>
    public static bool NothingToTranslate(string text, string to)
    {
        string words = ProtectedText.Protect(text).Text;
        return !HasWords(words) && string.Equals(Punctuation(words, to), words, StringComparison.Ordinal);
    }

    /// <summary>Text used when a run is stopped by the person who started it.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// One batch, with the waiting and the giving up.
    /// <para/>
    /// Null means the run should stop, and <see cref="StoppedBecause"/> says
    /// why. Everything already translated has been handed back.
    /// </summary>
    private async Task<IReadOnlyList<string>?> Ask(IReadOnlyList<string> texts, string from, string to,
                                                   CancellationToken cancel)
    {
        bool checkPassed = false;
        for (int attempt = 0; attempt < Pacing.Attempts; attempt++)
        {
            if (cancel.IsCancellationRequested) { StoppedBecause = Cancelled; return null; }

            if (attempt > 0)
            {
                await _wait(Pacing.BackOff(attempt), cancel).ConfigureAwait(false);
                if (cancel.IsCancellationRequested) { StoppedBecause = Cancelled; return null; }
            }

            try
            {
                var came = await _send(texts, from, to, cancel).ConfigureAwait(false);
                Sent++;

                // A service that answers with the wrong number of lines has not
                // translated this batch, whatever it returned: there is no way
                // to tell which answer belongs to which line, and guessing
                // would put one line's words under another line's key.
                if (came == null || came.Count != texts.Count)
                {
                    StoppedBecause = Loc.T("packText.mt.miscounted");
                    return null;
                }
                return came;
            }
            catch (OperationCanceledException)
            {
                StoppedBecause = Cancelled;
                return null;
            }
            catch (ServiceRefused refused)
            {
                Refused++;

                // A block is not a slow-down, even when it arrives with the
                // same status. Waiting thirty seconds and asking again is more
                // automated traffic from a connection already accused of it.
                // What can lift it is a person passing Google's check - once:
                // refused again straight after, the run stops.
                if (refused.Blocked)
                {
                    if (!checkPassed && Person != null && refused.CheckAddress != null)
                    {
                        var outcome = await Person(refused.CheckAddress, cancel).ConfigureAwait(false);
                        if (outcome == CheckOutcome.Passed)
                        {
                            checkPassed = true;
                            attempt--;   // asked again at once: that is not a retry of the block
                            continue;
                        }
                        // Nothing for a person to answer: on through the website,
                        // which does ask one, if there is a way to it.
                        if (outcome == CheckOutcome.NothingToAnswer && Instead != null && SwitchedTo == null)
                        {
                            var other = await Instead(cancel).ConfigureAwait(false);
                            if (other != null)
                            {
                                _send = other;
                                SwitchedTo = other;
                                checkPassed = true;
                                attempt--;
                                continue;
                            }
                        }
                    }
                    StoppedBecause = refused.Message;
                    return null;
                }

                if (!Pacing.WorthRetrying(refused.Status))
                {
                    // Asking again would be wrong in the same way, and would
                    // only be more traffic towards a block on somebody's own
                    // connection.
                    StoppedBecause = Loc.F("packText.mt.refused",
                                           "status", refused.Status.ToString(),
                                           "why", refused.Message ?? "");
                    return null;
                }
            }
        }

        StoppedBecause = Loc.T("packText.mt.noAnswer");
        return null;
    }

    /// <summary>
    /// Put the markup back and judge what came out.
    /// <para/>
    /// A line that lost a <c>{name}</c>, a tag or a token is reported with its
    /// trouble and NOT written. Two checks rather than one: the markers that
    /// went missing, and then the finished line held to the same standard a
    /// hand translation is held to - because a translator can hand back a
    /// marker and still have mangled what was around it.
    /// </summary>
    private static List<Result> Unmask(IReadOnlyList<Line> lines, ProtectedText.Masked[] masked,
                                       IReadOnlyList<int> batch, IReadOnlyList<string> came,
                                       IReadOnlyDictionary<string, string>? spellings)
    {
        var results = new List<Result>(batch.Count);
        for (int n = 0; n < batch.Count; n++)
        {
            int i = batch[n];
            var lost = new List<string>();
            string put = ProtectedText.Restore(came[n], masked[i], spellings, lost);

            string? trouble = null;
            if (lost.Count > 0)
                trouble = Loc.F("packText.mt.lostCodes", "codes", Loc.JoinAnd(lost));
            else if (!ProtectedText.KeptItsCodes(lines[i].Text, put))
                trouble = Loc.T("packText.mt.differentCodes");
            else if (string.IsNullOrWhiteSpace(put))
                trouble = Loc.T("packText.mt.empty");

            results.Add(new Result(lines[i].Key, put, trouble));
        }
        return results;
    }
}
