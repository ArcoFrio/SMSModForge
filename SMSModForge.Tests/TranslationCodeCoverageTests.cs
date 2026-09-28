using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// No sentence in the editor's code reaches the screen without passing
/// through the translation.
/// <para/>
/// The layouts are the easy half. The other half is every message, note,
/// summary and validation line the code builds, and a sentence built in code
/// looks the same in the source whether it is shown or only logged. So this
/// reads the syntax around each one: a log line, an exception, an attribute or
/// a pattern is the program talking to itself; anything else with words in it
/// is on screen somewhere, or could be, and belongs in the English file.
/// <para/>
/// What is deliberately English says so where it is written: a comment on the
/// line reading <c>English on purpose:</c> and why. That keeps the reason next
/// to the text, where the next person to touch it will see it.
/// </summary>
public class TranslationCodeCoverageTests
{
    private readonly ITestOutputHelper _out;
    public TranslationCodeCoverageTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Files that are the game's own words: names of its quests, places,
    /// variables and characters, spelled the way the game spells them. The game
    /// is English only, and the editor shows these names exactly as a player
    /// sees them in it.
    /// </summary>
    internal static readonly Dictionary<string, string> GamesOwnWords = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"Model\VanillaGameVariables.cs"] = "the game's variable names",
        [@"Model\VanillaQuests.cs"] = "the game's quests: their names, tasks and journal text",
        [@"Model\VanillaPlaces.cs"] = "the game's levels, by the names its scenes have",
        [@"Model\VanillaRoomTalks.cs"] = "the game's levels, by the names its scenes have",
        [@"Model\WorldMapDistricts.cs"] = "the districts on the game's world map",
    };

    private static IEnumerable<string> EditorSources()
    {
        string dir = TranslationCoverageTests.EditorDir;
        string sep = Path.DirectorySeparatorChar.ToString();
        return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                        .Where(p => !p.Contains(sep + "obj" + sep) && !p.Contains(sep + "bin" + sep));
    }

    [Fact]
    public void EverySentenceTheEditorsCodeShowsComesFromTheEnglishFile()
    {
        string dir = TranslationCoverageTests.EditorDir;
        var found = new List<string>();
        var perFile = new Dictionary<string, int>();
        foreach (string path in EditorSources())
        {
            string rel = Path.GetRelativePath(dir, path);
            if (GamesOwnWords.ContainsKey(rel)) continue;
            foreach (var (line, text) in CodeWords.In(File.ReadAllText(path)))
            {
                found.Add($"{rel}:{line}  {text}");
                perFile[rel] = perFile.GetValueOrDefault(rel) + 1;
            }
        }
        foreach (var kv in perFile.OrderByDescending(kv => kv.Value)) _out.WriteLine($"{kv.Value,5}  {kv.Key}");
        _out.WriteLine("");
        foreach (string f in found) _out.WriteLine(f);
        Assert.Empty(found);
    }

    /// <summary>The scan can fail: each way a sentence reaches the screen is
    /// found, and each way the program only talks to itself is not.</summary>
    [Fact]
    public void TheCodeScanFindsShownSentences_AndLeavesThePrograms()
    {
        const string code = """
            class C
            {
                [Description("An attribute is metadata")]
                void M(int n, string name)
                {
                    MessageBox.Show("Could not save the pack.", "Save");
                    Tell("The pack has " + n + " characters in it.");
                    string s = $"Remove '{name}' from the pack?";
                    button.Content = "Delete";
                    var i = new Issue(Severity.Error, where, "Character name is required", "character.nameMissing");
                    Log.LogWarning("Quest places: nothing matched " + name);
                    Debug.WriteLine("Took the slow path here");
                    throw new InvalidOperationException("This should never happen at all");
                    var r = new Regex(@"^\w+ \d+$");
                    string k = Loc.T("quests.addQuest");
                    string e = "Shown to nobody at all"; // English on purpose: written into a log file.
                    // English on purpose: the game's own quest name.
                    string q = "Just A Drink (Liz)";
                    string id = "character.nameMissing";
                    OnPropertyChanged("Title");
                    string where = $"Dialogue '{d}' node {n}";
                    string token = "vanilla:Beach";
                }
            }
            """;
        var found = CodeWords.In(code).Select(f => f.Text).ToList();
        foreach (string f in found) _out.WriteLine(f);
        Assert.Contains(found, f => f.Contains("Could not save the pack."));
        Assert.Contains(found, f => f == "Save");
        Assert.Contains(found, f => f.Contains("The pack has") && f.Contains("characters in it."));
        Assert.Contains(found, f => f.Contains("Remove '") && f.Contains("from the pack?"));
        Assert.Contains(found, f => f == "Delete");
        Assert.Contains(found, f => f == "Character name is required");
        Assert.Contains(found, f => f.StartsWith("Dialogue '") && f.Contains("' node "));
        Assert.DoesNotContain(found, f => f.Contains("vanilla:Beach"));
        Assert.Equal(7, found.Count);
    }
}

/// <summary>Sentences in C# source that nothing marks as the program's own.</summary>
internal static class CodeWords
{
    private static readonly Regex Word = new(@"[^\W\d_]{2,}", RegexOptions.Compiled);
    private static readonly Regex Letters = new(@"\p{L}+", RegexOptions.Compiled);

