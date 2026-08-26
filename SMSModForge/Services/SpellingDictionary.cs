using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SMSModForge.Model;

namespace SMSModForge.Services;

/// <summary>
/// Word-list plumbing for the node Text box's spell check. The checking itself
/// is entirely Windows' — WPF's <c>SpellCheck.IsEnabled</c> calls the platform
/// spell checker — and so is the dictionary. Nothing here implements spelling
/// logic or ships an English word list.
/// <para/>
/// Everything goes through the <b>Windows per-user dictionary</b>, the same file
/// Edge and Office write when you pick "Add to dictionary". That is deliberate,
/// and it is also the only thing that works: WPF's own
/// <c>SpellCheck.CustomDictionaries</c> (an app-scoped <c>.lex</c>) is accepted
/// without error on Windows 11 and then completely ignored — the platform
/// checker never sees it. Measured with a control word that no dictionary
/// contains, so "no squiggle" couldn't be confused with "the speller hadn't
/// finished yet": the .lex words stayed flagged at 1s, 3s, 6s and 10s, while
/// the same words placed in the file below were accepted.
/// </summary>
public static class SpellingDictionary
{
    /// <summary>
    /// The machine's added-words file. The <c>neutral</c> folder is
    /// language-independent and is the one the platform checker actually honours
    /// here — a <c>default.dic</c> freshly created under <c>en-US</c> was NOT
    /// picked up, because the checker only watches the language folders that
    /// already existed when its session started. <c>neutral</c> ships with
    /// Windows, so it is always present.
    /// </summary>
    public static string WindowsUserDictionaryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Spelling", "neutral", "default.dic");

    /// <summary>
    /// How long the platform checker takes to notice the file changed. Measured
    /// between 0.5s (too early) and 2.5s (applied); callers that want the
    /// squiggle to clear on its own should wait at least this long. The editor
    /// doesn't rely on it — it clears the underline immediately via
    /// <c>SpellingError.IgnoreAll</c> and lets the file make it permanent.
    /// </summary>
    public static TimeSpan ReloadDelay => TimeSpan.FromSeconds(3);

    /// <summary>
    /// Adds <paramref name="word"/> to the Windows per-user dictionary. Returns
    /// false only if it couldn't be written; a word already present counts as
    /// success.
    /// <para/>
    /// This is a shared system file, so it is only ever written from an explicit
    /// user action — the "Add to dictionary" menu item, or the opt-in bulk add.
    /// Nothing lands in it that the author didn't ask for.
    /// </summary>
    public static bool AddToWindowsDictionary(string word) => AddToWindowsDictionary(new[] { word }) >= 0;

    /// <summary>
    /// Bulk form. Returns the number of words actually added (0 when they were
    /// all already there), or -1 if the file couldn't be written.
    /// </summary>
    public static int AddToWindowsDictionary(IEnumerable<string> words)
    {
        try
        {
            var existing = ReadWindowsUserDictionary();
            var known = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

            var added = new List<string>();
            foreach (var raw in words)
            {
                var w = (raw ?? "").Trim();
                if (w.Length == 0 || !known.Add(w)) continue;
                added.Add(w);
            }
            if (added.Count == 0) return 0;

            var path = WindowsUserDictionaryPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Rewrite whole rather than append: the file is UTF-16 with a BOM,
            // and a blind byte append would either duplicate the BOM or land
            // mid-line.
            existing.AddRange(added);
            File.WriteAllText(path, string.Join("\r\n", existing) + "\r\n", Utf16);
            return added.Count;
        }
        catch { return -1; }
    }

    /// <summary>Current contents of the Windows per-user dictionary, one word
    /// per entry. Empty when it doesn't exist or can't be read.</summary>
    public static List<string> ReadWindowsUserDictionary()
    {
        try
        {
            var path = WindowsUserDictionaryPath;
            if (!File.Exists(path)) return new List<string>();
            return File.ReadAllLines(path, Utf16)
                       .Select(l => l.Trim())
                       .Where(l => l.Length > 0)
                       .ToList();
        }
        catch { return new List<string>(); }
    }

    /// <summary>
    /// Every proper noun this pack invents — character, actor, place and NPC
    /// names. Offered to the author as a one-click bulk add so a pack's cast
    /// stops squiggling, rather than being written to their system dictionary
    /// behind their back.
    /// </summary>
    public static List<string> CollectPackWords(ModPack pack)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = new List<string>();

        void Take(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            // A multi-word display name ("Solid Snake", "Doctor Frost") never
            // matches as one token — the checker tests words, so both halves
            // have to be entries in their own right.
            foreach (var part in raw.Split(new[] { ' ', '\t', '-', '_', '/', '\\' },
                                            StringSplitOptions.RemoveEmptyEntries))
            {
                var w = part.Trim();
                // Identifier fragments (digits, punctuation) aren't words and
                // would only weaken the dictionary.
                if (w.Length < 2 || !w.All(char.IsLetter)) continue;
                if (seen.Add(w)) words.Add(w);
            }
        }

        foreach (var c in pack.Characters) { Take(c.Name); Take(c.DisplayName); }
        foreach (var a in pack.Actors) Take(a.DisplayName);
        foreach (var p in pack.Places) Take(p.DisplayName);
        foreach (var n in pack.Npcs) Take(n.DisplayName);

        words.Sort(StringComparer.OrdinalIgnoreCase);
        return words;
    }

    /// <summary>UTF-16 LE with a BOM — what Windows itself writes for
    /// <c>default.dic</c>.</summary>
    private static Encoding Utf16 => new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
}
