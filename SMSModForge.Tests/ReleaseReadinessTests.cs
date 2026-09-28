using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using SMSModForge.Localization;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Is this thing fit to publish?
/// <para/>
/// One question so far, and it is asked because the answer was no: every
/// release up to and including 1.2.0 shipped the plugin's F10/F11/F12 scene
/// dumps. Those are diagnostics for whoever is developing the plugin — three
/// function keys a player can press by accident that write megabytes of
/// reflected scene state into their game folder — and they were compiled in
/// unconditionally.
/// <para/>
/// They are behind <c>#if DEBUG</c> now, which makes a Release build clean by
/// construction. What that does NOT prevent is packaging a Debug build by
/// mistake, and nothing in the editor's own test run can see it: the suite
/// compiles in Debug and the plugin is a separate assembly for a different
/// framework. So this reads the bytes of whatever is about to ship.
/// <para/>
/// Point it at the DLL or at the packaged zip, and at the last release's
/// English (see <see cref="Every_language_has_this_versions_texts"/>):
/// <code>
///   set SMSMODFORGE_RELEASE_PLUGIN=...\Starmaker - ModForge Plugin 1.3.0.zip
///   git show v1.6.0:SMSModForge/Languages/en.txt > last-en.txt
///   set SMSMODFORGE_LAST_RELEASE_EN=last-en.txt
///   dotnet test --filter ReleaseReadinessTests
/// </code>
/// </summary>
public sealed class ReleaseReadinessTests
{
    private readonly ITestOutputHelper _out;
    public ReleaseReadinessTests(ITestOutputHelper o) => _out = o;

    /// <summary>
    /// The string the plugin compiles in only under <c>#if DEBUG</c>.
    /// <para/>
    /// Repeated here rather than referenced: the plugin targets .NET Framework
    /// and this project does not, so there is no assembly to read it from. A
    /// second test below proves the two have not drifted, by finding it in the
    /// plugin's source.
    /// </summary>
    private const string DebugMarker = "SMSMODFORGE_PLUGIN_DEBUG_BUILD";

    /// <summary>Everything a Release plugin must not contain — the marker, and
    /// the dump machinery it guards.</summary>
    private static readonly string[] MustNotShip =
    {
        DebugMarker, "DumpScriptsOnScreen", "DumpDialogues", "DumpConditionDebug", "ScriptDump",
        "QuestJournalDump",
    };

    /// <summary>The plugin DLL's bytes, from a DLL or from inside a zip.</summary>
    private static byte[]? PluginBytes(string path)
    {
        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return File.ReadAllBytes(path);

        using var zip = ZipFile.OpenRead(path);
        var entry = zip.Entries.FirstOrDefault(
            e => e.Name.Equals("SMSModForge.PackPlugin.dll", StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;

        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Whether a .NET assembly's bytes hold this text, in either of
    /// the two encodings one can appear in: UTF-16 for a string literal, UTF-8
    /// for a type or member name.</summary>
    private static bool Holds(byte[] assembly, string text)
        => Contains(assembly, Encoding.Unicode.GetBytes(text))
        || Contains(assembly, Encoding.UTF8.GetBytes(text));

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return true;
        }
        return false;
    }

    [Fact]
    public void The_plugin_about_to_ship_carries_no_debug_machinery()
    {
        string? path = Environment.GetEnvironmentVariable("SMSMODFORGE_RELEASE_PLUGIN");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _out.WriteLine("SMSMODFORGE_RELEASE_PLUGIN not set; nothing to check.");
            return;
        }

        var bytes = PluginBytes(path);
        Assert.True(bytes != null, $"no SMSModForge.PackPlugin.dll in {path}");
        _out.WriteLine($"{Path.GetFileName(path)} -> {bytes!.Length:N0} bytes");

        var found = MustNotShip.Where(s => Holds(bytes, s)).ToList();
        foreach (string s in MustNotShip)
            _out.WriteLine($"   {(found.Contains(s) ? "PRESENT" : "absent ")}  {s}");

        // Two different faults, and saying the wrong one sends somebody to the
        // wrong fix. The marker can only come from #if DEBUG, so it means a
        // Debug build was packaged. The dump machinery without it means a build
        // from before any of this was conditional - 1.2.0 was compiled with
        // -c Release and still carries all of it.
        string why = found.Contains(DebugMarker)
            ? "this is a Debug build of the plugin. Build it with -c Release before packaging."
            : "this plugin predates the #if DEBUG guard, so its scene dumps ship to players. "
              + "Rebuild it from current source.";

        Assert.True(found.Count == 0, why + " Found: " + string.Join(", ", found));
    }

    /// <summary>
    /// The words the game shows players come from the English file built into
    /// the plugin, and in any other language from the translation files beside
    /// it. A plugin built without the first shows keys where its words should
    /// be; a zip without the second ships a language setting that does nothing.
    /// </summary>
    [Fact]
    public void The_plugin_about_to_ship_carries_its_words_and_the_zip_its_translations()
    {
        string? path = Environment.GetEnvironmentVariable("SMSMODFORGE_RELEASE_PLUGIN");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _out.WriteLine("SMSMODFORGE_RELEASE_PLUGIN not set; nothing to check.");
            return;
        }

