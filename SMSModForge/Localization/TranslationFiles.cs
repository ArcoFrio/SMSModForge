using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SMSModForge.Shared;

namespace SMSModForge.Localization;

/// <summary>
/// What the Language menu does with files: start or bring up to date an
/// author's own translation, read one back and say what is wrong with it, and
/// put back keys a Replace All changed.
/// <para/>
/// No windows here. The menu asks the questions and shows the answers; this
/// is the part that has to be right, so it is the part the tests drive.
/// </summary>
public static class TranslationFiles
{
    /// <summary>
    /// The language a code names, as its own speakers write it - "Español",
    /// "日本語" - or null when Windows has never heard of the code.
    /// </summary>
    public static string? NativeName(string code)
    {
        if (!TextFile.IsKey(code)) return null;
        try
        {
            // Predefined only: otherwise an unknown code comes back as a
            // made-up culture named after itself, which nobody speaks.
            var culture = CultureInfo.GetCultureInfo(code, predefinedOnly: true);
            string name = culture.NativeName;
            return name.Length == 0 ? null : char.ToUpper(name[0], culture) + name.Substring(1);
        }
        catch (CultureNotFoundException) { return null; }
    }

    /// <summary>
    /// Write the author's own file for <paramref name="code"/> into
    /// <paramref name="folder"/>: brought up to date if it is there already,
    /// otherwise started from the one that shipped (so a correction starts
    /// from what is on screen rather than from nothing), otherwise from the
    /// English. Nothing anybody wrote is lost - see <see cref="TextFileWriter"/>.
    /// </summary>
    /// <returns>Where it was written, and whether it was there before.</returns>
    public static (string Path, bool Existed) CreateOrUpdate(string code, string folder, string shippedFolder)
    {
        string path = System.IO.Path.Combine(folder, code + ".txt");
        bool existed = File.Exists(path);
        TextFile? start = existed ? Loc.Read(path) : null;

        if (start == null)
        {
            string shipped = System.IO.Path.Combine(shippedFolder, code + ".txt");
            if (File.Exists(shipped)) start = Loc.Read(shipped);
        }
        if (start == null)
        {
            // A new language names itself in its own words from the first line.
            start = new TextFile();
            start.Add(new TextFile.Entry
            {
                Key = "language.name",
                Text = NativeName(code) ?? code,
                English = Loc.English.Get("language.name"),
            });
        }

        var notes = new List<string>
        {
            Loc.F("translation.file.title", "language", NativeName(code) ?? code, "code", code),
            "",
        };
        notes.AddRange(Loc.English.TopNotes.Skip(1));

        // Updating rewrites a file somebody may have spent days in. Keep the
        // one being replaced beside it, the same way fixing keys does: the
        // merge is careful, but "careful" is not a reason to be the only copy.
        // Named .txt.bak rather than .bak.txt so it is not read as a language.
        if (existed)
        {
            try { File.Copy(path, path + ".bak", overwrite: true); }
            catch (IOException) { /* a backup that cannot be written is not a reason to refuse the update */ }
            catch (UnauthorizedAccessException) { }
        }

        Loc.Write(path, TextFileWriter.Build(Loc.English, start, code, notes));
        return (path, existed);
    }

    /// <summary>The language a file is in, from its name: <c>pt-BR.txt</c> is pt-BR.</summary>
    public static string CodeOf(string path) => System.IO.Path.GetFileNameWithoutExtension(path);

    /// <summary>
    /// Every finding, said in the language on screen, worst first. Missing
    /// lines are listed up to <paramref name="missingShown"/>, and texts still
    /// in English only counted: in a file somebody has just started, those two
    /// are most of it, and a list of them is not something anybody reads.
    /// <para/>
    /// <paramref name="pack"/> says it of a pack's translation, which is
    /// measured against the pack's own words rather than ModForge's English.
    /// </summary>
    public static string Report(TextCheck.Result result, int missingShown = 40, bool pack = false)
    {
        var sb = new StringBuilder();
        int percent = result.Total == 0 ? 100 : (int)Math.Floor(100.0 * result.Translated / result.Total);
        sb.AppendLine(Loc.F("translation.check.summary", "translated", result.Translated, "total", result.Total, "percent", percent));

        var errors = result.Findings.Where(f => f.IsError).ToList();
        var rest = result.Findings.Where(f => !f.IsError && f.Kind != TextCheck.Kind.Untranslated
                                              && f.Kind != TextCheck.Kind.Missing).ToList();
        var missing = result.Findings.Where(f => f.Kind == TextCheck.Kind.Missing).ToList();
        int untranslated = result.Of(TextCheck.Kind.Untranslated);

        if (errors.Count == 0 && rest.Count == 0 && missing.Count == 0 && untranslated == 0)
        {
            sb.AppendLine().AppendLine(Loc.T("translation.check.clean"));
            return sb.ToString();
        }

        if (errors.Count > 0)
        {
            sb.AppendLine().AppendLine(Loc.P("translation.check.errors", errors.Count));
            foreach (var f in errors) sb.AppendLine("  • " + Say(f, pack));
        }
        if (rest.Count > 0)
        {
            sb.AppendLine().AppendLine(Loc.P("translation.check.notes", rest.Count));
            foreach (var f in rest) sb.AppendLine("  • " + Say(f, pack));
        }
        if (missing.Count > 0)
        {
            sb.AppendLine().AppendLine(pack ? Loc.P("packText.check.missing", missing.Count)
                                            : Loc.P("translation.check.missing", missing.Count));
            foreach (var f in missing.Take(missingShown)) sb.AppendLine("  • " + Say(f, pack));
            if (missing.Count > missingShown)
                sb.AppendLine("  " + Loc.P("translation.check.andMore", missing.Count - missingShown));
        }
        if (untranslated > 0)
            sb.AppendLine().AppendLine(pack ? Loc.P("packText.check.untranslated", untranslated)
                                            : Loc.P("translation.check.untranslated", untranslated));
        return sb.ToString();
    }

