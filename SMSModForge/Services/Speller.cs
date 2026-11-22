using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SMSModForge.Services;

/// <summary>
/// Which words in a string Windows thinks are misspelled.
/// <para/>
/// WPF only offers spelling through a <see cref="TextBox"/>: there is no API
/// that takes a string and hands back the bad words. So this keeps one TextBox
/// that is never shown, puts the text in it, and reads the answers back out.
/// That is the same speller the editing boxes already use — the same dictionary
/// as Edge and Office, and the same words an author has added to it — rather
/// than a second opinion that would disagree with the box below the list.
/// <para/>
/// Measured and arranged, because a control WPF has never laid out has not run
/// its speller. Never added to a window, so nothing about it is on screen.
/// <para/>
/// One instance, reused: a TextBox per line of dialogue would be thousands of
/// controls for a list that scrolls.
/// </summary>
public static class Speller
{
    [ThreadStatic] private static TextBox? _box;

    /// <summary>One misspelled word: where it starts in the text, and how long.</summary>
    public readonly record struct Bad(int At, int Length);

    /// <summary>
    /// The misspelled stretches of <paramref name="text"/>, in order.
    /// <para/>
    /// Empty for anything that cannot be checked — no dispatcher, a speller
    /// Windows does not have for this language, an empty string. A row with no
    /// squiggles is the same row it was before this existed, so nothing has to
    /// be said about it.
    /// </summary>
    public static IReadOnlyList<Bad> Check(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<Bad>();
        if (TryKnown(text, out var known)) return known;

        var box = Box();
        if (box == null) return Array.Empty<Bad>();
        if (!string.Equals(box.Language?.IetfLanguageTag, _language, StringComparison.OrdinalIgnoreCase))
        {
            try { box.Language = System.Windows.Markup.XmlLanguage.GetLanguage(_language); }
            catch (ArgumentException) { return Array.Empty<Bad>(); }
        }

        var found = new List<Bad>();
        try
        {
            box.Text = WithoutCodes(text!);
            // Laying out is what runs the speller; without this the box is
            // asked about text it has never looked at and says nothing.
            box.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            box.Arrange(new System.Windows.Rect(box.DesiredSize));

            int at = 0;
            while (at < text.Length && found.Count < 200)
            {
                int next = box.GetNextSpellingErrorCharacterIndex(at, LogicalDirection.Forward);
                if (next < 0) break;

                int length = box.GetSpellingErrorLength(next);
                if (length <= 0) break;

                found.Add(new Bad(next, length));
                at = next + length;
            }
        }
        catch (Exception)
        {
            // The speller is a courtesy. A row that cannot be checked is drawn
            // without squiggles rather than not drawn at all.
            return Array.Empty<Bad>();
        }
        Remember(text!, found);
        return found;
    }

    /// <summary>
    /// The text with the pack's codes blanked out, each by as many spaces as it is long so a mark still falls on
    /// the right letters: a variable's value ([PV:Confidence]), a name the player chose ({PC}, {M}), a value in an
    /// action's box ($Mood). They are not words, and a red line under [PV:Confidence] read as the code being wrong
    /// (found by the review of every tutorial step, 1.10.0).
    /// </summary>
    internal static string WithoutCodes(string text)
        => text.IndexOfAny(CodeStarts) < 0 ? text
           : Codes.Replace(text, m => new string(' ', m.Length));

    private static readonly char[] CodeStarts = { '[', '{', '$' };

    /// <summary>Whether the text holds a code at all.</summary>
    internal static bool HasCodes(string text) => text.IndexOfAny(CodeStarts) >= 0 && Codes.IsMatch(text);

    /// <summary>The text in pieces, each code a piece of its own (true), the words between them the others.</summary>
    internal static IEnumerable<(string Text, bool Code)> CodePieces(string text)
    {
        int at = 0;
        foreach (System.Text.RegularExpressions.Match m in Codes.Matches(text))
        {
            if (m.Index > at) yield return (text[at..m.Index], false);
            yield return (m.Value, true);
            at = m.Index + m.Length;
        }
        if (at < text.Length) yield return (text[at..], false);
    }
    private static readonly System.Text.RegularExpressions.Regex Codes =
        new(@"\[PV:[^\]\s]*\]|\{[A-Za-z][A-Za-z0-9_]*\}|\$[A-Za-z_][A-Za-z0-9_]*", System.Text.RegularExpressions.RegexOptions.Compiled);

    // ── What has been asked already ──────────────────────────────────

    /// <summary>
    /// Every answer given, by the text asked about.
    /// <para/>
    /// Asking Windows costs about forty milliseconds a line, measured on a real
    /// pack, and a conversation redrawn after an undo asked about every line in
    /// it again - a hundred lines, four seconds, for one line moved. The words
    /// of a line that has not changed are not misspelled any differently, so
    /// each answer is kept until the dictionary changes (<see cref="Forget"/>).
    /// Per thread, like the box that asks.
    /// </summary>
    [ThreadStatic] private static Dictionary<string, IReadOnlyList<Bad>>? _known;