        var bytes = PluginBytes(path);
        Assert.True(bytes != null, $"no SMSModForge.PackPlugin.dll in {path}");
        Assert.True(Holds(bytes!, Shared.GameTexts.EnglishResource), "the plugin has no English file built in");
        Assert.True(Holds(bytes!, "game.save.returnToMenu = "), "the plugin's English file has no [In the game] texts");
        // The control: a text that is in no file must not be found.
        Assert.False(Holds(bytes!, "game.save.noSuchText = "));

        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return;
        using var zip = ZipFile.OpenRead(path);
        var inZip = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shipped = Directory.EnumerateFiles(Path.Combine(TranslationCoverageTests.EditorDir, "Languages"), "*.txt")
                               .Select(Path.GetFileName)
                               .Where(n => !n!.Equals("en.txt", StringComparison.OrdinalIgnoreCase))
                               .ToList();
        foreach (string? name in shipped)
        {
            _out.WriteLine("   " + name);
            Assert.Contains("BepInEx/plugins/SMSModForge/Languages/" + name, inZip);
        }
    }

    /// <summary>
    /// Every language has what this version added to or changed in the English.
    /// <para/>
    /// Nothing else stops a release short of it. A text added during a version
    /// is English-only until somebody translates it, and a missing line, or one
    /// translated from English that has since changed, quietly shows the
    /// English: the ordinary suite fails only on a line that is damaged. So
    /// here every shipped language must have no text missing, empty, short of a
    /// count form, translated from outdated English, or marked for review.
    /// <para/>
    /// The last release's English finds the one gap that check cannot: a file
    /// brought up to date with <c>SMSMODFORGE_REFRESH_TRANSLATIONS=1</c> starts
    /// every new text as the English itself, which counts as filled in. What
    /// this version added or reworded and is still word for word the English
    /// is listed for a look rather than failed - a name or a shortcut is right
    /// that way, and only a person can tell.
    /// </summary>
    [Fact]
    public void Every_language_has_this_versions_texts()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMSMODFORGE_RELEASE_PLUGIN")))
        {
            _out.WriteLine("SMSMODFORGE_RELEASE_PLUGIN not set; not a release, nothing to check.");
            return;
        }

        string? last = Environment.GetEnvironmentVariable("SMSMODFORGE_LAST_RELEASE_EN");
        Assert.False(string.IsNullOrWhiteSpace(last),
            "Set SMSMODFORGE_LAST_RELEASE_EN to the last release's English "
            + "(git show v<last>:SMSModForge/Languages/en.txt > last-en.txt), or to none "
            + "when the last release had no language files.");
        TextFile? before = null;
        if (!last!.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            Assert.True(File.Exists(last), "no file at SMSMODFORGE_LAST_RELEASE_EN: " + last);
            before = TextFile.Parse(File.ReadAllText(last));
        }

        // A count's forms are one sentence, so they are compared as one.
        static string Sentence(string key)
        {
            int dot = key.LastIndexOf('.');
            return dot > 0 && key[(dot + 1)..] is "zero" or "one" or "two" or "few" or "many" or "other"
                ? key[..dot] : key;
        }

        var english = TranslationCoverageTests.EnglishSource;
        var changed = english.Entries.Where(e => before?.Get(e.Key) != e.Text)
                                     .Select(e => Sentence(e.Key))
                                     .ToHashSet();
        _out.WriteLine($"{changed.Count} texts added or reworded since the last release");

        var problems = new List<string>();
        foreach (string path in Directory.EnumerateFiles(Path.Combine(TranslationCoverageTests.EditorDir, "Languages"), "*.txt")
                                         .Where(p => !Path.GetFileName(p).Equals("en.txt", StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string code = Path.GetFileNameWithoutExtension(path);
            var check = TextCheck.Run(english, TextFile.Parse(File.ReadAllText(path)), code);
            int stillEnglish = 0;
            foreach (var f in check.Findings)
            {
                if (f.IsError || f.Kind is TextCheck.Kind.Missing or TextCheck.Kind.Empty or TextCheck.Kind.MissingForm
                                        or TextCheck.Kind.EnglishChanged or TextCheck.Kind.NeedsReview)
                    problems.Add($"{code}: {TranslationFiles.Say(f)}");
                else if (f.Kind == TextCheck.Kind.Untranslated && changed.Contains(Sentence(f.Key)))
                {
                    stillEnglish++;
                    _out.WriteLine($"   {code}: still the English - {f.Key} = {f.English}");
                }
            }
            _out.WriteLine($"{code}: {check.Translated} of {check.Total}, {stillEnglish} of this version's still the English");
        }

        foreach (string p in problems) _out.WriteLine(p);
        Assert.True(problems.Count == 0,
            $"Texts a language is missing or has out of date: {problems.Count} - see the output. "
            + "Translate them, bring the files in with SMSMODFORGE_REFRESH_TRANSLATIONS, and run this again.");
    }

    [Fact]
    public void The_marker_this_checks_for_is_the_one_the_plugin_defines()
    {
        // The control. Without it this file could go on checking for a string
        // the plugin stopped using, pass on every Debug build ever packaged,
        // and report a clean bill of health forever.
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here != null && !Directory.Exists(Path.Combine(here.FullName, "SMSModForge.PackPlugin")))
            here = here.Parent;

        Assert.NotNull(here);
        string source = File.ReadAllText(
            Path.Combine(here!.FullName, "SMSModForge.PackPlugin", "Plugin.cs"));

        Assert.Contains($"DebugBuildMarker = \"{DebugMarker}\"", source);

        // And it must be behind the conditional, or a Release build would carry
        // it and this check would fail on a plugin that is perfectly fine.
        int at = source.IndexOf(DebugMarker, StringComparison.Ordinal);
        string before = source[..at];
        Assert.EndsWith("#if DEBUG", before[..before.LastIndexOf("#if DEBUG", StringComparison.Ordinal)]
                        + "#if DEBUG");

        _out.WriteLine($"plugin defines {DebugMarker} under #if DEBUG");
    }
}
