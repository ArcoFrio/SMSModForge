using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Tutorials;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Tutorials page (the author, 2026-11-21): "The sub-tutorials shouldn't be in a separate group, but indented
/// on the base ones"; "The tutorial descriptions should also show a summary of what tabs and changes they involve";
/// "What changed doesn't really explain what clicking on it does".
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class TutorialListTests
{
    private readonly ITestOutputHelper _out;
    public TutorialListTests(ITestOutputHelper o) => _out = o;

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in All<T>(child)) yield return d;
        }
    }

    [Fact]
    public void EveryTutorialSaysWhatItAddsAndWhereAndADeeperOneNamesARealParent()
    {
        var all = TutorialCatalog.All.Where(t => t.IsOnLadder).ToList();
        foreach (var t in all)
        {
            var item = new TutorialListItem(t, false);
            _out.WriteLine($"{t.Level,2} {t.Id}: {item.TabsLabel} | Adds: {item.Makes}");
            Assert.False(string.IsNullOrWhiteSpace(t.Makes), t.Id + " does not say what it adds");
            Assert.NotEqual(t.MakesKey, t.Makes);
            Assert.False(string.IsNullOrWhiteSpace(item.TabsLabel), t.Id + " works on no tab");
            if (t.IsDeeper)
            {
                var parent = TutorialCatalog.All.SingleOrDefault(p => p.Id == t.ParentId);
                Assert.True(parent != null, $"{t.Id} goes deeper into '{t.ParentId}', which is no tutorial");
                Assert.False(parent!.IsDeeper, $"{t.Id}'s parent is itself a deeper one");
                Assert.True(parent.Level < t.Level, $"{t.Id} comes before the tutorial it goes deeper into");
            }
        }
        Assert.Contains(all, t => t.IsDeeper);
    }

    [Fact]
    public void ADeeperTutorialIsDrawnIndentedUnderItsBase_AndContinueLearningKeepsTheStorysOrder()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.ModForge;
            vm.HomePage = HomePage.Tutorials;
            window.Width = 1400; window.Height = 900;
            WindowHarness.Pump(); window.UpdateLayout(); WindowHarness.Pump();

            // In the list: each base tutorial, then the ones going deeper into it, under its heading.
            var listed = vm.Tutorials.ToList();
            foreach (var deeper in listed.Where(t => t.IsDeeper))
            {
                int at = listed.IndexOf(deeper);
                int parent = listed.FindIndex(t => t.Def.Id == deeper.Def.ParentId);
                Assert.True(parent >= 0 && parent < at, $"{deeper.Def.Id} is not listed after its base");
                Assert.All(listed.Skip(parent + 1).Take(at - parent - 1), between =>
                    Assert.True(between.IsDeeper && between.Def.ParentId == deeper.Def.ParentId,
                                $"{between.Def.Id} stands between {deeper.Def.Id} and its base"));
                Assert.Equal(listed[parent].Group, deeper.Group);
            }
            Assert.DoesNotContain(listed, t => t.Group == SMSModForge.Localization.Loc.T("tutorial.group.deeper"));

            // As drawn: the card of a deeper one starts further right than its base's.
            var page = (FrameworkElement)window.FindName("HomePageTutorials");
            // The cards themselves: a plain ContentControl (their buttons are content controls too).
            var cards = All<ContentControl>(page).Where(c => c.GetType() == typeof(ContentControl) && c.DataContext is TutorialListItem
                                                             && c.Style != null && c.IsVisible).ToList();
            Assert.Equal(listed.Count, cards.Count);
            double Left(TutorialListItem item)
            {
                var card = cards.First(c => ReferenceEquals(c.DataContext, item) || ((TutorialListItem)c.DataContext).Def.Id == item.Def.Id);
                return card.TransformToAncestor(page).Transform(new Point(0, 0)).X;
            }
            var first = listed.First(t => t.IsDeeper);
            var itsBase = listed.First(t => t.Def.Id == first.Def.ParentId);
            double indent = Left(first) - Left(itsBase);
            _out.WriteLine($"{first.Def.Id} is drawn {indent:0} px right of {itsBase.Def.Id}");
            Assert.True(indent >= 24, $"{first.Def.Id} is not indented under {itsBase.Def.Id} ({indent:0} px)");
            // Control: two base tutorials line up.
            var bases = listed.Where(t => !t.IsDeeper && t.Def.IsOnLadder).Take(2).ToList();
            Assert.Equal(Left(bases[0]), Left(bases[1]), 1);

            // Continue learning: the story's order, not the list's.
            var ladder = TutorialCatalog.All.Where(t => t.IsOnLadder).OrderBy(t => t.Level).ToList();
            Assert.Equal(ladder.First(t => !SMSModForge.Services.EditorPrefs.IsTutorialComplete(t.Id)
                                            || SMSModForge.Services.EditorPrefs.IsTutorialOutdated(t.Id, t.Revision)).Id,
                         vm.NextTutorialItem!.Def.Id);
            // Each card's button says what it does.
            Assert.All(cards, c => Assert.Contains(All<Button>(c), b => b.ToolTip is string tip && tip.Length > 10));
        });
    }

    /// <summary>
    /// A rest of the mouse opens a card on its opening words and a line per step (the author, 2026-11-21: "hovering
    /// over a tutorial for 0.5s should expand the tutorial card ... a very short 1-line version of each of its
    /// steps"). Through the method the half-second's timer calls; measured on the card as drawn.
    /// </summary>
    [Fact]
    public void ARestOfTheMouseOpensACardOnALinePerStep_AndLeavingFoldsIt()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), SMSModForge.View.HoverReveal.Delay);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.ModForge;
            vm.HomePage = HomePage.Tutorials;
            window.Width = 1400; window.Height = 900;
            WindowHarness.Pump(); window.UpdateLayout(); WindowHarness.Pump();
            var page = (FrameworkElement)window.FindName("HomePageTutorials");
            var card = All<ContentControl>(page).First(c => c.GetType() == typeof(ContentControl)
                                                             && c.DataContext is TutorialListItem { Def.Id: "a-place" });
            var item = (TutorialListItem)card.DataContext;
            var peek = All<Border>(card).Single(b => b.Name == "TutorialPeek");
            double closed = card.ActualHeight;
            Assert.False(peek.IsVisible, "the card is open before the mouse rested on it");

            SMSModForge.View.HoverReveal.Open(card);
            WindowHarness.Pump(); window.UpdateLayout(); WindowHarness.Pump();
            Assert.True(peek.IsVisible && peek.ActualHeight > 40, "a rest of the mouse opened nothing");
            Assert.True(card.ActualHeight > closed + 40, $"the card did not grow ({closed:0} to {card.ActualHeight:0})");
            var lines = All<TextBlock>(peek).Where(t => t.IsVisible && t.ToolTip is string).ToList();
            Assert.Equal(item.Def.Steps.Count, lines.Count);
            // Each is a line: as high as one line of its words, never wrapped.
            double one = lines.Min(t => t.ActualHeight);
            Assert.All(lines, t => Assert.True(t.ActualHeight < one * 1.5, $"\"{t.ToolTip}\" takes more than a line"));
            Assert.Contains(lines, t => ((string)t.ToolTip).Contains("+ Place", StringComparison.Ordinal));
            // Two columns, each read top to bottom: step 2 under step 1.
            var first = lines[0].TranslatePoint(new Point(0, 0), peek);
            var second = lines[1].TranslatePoint(new Point(0, 0), peek);
            Assert.Equal(first.X, second.X, 1);
            Assert.True(second.Y > first.Y);

            SMSModForge.View.HoverReveal.Close(card);
            WindowHarness.Pump(); window.UpdateLayout(); WindowHarness.Pump();
            Assert.False(peek.IsVisible, "leaving left the card open");
            Assert.Equal(closed, card.ActualHeight, 1);
        });
    }

    [Theory]
    [InlineData("On the Places tab, click + Place. Then name it.", "On the Places tab, click + Place.")]
    [InlineData("OnRisingEdge:\nonce, as the conditions start passing.", "OnRisingEdge.")]
    [InlineData("Type Home in the button's Label override", "Type Home in the button's Label override")]
    [InlineData("Version 1.2 is out. Good.", "Version 1.2 is out.")]
    [InlineData("Click the ... beside Back sprite. Then pick it.", "Click the ... beside Back sprite.")]
    public void AStepsLineIsItsFirstSentence(string body, string gist) => Assert.Equal(gist, TutorialListItem.Gist(body));

    [Fact]
    public void AnUpdatedTutorialsButtonSaysItIsTakenAgain()
    {
        // One revised since it was first written, so an older version of it can have been taken.
        var t = TutorialCatalog.All.First(t => t.IsOnLadder && t.Revision > 1);
        int was = SMSModForge.Services.EditorPrefs.CompletedRevision(t.Id);
        try
        {
            SMSModForge.Services.EditorPrefs.ForgetTutorial(t.Id);
            SMSModForge.Services.EditorPrefs.MarkTutorialComplete(t.Id, t.Revision - 1);
            var item = new TutorialListItem(t, true);
            Assert.True(item.IsOutdated);
            Assert.Equal(SMSModForge.Localization.Loc.T("tutorials.takeNewVersion"), item.ActionLabel);
            Assert.Contains(SMSModForge.Localization.Loc.T("tutorials.takeNewVersion.tip"), item.ActionTip);
            // Control: taken in this version, it is "Again".
            SMSModForge.Services.EditorPrefs.MarkTutorialComplete(t.Id, t.Revision);
            Assert.Equal(SMSModForge.Localization.Loc.T("tutorials.again"), new TutorialListItem(t, true).ActionLabel);
        }
        finally
        {
            SMSModForge.Services.EditorPrefs.ForgetTutorial(t.Id);
            if (was > 0) SMSModForge.Services.EditorPrefs.MarkTutorialComplete(t.Id, was);
        }
    }
}