    /// <summary>
    /// Words with a space between them, whatever else is in the gap:
    /// "Dialogue '{…}' node {…}" is a sentence with its names left out, while
    /// "vanilla:Beach" and "place.key" are names.
    /// </summary>
    private static bool IsProse(string text)
    {
        var words = Letters.Matches(text);
        if (words.Count < 2 || !words.Any(w => w.Length >= 2)) return false;
        for (int i = 0; i + 1 < words.Count; i++)
        {
            int from = words[i].Index + words[i].Length;
            string gap = text.Substring(from, words[i + 1].Index - from);
            if (gap.Any(char.IsWhiteSpace)) return true;
        }
        return false;
    }

    private static readonly Regex TalksToItself = new(
        @"^(Log\w*|WriteLine|Write|Trace\w*|Debug\w*|Print|Fail|Assert\w*|Fatal|Warn\w*|Info|ExpectTabChange)$", RegexOptions.Compiled);

    /// <summary>Where a single word is still a text: a caption, a button, a heading.</summary>
    private static readonly Regex ShownSinks = new(
        @"^(Show|Tell|Ask|Confirm|Notify|Announce|Prompt)$", RegexOptions.Compiled);

    private static readonly Regex ShownProperties = new(
        @"^(Text|Content|Header|ToolTip|Title|Caption|Label|Description|Summary|Hint|Body|Heading|Message|Watermark|Note|Tip)$",
        RegexOptions.Compiled);

    public static List<(int Line, string Text)> In(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        var found = new List<(int, string)>();
        var reported = new HashSet<TextSpan>();

        foreach (var node in root.DescendantNodes())
        {
            string? text = node switch
            {
                LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression) => lit.Token.ValueText,
                InterpolatedStringExpressionSyntax interp => string.Concat(interp.Contents.Select(c =>
                    c is InterpolatedStringTextSyntax t ? t.TextToken.ValueText : "{…}")),
                _ => null,
            };
            if (text == null || !Word.IsMatch(text)) continue;
            if (node.Parent is InterpolationSyntax) continue;

            // A sentence built from pieces is one finding, reported whole.
            SyntaxNode whole = node;
            while (whole.Parent is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression)) whole = b;
            while (whole.Parent is ParenthesizedExpressionSyntax) whole = whole.Parent;

            bool prose = IsProse(text) || text.EndsWith("…") || text.EndsWith("...");
            if (!prose && !IsSingleWordShown(whole, text)) continue;
            if (IsTheProgramsOwn(whole)) continue;

            int line = tree.GetLineSpan(node.Span).StartLinePosition.Line;
            if (MarkedEnglish(lines, line) || MarkedAbove(whole)) continue;
            if (!reported.Add(whole.Span)) continue;
            found.Add((line + 1, whole == node ? text : Describe(whole)));
        }
        return found;
    }

    private static string Describe(SyntaxNode whole)
    {
        var parts = whole.DescendantNodesAndSelf().Select(n => n switch
        {
            LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression) => lit.Token.ValueText,
            InterpolatedStringExpressionSyntax i => string.Concat(i.Contents.Select(c =>
                c is InterpolatedStringTextSyntax t ? t.TextToken.ValueText : "{…}")),
            _ => null,
        }).Where(s => s != null);
        return string.Join("{…}", parts);
    }

    /// <summary>A single word counts only where the code puts it on screen.</summary>
    private static bool IsSingleWordShown(SyntaxNode whole, string text)
    {
        if (!char.IsUpper(text.TrimStart()[0])) return false;
        if (whole.Parent is AssignmentExpressionSyntax assign && assign.Right == whole)
        {
            string target = assign.Left switch
            {
                MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                _ => "",
            };
            return ShownProperties.IsMatch(target);
        }
        if (whole.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } })
            return ShownSinks.IsMatch(NameOf(call.Expression));
        return false;
    }

    private static bool IsTheProgramsOwn(SyntaxNode node)
    {
        for (var n = node.Parent; n != null; n = n.Parent)
        {
            switch (n)
            {
                case AttributeSyntax:
                    return true;
                case InvocationExpressionSyntax call:
                {
                    string name = NameOf(call.Expression);
                    if (TalksToItself.IsMatch(name) || name == "nameof") return true;
                    if (call.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner }
                        && owner.Identifier.Text is "Regex" or "Loc" or "Path" or "File" or "Directory")
                        return true;
                    break;
                }
                case ObjectCreationExpressionSyntax create:
                {
                    string type = create.Type.ToString();
                    // Font names are names: "Consolas, Courier New" is a list
                    // Windows reads, not words anybody translates.
                    if (type.EndsWith("Exception") || type == "Regex" || type == "FontFamily") return true;
                    break;
                }
                case StatementSyntax or MemberDeclarationSyntax:
                    return false;
            }
        }
        return false;
    }

    private static string NameOf(ExpressionSyntax e) => e switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        _ => "",
    };

    /// <summary>
    /// "English on purpose:" in front of the statement, the field or the
    /// initializer entry the text is part of: one note above a list of stored
    /// values speaks for every value in it.
    /// </summary>
    private static bool MarkedAbove(SyntaxNode node)
    {
        const string mark = "English on purpose:";
        for (var n = node; n != null; n = n.Parent)
        {
            if (n.GetLeadingTrivia().ToFullString().Contains(mark)) return true;
            if (n is StatementSyntax or MemberDeclarationSyntax) return false;
        }
        return false;
    }

    /// <summary>"English on purpose:" on the line, or alone on the line above.</summary>
    private static bool MarkedEnglish(string[] lines, int line)
    {
        const string mark = "English on purpose:";
        if (line < lines.Length && lines[line].Contains(mark)) return true;
        for (int i = line - 1; i >= 0; i--)
        {
            string l = lines[i].Trim();
            if (!l.StartsWith("//")) return false;
            if (l.Contains(mark)) return true;
        }
        return false;
    }
}
