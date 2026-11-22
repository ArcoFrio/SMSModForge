using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.Rendering;
using SMSModForge.Shared;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// <c>{name:key}</c> (1.9.0): a character's name in a pack's text that follows a new
/// name. The runtime half is in-game check K9.
/// </summary>
public sealed class NameTokenTests
{
    [Fact]
    public void ATokenIsBracesNameColonAndAKeyWithNoSpaces()
    {
        Assert.Equal("{name:anna}", NameTokens.Make("anna"));
        Assert.True(NameTokens.TryParse("{name:anna}", out string key));
        Assert.Equal("anna", key);

        // Not one: a space in the key, another word before the colon, part of a
        // longer text, an empty key.
        Assert.False(NameTokens.TryParse("{name:big anna}", out _));
        Assert.False(NameTokens.TryParse("{nom:anna}", out _));
        Assert.False(NameTokens.TryParse("x{name:anna}", out _));
        Assert.False(NameTokens.TryParse("{name:}", out _));

        var found = NameTokens.FindAll("Hi {name:anna} and {name:Josef2}.");
        Assert.Equal(new[] { "anna", "Josef2" }, found.Select(f => f.Key));
        Assert.Equal(3, found[0].Start);
    }

    [Fact]
    public void ResolvingReplacesWhomItKnows_AndLeavesTheRestAsWritten()
    {
        string said = NameTokens.Resolve("{name:anna} and {name:nobody}",
                                         k => k == "anna" ? "Annie" : null);
        Assert.Equal("Annie and {name:nobody}", said);
    }

    [Fact]
    public void ATranslationMustKeepIt_LikeAnyOtherCode()
    {
        // Counted as a code, so a translation that drops it - or a machine
        // translation that translates "name" - is caught.
        Assert.Contains("{name:anna}", TextCodes.Of("Ask {name:anna} first."));
        Assert.True(Services.Translation.ProtectedText.KeptItsCodes("Ask {name:anna} first.", "Demande à {name:anna} d'abord."));
        Assert.False(Services.Translation.ProtectedText.KeptItsCodes("Ask {name:anna} first.", "Demande à Anna d'abord."));
        Assert.False(Services.Translation.ProtectedText.KeptItsCodes("Ask {name:anna} first.", "Demande à {nom:anna} d'abord."));
    }

    [Fact]
    public void TheLineMarkupReadsItAsATokenForSomebody_AndAsWordsForNobody()
    {
        var was = DialogueMarkup.NameOf;
        try
        {
            DialogueMarkup.NameOf = k => k == "anna" ? "Annie" : null;
            const string line = "Ask {name:anna}, not {name:nobody}.";
            var spans = DialogueMarkup.Parse(line);
            Assert.Equal(new[] { "{name:anna}" }, spans.Where(s => s.IsToken).Select(s => s.Of(line)));
            Assert.Equal("Annie", DialogueMarkup.NameIn("{name:anna}"));
            Assert.Null(DialogueMarkup.NameIn("{name:nobody}"));

            // Every character still lands in exactly one span: the box is driven from these.
            Assert.Equal(line, string.Concat(spans.Select(s => s.Of(line))));

            // With no pack to ask, the game's cast answers.
            DialogueMarkup.NameOf = null;
            Assert.Equal("Anna", DialogueMarkup.NameIn("{name:anna}"));
            Assert.Equal("Master Zhen", DialogueMarkup.NameIn("{name:masterzhen}"));
        }
        finally { DialogueMarkup.NameOf = was; }
    }
}