    /// <summary>One finding in words; <paramref name="pack"/> as in <see cref="Report"/>.</summary>
    public static string Say(TextCheck.Finding f, bool pack = false)
    {
        switch (f.Kind)
        {
            case TextCheck.Kind.BadLine:
                return Loc.F("translation.check.badLine", "line", f.Line, "found", f.Found);
            case TextCheck.Kind.Duplicate:
                return Loc.F("translation.check.duplicate", "line", f.Line, "key", f.Key, "first", f.Suggestion);
            case TextCheck.Kind.Unknown:
                if (pack)
                    return f.Suggestion != null
                        ? Loc.F("packText.check.unknownNear", "line", f.Line, "key", f.Key, "near", f.Suggestion)
                        : Loc.F("packText.check.unknown", "line", f.Line, "key", f.Key);
                return f.Suggestion != null
                    ? Loc.F("translation.check.unknownNear", "line", f.Line, "key", f.Key, "near", f.Suggestion)
                    : Loc.F("translation.check.unknown", "line", f.Line, "key", f.Key);
            case TextCheck.Kind.Missing:
                return Loc.F("translation.check.missingOne", "key", f.Key, "english", f.English);
            case TextCheck.Kind.Empty:
                return pack ? Loc.F("packText.check.empty", "line", f.Line, "key", f.Key)
                            : Loc.F("translation.check.empty", "line", f.Line, "key", f.Key);
            case TextCheck.Kind.MissingForm:
                return Loc.F("translation.check.missingForm", "key", f.Key, "forms", f.Suggestion);
            case TextCheck.Kind.Codes:
                var parts = new List<string>();
                if (f.Lost.Count > 0) parts.Add(Loc.F("translation.check.lost", "codes", string.Join(" ", f.Lost)));
                if (f.Added.Count > 0)
                    parts.Add(pack ? Loc.F("packText.check.added", "codes", string.Join(" ", f.Added))
                                   : Loc.F("translation.check.added", "codes", string.Join(" ", f.Added)));
                return pack ? Loc.F("packText.check.codes", "line", f.Line, "key", f.Key, "what", Loc.JoinAnd(parts))
                            : Loc.F("translation.check.codes", "line", f.Line, "key", f.Key, "what", Loc.JoinAnd(parts));
            case TextCheck.Kind.AccessKey:
                return Loc.F("translation.check.accessKey", "line", f.Line, "key", f.Key);
            case TextCheck.Kind.EnglishChanged:
                return pack ? Loc.F("packText.check.englishChanged", "line", f.Line, "key", f.Key, "was", f.Was, "now", f.English)
                            : Loc.F("translation.check.englishChanged", "line", f.Line, "key", f.Key, "was", f.Was, "now", f.English);
            case TextCheck.Kind.NeedsReview:
                return pack ? Loc.F("packText.check.review", "line", f.Line, "key", f.Key, "now", f.English)
                            : Loc.F("translation.check.review", "line", f.Line, "key", f.Key, "now", f.English);
            default:
                return f.Key;
        }
    }

    /// <summary>
    /// The file with every damaged key the check could place put back as it
    /// was - only the key, on its own line; the text a translator wrote stays
    /// exactly as it is. Returns how many were put back.
    /// </summary>
    public static int PutKeysBack(string path, TextCheck.Result result)
    {
        var fixes = result.Findings
            .Where(f => f.Kind == TextCheck.Kind.Unknown && f.Suggestion != null && f.Line > 0)
            .ToDictionary(f => f.Line, f => (From: f.Key, To: f.Suggestion!));
        if (fixes.Count == 0) return 0;

        string content = File.ReadAllText(path, Encoding.UTF8);
        if (content.Length > 0 && content[0] == '﻿') content = content.Substring(1);
        string newline = content.Contains("\r\n") ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n").Split('\n');
        int done = 0;
        foreach (var fix in fixes)
        {
            int i = fix.Key - 1;
            if (i < 0 || i >= lines.Length) continue;
            string line = lines[i];
            int at = line.IndexOf(fix.Value.From, StringComparison.Ordinal);
            int eq = line.IndexOf('=');
            if (at < 0 || eq < 0 || at > eq) continue;
            lines[i] = line.Substring(0, at) + fix.Value.To + line.Substring(at + fix.Value.From.Length);
            done++;
        }
        if (done > 0)
        {
            File.Copy(path, path + ".bak", overwrite: true);
            Loc.Write(path, string.Join(newline, lines));
        }
        return done;
    }
}
