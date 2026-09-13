using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The node list draws its lines one of two ways, and the switch between them
/// is a setting somebody has to find on purpose.
/// <para/>
/// Off by default, because it is a different-looking list from the one an
/// author has been using and it costs the spelling squiggles on those rows. A
/// list that redecorated itself after an update is a change nobody asked for.
/// <para/>
/// Driven through the real window: both rows live in the same template and are
/// switched by Visibility, and a binding that resolves to nothing in WPF hides
/// a control silently — which would look exactly like the setting working.
/// </summary>
public sealed class GameLookRowTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly bool _was = SMSModForge.Services.EditorPrefs.GameLookNodeRows;

    public GameLookRowTests(ITestOutputHelper o) => _out = o;

    /// <summary>Somebody is running this on their own machine, with their own
    /// preference set.</summary>
    public void Dispose() => SMSModForge.Services.EditorPrefs.GameLookNodeRows = _was;

    private static void ShowDialogues(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues")
            { tabs.SelectedIndex = i; WindowHarness.Pump(); return; }

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

    private static List<T> In<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();
        Collect(root, found);
        return found;
    }

    /// <summary>A conversation with one spoken line in it, on screen.</summary>
    private static MainViewModel WithALine(MainWindow window)
    {
        var vm = (MainViewModel)window.DataContext;
        ShowDialogues(window);

        vm.AddDialogueCommand.Execute(null);
        WindowHarness.Pump();

        // A fresh conversation may come with no lines at all, depending on the
        // template it was built from - so ask for one rather than assuming.
        if (vm.SelectedDialogue!.Nodes.Count == 0)
        {
            vm.AddDialogueRootNodeCommand.Execute(null);
            WindowHarness.Pump();
        }
        Assert.NotEmpty(vm.SelectedDialogue.Nodes);

        var node = vm.SelectedDialogue.Nodes.First();
        node.Text = "Finally. There you are.";
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
        return vm;
    }

    [Fact]
    public void TheListStartsOnThePlainRows()
    {
        WindowHarness.Run(window =>
        {
            var vm = WithALine(window);
            vm.GameLookNodeRows = false;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var plain = In<MarkupTextBox>(window).Where(b => b.IsVisible).ToList();
            var fancy = In<DialogueLinePreview>(window).Where(p => p.IsVisible).ToList();

            _out.WriteLine($"plain rows visible: {plain.Count}, game-look: {fancy.Count}");
            Assert.NotEmpty(plain);
            Assert.Empty(fancy);
        });
    }

    [Fact]
    public void TurningItOnSwapsTheRows()
    {
        WindowHarness.Run(window =>
        {
            var vm = WithALine(window);
            vm.GameLookNodeRows = true;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var plain = In<MarkupTextBox>(window).Where(b => b.IsVisible).ToList();
            var fancy = In<DialogueLinePreview>(window).Where(p => p.IsVisible).ToList();

            _out.WriteLine($"plain rows visible: {plain.Count}, game-look: {fancy.Count}");
            Assert.NotEmpty(fancy);

            // The editing box below the list is a MarkupTextBox too, and it
            // stays: the switch is about the LIST.
            Assert.DoesNotContain(plain, b => b.HidesTags);
        });
    }

    [Fact]
    public void TheBracketedNameGoesWithThePlainRow()
    {
        // Two names in one row would be the obvious way to get this wrong: the
        // game-look row draws the speaker itself, bigger and in their colour.
        WindowHarness.Run(window =>
        {
            var vm = WithALine(window);

            vm.GameLookNodeRows = false;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();
            int bracketed = In<TextBlock>(window)
                .Count(t => t.IsVisible && t.Text.StartsWith("[") && t.Text.EndsWith("]"));

            vm.GameLookNodeRows = true;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();
            int afterwards = In<TextBlock>(window)
                .Count(t => t.IsVisible && t.Text.StartsWith("[") && t.Text.EndsWith("]"));

            _out.WriteLine($"[Name] blocks: {bracketed} plain, {afterwards} game-look");
            Assert.True(afterwards <= bracketed,
                        "the bracketed name is still shown beside the game-look row");
        });
    }

    [Fact]
    public void TheWholeListTakesThePanelColourNotJustTheRows()
    {
        // A dark strip per row with the list's own background showing between
        // them reads as stripes, not as a conversation. The list and the rows
        // take their colour from one brush so they cannot end up two greys.
        WindowHarness.Run(window =>
        {
            var vm = WithALine(window);
            var list = (ListBox)window.FindName("NodeList");

            vm.GameLookNodeRows = false;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();
            var plainBackground = list.Background;

            vm.GameLookNodeRows = true;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            _out.WriteLine($"list background: {plainBackground} -> {list.Background}");
            Assert.Same(DialogueLinePreview.Panel, list.Background);
            Assert.NotSame(DialogueLinePreview.Panel, plainBackground);
        });
    }

    [Fact]
    public void TheKindGlyphIsReadableOnTheDarkList()
    {
        // It is a theme brush the rest of the time, and on the light theme that
        // is near-black - which on a near-black panel is nothing at all.
        WindowHarness.Run(window =>
        {
            var vm = WithALine(window);
            vm.GameLookNodeRows = true;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var glyphs = In<TextBlock>(window)
                .Where(t => t.IsVisible && t.Text.Length == 1 && "▸⇄?◆".Contains(t.Text))
                .ToList();

            _out.WriteLine($"{glyphs.Count} kind glyph(s) on screen");
            Assert.NotEmpty(glyphs);
            foreach (var glyph in glyphs)
                Assert.Same(DialogueLinePreview.OnPanel, glyph.Foreground);
        });
    }

    [Fact]
    public void TheSettingIsRemembered()
    {
        // It is a look somebody chose. Re-choosing it every launch would make
        // the menu item a toy.
        bool was = SMSModForge.Services.EditorPrefs.GameLookNodeRows;
        try
        {
            SMSModForge.Services.EditorPrefs.GameLookNodeRows = !was;
            Assert.Equal(!was, SMSModForge.Services.EditorPrefs.GameLookNodeRows);

            SMSModForge.Services.EditorPrefs.GameLookNodeRows = was;
            Assert.Equal(was, SMSModForge.Services.EditorPrefs.GameLookNodeRows);
        }
        finally
        {
            SMSModForge.Services.EditorPrefs.GameLookNodeRows = was;
        }
    }

    [Fact]
    public void TheSpeakerIsHandedOverWithoutBrackets()
    {
        // What the game-look row binds to. The brackets are this editor's way
        // of telling a name from the words; the game has no such problem, and
        // two of its characters would be shown as "[Anna]".
        var def = new SMSModForge.Model.DialogueNodeDef { Id = 1, Text = "hello" };
        var node = new DialogueNodeViewModel(def);

        DialogueNodeViewModel.ActorDisplayNameProvider = key => key == "anna" ? "Anna" : key;
        DialogueNodeViewModel.ActorColorProvider =
            key => key == "anna" ? System.Windows.Media.Colors.Red : null;
        try
        {
            node.Actor = "anna";

            _out.WriteLine($"prefix '{node.SpeakerPrefix}', name '{node.SpeakerName}', "
                           + $"colour '{node.SpeakerColorHex}'");

            Assert.Equal("[Anna]", node.SpeakerPrefix);
            Assert.Equal("Anna", node.SpeakerName);
            Assert.Equal("#FF0000", node.SpeakerColorHex);
        }
        finally
        {
            DialogueNodeViewModel.ActorDisplayNameProvider = null;
            DialogueNodeViewModel.ActorColorProvider = null;
        }
    }

    [Fact]
    public void ASpeakerWithNoColourHandsOverNothing()
    {
        // The control for the one above: an empty string means "the ordinary
        // colour", and inventing one would paint every unnamed character the
        // same wrong shade.
        var def = new SMSModForge.Model.DialogueNodeDef { Id = 1, Text = "hello" };
        var node = new DialogueNodeViewModel(def);

        DialogueNodeViewModel.ActorColorProvider = _ => null;
        try
        {
            node.Actor = "someone";
            Assert.Equal("", node.SpeakerColorHex);
        }
        finally { DialogueNodeViewModel.ActorColorProvider = null; }
    }
}