    /// <summary>Enough for every line of a large pack several times over;
    /// past it the answers start again rather than growing for ever.</summary>
    private const int MostRemembered = 50000;

    /// <summary>The answer for <paramref name="text"/> if it has been asked
    /// already, without asking Windows.</summary>
    public static bool TryKnown(string? text, out IReadOnlyList<Bad> bad)
    {
        bad = Array.Empty<Bad>();
        if (string.IsNullOrWhiteSpace(text)) return true;
        return _known != null && _known.TryGetValue(Key(text!), out bad!);
    }

    private static void Remember(string text, IReadOnlyList<Bad> bad)
    {
        _known ??= new Dictionary<string, IReadOnlyList<Bad>>(StringComparer.Ordinal);
        if (_known.Count >= MostRemembered) _known.Clear();
        _known[Key(text)] = bad;
    }

    /// <summary>An answer is for a text in a language: the same words are
    /// spelled right in one and wrong in another.</summary>
    private static string Key(string text) => _language + "\u0001" + text;

    private static string KeyIn(string language, string text) => language + "\u0001" + text;

    /// <summary>
    /// The language the words asked about are in, as a tag Windows' speller
    /// knows ("en-US", "pt-BR"). US English until told otherwise, which is what
    /// every text was checked in before the pack's language was asked.
    /// </summary>
    [ThreadStatic] private static string? _languageTag;
    private static string _language => _languageTag ?? "en-US";

    /// <summary>The language words are checked in now.</summary>
    public static string Language => _language;

    /// <summary>
    /// Check in <paramref name="tag"/> from now on. What is showing marks is
    /// told to ask again - its words are the same, but whether they are
    /// spelled right is not. Answers already given in another language are
    /// kept for switching back.
    /// </summary>
    public static void UseLanguage(string? tag)
    {
        string wanted = string.IsNullOrWhiteSpace(tag) ? "en-US" : tag!;
        if (string.Equals(_language, wanted, StringComparison.OrdinalIgnoreCase)) return;
        _languageTag = wanted;
        Changed?.Invoke();
    }

    /// <summary>
    /// Forget every answer: a word was added to the dictionary, so any of them
    /// may now be wrong. Everything showing marks is told to ask again.
    /// </summary>
    public static void Forget()
    {
        _known?.Clear();
        _worker?.BeginInvoke(new Action(() => _known?.Clear()));
        Changed?.Invoke();
    }

    // ── Asking off the UI thread (1.10.0) ────────────────────────────

    private static System.Windows.Threading.Dispatcher? _worker;
    private static readonly object WorkerGate = new();
    private static int _pending;

    /// <summary>Answers asked for and not yet given back (the test harness waits for none).</summary>
    public static int Pending => System.Threading.Volatile.Read(ref _pending);

    /// <summary>
    /// The answer for <paramref name="text"/>, given to <paramref name="answered"/> on the thread that asked: at
    /// once when it is known, otherwise asked on a thread of the speller's own. Asking Windows takes about forty
    /// milliseconds a line, and asked on the UI thread a conversation's first showing held the window a second
    /// (UiDelayTests' profile, 2026-11-21); a click in between is now answered straight away. The box, the
    /// language and the answers are per thread already, so the speller's thread keeps its own; each answer is
    /// kept on the asking thread too.
    /// </summary>
    public static void Ask(string text, Action<IReadOnlyList<Bad>> answered)
    {
        if (TryKnown(text, out var known)) { answered(known); return; }
        var back = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        string language = _language;
        var worker = Worker();
        if (worker == null) { answered(Check(text)); return; }
        System.Threading.Interlocked.Increment(ref _pending);
        worker.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            IReadOnlyList<Bad> bad;
            try
            {
                _languageTag = language;
                bad = Check(text);
            }
            catch (Exception) { bad = Array.Empty<Bad>(); }
            back.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                System.Threading.Interlocked.Decrement(ref _pending);
                // Kept under the language it was asked in, which the window may have left meanwhile.
                _known ??= new Dictionary<string, IReadOnlyList<Bad>>(StringComparer.Ordinal);
                if (_known.Count >= MostRemembered) _known.Clear();
                _known[KeyIn(language, text)] = bad;
                answered(bad);
            }));
        }));
    }

    private static System.Windows.Threading.Dispatcher? Worker()
    {
        lock (WorkerGate)
        {
            if (_worker != null) return _worker;
            if (System.Windows.Application.Current == null) return null;
            System.Windows.Threading.Dispatcher? made = null;
            using var ready = new System.Threading.ManualResetEventSlim();
            var thread = new System.Threading.Thread(() =>
            {
                made = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                ready.Set();
                System.Windows.Threading.Dispatcher.Run();
            })
            { IsBackground = true, Name = "ModForge speller" };   // English on purpose: a thread's name, seen only in a debugger.
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            ready.Wait();
            return _worker = made;
        }
    }

    /// <summary>The answers were forgotten; ask again.</summary>
    public static event Action? Changed;

    private static TextBox? Box()
    {
        if (_box != null) return _box;
        if (System.Windows.Application.Current == null) return null;

        _box = new TextBox();
        SpellCheck.SetIsEnabled(_box, true);
        return _box;
    }
}
