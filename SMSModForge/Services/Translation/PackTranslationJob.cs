using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;

namespace SMSModForge.Services.Translation;

/// <summary>
/// Translating a pack into one or more languages, and writing the results into
/// its translation files as they arrive.
/// <para/>
/// <b>What still needs doing is read from the files themselves.</b> A line the
/// file already translates is not sent again — which is not an optimisation, it
/// is the whole of how a run resumes. A run stopped by a rate limit, a closed
/// lid or somebody pressing cancel has written everything it got; run it again
/// and it is given only what is still empty. There is no progress file to go
/// stale, and nothing to clean up if the rest is finished by hand.
/// <para/>
/// It also means the machine never overwrites a person. A line somebody has
/// translated, or corrected, is a line the file translates, so it is left
/// exactly alone.
/// <para/>
/// <b>Structure comes from the pack, not from the translator.</b> Each file is
/// brought up to date against the pack before anything is sent, so every
/// language has an entry for every text the pack has — a scene added in one
/// language is in all of them, untranslated, rather than missing from some.
/// That is <see cref="PackTranslations.CreateOrUpdate"/>'s job and this leans
/// on it rather than repeating it.
/// </summary>
public static class PackTranslationJob
{
    /// <summary>How one language's run ended.</summary>
    public sealed record Done(string Code, int Translated, int Damaged, string? StoppedBecause)
    {
        public bool Finished => StoppedBecause == null;

        /// <summary>Translated through the fallback, because the first engine
        /// failed its test sentence.</summary>
        public bool ByFallback { get; init; }
    }

    /// <summary>
    /// The lines <paramref name="existing"/> has no translation for.
    /// <para/>
    /// <b>Having a value is not being translated.</b> A pack's translation file
    /// starts out holding the pack's OWN words on every line, ready to be
    /// written over - so "the file says something for this key" is true of a
    /// file nobody has touched. The question is whether what it says is still
    /// the pack's words, which is the same question
    /// <see cref="TextCheck"/> asks when it reports a line as untranslated.
    /// Getting this wrong cost three tests: the first version found nothing to
    /// do on a brand new file and reported a run that translated nothing as a
    /// success.
    /// <para/>
    /// A line written <c>""</c> is a deliberate nothing and is left alone. So
    /// is one that reads the same as the pack's words for a good reason - a
    /// name, "OK" - once it is marked so (<see cref="TextFile.SameNote"/>): the
    /// machine marks what it gives back unchanged, and a person can mark a line
    /// by hand. Unmarked, it cannot be told from a line nobody has touched.
    /// <para/>
    /// A line whose words the pack has CHANGED is included: the file carries a
    /// translation, but of something the pack no longer says.
    /// <para/>
    /// Given the language, a line with nothing in it a translator would change
    /// - "...", "?!", a line that is only a {name} - is left out too. It is
    /// never sent (see <see cref="TranslationRun"/>), so it would otherwise be
    /// "still to translate" after every run, and anything that offers to finish
    /// the job would offer it again for ever. Chinese and Japanese write those
    /// with punctuation of their own, so there it still counts until written.
    /// </summary>
    /// <param name="lettersOnly">The lines whose words are kept and only their
    /// letters change (<see cref="PackTranslations.LettersOnlyKeys"/>). In a
    /// language of the same alphabet such a line is done when it reads as
    /// written; in one with its own, it is done when it has been spelled out.</param>
    public static List<TranslationRun.Line> Missing(TextFile? source, TextFile? existing, string? to = null,
                                                   ISet<string>? lettersOnly = null)
    {
        var todo = new List<TranslationRun.Line>();
        if (source == null) return todo;
        bool sameLetters = to != null && !PackNames.NeedsSpelling(to);

        foreach (var entry in source.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Text)) continue;   // nothing to translate
            if (to != null && TranslationRun.NothingToTranslate(entry.Text, to)) continue;

            var already = existing?.Find(entry.Key);
            if (sameLetters && already != null && lettersOnly != null && lettersOnly.Contains(entry.Key)
                && Alike(already.Text, entry.Text) && !ChangedSince(already, entry.Text))
                continue;
            if (already != null && !Untranslated(already, entry.Text)) continue;

