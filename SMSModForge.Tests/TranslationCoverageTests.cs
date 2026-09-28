using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Localization;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Nothing the editor shows is left out of the English file.
/// <para/>
/// These read the SOURCE: every window layout, and the English file beside
/// them. A word typed straight into a layout is a word no translation can
/// reach, and it looks exactly like a translated one until somebody switches
/// language and finds it still in English - so the scan fails on it here,
/// where it was typed.
/// </summary>
public class TranslationCoverageTests
{
    private readonly ITestOutputHelper _out;
    public TranslationCoverageTests(ITestOutputHelper output) => _out = output;

    internal static string EditorDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "SMSModForge");
                if (File.Exists(Path.Combine(candidate, "SMSModForge.csproj"))) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("SMSModForge is not above " + AppContext.BaseDirectory);
        }
    }

    internal static string EnglishPath => Path.Combine(EditorDir, "Languages", "en.txt");

    internal static TextFile EnglishSource => TextFile.Parse(File.ReadAllText(EnglishPath));

    internal static IEnumerable<string> XamlFiles
        => Directory.EnumerateFiles(EditorDir, "*.xaml", SearchOption.AllDirectories)
                    .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                             && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    private static string Rel(string path) => Path.GetRelativePath(EditorDir, path);

    [Fact]
    public void TheEnglishFileReadsCleanly_AndIsTheOneBuiltIn()
    {
        var english = EnglishSource;
        foreach (var bad in english.BadLines) _out.WriteLine($"line {bad.Line}: {bad.Content}");
        Assert.Empty(english.BadLines);
        Assert.True(english.Entries.Count > 1000, "the English file has " + english.Entries.Count + " texts");
        // The file in the exe is this file, not a copy from another build.
        Assert.Equal(english.Entries.Select(e => e.Key + "=" + e.Text), Loc.English.Entries.Select(e => e.Key + "=" + e.Text));
    }

    private static readonly Regex KeyUse = new(@"\{l:T\s+([\w.\-]+)\s*\}", RegexOptions.Compiled);

    private static readonly Regex PluralUse = new(
        @"l:PluralConverter\.Instance\}\s*,\s*ConverterParameter=([\w.\-]+)", RegexOptions.Compiled);

    /// <summary>A value put into a sentence: the key is a whole text, with a gap.</summary>
    private static readonly Regex FormatUse = new(
        @"l:FormatConverter\.Instance\}\s*,\s*ConverterParameter=([\w.\-]+)", RegexOptions.Compiled);

    [Fact]
    public void EveryKeyAWindowAsksForIsInTheEnglishFile()
    {
        var english = EnglishSource;
        var missing = new List<string>();
        int uses = 0;
        foreach (string path in XamlFiles)
        {
            string text = File.ReadAllText(path);
            foreach (Match m in KeyUse.Matches(text))
            {
                uses++;
                if (!english.Has(m.Groups[1].Value)) missing.Add(Rel(path) + ": " + m.Groups[1].Value);
            }
            foreach (Match m in PluralUse.Matches(text))
            {
                uses++;
                if (!english.Has(m.Groups[1].Value + ".other")) missing.Add(Rel(path) + ": " + m.Groups[1].Value + " (a count)");
            }
            foreach (Match m in FormatUse.Matches(text))
            {
                uses++;
                if (!english.Has(m.Groups[1].Value)) missing.Add(Rel(path) + ": " + m.Groups[1].Value + " (a sentence with a gap)");
            }
        }
        foreach (string m in missing) _out.WriteLine(m);
        Assert.True(uses > 1000, "found " + uses + " uses - is the pattern still right?");
        Assert.Empty(missing);
    }

    [Fact]
    public void NoWindowLayoutHasWordsOfItsOwn()
    {
        var found = new List<string>();
        foreach (string path in XamlFiles)
            foreach (var (line, what) in XamlWords.In(File.ReadAllText(path)))
                found.Add($"{Rel(path)}:{line}  {what}");
        foreach (string f in found) _out.WriteLine(f);
        Assert.Empty(found);
    }

    /// <summary>
    /// The scan above passing means nothing unless it can fail: every place a
    /// word can hide in a layout, each of which it has to find.
    /// </summary>
    [Fact]
    public void TheLayoutScanFindsWordsWhereverTheyCanHide()
    {
        const string layout = """
            <Window xmlns="x" Title="A window">
                <!-- <Button Content="Inside a comment is not on screen"/> -->
                <Button Content="Save" ToolTip="Writes the pack."/>
                <MenuItem Header="_File" InputGestureText="Ctrl+S"/>
                <TextBlock Text="{Binding N, StringFormat='Adds {0} objects'}"/>
                <TextBlock Text="{Binding N, StringFormat=vanilla: {0}}"/>
                <TextBlock Text="{}{0} literal words"/>
                <Setter Property="ToolTip" Value="A tip in a style"/>
                <MultiBinding StringFormat="{}{0} was {1}"/>
                <ComboBoxItem>Closed</ComboBoxItem>
                <Run Text=" issue(s)"/>
                <sys:String x:Key="Greeting">Hello there</sys:String>
                <Button Style="{StaticResource ToolbarButtonWithKey}" Tag="Del"/>
                <TextBlock Text="{}{PC}"/>
                <TextBlock Text="{l:T quests.addQuest}" ToolTip="{Binding Tip}"/>
                <TextBlock Text="{Binding N, StringFormat={l:T home.issuesCount}}"/>
                <TextBlock Text="100%"/>
                <sys:String>BustSource</sys:String>
                <ctl:PathPickerBox Filter="Audio files (*.ogg)|*.ogg"/>
                <ctl:PathPickerBox Filter="{x:Static view:PickerFilters.Audio}"/>
            </Window>
            """;
        var found = XamlWords.In(layout).Select(f => f.What).ToList();
        foreach (string f in found) _out.WriteLine(f);
        foreach (string word in new[]
                 {
                     "A window", "Save", "Writes the pack.", "_File", "Ctrl+S", "Adds {0} objects", "vanilla: {0}",
                     "{0} literal words", "A tip in a style", "{0} was {1}", "Closed", " issue(s)", "Hello there", "Del",
                     "Audio files",
                 })
            Assert.Contains(found, f => f.Contains(word));
        Assert.DoesNotContain(found, f => f.Contains("comment"));
        Assert.DoesNotContain(found, f => f.Contains("{PC}") || f.Contains("quests.addQuest") || f.Contains("100%")
                                          || f.Contains("BustSource") || f.Contains("issuesCount"));
        Assert.Equal(15, found.Count);
    }
}