/// <summary>The name button over a line, and a token drawn in the tree, through the
/// real window.</summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NameTokenOnScreenTests
{
    private readonly ITestOutputHelper _out;
    public NameTokenOnScreenTests(ITestOutputHelper o) => _out = o;

    private static MainViewModel WithALine(MainWindow window, string speaker)
    {
        var vm = (MainViewModel)window.DataContext;
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues")
            { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();

        vm.AddDialogueCommand.Execute(null);
        WindowHarness.Pump();
        if (vm.SelectedDialogue!.Nodes.Count == 0)
        {
            vm.AddDialogueRootNodeCommand.Execute(null);
            WindowHarness.Pump();
        }
        vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
        vm.SelectedNode.Actor = speaker;
        vm.SelectedNode.Text = "Finally. There you are.";
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
        return vm;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    [Fact]
    public void TheNameButtonOffersWhoSpeaksFirst_ThenThePacksOwn_ThenTheGames_AndWritesTheToken()
    {
        WindowHarness.Run(window =>
        {
            var vm = WithALine(window, "josef");
            vm.AddCharacterCommand.Execute(null);
            WindowHarness.Pump();
            var mine = vm.SelectedCharacter!;
            mine.DisplayName = "Neighbour";
            WindowHarness.Pump();

            var box = (MarkupTextBox)window.FindName("NodeTextBox");
            box.Focus();
            box.Select(8, 0);                       // "Finally.| There you are."
            WindowHarness.Pump();

            var menu = window.NameTokenMenu(box.InsertionPoint)!;
            string Words(MenuItem m) => m.Header is Panel p
                ? string.Join(" ", p.Children.OfType<TextBlock>().Select(t => t.Text)) : m.Header?.ToString() ?? "";
            var top = menu.Items.OfType<MenuItem>().ToList();
            foreach (var m in top) _out.WriteLine(Words(m));

            Assert.Equal(Localization.Loc.T("dialogues.nameToken.speaking"), Words(top[0]));
            Assert.Equal("Josef {name:josef}", Words(top[1]));
            Assert.Equal(Localization.Loc.T("dialogues.nameToken.own"), Words(top[2]));
            Assert.Contains(top, m => Words(m) == "Neighbour {name:" + mine.Key + "}");
            var games = top.Last();
            Assert.Equal(Localization.Loc.T("dialogues.nameToken.games"), Words(games));
            var rows = games.Items.OfType<MenuItem>().Select(Words).ToList();
            Assert.Contains("Anna {name:anna}", rows);
            // Josef is offered once, at the top: he speaks here.
            Assert.DoesNotContain(rows, r => r.StartsWith("Josef ", StringComparison.Ordinal));
            // ...and the player never: their name is {PC}.
            Assert.DoesNotContain(rows, r => r.Contains("{name:player}"));

            var anna = games.Items.OfType<MenuItem>().First(m => Words(m) == "Anna {name:anna}");
            anna.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WindowHarness.Pump();
            _out.WriteLine($"node '{vm.SelectedNode!.Text}'");
            // Spaced as the box spaces anything it writes in (the sound cues too).
            Assert.Equal("Finally. {name:anna} There you are.", vm.SelectedNode.Text);
        });
    }

    [Fact]
    public void InTheTreeTheTokenReadsAsTheirName_TheNewOneOnceTheyHaveIt()
    {
        WindowHarness.Run(window =>
        {
            var vm = WithALine(window, "josef");
            vm.SelectedNode!.Text = "Ask {name:anna}, not {name:nobodyatall}.";
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            string Row()
            {
                var row = Descendants<MarkupTextBlock>(window)
                    .First(b => b.Name == "NodeRowText" && ReferenceEquals(b.DataContext, vm.SelectedNode));
                return new TextRange(row.ContentStart, row.ContentEnd).Text.TrimEnd('\r', '\n');
            }

            _out.WriteLine(Row());
            Assert.Equal("Ask Anna, not {name:nobodyatall}.", Row());

            var anna = vm.Characters.First(c => c.Model.VanillaCharacter == "Anna");
            anna.NameBox = "Annie";
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();
            _out.WriteLine(Row());
            Assert.Equal("Ask Annie, not {name:nobodyatall}.", Row());

            // The text itself is untouched: only how it reads.
            Assert.Equal("Ask {name:anna}, not {name:nobodyatall}.", vm.SelectedNode.Text);
        });
    }
}
