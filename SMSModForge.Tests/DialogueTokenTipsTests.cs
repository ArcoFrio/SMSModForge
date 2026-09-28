using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Documentation;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The cheatsheet under the dialogue list: every token the game resolves, each
/// with what it stands for written beside it, without eating the tree it sits
/// beside.
/// <para/>
/// The tokens are the game's, not this editor's — a braced word swapped for
/// whatever the player chose to call somebody. An author who does not know one
/// exists writes the word instead, and the line then says "Mom" to the player
/// who chose "Mum": indistinguishable, from their side, from the game losing
/// their choice. So the list is only worth having if it is complete, and it is
/// checked here as ONE list against the two places it is written out — this
/// panel and the in-app reference, which have already drifted once.
/// <para/>
/// The height is the other half. The dialogue tree shares this column and the
/// tree is where the work happens, so the panel has a ceiling of its own and a
/// fold. The markup tags used to share it too, squeezed into two columns of
/// one-word labels; the formatting buttons over the Text box write those now,
/// so the room went back to spelling the tokens out.
/// <para/>
/// Measured off the rendered panel rather than the file, because a TextBlock
/// that is in the XAML and collapsed, clipped or never realised is one nobody
/// can read.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class DialogueTokenTipsTests : System.IDisposable
{
    private readonly ITestOutputHelper _out;

    /// <summary>
    /// Somebody is running this on their own machine, with the panel folded
    /// away because they know the tokens by heart. A folded Expander never
    /// realises its content, so every check below would report the panel
    /// missing — which is the author's preference, not a fault in the editor.
    /// </summary>
    private readonly bool _was = SMSModForge.Services.EditorPrefs.ShowTextTokenTips;

    public DialogueTokenTipsTests(ITestOutputHelper o)
    {
        _out = o;
        SMSModForge.Services.EditorPrefs.ShowTextTokenTips = true;
    }

    public void Dispose() => SMSModForge.Services.EditorPrefs.ShowTextTokenTips = _was;

    /// <summary>
    /// Every name token confirmed to resolve in game.
    /// <para/>
    /// Taken from the code rather than written out again here, so this checks
    /// the panel against the list the EDITOR uses - the one that decides what
    /// gets marked in a dialogue line. A fourth copy of the list would be a
    /// fourth thing to drift, and it has drifted before.
    /// </summary>
    private static readonly string[] Tokens =
        SMSModForge.Rendering.DialogueMarkup.Tokens.ToArray();

    /// <summary>Markup, as the in-app reference spells it. Not on the panel any
    /// more - the buttons write it - but still documented.</summary>
    private static readonly string[] Markup =
        { "<size=70%>", "<b>", "<i>", "<color=#FF6666>" };

    private static IEnumerable<string> Everything => Tokens.Concat(Markup);

    private static void ShowDialogues(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues")
            { tabs.SelectedIndex = i; WindowHarness.Pump(); window.UpdateLayout();
              WindowHarness.Pump(); return; }

        throw new Xunit.Sdk.XunitException("no Dialogues tab");
    }

    private static void Collect<T>(DependencyObject root, List<T> into) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) into.Add(hit);
            Collect(child, into);
        }
    }

    private static List<TextBlock> Blocks(MainWindow window)
    {
        var found = new List<TextBlock>();
        Collect(window, found);
        return found;
    }

    [Fact]
    public void EveryEntryIsOnScreenInTheDialoguesTab()
    {
        WindowHarness.Run(window =>
        {
            ShowDialogues(window);
            var shown = Blocks(window).Where(b => b.IsVisible).Select(b => b.Text).ToList();

            foreach (string entry in Tokens)
            {
                bool there = shown.Any(t => t == entry);
                _out.WriteLine($"   {entry,-12} {(there ? "shown" : "MISSING")}");
                Assert.True(there, $"{entry} is not on screen in the Dialogues tab");
            }
        });
    }

    [Fact]
    public void EachEntryIsSpelledOutRatherThanLeftToGuess()
    {
        // What each token stands for, written out beside it where it can be
        // read without knowing there is anything to hover.
        WindowHarness.Run(window =>
        {
            ShowDialogues(window);
            var blocks = Blocks(window);

            foreach (string entry in Tokens)
            {
                var cell = blocks.FirstOrDefault(b => b.Text == entry && b.IsVisible);
                Assert.True(cell != null, $"{entry} is not on screen");

                int row = Grid.GetRow(cell!);
                int col = Grid.GetColumn(cell!);
                var label = blocks.FirstOrDefault(
                    b => b.IsVisible && b.Parent == cell!.Parent
                         && Grid.GetRow(b) == row && Grid.GetColumn(b) == col + 1);

                _out.WriteLine($"   {entry,-6} r{row}c{col}  {label?.Text ?? "(no label)"}");

                // A phrase rather than a word: "what they call Anna", not "Anna".
                Assert.True(label != null && label.Text.Contains(' '),
                            $"{entry} has no description beside it");
            }
        });
    }

    [Fact]
    public void TheMarkupIsLeftToTheButtons()
    {
        // The formatting buttons over the Text box write every tag the panel
        // used to list, so the panel no longer spends its room on them.
        WindowHarness.Run(window =>
        {
            ShowDialogues(window);
            var expanders = new List<Expander>();
            Collect(window, expanders);
            var fold = expanders.First(e => Blocks(window).Any(b => b.Text == "{PC}" && e.IsAncestorOf(b)));

            var inPanel = Blocks(window).Where(b => fold.IsAncestorOf(b)).Select(b => b.Text).ToList();
            foreach (string tag in new[] { "<b>", "<i>", "<size=70%>", "<color" })
                Assert.DoesNotContain(inPanel, t => t.StartsWith(tag, System.StringComparison.Ordinal));

            // ...and the buttons that took them over are there.
            foreach (string name in new[] { "MarkupBoldButton", "MarkupItalicButton", "MarkupColorButton", "MarkupSizeButton" })
                Assert.NotNull(window.FindName(name));
        });
    }

    [Fact]
    public void ItCannotGrowIntoTheTree()
    {
        // The constraint that shaped the panel. Another entry must cost the
        // dialogue tree nothing, which means the cheatsheet needs a ceiling of
        // its own rather than however tall its contents happen to be.
        WindowHarness.Run(window =>
        {
            ShowDialogues(window);

            var scrollers = new List<ScrollViewer>();
            Collect(window, scrollers);
            var bounded = scrollers.FirstOrDefault(
                s => !double.IsInfinity(s.MaxHeight) && s.MaxHeight > 0
                     && Blocks(window).Any(b => b.Text == "{PC}" && s.IsAncestorOf(b)));

            Assert.True(bounded != null, "the cheatsheet has no height ceiling");
            _out.WriteLine($"ceiling {bounded!.MaxHeight:0}px, actual {bounded.ActualHeight:0}px");
            Assert.True(bounded.ActualHeight <= bounded.MaxHeight + 1);

            var tree = (TreeView)window.FindName("DialogueTreeView");
            var expander = new List<Expander>();
            Collect(window, expander);
            var fold = expander.First(e => Blocks(window).Any(
                b => b.Text == "{PC}" && e.IsAncestorOf(b)));

            _out.WriteLine($"cheatsheet {fold.ActualHeight:0}px vs tree {tree.ActualHeight:0}px");
            Assert.True(fold.ActualHeight < tree.ActualHeight,
                        "the cheatsheet is taller than the tree it sits beside");
        });
    }

    [Fact]
    public void ItStartsOpenAndCanBeFoldedAway()
    {
        // It follows the remembered preference, which starts on: the panel
        // exists to be seen by somebody who does not know the tokens exist.
        // Foldable: for everyone who does, and wants the column back. The
        // preference is set open above and put back afterwards, since this
        // runs on a machine whose author may have folded it away.
        WindowHarness.Run(window =>
        {
            ShowDialogues(window);
            var expanders = new List<Expander>();
            Collect(window, expanders);
            var fold = expanders.FirstOrDefault(e => Blocks(window).Any(
                b => b.Text == "{PC}" && e.IsAncestorOf(b)));

            Assert.True(fold != null, "the cheatsheet is not foldable");
            Assert.True(fold!.IsExpanded, "it does not start open");

            double open = fold.ActualHeight;
            fold.IsExpanded = false;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            _out.WriteLine($"open {open:0}px, folded {fold.ActualHeight:0}px");
            Assert.True(fold.ActualHeight < open, "folding it gave nothing back");

            fold.IsExpanded = true;   // leave the harness as it was found
            WindowHarness.Pump();
        });
    }

    private static DocTopic? ValuesTopic()
        => DocTopics.Parts.SelectMany(p => p.Topics)
                    .FirstOrDefault(t => t.Title == "Putting values into text");

    private static string TopicText(DocTopic topic)
        => string.Join("\n", topic.Sections.SelectMany(
            s => s.Bullets.Select(b => b.Term + " " + b.Text)));

    [Fact]
    public void TheInAppReferenceNamesTheSameOnes()
    {
        var topic = ValuesTopic();
        Assert.True(topic != null, "the reference no longer has that topic");
        string text = TopicText(topic!);

        foreach (string entry in Everything)
        {
            _out.WriteLine($"   {entry,-12} {(text.Contains(entry) ? "documented" : "MISSING")}");
            Assert.Contains(entry, text);
        }
    }

    [Fact]
    public void TheReferenceNoLongerSaysThereAreOnlyFour()
    {
        // The specific way these two drifted: the reference kept a sentence
        // telling authors to treat anything beyond four as unsupported, which
        // outlived the four. A stale exclusivity claim is worse than none.
        var topic = ValuesTopic();
        Assert.True(topic != null, "the reference no longer has that topic");
        string text = TopicText(topic!);

        Assert.DoesNotContain("beyond the four", text);
        Assert.DoesNotContain("Those four are the ones known to work", text);
    }
}
