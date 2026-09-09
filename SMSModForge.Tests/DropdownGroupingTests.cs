using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Sorting the long dropdowns under headings.
/// <para/>
/// The game's variables are the reason: 1,644 distinct names in one flat list is a
/// scroll bar with no landmarks. Under the 60 lists that own them — Gallery,
/// Events, Inventory, Cooldown — it becomes somewhere to look rather than
/// somewhere to hunt.
/// <para/>
/// What is checked here is the ANSWER each list groups by, since a heading that
/// files things wrongly is worse than no heading: it sends an author to the
/// wrong place confidently.
/// </summary>
public sealed class DropdownGroupingTests
{
    private readonly ITestOutputHelper _out;
    public DropdownGroupingTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void EveryVanillaVariableLandsUnderTheListThatOwnsIt()
    {
        var names = VanillaGameVariables.AllNames;
        var homeless = names.Where(n => string.IsNullOrEmpty(VanillaGameVariables.ListOf(n)))
                            .ToList();

        var groups = names.GroupBy(VanillaGameVariables.ListOf)
                          .OrderByDescending(g => g.Count())
                          .ToList();

        _out.WriteLine($"{names.Count} variables in {groups.Count} groups; "
                       + $"{homeless.Count} without one");
        foreach (var g in groups.Take(6)) _out.WriteLine($"   {g.Key,-22} {g.Count()}");

        // Not one is left without a home, so nothing falls into an "Other"
        // bucket the author has to go looking through.
        Assert.Empty(homeless);

        // And the grouping is actually useful: dozens of headings rather than
        // one, and the largest still a fraction of the whole.
        Assert.True(groups.Count > 40, $"only {groups.Count} groups");
        Assert.True(groups[0].Count() < names.Count / 4,
                    "one group holds most of the list, which is no better than a flat one");
    }

    [Fact]
    public void ANameInSeveralListsIsFiledUnderTheFirst()
    {
        // Names repeat across lists and the address is name-only, so the first
        // list is the one anything resolving it will find. Filing it anywhere
        // else would point an author at a variable they are not editing.
        var repeated = VanillaGameVariables.All
            .GroupBy(v => v.Name, System.StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(v => v.List).Distinct().Count() > 1)
            .FirstOrDefault();

        if (repeated == null) { _out.WriteLine("no name appears in two lists"); return; }

        string first = VanillaGameVariables.All.First(
            v => string.Equals(v.Name, repeated.Key, System.StringComparison.OrdinalIgnoreCase)).List;
        _out.WriteLine($"'{repeated.Key}' is in {string.Join(", ", repeated.Select(v => v.List))}"
                       + $" -> filed under {VanillaGameVariables.ListOf(repeated.Key)}");

        Assert.Equal(first, VanillaGameVariables.ListOf(repeated.Key));
    }

    [Fact]
    public void SomethingTheGameDoesNotHaveIsNotGivenAList()
    {
        // The control: a lookup that answered for everything would file the
        // author's own variables under a game heading.
        Assert.Equal("", VanillaGameVariables.ListOf("a-name-the-game-never-had"));
        Assert.Equal("", VanillaGameVariables.ListOf(""));
        Assert.Equal("", VanillaGameVariables.ListOf(null));
    }

    [Fact]
    public void PackVariablesAreGroupedByTheFoldersTheirAuthorMade()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            // Through the editor's own add, so the view model's list and the
            // pack's stay in step - the options are built from the former.
            foreach (string name in new[] { "loose", "filed", "deep" })
            {
                vm.AddVariableCommand.Execute(null);
                vm.Variables.Last().Name = name;
            }
            WindowHarness.Pump();

            var inner = new VariableFolderDef { Name = "Chapter 2" };
            inner.Variables.Add("deep");
            var outer = new VariableFolderDef { Name = "Story" };
            outer.Variables.Add("filed");
            outer.Folders.Add(inner);
            vm.Pack.VariableFolders.Add(outer);

            vm.RebuildVariableNameOptions();
            WindowHarness.Pump();

            var view = vm.VariableNameOptionsGrouped;
            var headings = view.Groups
                .Cast<System.Windows.Data.CollectionViewGroup>()
                .ToDictionary(g => (string)g.Name, g => g.Items.Cast<string>().ToList());

            foreach (var h in headings) _out.WriteLine($"   {h.Key}: {string.Join(", ", h.Value)}");

            Assert.Contains("filed", headings["Story"]);

            // A nested folder reads as a path, so two folders called "Notes"
            // under different chapters do not collapse into one heading.
            Assert.Contains("deep", headings["Story / Chapter 2"]);

            // And anything not filed is still offered rather than vanishing.
            Assert.Contains("loose", headings["Ungrouped"]);
        });
    }

    [Fact]
    public void LevelsSplitIntoThePacksOwnAndTheGames()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddPlaceCommand.Execute(null);
            WindowHarness.Pump();

            var headings = vm.LevelOptionsGrouped.Groups
                .Cast<System.Windows.Data.CollectionViewGroup>()
                .ToDictionary(g => (string)g.Name, g => g.Items.Count);

            foreach (var h in headings) _out.WriteLine($"   {h.Key}: {h.Value}");

            Assert.Equal(2, headings.Count);
            Assert.Equal(1, headings["This pack"]);
            Assert.True(headings["The game's own"] > 100);
        });
    }
}
