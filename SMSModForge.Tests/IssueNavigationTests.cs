using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Validation;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Double-clicking a validation issue goes to the thing it is complaining
/// about.
/// <para/>
/// Nothing tested this, and for the game's own characters it did nothing at
/// all. The jump asks the character tree's <c>ItemContainerGenerator</c> for
/// the row; that generator reaches into groups perfectly well, but it cannot
/// hand back a container that was never built — and the character tree files
/// the game's cast under a heading that starts CLOSED. So the lookup returned
/// null, the jump gave up before doing anything, and a double-click on "this
/// bust is the wrong size" selected nothing, scrolled nowhere and flashed
/// nothing. It looked exactly like a list that is not clickable.
/// <para/>
/// It worked for the pack's own characters throughout, whose heading opens by
/// default — which is why it went unnoticed, and why the tests below that
/// matter use one of the game's. <see cref="TheOffendingFieldItselfIsFound"/>
/// uses a pack character on purpose and passes either way: it is here for the
/// second half of the jump, not for this.
/// <para/>
/// Driven through the real window: every part of this is view work — a tab
/// index, a tree container, a named field element — and none of it exists
/// without one.
/// </summary>
public sealed class IssueNavigationTests
{
    private readonly ITestOutputHelper _out;
    public IssueNavigationTests(ITestOutputHelper o) => _out = o;

    /// <summary>A character with more than one bust, so landing on the right
    /// one is a real claim rather than the only option.</summary>
    private static (CharacterViewModel Them, OutfitViewModel Bust) Subject(MainViewModel vm)
    {
        var them = vm.Characters.First(c => c.Outfits.Count > 1);
        return (them, them.Outfits[them.Outfits.Count - 1]);
    }

    private static void Navigate(MainWindow window, string where)
    {
        window.NavigateToIssue(new ValidationIssue(Severity.Warning, where, "in a test"));
        // The jump defers its tree work so the tab's content and the tree's
        // containers exist first, so draining the queue IS the wait.
        WindowHarness.Pump();
        WindowHarness.Pump();
    }

    [Fact]
    public void AnIssueOnABustSelectsThatBust()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var (them, bust) = Subject(vm);

            // Deliberately somewhere else first, so "it was already selected"
            // cannot be what makes this pass.
            vm.SelectedCharacter = vm.Characters.First(c => c != them);
            WindowHarness.Pump();

            Navigate(window, $"characters[{them.Name}].outfits[{bust.Model.Key}].mouth[2]");

            _out.WriteLine($"jumped to {them.Name}/{bust.Model.Key} -> "
                           + $"{vm.SelectedCharacter?.Name}/{vm.SelectedOutfit?.Model.Key}");

            Assert.Same(them, vm.SelectedCharacter);
            Assert.Same(bust, vm.SelectedOutfit);
        });
    }

    [Fact]
    public void AnIssueOnTheCharacterItselfSelectsTheCharacter()
    {
        // No outfits[…] in the path — the complaint is about the character, so
        // landing on them is the whole job.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var (them, _) = Subject(vm);

            vm.SelectedCharacter = vm.Characters.First(c => c != them);
            WindowHarness.Pump();

            Navigate(window, $"characters[{them.Name}].displayName");

            Assert.Same(them, vm.SelectedCharacter);
        });
    }

    [Fact]
    public void ItOpensTheGroupTheCharacterIsFiledUnder()
    {
        // The reason the lookup failed, stated as its own fact: one of the
        // game's characters sits inside a CLOSED group heading, and a row
        // inside a closed one has never been built. Whatever finds the row has
        // to open the heading first, or there is nothing to find, select or
        // scroll to - however the looking is done.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var (them, bust) = Subject(vm);

            Navigate(window, $"characters[{them.Name}].outfits[{bust.Model.Key}].mouth[2]");

            var tree = (TreeView)window.FindName("CharacterTree");
            var rows = new List<TreeViewItem>();
            Collect(tree, rows);

            var theirs = rows.FirstOrDefault(r => ReferenceEquals(r.DataContext, them));
            var theBust = rows.FirstOrDefault(r => ReferenceEquals(r.DataContext, bust));
            _out.WriteLine($"{rows.Count} row(s) realised; character row: {theirs != null}; "
                           + $"bust row: {theBust != null}");

            Assert.True(theirs != null,
                        "the character's row was never built, so nothing could be shown");
            Assert.True(theirs!.IsExpanded, "...and it was never opened to reach the bust");

            // The bust is what the issue named, so the bust is what ends up
            // selected - the character's row is the way in, not the target.
            Assert.True(theBust != null, "the bust's own row was never built");
            Assert.True(theBust!.IsSelected, "...and it is not the selected one");
        });
    }

    [Fact]
    public void TheOffendingFieldItselfIsFound()
    {
        // The other half of the jump, and the half the tree work exists to
        // enable: once the right bust is selected, its editor is on screen and
        // the tagged field can be reached. A jump that lands on the character
        // and stops has not taken anybody to their problem.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            // A character of the pack's own, because baseSprite is a field only
            // a bust the PACK draws has - one of the game's shows the
            // replace-textures panel instead.
            vm.AddCharacterCommand.Execute(null);
            WindowHarness.Pump();
            var them = vm.Characters.Last(c => c.IsPackBust && !c.IsPlayer && c.Outfits.Count > 0);
            var bust = them.Outfits[0];

            Navigate(window, $"characters[{them.Name}].outfits[{bust.Model.Key}].baseSprite");

            Assert.Same(bust, vm.SelectedOutfit);

            var found = window.FindIssueElement("baseSprite");
            _out.WriteLine("baseSprite element: " + (found?.GetType().Name ?? "(none)"));
            Assert.NotNull(found);
        });
    }

    [Fact]
    public void AnIssueNamingNobodyChangesNothing()
    {
        // The control. A path that resolves to no character must leave the
        // selection where it was rather than landing on whoever happens to be
        // first - which is what "find the row somehow" turns into if the
        // lookup is allowed to guess.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var (them, bust) = Subject(vm);
            vm.SelectedCharacter = them;
            vm.SelectedOutfit = bust;
            WindowHarness.Pump();

            Navigate(window, "characters[NobodyOfThatName].displayName");

            Assert.Same(them, vm.SelectedCharacter);
            Assert.Same(bust, vm.SelectedOutfit);
        });
    }

    private static void Collect(DependencyObject root, List<TreeViewItem> into)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TreeViewItem row) into.Add(row);
            Collect(child, into);
        }
    }
}
