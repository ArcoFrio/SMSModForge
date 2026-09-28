using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The English file and the editor agree both ways: every choice a dropdown
/// lists has its text, and every text in the file is one the editor shows.
/// <para/>
/// A dropdown of stored values falls back to the value itself when it has no
/// text, which in English looks right and in every other language is the one
/// English word on the screen. And a text nothing shows any more is work a
/// translator does for nobody, in every language, every version.
/// </summary>
public class TranslationKeyUseTests
{
    private readonly ITestOutputHelper _out;
    public TranslationKeyUseTests(ITestOutputHelper output) => _out = output;

    private static readonly Regex ChoiceCombo = new(
        @"<ComboBox\s(?<attrs>[^>]*[^/>])>(?<body>(?:(?!</ComboBox>|<ComboBox\s).)*?ChoiceTextConverter\.Instance\}, ConverterParameter=(?<field>\w+)\})",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex StaticSource = new(@"ItemsSource=""\{x:Static vm:(?<type>\w+)\.(?<member>\w+)\}""", RegexOptions.Compiled);
    private static readonly Regex BindingSource = new(@"ItemsSource=""\{Binding (?<path>\w+)\}""", RegexOptions.Compiled);

    /// <summary>
    /// Lists a dropdown binds to by name on the row it sits on, which change
    /// with the row: the union of what they can be.
    /// </summary>
    private static readonly Dictionary<string, IEnumerable<string>> RowLists = new()
    {
        ["Categories"] = NodeActionViewModel.SetActiveCategories.Concat(NodeActionViewModel.SetSpriteCategories),
    };

    /// <summary>Every (field, stored value) a dropdown shows through the
    /// choice texts: the layout's own dropdowns, then the action and
    /// condition fields that offer a fixed list.</summary>
    private static List<(string Field, string Option, string Where)> ChoicesShown()
    {
        var list = new List<(string, string, string)>();
        foreach (string path in Directory.EnumerateFiles(TranslationCoverageTests.EditorDir, "*.xaml", SearchOption.AllDirectories))
        {
            if (path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                || path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            string xaml = File.ReadAllText(path);
            foreach (Match m in ChoiceCombo.Matches(xaml))
            {
                string field = m.Groups["field"].Value;
                string attrs = m.Groups["attrs"].Value;
                string where = Path.GetFileName(path) + " line " + (xaml.Take(m.Index).Count(c => c == '\n') + 1);
                IEnumerable<string>? options = null;
                if (StaticSource.Match(attrs) is { Success: true } s)
                {
                    var type = typeof(MainViewModel).Assembly.GetType("SMSModForge.ViewModel." + s.Groups["type"].Value);
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                    object? value = type?.GetProperty(s.Groups["member"].Value, flags)?.GetValue(null)
                                    ?? type?.GetField(s.Groups["member"].Value, flags)?.GetValue(null);
                    options = (value as IEnumerable)?.OfType<string>();
                }
                else if (BindingSource.Match(attrs) is { Success: true } b && RowLists.TryGetValue(b.Groups["path"].Value, out var row))
                {
                    options = row;
                }
                Assert.True(options != null,
                    $"{where}: cannot tell what the '{field}' dropdown lists. Bind it to a static list, or name its list in RowLists.");
                foreach (string o in options!) list.Add((field, o, where));
            }
        }

        foreach (var (kind, byType) in new[] { ("action", ActionSchemas.ByType), ("condition", ConditionSchemas.ByType) })
            foreach (var (type, schemas) in byType)
                foreach (var p in schemas.Where(p => p.Type == ParamType.Choice))
                    foreach (string o in p.FixedOptions)
                        list.Add((p.Key, o, kind + " " + type + "." + p.Key));
        return list;
    }

    /// <summary>
    /// Fixed options that are names the game reads rather than words: the
    /// Unity component and its properties that Set component property writes.
    /// They are shown as stored, the way the Unity documentation spells them.
    /// </summary>
    private static readonly HashSet<string> UnityNames = new(StringComparer.Ordinal)
    {
        "CanvasGroup", "alpha", "interactable", "blocksRaycasts",
    };

    [Fact]
    public void EveryChoiceADropdownListsHasItsText()
    {
        var shown = ChoicesShown();
        // The control: if the layout scan found nothing it would pass.
        Assert.Contains(shown, c => c.Field == "questOperation");
        Assert.Contains(shown, c => c.Field == "voiceTemplate");
        Assert.Contains(shown, c => c.Field == "kind" && c.Option == "Places");

        var missing = shown.Where(c => !UnityNames.Contains(c.Option)
                                       && !Loc.English.Has(ParamSchema.ChoiceKey(c.Field, c.Option)))
                           .Select(c => $"{c.Where}: {ParamSchema.ChoiceKey(c.Field, c.Option)} for '{c.Option}'")
                           .Distinct().ToList();
        foreach (string m in missing) _out.WriteLine(m);
        Assert.Empty(missing);

        // And the Unity names really are only names the game reads.
        var unityOffered = shown.Where(c => UnityNames.Contains(c.Option)).Select(c => c.Where).Distinct().ToList();
        Assert.All(unityOffered, w => Assert.StartsWith("action " + NodeActionTypes.SetComponentProperty + ".", w));
    }

    [Fact]
    public void EveryEnumShownAsWordsHasATextForEachMember_AndNoMore()
    {
        var byType = Loc.English.Entries.Select(e => e.Key).Where(k => k.StartsWith("enum.", StringComparison.Ordinal))
                        .GroupBy(k => k.Split('.')[1]).ToList();
        Assert.NotEmpty(byType);
        var problems = new List<string>();
        foreach (var group in byType)
        {
            var type = typeof(MainViewModel).Assembly.GetTypes().SingleOrDefault(t => t.IsEnum && t.Name == group.Key);
            if (type == null) { problems.Add($"enum.{group.Key}: there is no such enum"); continue; }
            var wanted = Enum.GetValues(type).Cast<Enum>().Select(EnumTextConverter.KeyOf).ToHashSet();
            foreach (string k in wanted.Except(group)) problems.Add(k + ": missing");
            foreach (string k in group.Except(wanted)) problems.Add(k + ": the enum has no such member");
        }
        foreach (string p in problems) _out.WriteLine(p);
        Assert.Empty(problems);
    }

    /// <summary>The parts of what a pack changes, which the save warning
    /// says in words that depend on where each falls in the sentence.</summary>
    private static readonly string[] ChangeParts =
    {
        SaveLoadChecks.QuestsPart, SaveLoadChecks.ConversationsPart,
        SaveLoadChecks.QuestStartsPart, SaveLoadChecks.ConversationPlaysPart,
    };

    [Fact]
    public void EveryPartOfWhatAPackChangesHasItsWordsForEveryPlaceInTheSentence()
    {
        foreach (string part in ChangeParts)
            foreach (string where in new[] { "first", "then", "listed" })
                Assert.True(Loc.English.Has("game.change." + part + "." + where), "game.change." + part + "." + where);
    }

    /// <summary>
    /// A key ending in <c>.one</c>, <c>.other</c> and the rest is read as one
    /// form of a text with a number in it - by the writer, which asks a
    /// translation for the forms its language needs, and by the check, which
    /// reports the ones it lacks. A key that merely ends in that word
    /// ("heading.other") sends a translator looking for a count that is not
    /// there, so it has to be named something else.
    /// </summary>
    [Fact]
    public void AKeyThatLooksLikeACountIsOne()
    {
        var byBase = Loc.English.Entries.Select(e => e.Key).Where(k => TextFile.FormOf(k) != null)
                        .GroupBy(TextFile.BaseOf, StringComparer.OrdinalIgnoreCase);
        var wrong = byBase.Where(g => !g.Select(TextFile.FormOf).OrderBy(f => f)
                                        .SequenceEqual(new[] { "one", "other" }))
                          .Select(g => g.Key + ": " + string.Join(", ", g.Select(TextFile.FormOf)))
                          .ToList();
        foreach (string w in wrong) _out.WriteLine(w);
        Assert.Empty(wrong);
    }

    [Fact]
    public void EveryTextInTheEnglishFileIsOneTheEditorShows()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Named in the code or the layout, as a whole key.
        var quoted = new Regex(@"""([A-Za-z][\w\-]*(?:\.[\w\-]+)+)""");
        // A plural's key goes to its converter as the parameter.
        var inLayout = new Regex(@"(?:\{l:T |ConverterParameter=)([\w.\-]+)");
        foreach (string path in Directory.EnumerateFiles(TranslationCoverageTests.EditorDir, "*.*", SearchOption.AllDirectories)
                                         .Concat(Directory.EnumerateFiles(Path.Combine(TranslationCoverageTests.EditorDir, "..", "Shared"), "*.cs"))
                                         .Concat(Directory.EnumerateFiles(Path.Combine(TranslationCoverageTests.EditorDir, "..", "SMSModForge.PackPlugin"), "*.cs")))
        {
            if (path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                || path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            string ext = Path.GetExtension(path);
            if (ext != ".cs" && ext != ".xaml") continue;
            string text = File.ReadAllText(path);
            foreach (Match m in quoted.Matches(text)) used.Add(m.Groups[1].Value);
            if (ext == ".xaml")
                foreach (Match m in inLayout.Matches(text)) used.Add(m.Groups[1].Value);
        }

        // Built from something the editor lists.
        foreach (var t in ThemeManager.All) used.Add(t.NameKey);
        foreach (string part in ChangeParts)
            foreach (string where in new[] { "first", "then", "listed" })
                used.Add("game.change." + part + "." + where);
        foreach (var q in PreviewQualityManager.All) { used.Add(q.NameKey); used.Add(q.NameKey + ".tip"); }
        foreach (var k in InputKeys.All) { used.Add(k.Name); used.Add(k.GroupKey); }
        foreach (var (field, option, _) in ChoicesShown()) used.Add(ParamSchema.ChoiceKey(field, option));
        foreach (var type in typeof(MainViewModel).Assembly.GetTypes().Where(t => t.IsEnum))
            foreach (Enum value in Enum.GetValues(type))
                used.Add(EnumTextConverter.KeyOf(value));

        var keys = Loc.English.Entries.Select(e => e.Key).ToList();
        var unused = keys.Where(k => !used.Contains(k) && !used.Contains(TextFile.BaseOf(k))).ToList();
        foreach (string k in unused) _out.WriteLine(k + " = " + Loc.English.Get(k));
        _out.WriteLine($"{keys.Count - unused.Count} of {keys.Count} texts in use");
        Assert.Empty(unused);
    }
}
