using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SMSModForge.Setup;

/// <summary>
/// The setup's words, from the editor's own language files - its setup.* texts,
/// translated with everything else - in the language Windows is set to,
/// English where a text is not translated. Read from the editor zip the setup
/// carries, or from the Languages folder beside the uninstaller: the setup has
/// no language menu to pick one from, so the computer's is the only sensible
/// guess, as it is for the editor's first start.
/// </summary>
public sealed class SetupTexts
{
    private readonly Dictionary<string, string> _texts;
    private readonly Dictionary<string, string> _english;

    private SetupTexts(Dictionary<string, string> texts, Dictionary<string, string> english)
    {
        _texts = texts;
        _english = english;
    }

    public string Code { get; private init; } = "en";

    public string T(string key)
        => _texts.TryGetValue(key, out var t) && t.Length > 0 ? t
         : _english.TryGetValue(key, out var e) ? e
         : key;

    public string F(string key, params (string Name, string Value)[] gaps)
    {
        string text = T(key);
        foreach (var (name, value) in gaps) text = text.Replace("{" + name + "}", value);
        return text;
    }

    /// <summary>The languages ModForge ships, as their files are named.</summary>
    public static readonly string[] Shipped = { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-Hans" };

    /// <summary>Which of <see cref="Shipped"/> fits a culture: the whole name
    /// first (pt-BR), then the language (pt), Chinese as Simplified.</summary>
    public static string Pick(CultureInfo culture)
    {
        string name = culture.Name;
        var exact = Shipped.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        string language = culture.TwoLetterISOLanguageName;
        if (language == "zh") return "zh-Hans";
        return Shipped.FirstOrDefault(c => c.Split('-')[0].Equals(language, StringComparison.OrdinalIgnoreCase)) ?? "en";
    }

    /// <summary>From an editor zip's Languages folder.</summary>
    public static SetupTexts FromZip(Stream? editorZip, CultureInfo culture)
    {
        string code = Pick(culture);
        if (editorZip == null) return new SetupTexts(new(), new()) { Code = code };
        using var zip = new ZipArchive(editorZip, ZipArchiveMode.Read);
        string? Read(string c)
        {
            var entry = zip.GetEntry("Languages/" + c + ".txt");
            if (entry == null) return null;
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }
        return Make(Read("en"), code == "en" ? null : Read(code), code);
    }

    /// <summary>From a Languages folder (the installed editor's, beside the
    /// uninstaller; the repository's, in tests).</summary>
    public static SetupTexts FromFolder(string folder, CultureInfo culture)
    {
        string code = Pick(culture);
        string? Read(string c)
        {
            string path = Path.Combine(folder, c + ".txt");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        return Make(Read("en"), code == "en" ? null : Read(code), code);
    }

    private static SetupTexts Make(string? english, string? translated, string code)
        => new(Parse(translated), Parse(english)) { Code = code };

    /// <summary>The setup.* lines of a language file: key = text, \n a new line.</summary>
    public static Dictionary<string, string> Parse(string? file)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (file == null) return texts;
        foreach (var raw in file.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (!line.StartsWith("setup.", StringComparison.Ordinal)) continue;
            int at = line.IndexOf(" = ", StringComparison.Ordinal);
            if (at < 0) continue;
            texts[line.Substring(0, at).Trim()] = line.Substring(at + 3).Replace("\\n", "\n");
        }
        return texts;
    }
}
