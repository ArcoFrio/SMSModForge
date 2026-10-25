using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.View;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The save prompt says what changed for a person (the author, 1.8.0: "not
/// overwhelm the user with information that's too technical ... node key
/// changes give you full code-like info"): under the thing each change
/// belongs to, in the editor's words, values only where they say something.
/// </summary>
public class SaveConfirmTests
{
    private readonly ITestOutputHelper _out;
    public SaveConfirmTests(ITestOutputHelper o) => _out = o;

    private static string Saved(ModPack pack) => PackRepository.SerializeAsSaved(pack);

    private List<PackChange> AfterASession(out ModPack pack)
    {
        pack = ThemeSnapshotTests.Fixture();
        string before = Saved(pack);
        SaveConfirmSnapshotTests.EditASession(pack);
        var changes = PackDiff.Compute(before, Saved(pack));
        foreach (var c in changes) _out.WriteLine($"{c.Group} | {c.Glyph} {c.Text} | {c.Detail}");
        return changes;
    }

    [Fact]
    public void ASessionReadsAsWordsNotAsTheFile()
    {
        var changes = AfterASession(out var pack);
        var ids = pack.Dialogues.SelectMany(d => d.Nodes).Select(n => n.Id.ToString(CultureInfo.InvariantCulture)).Where(id => id.Length > 1).ToList();
        foreach (var c in changes)
        {
            string shown = c.Group + " " + c.Text + " " + c.Detail;
            foreach (string code in new[] { "nodes", "actionsOnStart", "params", "rootNodeIds", "children", "defaultValue", "displayName", "#1", "{ ", "nested" })
                Assert.DoesNotContain(code, shown);
            Assert.DoesNotContain(ids, id => System.Text.RegularExpressions.Regex.IsMatch(shown, $@"(?<![\d.]){id}(?![\d.])"));
        }

        Assert.Contains(changes, c => c.Group == "Dialogue · Chat at the cave (evening)" && c.Text == "Display name"
                                      && c.Detail == "Chat at the cave  →  Chat at the cave (evening)");
        Assert.Contains(changes, c => c.Text.EndsWith("› Expression") && c.Detail == "Happy  →  Sad");
        Assert.Contains(changes, c => c.Kind == PackChangeKind.Added && c.Text == "New: “One more thing before you go.”");
        Assert.Contains(changes, c => c.Kind == PackChangeKind.Removed && c.Text == "Removed: “Maybe later.”");
        Assert.Contains(changes, c => c.Text.EndsWith("› on start: PlaySFX") && c.Detail == "Clip: door");
        Assert.Contains(changes, c => c.Group == "Scene folder · Beach" && c.Detail == "added beach_wave");
    }

    [Fact]
    public void WhatFollowsALineIsLeftOutWhenALineAddedOrTakenOutSaysIt()
    {
        var changes = AfterASession(out _);
        Assert.DoesNotContain(changes, c => c.Text.EndsWith("What follows") || c.Text.EndsWith("Opening lines"));

        // The control: a line led somewhere else, with no line added.
        var pack = ThemeSnapshotTests.Fixture();
        string before = Saved(pack);
        var choice = pack.Dialogues[0].Nodes.Single(n => n.Id == 2);
        choice.Children.Remove(4);
        var rows = PackDiff.Compute(before, Saved(pack));
        var row = Assert.Single(rows);
        _out.WriteLine($"{row.Text} | {row.Detail}");
        Assert.Equal("a choice with no words › What follows", row.Text);
        Assert.Equal("“Let's swim.”, “Maybe later.”  →  “Let's swim.”", row.Detail);
    }

    [Fact]
    public void LinesAddedAndTakenOutAreNoNewOrderButAMoveIs()
    {
        var changes = AfterASession(out _);
        Assert.DoesNotContain(changes, c => c.Kind == PackChangeKind.Reordered);

        var pack = ThemeSnapshotTests.Fixture();
        string before = Saved(pack);
        var nodes = pack.Dialogues[0].Nodes;
        var moved = nodes[3];
        nodes.RemoveAt(3);
        nodes.Insert(0, moved);
        var row = Assert.Single(PackDiff.Compute(before, Saved(pack)));
        Assert.Equal(PackChangeKind.Reordered, row.Kind);
        Assert.Equal("Lines: new order", row.Text);
    }

    [Fact]
    public void NumbersHaveAPointWhateverTheSystemUses()
    {
        var was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var pack = ThemeSnapshotTests.Fixture();
            pack.Sfx[0].DefaultVolume = 0.667f;
            string before = Saved(pack);
            pack.Sfx[0].DefaultVolume = 0.65f;
            var row = Assert.Single(PackDiff.Compute(before, Saved(pack)));
            Assert.Equal("SFX · Door", row.Group);
            Assert.Equal("Default volume", row.Text);
            Assert.Equal("0.667  →  0.65", row.Detail);
        }
        finally { CultureInfo.CurrentCulture = was; }
    }

    [Fact]
    public void ALongLineShowsWhereItChanged()
    {
        var pack = ThemeSnapshotTests.Fixture();
        string before = Saved(pack);
        pack.Dialogues[0].Nodes[0].Text += " Right?";
        var row = Assert.Single(PackDiff.Compute(before, Saved(pack)));
        Assert.EndsWith("› Text", row.Text);
        // Both from just before the change, so the words added are in view -
        // not the same first seventy letters twice.
        Assert.Equal("…- 8, 9 and (a whisper).", row.Was);
        Assert.Equal("…- 8, 9 and (a whisper). Right?", row.Now);
    }

    [Fact]
    public void ATranslationsChangeNamesTheLineNotTheKey()
    {
        var pack = ThemeSnapshotTests.Fixture();
        string key = PackTexts.LineKey("chat", "3");
        var before = TextFile.Parse($"{key} = Nadamos.\n");
        var after = TextFile.Parse($"{key} = Vamos a nadar.\n");
        var row = Assert.Single(TranslationDiff.Compute("es", before, after,
                                                        Validation.TranslationValidation.PlacesOfTexts(pack)));
        Assert.Equal(key, row.Path);   // still found by its key
        Assert.Equal("Chat at the cave › “Let's swim.”", row.Text);
        Assert.Equal("Nadamos.  →  Vamos a nadar.", row.Detail);
    }

    [Fact]
    public void EveryFieldWithAName()
    {
        foreach (string field in ChangeWords.NamedFields)
            Assert.True(Loc.English.Has(ChangeWords.FieldKey(field)), ChangeWords.FieldKey(field));
        Assert.Equal("Noise scale", ChangeWords.FieldLabel("noiseScale"));   // one without: its own name in words
    }
}

