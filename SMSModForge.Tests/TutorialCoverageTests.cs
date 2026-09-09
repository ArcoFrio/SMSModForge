using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.Tutorials;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The two ways a tutorial goes wrong that walking it cannot catch.
/// <para/>
/// <see cref="TutorialProgressionTests"/> proves every step can be completed and
/// <see cref="TutorialShapeTests"/> proves every anchor exists. Both read the
/// pack and the window. Neither reads the WORDS, and neither notices a feature
/// that has no tutorial at all — which is how the UI tab shipped untaught and
/// how a step went on naming a folder packs are no longer installed in.
/// <para/>
/// Both were found by a person looking, which is the problem: looking is what
/// missed them the first time. They are gates now.
/// </summary>
public class TutorialCoverageTests
{
    private readonly ITestOutputHelper _out;
    public TutorialCoverageTests(ITestOutputHelper o) => _out = o;

    // ── Every tab is taught by something ─────────────────────────────

    [Fact]
    public void Every_tab_in_the_window_has_a_tutorial_that_visits_it()
    {
        // Tabs are how an author decides what to learn next, so a tab nothing
        // teaches is a feature discovered by poking at it. Read off the real
        // window rather than a list kept here: adding a tab then fails this
        // until something teaches it, which is the whole point.
        var visited = new Dictionary<int, List<string>>();
        foreach (var tut in TutorialCatalog.All)
            foreach (var step in tut.Steps)
            {
                if (step.Tab < 0) continue;
                if (!visited.TryGetValue(step.Tab, out var who))
                    visited[step.Tab] = who = new List<string>();
                if (!who.Contains(tut.Id)) who.Add(tut.Id);
            }

        WindowHarness.Run(window =>
        {
            var tabs = (TabControl)window.FindName("MainTabs");
            Assert.NotNull(tabs);
            WindowHarness.Pump();

            var untaught = new List<string>();
            for (int i = 0; i < tabs.Items.Count; i++)
            {
                string header = ((TabItem)tabs.Items[i]).Header?.ToString() ?? "(unnamed)";
                visited.TryGetValue(i, out var who);

                _out.WriteLine($"  [{i,2}] {header,-16} "
                               + (who == null ? "NOTHING" : string.Join(", ", who)));
                if (who == null) untaught.Add($"[{i}] {header}");
            }

            Assert.True(untaught.Count == 0,
                        "no tutorial visits: " + string.Join(", ", untaught));
        });
    }

    [Fact]
    public void Every_tutorial_on_the_ladder_visits_a_tab()
    {
        // The control for the check above: a tutorial whose steps never name a
        // tab would satisfy nothing, and would also mean the coverage map is
        // being built from something that does not describe where an author
        // actually goes.
        foreach (var tut in TutorialCatalog.All.Where(t => t.IsOnLadder))
            Assert.True(tut.Steps.Any(s => s.Tab >= 0),
                        $"{tut.Id} never switches to a tab");
    }

    // ── Nothing is taught that no longer exists ──────────────────────

    /// <summary>
    /// Words that must not appear in tutorial prose, and what to say instead.
    /// <para/>
    /// DERIVED, not listed. An action folded out of the picker is one an author
    /// can no longer choose, so naming it in a tutorial sends them hunting a
    /// dropdown entry that is not there — and deriving it means the next merge
    /// is covered without anybody remembering to come back here. Hand-listing
    /// would have missed four of these: only ActivateScene was obvious.
    /// <para/>
    /// The conditions come from <see cref="VariableMerge"/>, which owns the
    /// record of what it folded away. Everything else is hand-kept, and
    /// <see cref="The_hand_kept_entries_still_describe_something_retired"/>
    /// stops those outliving what they ban.
    /// </summary>
    private static IEnumerable<(string Word, string Instead)> Retired()
    {
        var offered = new HashSet<string>(new ViewModel.MainViewModel().ActionTypes,
                                          StringComparer.Ordinal);

        foreach (string type in NodeActionTypes.All)
            if (!offered.Contains(type))
                yield return (type, "the action that replaced it in the picker");

        foreach (string type in VariableMerge.SupersededTypes)
            yield return (type, "VariableCompare, with its Comparison field");

        // Where a pack is installed. The old folder is still READ, so a pack
        // sitting there works - but it is not where anybody should be sent,
        // and a tutorial is exactly where somebody is sent.
        yield return ("ModPacks", "the " + SMSModForge.Shared.ModsFolder.Name
                                  + " folder in the game folder");
    }