            todo.Add(new TranslationRun.Line(entry.Key, entry.Text));
        }
        return todo;
    }

    /// <summary>Whether this line of the file still needs doing.</summary>
    private static bool Untranslated(TextFile.Entry mine, string source)
    {
        // Meant to be nothing, and said so.
        if (mine.Blank) return false;

        // Meant to read the same, and said so - about what the pack says now.
        if (mine.Same && Alike(mine.Text, source) && !ChangedSince(mine, source)
            && (mine.English == null || Alike(mine.English, source)))
            return false;

        // Nothing there, or the pack's own words still sitting where a
        // translation goes.
        if (string.IsNullOrEmpty(mine.Text)) return true;
        if (Alike(mine.Text, source)) return true;

        // Translated, but from words the pack has since changed.
        if (ChangedSince(mine, source)) return true;
        if (mine.English != null && !Alike(mine.English, source))
            return true;

        return false;
    }

    /// <summary>
    /// The same words, as far as a translation file can tell: it trims every
    /// line it reads, so a space or a tab at either end of the pack's words
    /// never survives into the note of what a line was translated from. Told
    /// apart exactly, a line ending in a space was out of date again after
    /// every run - offered for translating each time, and no language ever
    /// reached 100% (the author's pack, 2026-09-28: 43 lines, in every
    /// language). The game already compares them this way (<see cref="PackTexts.Normal"/>).
    /// </summary>
    private static bool Alike(string? a, string? b)
        => string.Equals(PackTexts.Normal(a), PackTexts.Normal(b), StringComparison.Ordinal);

    /// <summary>Marked as translated from words the pack has changed since -
    /// really changed: files written before 2026-09-28 carry the mark on lines
    /// whose only change was the space the file's trim took off their end.</summary>
    private static bool ChangedSince(TextFile.Entry mine, string source)
        => mine.ChangedFrom != null && !Alike(mine.ChangedFrom, source);

    /// <summary>One language ModForge offers, and how much of the pack it still lacks.</summary>
    public sealed record Waiting(string Code, string Name, int Missing, int Total, bool HasFile);

    /// <summary>
    /// For every language ModForge has words of its own in - the pack's own
    /// language left out, its words being that already - how many of the
    /// pack's texts are still to translate, by <see cref="Missing"/>.
    /// <para/>
    /// One count for everything that asks: the Translate window's notes, and
    /// the offer to translate before an export. Two counts would disagree, and
    /// an offer that says there is work the window then cannot find is worse
    /// than no offer.
    /// </summary>
    public static List<Waiting> StillToTranslate(ModPack pack, string packRoot)
    {
        var list = new List<Waiting>();
        var source = PackTranslations.Source(pack);
        var letters = PackTranslations.LettersOnlyKeys(pack);
        int total = source.Entries.Count(e => !string.IsNullOrWhiteSpace(e.Text));
        foreach (var language in Loc.Available())
        {
            if (string.Equals(language.Code, pack.OwnLanguage, StringComparison.OrdinalIgnoreCase)) continue;
            var file = string.IsNullOrEmpty(packRoot) ? null : Loc.Read(PackTranslations.PathOf(packRoot, language.Code));
            list.Add(new Waiting(language.Code, language.Name, Missing(source, file, language.Code, letters).Count, total, file != null));
        }
        return list;
    }

    /// <summary>Every language the pack already has a file for.</summary>
    public static List<string> Languages(string packRoot)
    {
        var codes = new List<string>();
        string folder = PackTranslations.FolderOf(packRoot);
        if (!Directory.Exists(folder)) return codes;

        foreach (string path in Directory.EnumerateFiles(folder, "*" + PackTexts.Extension))
        {
            string code = Path.GetFileNameWithoutExtension(path);
            if (TextFile.IsKey(code)) codes.Add(code);
        }
        codes.Sort(StringComparer.OrdinalIgnoreCase);
        return codes;
    }

    /// <summary>
    /// Translate <paramref name="codes"/>, writing each language's file as its
    /// results arrive.
    /// <para/>
    /// One language at a time rather than all at once: the service is rate
    /// limited per address, so asking it for five languages in parallel is five
    /// times the way to be blocked, and a block would cost all five rather than
    /// stopping one with the rest still to come.
    /// </summary>
    /// <summary>
    /// Another way to the translator, asked for only when the first one fails
    /// its test sentence: the engine to use, or why there is none.
    /// </summary>
    public delegate Task<(TranslationRun.Send? Send, string? WhyNot)> Fallback(CancellationToken cancel);

    /// <summary>
    /// Character names kept out of a translation (<see cref="PackNames"/>),
    /// and how each is written in each language. A language with no entry
    /// here, or a name its entry lacks, keeps the name exactly as written.
    /// </summary>
    public sealed class KeptNames
    {
        public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();

        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Spellings { get; init; }
            = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string>? In(string code)
            => Spellings.TryGetValue(code, out var spelled) ? spelled : null;

        /// <summary>How <paramref name="name"/> is written in <paramref name="code"/>,
        /// or null when the author gave no spelling for it.</summary>
        public string? Spelled(string code, string name)
        {
            var spelled = In(code);
            return spelled != null && spelled.TryGetValue((name ?? "").Trim(), out var s) && !string.IsNullOrWhiteSpace(s)
                ? s.Trim() : null;
        }
    }

    /// <summary>
    /// Where a run is: in the language it is on, and across all of them.
    /// <para/>
    /// The whole run is counted in texts, not in languages, so a language that
    /// is nearly done already does not count for as much as one starting from
    /// nothing. What each language has left is counted before the run starts,
    /// from its file as it stands (<see cref="Missing"/>), and put right as
    /// each one is reached - so the total may move a little when it does.
    /// </summary>
    /// <param name="Number">Which of the languages this is, from 1.</param>
    public sealed record Progress(string Code, int Number, int Languages, int Done, int Total, int AllDone, int AllTotal);

    /// <summary>How many texts <paramref name="code"/> has left to translate,
    /// by its file as it stands - read, and nothing written.</summary>
    public static int Left(ModPack pack, string packRoot, string code)
    {
        if (string.Equals(code, pack.OwnLanguage, StringComparison.OrdinalIgnoreCase)) return 0;
        var file = Loc.Read(PackTranslations.PathOf(packRoot, code));
        return Missing(PackTranslations.Source(pack, file), file, code, PackTranslations.LettersOnlyKeys(pack)).Count;
    }

    public static async Task<List<Done>> Run(
        ModPack? pack, string packRoot, IReadOnlyList<string>? codes,
        TranslationRun.Send send, TranslationRun.Wait wait,
        Action<string, int, int>? progress = null,
        CancellationToken cancel = default,
        bool probeFirst = false,
        Fallback? fallback = null,
        Action<string>? said = null,
        KeptNames? keepNames = null,
        TranslationRun.AskAPerson? person = null,
        Action<Progress>? stepped = null)
    {
        var done = new List<Done>();
        if (pack == null || string.IsNullOrEmpty(packRoot) || codes == null) return done;
        bool byFallback = false;

        // A test sentence before anything of the pack is sent, into the first
        // language asked for. An engine that has changed how it answers fails
        // this in one small request instead of across thousands of lines.
        // Off for the tests of the run itself, which drive a fake that is
        // meant to misbehave in other ways.
        if (probeFirst && codes.Count > 0)
        {
            // Refused with a check to pass: the person is shown it, and the
            // test sentence asked again once they have passed it.
            var (wrong, blocked, passed) = await TranslationRun.CheckWithPerson(send, codes[0], person, cancel)
                                                               .ConfigureAwait(false);
            // "passed" also when Google gave the person nothing to answer: the
            // website then asks its own check, which a person can.

            // Failing the test is not the same as being refused. A block says
            // the service has stopped serving this connection, and asking it
            // again by another door would be getting round that - so only an
            // engine that answered wrongly, or not at all, is worth a second way
            // in. Unless a person has passed Google's check: the connection is
            // then shown to be theirs, and the website carries on for them.
            if (wrong != null && (!blocked || passed) && fallback != null && !cancel.IsCancellationRequested)
            {
                said?.Invoke(Loc.T("packText.web.switching"));
                var (other, whyNot) = await fallback(cancel).ConfigureAwait(false);
                string? otherWrong = whyNot;
                if (other != null)
                    otherWrong = (await TranslationRun.Check(other, codes[0], cancel).ConfigureAwait(false)).Wrong;

                if (other != null && otherWrong == null)
                {
                    send = other;
                    wrong = null;
                    byFallback = true;
                }
                else if (otherWrong != null && otherWrong != TranslationRun.Cancelled)
                {
                    wrong += " " + Loc.F("packText.web.alsoFailed", "why", otherWrong);
                }
            }

            if (wrong != null)
            {
                foreach (string code in codes) done.Add(new Done(code, 0, 0, wrong));
                return done;
            }
        }

        string from = SourceLanguage(pack);
        // Who says what, and who each kept name is: for the languages whose
        // words change with it.
        var who = GenderHints.For(pack);
        // The lines whose words are kept: spelled out, never translated.
        var letters = PackTranslations.LettersOnlyKeys(pack);

        // What each language has left, counted before the first is started,
        // so the whole run has a size from the beginning.
        var left = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (stepped != null)
            foreach (string code in codes)
                left[code] = Left(pack, packRoot, code);
        int finished = 0;   // texts in the languages already done
        int number = 0;
        void Step(string code, int done, int total)
        {
            if (stepped == null) return;
            left[code] = total;
            int later = 0;
            for (int i = number; i < codes.Count; i++) later += left[codes[i]];
            stepped(new Progress(code, number, codes.Count, done, total,
                                 finished + done, finished + total + later));
        }

        foreach (string code in codes)
        {
            if (cancel.IsCancellationRequested) break;
            number++;

            // The pack's own words are already in its own language.
            if (string.Equals(code, pack.OwnLanguage, StringComparison.OrdinalIgnoreCase))
            {
                done.Add(new Done(code, 0, 0, null));
                continue;
            }

            // Bring the file up to date with the pack first, so what is counted
            // as missing is measured against what the pack says NOW.
            PackTranslations.CreateOrUpdate(pack, packRoot, code);

            string path = PackTranslations.PathOf(packRoot, code);
            TextFile? file = Loc.Read(path);
            // With the lines typed only in this language, so writing the file
            // back does not set them aside as unused. Missing skips them: with
            // no words of the pack's own there is nothing to translate from.
            var source = PackTranslations.Source(pack, file);
            var todo = Missing(source, file, code, letters);

            // Kept in hand and written from, rather than read back each time.
            // A batch is forty lines and a pack is thousands, so this is
            // written over a hundred times in one run; re-reading and
            // re-parsing the whole file for each of those would cost more than
            // the translating. Nothing else writes to it while a run is going.
            var holding = file ?? new TextFile();
            int translated = 0, damaged = 0;

            // Names kept: a character's name field is never sent. It is the
            // name as the author spelled it for this language - over whatever
            // the file said, since the author has just checked it - or, where
            // there is no spelling, the name as written, marked as the same.
            if (keepNames != null)
            {
                bool wrote = false;
                foreach (var entry in source.Entries.Where(e => PackNames.IsNameKey(e.Key) && !string.IsNullOrWhiteSpace(e.Text)
                                                               && keepNames.Names.Contains(e.Text.Trim())))
                {
                    string? spelled = keepNames.Spelled(code, entry.Text);
                    bool waiting = todo.Any(t => t.Key == entry.Key);
                    if (spelled == null && !waiting) continue;
                    if (spelled != null && string.Equals(holding.Translated(entry.Key), spelled, StringComparison.Ordinal)
                        && !waiting)
                        continue;
                    Put(holding, source, entry.Key, spelled ?? entry.Text);
                    wrote = true;
                    if (waiting) translated++;
                }
                // Only the names being kept: with the game's alone kept, the
                // pack's own characters' names are translated like the rest.
                todo.RemoveAll(t => PackNames.IsNameKey(t.Key)
                                    && keepNames.Names.Contains(source.Get(t.Key).Trim()));
                if (wrote) Write(pack, packRoot, code, source, holding);
            }

            // The lines whose words are kept come out of the translating
            // altogether: as written where the alphabet is the same, and
            // spelled out in this language's letters where it is not.
            var spell = todo.Where(t => letters.Contains(t.Key)).ToList();
            int spelledDone = 0;
            if (spell.Count > 0)
            {
                todo.RemoveAll(t => letters.Contains(t.Key));
                Step(code, 0, spell.Count + todo.Count);
                spelledDone = await SpellOut(spell, holding, source, code, send, cancel, person,
                                             fallback == null ? null : async c => (await fallback(c).ConfigureAwait(false)).Send)
                                    .ConfigureAwait(false);
                translated += spelledDone;
                Write(pack, packRoot, code, source, holding);
                Step(code, spelledDone, spell.Count + todo.Count);
            }

            if (todo.Count == 0)
            {
                Step(code, spelledDone, spelledDone);
                done.Add(new Done(code, translated, 0, null));
                finished += spelledDone;
                continue;
            }
            int before = spell.Count;
            Step(code, spelledDone, before + todo.Count);

            var run = new TranslationRun(send, wait)
            {
                Person = person,
                // Refused mid-run with nothing for a person to answer: the
                // website, tested the same way the first engine was.
                Instead = fallback == null ? null : async c =>
                {
                    said?.Invoke(Loc.T("packText.web.switching"));
                    var (other, _) = await fallback(c).ConfigureAwait(false);
                    if (other == null) return null;
                    var (otherWrong, _, _) = await TranslationRun.Check(other, code, c).ConfigureAwait(false);
                    return otherWrong == null ? other : null;
                },
            };
            await run.Go(todo, from, code, results =>
            {
                foreach (var one in results)
                {
                    if (!one.Usable) { damaged++; continue; }
                    Put(holding, source, one.Key, one.Text);
                    translated++;
                }

                // Written per batch rather than at the end. This is what makes
                // a run that is cut off keep its work.
                Write(pack, packRoot, code, source, holding);
                progress?.Invoke(code, translated, before + todo.Count);
                Step(code, translated + damaged, before + todo.Count);
            }, cancel, keepNames?.Names, keepNames?.In(code), who).ConfigureAwait(false);

            // Switched to the website mid-run: the languages after it go on there.
            if (run.SwitchedTo != null)
            {
                send = run.SwitchedTo;
                byFallback = true;
            }
            done.Add(new Done(code, translated, damaged, run.StoppedBecause) { ByFallback = byFallback });
            finished += before + todo.Count;
        }

        return done;
    }

    /// <summary>
    /// Put the lines whose words are kept into <paramref name="holding"/>, and
    /// say how many were done: each as written, where
    /// <paramref name="code"/> shares the pack's alphabet; spelled out word by
    /// word in its letters where it has its own (<see cref="LettersOnly"/>).
    /// <para/>
    /// A line none of whose words came back spelled is left waiting rather
    /// than written as it was - written, it would read as done, in letters
    /// its players may not read, and never be asked for again.
    /// </summary>
    private static async Task<int> SpellOut(List<TranslationRun.Line> lines, TextFile holding, TextFile source,
                                            string code, TranslationRun.Send send, CancellationToken cancel,
                                            TranslationRun.AskAPerson? person,
                                            Func<CancellationToken, Task<TranslationRun.Send?>>? instead)
    {
        if (!PackNames.NeedsSpelling(code))
        {
            foreach (var line in lines) Put(holding, source, line.Key, line.Text);
            return lines.Count;
        }

        var words = LettersOnly.Words(lines.Select(l => l.Text));
        var spelled = new Dictionary<string, string>(StringComparer.Ordinal);
        // A few dozen at a time: every word is asked in two sentences, and a
        // long list is one long request.
        const int PerRequest = 40;
        for (int i = 0; i < words.Count && !cancel.IsCancellationRequested; i += PerRequest)
        {
            try
            {
                var some = await SuggestSpellings(send, words.Skip(i).Take(PerRequest).ToList(), code, cancel,
                                                  person, instead).ConfigureAwait(false);
                foreach (var pair in some) spelled[pair.Key] = pair.Value;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Refused, or no answer: what was spelled so far is used, and
                // the rest waits for the next run.
                break;
            }
        }

        int done = 0;
        foreach (var line in lines)
        {
            string respelled = LettersOnly.Respell(line.Text, spelled);
            if (LettersOnly.Words(new[] { line.Text }).Count > 0 && Alike(respelled, line.Text)) continue;
            Put(holding, source, line.Key, respelled);
            done++;
        }
        return done;
    }

    /// <summary>
    /// Ask the translator how <paramref name="names"/> are spelled in
    /// <paramref name="code"/>, for the author to check before any is used
    /// (<see cref="PackNames.ReadSuggestions"/>). One request for all of them,
    /// and one to <paramref name="instead"/> - the website - for any the first
    /// could not spell. A name neither can suggest is simply not in the result.
    /// </summary>
    public static async Task<Dictionary<string, string>> SuggestSpellings(
        TranslationRun.Send send, IReadOnlyList<string> names, string code, CancellationToken cancel = default,
        TranslationRun.AskAPerson? person = null,
        Func<CancellationToken, Task<TranslationRun.Send?>>? instead = null,
        Action<string>? unread = null)
    {
        if (names == null || names.Count == 0) return new Dictionary<string, string>(StringComparer.Ordinal);
        var texts = PackNames.SuggestionTexts(names);
        IReadOnlyList<string> answers;
        bool fromWebsite = false;
        try
        {
            answers = await send(texts, "en", code, cancel).ConfigureAwait(false);
        }
        catch (TranslationRun.ServiceRefused refused) when (!cancel.IsCancellationRequested)
        {
            // Google's check, when there is one: a person passes it, and the
            // free service is asked once more. Anything else - a check with
            // nothing on it to answer, "too many requests" with no check at
            // all, no answer in time - and the website is asked instead. It
            // is the refusal the author sees otherwise, and the column stays
            // empty (author, 2026-09-26).
            var outcome = refused.Blocked && refused.CheckAddress != null && person != null
                ? await person(refused.CheckAddress, cancel).ConfigureAwait(false)
                : TranslationRun.CheckOutcome.NothingToAnswer;
            TranslationRun.Send? again = outcome == TranslationRun.CheckOutcome.Passed ? send
                : outcome == TranslationRun.CheckOutcome.NothingToAnswer && instead != null
                    ? await instead(cancel).ConfigureAwait(false)
                    : null;
            if (again == null) throw;
            fromWebsite = outcome != TranslationRun.CheckOutcome.Passed;
            answers = await again(texts, "en", code, cancel).ConfigureAwait(false);
        }
        var suggested = PackNames.ReadSuggestions(names, answers, code);

        // The names the free service did not spell are asked of the website,
        // once. They are not the same translator and do not answer alike: in
        // Chinese the free service left the names in Latin letters ("我叫 Solid
        // Snake。"), where the website spelled nearly every one. Not a way round
        // a block - the free service answered. What the website cannot give
        // either stays empty, and what the free service did spell stands.
        var missing = names.Where(n => !suggested.ContainsKey(n)).ToList();
        if (missing.Count > 0 && !fromWebsite && instead != null && !cancel.IsCancellationRequested)
        {
            try
            {
                var website = await instead(cancel).ConfigureAwait(false);
                if (website != null)
                {
                    var moreTexts = PackNames.SuggestionTexts(missing);
                    var more = await website(moreTexts, "en", code, cancel).ConfigureAwait(false);
                    foreach (var pair in PackNames.ReadSuggestions(missing, more, code)) suggested[pair.Key] = pair.Value;
                    texts = moreTexts;
                    answers = more;
                }
            }
            catch (TranslationRun.ServiceRefused)
            {
                // The website would not answer either: the free service's
                // spellings are still good, and are kept.
            }
        }

        // Answered, and nothing could be read out of it: what came back last is
        // told, so a language that never gets a suggestion can be seen to.
        // Both sentences, with the marker and with the first name.
        if (suggested.Count == 0 && answers != null && answers.Count == texts.Count && answers.Count >= 4)
            unread?.Invoke(string.Join("  |  ", answers[0], answers[2], answers[1], answers[3]));
        return suggested;
    }

    /// <summary>
    /// The language to tell the service the pack is written in.
    /// <para/>
    /// The pack's own, where it names one other than English. A pack that
    /// does not name one is sent as "auto" rather than as English: every pack
    /// made before a pack could say was taken to be English, and a Portuguese
    /// author's older pack declared English would be translated as though it
    /// were, and quietly mangled. The service tells English apart by itself.
    /// </summary>
    public static string SourceLanguage(ModPack pack)
        => pack.ShouldSerializeLanguage() ? pack.OwnLanguage : "auto";

    /// <summary>
    /// Put one translated line into the file in hand.
    /// <para/>
    /// The note saying it needs looking at goes with it: this IS a translation
    /// of what the pack says now, so leaving "changed from" on it would send
    /// somebody to check a line that has just been done.
    /// <para/>
    /// A line the machine gave back word for word is marked as meant to read
    /// the same (a name, usually), so it is done rather than waiting.
    /// </summary>
    private static void Put(TextFile file, TextFile source, string key, string text)
    {
        string original = source.Get(key);
        bool same = Alike(text, original);
        var entry = file.Find(key);
        if (entry != null)
        {
            entry.Text = text;
            entry.Blank = false;
            entry.ChangedFrom = null;
            entry.English = original;
            entry.Same = same;
            return;
        }

        file.Add(new TextFile.Entry { Key = key, Text = text, English = original, Same = same });
    }

    /// <summary>
    /// Write the language's file out.
    /// <para/>
    /// Rebuilt from the pack rather than appended to, so the file stays in the
    /// pack's order with its notes and headings intact - and so a text the pack
    /// has since changed is not left looking translated.
    /// </summary>
    private static void Write(ModPack pack, string packRoot, string code,
                              TextFile source, TextFile file)
        => PackTranslations.Write(pack, packRoot, code, source, file, Loc.T("packText.mt.fileNote"));
}
