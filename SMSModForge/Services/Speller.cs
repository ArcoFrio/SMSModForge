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
            box.Text = text;
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
        Changed?.Invoke();
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
