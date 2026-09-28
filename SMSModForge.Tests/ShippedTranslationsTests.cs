using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The translations that ship with the editor - and, from the same files, with
/// the game plugin - read cleanly: nothing the check calls a mistake, every
/// one naming its own language and saying whether a person has checked it.
/// <para/>
/// What is missing or out of date is reported, not failed: English moves on
/// between releases, and a translation that lags shows English for the texts
/// it lacks, which is what it is meant to do. Before a release, refresh them
/// (below) and have the new texts translated.
/// </summary>
public class ShippedTranslationsTests
{
    private readonly ITestOutputHelper _out;
    public ShippedTranslationsTests(ITestOutputHelper output) => _out = output;

    private static string Folder => Path.Combine(TranslationCoverageTests.EditorDir, "Languages");

    private static IEnumerable<string> Shipped()
        => Directory.EnumerateFiles(Folder, "*.txt")
                    .Where(p => !Path.GetFileName(p).Equals("en.txt", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void EveryShippedTranslationReadsCleanly()
    {
        var problems = new List<string>();
        foreach (string path in Shipped())
        {
            string code = Path.GetFileNameWithoutExtension(path);
            var file = Loc.Read(path)!;
            var check = TextCheck.Run(Loc.English, file, code);
            _out.WriteLine($"{code}: {check.Translated} of {check.Total} translated, "
                           + $"{check.Of(TextCheck.Kind.Missing)} missing, "
                           + $"{check.Of(TextCheck.Kind.EnglishChanged) + check.Of(TextCheck.Kind.NeedsReview)} to review, "
                           + $"{check.Of(TextCheck.Kind.Untranslated)} still English");

            if (TranslationFiles.NativeName(code) == null) problems.Add($"{code}: not a language Windows knows");
            if (string.IsNullOrWhiteSpace(file.Get("language.name")) || file.Get("language.name") == "English")
                problems.Add($"{code}: language.name does not name the language");
            if (file.Get("language.machineTranslated") is not ("yes" or "no"))
                problems.Add($"{code}: language.machineTranslated is neither yes nor no");
            foreach (var f in check.Findings.Where(f => f.IsError || f.Kind == TextCheck.Kind.MissingForm || f.Kind == TextCheck.Kind.AccessKey))
                problems.Add($"{code}: {TranslationFiles.Say(f)}");
        }
        foreach (string p in problems) _out.WriteLine(p);
        Assert.Empty(problems);
    }

    /// <summary>
    /// Not a check: the step that writes the shipped files. Runs only when
    /// <c>SMSMODFORGE_REFRESH_TRANSLATIONS</c> is set.
    /// <para/>
    /// Set to <c>1</c>, it brings every shipped file up to date with the
    /// English, the way Language ▸ New or update a translation does an
    /// author's own: new texts in English, changed ones marked. Set to a
    /// folder, it first takes translated texts from <c>&lt;folder&gt;/&lt;code&gt;/*.txt</c>
    /// (plain key = text lines, as a translator or a machine hands them
    /// back) over what the file had, for every code with a folder there.
    /// </summary>
    [Fact]
    public void RefreshShippedTranslations()
    {
        string? how = Environment.GetEnvironmentVariable("SMSMODFORGE_REFRESH_TRANSLATIONS");
        if (string.IsNullOrWhiteSpace(how))
        {
            _out.WriteLine("SMSMODFORGE_REFRESH_TRANSLATIONS not set; nothing written.");
            return;
        }

        var codes = Shipped().Select(Path.GetFileNameWithoutExtension).ToList();
        bool fromParts = Directory.Exists(how);
        if (fromParts)
            codes = codes.Union(Directory.EnumerateDirectories(how).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase).ToList();

        foreach (string? code in codes)
        {
            string path = Path.Combine(Folder, code + ".txt");
            TextFile? had = File.Exists(path) ? Loc.Read(path) : null;

            var merged = new TextFile();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (fromParts && Directory.Exists(Path.Combine(how, code!)))
            {
                foreach (string part in Directory.EnumerateFiles(Path.Combine(how, code!), "*.txt").OrderBy(p => p, StringComparer.Ordinal))
                {
                    var file = TextFile.Parse(File.ReadAllText(part));
                    foreach (var bad in file.BadLines) _out.WriteLine($"{code} {Path.GetFileName(part)} line {bad.Line}: {bad.Content}");
                    foreach (var e in file.Entries)
                        if (taken.Add(e.Key)) merged.Add(e);
                }
            }
            if (had != null)
                foreach (var e in had.Entries)
                    if (taken.Add(e.Key)) merged.Add(e);
            if (had != null) merged.TopNotes.AddRange(had.TopNotes);

            var notes = new List<string>
            {
                Loc.F("translation.file.title", "language", TranslationFiles.NativeName(code!) ?? code!, "code", code!),
                "",
            };
            notes.AddRange(Loc.English.TopNotes.Skip(1));
            Loc.Write(path, TextFileWriter.Build(Loc.English, merged, code!, notes));

            var check = TextCheck.Run(Loc.English, Loc.Read(path)!, code!);
            _out.WriteLine($"{code}: written, {check.Translated} of {check.Total} translated");
            foreach (var f in check.Findings.Where(f => f.IsError || f.Kind == TextCheck.Kind.MissingForm || f.Kind == TextCheck.Kind.AccessKey))
                _out.WriteLine($"   {TranslationFiles.Say(f)}");
        }
    }
}
