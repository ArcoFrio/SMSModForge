using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SMSModForge.Shared;

namespace SMSModForge.Localization;

/// <summary>
/// The editor's words, in the language the author picked.
/// <para/>
/// Every text on screen comes from here by key: <c>Loc.T("quests.addQuest")</c>
/// in code, <c>{l:T quests.addQuest}</c> in a window's layout. The English file
/// is built into the editor and is the authority on what exists; a translation
/// is a file of the same shape in the Languages folder beside the editor, or in
/// the author's own Languages folder, where one they made or were given
/// overrides the one that shipped.
/// <para/>
/// The language is chosen once, before the first window: a window's layout
/// reads its texts as it is built, so switching means starting the editor again.
/// </summary>
public static class Loc
{
    /// <summary>What the file is called inside the editor's assembly.</summary>
    private const string EnglishResource = "SMSModForge.Languages.en.txt";

    public const string EnglishCode = "en";

    private static readonly Lazy<TextFile> _english = new(LoadEnglish);

    private static Texts? _current;

    /// <summary>The texts in use. English until <see cref="Use"/> says otherwise.</summary>
    public static Texts Current
    {
        get => _current ??= new Texts(English, null, EnglishCode);
        private set => _current = value;
    }

    /// <summary>The English file, which every language is measured against.</summary>
    public static TextFile English => _english.Value;

    public static string T(string key) => Current.T(key);

    /// <summary>A key that may be empty: a heading a section does not have, a
    /// hint a step does without. Empty is empty, not a missing text.</summary>
    public static string Optional(string key) => string.IsNullOrEmpty(key) ? "" : Current.T(key);

    public static string F(string key, params object[] namesAndValues) => Current.F(key, namesAndValues);

    public static string P(string key, long count, params object[] namesAndValues) => Current.P(key, count, namesAndValues);

    /// <summary>"A, B or C", the way the language joins a list.</summary>
    public static string JoinOr(IEnumerable<string> items) => Current.JoinOr(items.ToList());

    /// <summary>"A, B and C", the way the language joins a list.</summary>
    public static string JoinAnd(IEnumerable<string> items) => Current.JoinAnd(items.ToList());

    /// <summary>"A, B, C": a list with no "and", such as the counts beside a row.</summary>
    public static string JoinList(IEnumerable<string> items) => Current.JoinList(items.ToList());

    /// <summary>The folder of languages that ship with the editor.</summary>
    public static string ShippedFolder => Path.Combine(AppContext.BaseDirectory, "Languages");

    /// <summary>The author's own: translations they made or were given. One
    /// here wins over a shipped one with the same code, and an update of the
    /// editor never touches it.</summary>
    public static string UserFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SMSModForge", "Languages");

    /// <summary>Switch to <paramref name="code"/>. An unknown code is English.</summary>
    public static void Use(string? code)
    {
        Current = Preview(code);
        Changed?.Invoke();
    }

    /// <summary>The language on screen has just changed. Every text a window
    /// took from <c>{l:T}</c> follows it by itself - see
    /// <see cref="LocSource"/>; this is for the rest.</summary>
    public static event Action? Changed;