/// <summary>Words typed straight into a window layout.</summary>
internal static class XamlWords
{
    private static readonly Regex Attr = new(
        @"(?<=\s)(Text|Content|Header|ToolTip|Title|InputGestureText|Filter)=""([^""]*)""", RegexOptions.Compiled);

    private static readonly Regex SetterValue = new(
        @"<Setter\s+Property=""(?:Text|Content|ToolTip|Header|Title)""\s+Value=""([^""]*)""", RegexOptions.Compiled);

    private static readonly Regex FormatAttr = new(@"(?<=\s)StringFormat=""([^""]*)""", RegexOptions.Compiled);

    private static readonly Regex FormatInBinding = new(
        @"StringFormat=(?:'((?:[^']|'')*)'|([^,{}'](?:[^,{}]|\{\d+(?::[^}]*)?\})*))", RegexOptions.Compiled);

    private static readonly Regex KeyTag = new(
        @"<[\w:.]+\b[^>]*ToolbarButtonWithKey[^>]*?\sTag=""([^""{][^""]*)""", RegexOptions.Compiled);

    private static readonly Regex ElementText = new(@"<([\w:.]+)((?:\s[^>]*)?)>([^<]*[^\W\d_]{2}[^<]*)</\1>", RegexOptions.Compiled);

    /// <summary>A dialogue token such as {PC}: syntax, not language.</summary>
    private static readonly Regex Token = new(@"\{[A-Z]+\}", RegexOptions.Compiled);

    /// <summary>Letters, once the layout's escapes are read: <c>&amp;#x25CF;</c> is
    /// a dot, not the word "x25CF".</summary>
    private static bool HasWords(string s)
        => Regex.IsMatch(Token.Replace(System.Net.WebUtility.HtmlDecode(s), ""), @"[^\W\d_]{2}");

    public static List<(int Line, string What)> In(string xaml)
    {
        // Comments go, but keep their length so line numbers still point right.
        string text = Regex.Replace(xaml, "<!--.*?-->", m => new string(m.Value.Select(c => c == '\n' ? '\n' : ' ').ToArray()),
                                    RegexOptions.Singleline);
        var found = new List<(int, string)>();
        void Add(int at, string what) => found.Add((text.Take(at).Count(c => c == '\n') + 1, what));

        foreach (Match m in Attr.Matches(text))
        {
            string v = m.Groups[2].Value;
            if (v.StartsWith("{}")) { if (HasWords(v[2..])) Add(m.Index, m.Groups[1].Value + "=\"" + v[2..] + "\""); continue; }
            if (v.StartsWith("{"))
            {
                foreach (Match f in FormatInBinding.Matches(v))
                {
                    string fmt = f.Groups[1].Success ? f.Groups[1].Value.Replace("''", "'") : f.Groups[2].Value;
                    if (HasWords(fmt)) Add(m.Index, "StringFormat " + fmt);
                }
                continue;
            }
            if (HasWords(v)) Add(m.Index, m.Groups[1].Value + "=\"" + v + "\"");
        }
        foreach (Match m in SetterValue.Matches(text))
        {
            string v = m.Groups[1].Value;
            if (!v.StartsWith("{") && HasWords(v)) Add(m.Index, "Setter " + v);
        }
        foreach (Match m in FormatAttr.Matches(text))
        {
            string v = m.Groups[1].Value;
            if (v.StartsWith("{}")) v = v[2..];
            else if (v.StartsWith("{")) continue;
            if (HasWords(v)) Add(m.Index, "StringFormat " + v);
        }
        foreach (Match m in KeyTag.Matches(text))
            if (HasWords(m.Groups[1].Value)) Add(m.Index, "Tag " + m.Groups[1].Value);
        foreach (Match m in ElementText.Matches(text))
        {
            string tag = m.Groups[1].Value, attrs = m.Groups[2].Value;
            // A string in a list of names (a grouping property), or an enum
            // value in an array, is the program's spelling of something - the
            // words a person sees come from a template. A keyed string is a text.
            bool programs = (tag == "sys:String" && !attrs.Contains("x:Key")) || tag.StartsWith("model:");
            if (!programs) Add(m.Index, "<" + tag + ">" + m.Groups[3].Value.Trim());
        }
        return found;
    }
}