/// <summary>The prompt as drawn: nothing runs past the list, however narrow
/// the window or long the value.</summary>
[Trait("Speed", "Slow")]   // builds windows on the harness thread
public sealed class SaveConfirmDrawnTests
{
    private readonly ITestOutputHelper _out;
    public SaveConfirmDrawnTests(ITestOutputHelper o) => _out = o;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    [Fact]
    public void NothingOverflowsTheWindowAtItsNarrowest()
    {
        WindowHarness.Run(_ =>
        {
            var pack = ThemeSnapshotTests.Fixture();
            string before = PackRepository.SerializeAsSaved(pack);
            SaveConfirmSnapshotTests.EditASession(pack);
            pack.Dialogues[0].Nodes[0].Text = string.Concat(Enumerable.Repeat("A very long line that goes on and on. ", 12));
            var changes = PackDiff.Compute(before, PackRepository.SerializeAsSaved(pack));

            var prompt = SaveConfirmWindow.Build(changes, @"C:\Packs\A pack\With a long folder name");
            prompt.Width = prompt.MinWidth;
            prompt.Left = -10000;
            prompt.Top = -10000;
            prompt.Show();
            prompt.UpdateLayout();
            WindowHarness.Pump();

            var list = (ListBox)prompt.FindName("ChangeList");
            var viewer = Descendants<ScrollViewer>(list).First();
            // The part of the list that shows rows: inside its padding.
            var shown = Descendants<ScrollContentPresenter>(viewer).First();
            double right = shown.TranslatePoint(new Point(shown.ActualWidth, 0), prompt).X;
            var texts = Descendants<TextBlock>(list).Where(t => t.IsVisible && t.Name is "RowText" or "RowDetail").ToList();
            Assert.NotEmpty(texts);
            foreach (var t in texts)
            {
                double end = t.TranslatePoint(new Point(t.ActualWidth, 0), prompt).X;
                Assert.True(end <= right + 1, $"'{t.Text}' ends at {end:0}, past the list's {right:0}");
                if (t.Name == "RowDetail")
                    Assert.True(t.ActualHeight <= 34.5, $"'{t.Text}' is {t.ActualHeight:0}px tall: more than two lines");
            }
            Assert.Equal(0, viewer.ScrollableWidth);
            _out.WriteLine($"{texts.Count} texts inside {right:0}px");
            prompt.Close();
        });
    }
}