    /// <summary>
    /// The texts <paramref name="code"/> would show, without switching to it:
    /// how the editor asks "restart in Español?" in Español, to somebody who
    /// may not read the language on screen.
    /// </summary>
    public static Texts Preview(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == EnglishCode) return new Texts(English, null, EnglishCode);
        if (code == PseudoText.Code) return new Texts(English, null, PseudoText.Code);
        var language = Available().FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        return language == null ? new Texts(English, null, EnglishCode) : Preview(language);
    }

    /// <summary>The texts of a language from <see cref="Available"/>, without
    /// looking for it again.</summary>
    public static Texts Preview(Language language)
    {
        TextFile? file = language.Path == null ? null : Read(language.Path);
        // The author's own, over the one that shipped: their words where they
        // wrote some, and the shipped language's everywhere else - see
        // UiTextEdits.Layer.
        if (file != null && language.ShippedPath != null && Read(language.ShippedPath) is { } shipped)
            file = UiTextEdits.Layer(shipped, file, English);
        return file == null
            ? new Texts(English, null, EnglishCode)
            : new Texts(English, file, language.Code);
    }

    /// <summary>
    /// The language to start in: the one the author picked, or, if they never
    /// picked one, Windows' own when there is a translation for it
    /// (<see cref="LanguageMatch"/>).
    /// </summary>
    public static string StartingCode(string? picked)
    {
        if (!string.IsNullOrEmpty(picked)) return picked;
        return LanguageMatch.Best(CultureInfo.CurrentUICulture.Name, Available().Select(l => l.Code)) ?? EnglishCode;
    }

    /// <summary>A language the editor can show, where it came from, and who
    /// translated it, as the file credits them.</summary>
    public sealed record Language(string Code, string Name, string? Path, bool IsUsers, bool IsMachine, string Translators = "")
    {
        /// <summary>The file that shipped for this language, when
        /// <see cref="Path"/> is the author's own laid over it.</summary>
        public string? ShippedPath { get; init; }
    }

    /// <summary>English, then every translation found, the author's own
    /// replacing a shipped one with the same code.</summary>
    public static IReadOnlyList<Language> Available()
    {
        var byCode = new Dictionary<string, Language>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, isUsers) in new[] { (ShippedFolder, false), (UserFolder, true) })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (string path in Directory.EnumerateFiles(folder, "*.txt"))
            {
                string code = System.IO.Path.GetFileNameWithoutExtension(path);
                if (code.Equals(EnglishCode, StringComparison.OrdinalIgnoreCase) || !TextFile.IsKey(code)) continue;
                var file = Read(path);
                if (file == null) continue;
                // The author's own over a shipped one: laid over it, and saying
                // what it says of itself - its name, whether a machine made it,
                // who translated it - only where it says anything. A file of a
                // few corrections says none of that.
                byCode.TryGetValue(code, out var shipped);
                string name = file.Get("language.name") is { Length: > 0 } n ? n : shipped?.Name ?? code;
                string? machineSays = file.Get("language.machineTranslated");
                bool machine = machineSays != null
                    ? string.Equals(machineSays, "yes", StringComparison.OrdinalIgnoreCase)
                    : shipped?.IsMachine ?? false;
                string translators = file.Get("language.translators") is { Length: > 0 } t ? t : shipped?.Translators ?? "";
                byCode[code] = new Language(code, name, path, isUsers, machine, translators)
                {
                    ShippedPath = isUsers && shipped != null && !shipped.IsUsers ? shipped.Path : null,
                };
            }
        }
        var list = new List<Language> { new(EnglishCode, English.Get("language.name") ?? "English", null, false, false) };
        list.AddRange(byCode.Values.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase));
        return list;
    }

    /// <summary>
    /// The codes of <see cref="Available"/>, English first, without reading a
    /// single file. <see cref="Available"/> parses every language to learn its
    /// name and who translated it - about a tenth of a second - and the list
    /// of languages to edit a pack in asked for it on every undo, for nothing
    /// but the codes (2026-09-27).
    /// </summary>
    public static IReadOnlyList<string> AvailableCodes()
    {
        var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in new[] { ShippedFolder, UserFolder })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (string path in Directory.EnumerateFiles(folder, "*.txt"))
            {
                string code = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!code.Equals(EnglishCode, StringComparison.OrdinalIgnoreCase) && TextFile.IsKey(code)) codes.Add(code);
            }
        }
        var list = new List<string> { EnglishCode };
        list.AddRange(codes);
        return list;
    }

    /// <summary>A translation file, or null if it cannot be read at all.</summary>
    public static TextFile? Read(string path)
    {
        try { return TextFile.Parse(File.ReadAllText(path, Encoding.UTF8)); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Written as UTF-8 with a byte-order mark, which every Windows
    /// editor down to old Notepad reads as UTF-8 rather than guessing.</summary>
    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static TextFile LoadEnglish()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EnglishResource)
                           ?? throw new InvalidOperationException(EnglishResource + " is not built into the editor.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return TextFile.Parse(reader.ReadToEnd());
    }
}