    private static IEnumerable<(string Where, string Text)> AllProse()
    {
        foreach (var tut in TutorialCatalog.All)
        {
            yield return ($"{tut.Id} (summary)", tut.Summary);
            yield return ($"{tut.Id} (title)", tut.Title);

            for (int i = 0; i < tut.Steps.Count; i++)
            {
                var step = tut.Steps[i];
                yield return ($"{tut.Id}[{i}] \"{step.Title}\"", step.Title + "\n" + step.Body);
                if (!string.IsNullOrEmpty(step.Hint))
                    yield return ($"{tut.Id}[{i}] hint", step.Hint);
            }
        }
    }

    [Fact]
    public void No_tutorial_names_something_the_editor_no_longer_offers()
    {
        var found = new List<string>();

        foreach (var (word, instead) in Retired())
            foreach (var (where, text) in AllProse())
                if (text.IndexOf(word, StringComparison.Ordinal) >= 0)
                    found.Add($"{where} says \"{word}\" - say \"{instead}\"");

        foreach (string f in found) _out.WriteLine(f);
        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    [Fact]
    public void The_hand_kept_entries_still_describe_something_retired()
    {
        // The derived entries look after themselves. This is the one that
        // cannot: if the install folder were ever named ModPacks again, the
        // tutorials would be barred from saying something true.
        Assert.NotEqual("ModPacks", SMSModForge.Shared.ModsFolder.Name);

        _out.WriteLine($"{Retired().Count()} names retired; packs install into "
                       + SMSModForge.Shared.ModsFolder.Name);
    }

    // ── What the tests themselves cannot see ─────────────────────────

    [Fact]
    public void The_ladder_is_not_built_differently_per_configuration()
    {
        // Read out of the SOURCE, because this is the one thing no assertion
        // about TutorialCatalog.All can reach: the suite compiles in Debug, so
        // a list built differently under #else is invisible to every check
        // here - including the coverage gate above, which passed while the
        // shipped build had no UI tutorial in it at all.
        //
        // The fix was to state the ladder once and put the conditional around
        // the diagnostics alone. This is what keeps it that way.
        string source = File.ReadAllText(SourceOf("TutorialCatalog.cs"));

        int declarations = Regex.Matches(source, @"IReadOnlyList<TutorialDef>\s+All\b").Count;
        _out.WriteLine($"{declarations} declaration(s) of All");
        Assert.True(declarations == 1,
                    $"the ladder is declared {declarations} times - one per configuration is "
                    + "exactly the shape that shipped a tutorial nobody could see");

        // And the conditional that remains may only add the diagnostics.
        int at = source.IndexOf("IReadOnlyList<TutorialDef>", StringComparison.Ordinal);
        string expression = source[at..source.IndexOf(";", at, StringComparison.Ordinal)];

        Assert.DoesNotContain("#else", expression);
        foreach (Match conditional in Regex.Matches(expression, @"#if[^\n]*\n([^\n]*)"))
            Assert.Contains("_smoke", conditional.Groups[1].Value);
    }

    /// <summary>A file in the editor project, found from the test binary.</summary>
    private static string SourceOf(string fileName)
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here != null && !Directory.Exists(Path.Combine(here.FullName, "SMSModForge")))
            here = here.Parent;

        Assert.NotNull(here);
        string found = Directory
            .EnumerateFiles(Path.Combine(here!.FullName, "SMSModForge"), fileName,
                            SearchOption.AllDirectories)
            .FirstOrDefault()!;

        Assert.True(found != null, $"could not find {fileName} to read");
        return found!;
    }

    // ── The newest thing an author has to get right ──────────────────

    [Fact]
    public void The_first_tutorial_explains_how_a_pack_is_released()
    {
        // Publishing and versioning are the last things an author does and the
        // easiest to get wrong - a pack extracted into the wrong folder starts
        // the game perfectly and simply is not there. Getting started is where
        // somebody learns what the end looks like.
        var first = TutorialCatalog.All.First(t => t.IsOnLadder);
        string prose = string.Join("\n", first.Steps.Select(s => s.Title + "\n" + s.Body));

        _out.WriteLine($"checking {first.Id} ({first.Steps.Count} steps)");

        Assert.Contains("Publish", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("version", prose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SMSModForge.Shared.ModsFolder.Name, prose, StringComparison.Ordinal);
    }
}
