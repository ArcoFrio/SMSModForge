using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Smart suggestions in the window (the author, 1.8.0): an empty dropdown
/// opens with the likely picks on top, starred, in that box only; a commit
/// changes what is likely; Options turns it off. Read off where the rows are
/// drawn, not off the ranking.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class SmartSuggestionsWindowTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-suggest-" + Guid.NewGuid().ToString("N"));

    public SmartSuggestionsWindowTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static readonly string[] MirasScenes = { "MiraNightmarketScene01", "MiraNightmarketScene02", "MiraHarborScene01" };

    /// <summary>The pack open on Mira's dialogue, with a Set Active action
    /// switched to scenes on its second line; its target box.</summary>
    private (MainViewModel Vm, ComboBox Target) Opened(MainWindow window, Action<ModPack>? alter = null)
    {
        var pack = SmartSuggestionsTests.Pack();
        alter?.Invoke(pack);
        PackRepository.Save(pack, _dir);
        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        vm.SelectedTabIndex = 5;
        vm.SelectedDialogue = vm.Dialogues.Single(d => d.Key == "MiraEvening");
        vm.SelectedNode = vm.SelectedDialogue.Nodes.Single(n => n.Id == 2);
        var action = vm.SelectedNode.AddActionOnStart();
        action.Type = NodeActionTypes.SetGameObjectActive;
        action.Category = NodeActionViewModel.CatScene;
        Settle(vm);
        window.UpdateLayout();

        var target = Descendants<ComboBox>(window).Single(c => c.IsVisible && ReferenceEquals(c.ItemsSource, vm.SceneKeyOptionsGrouped)
                                                                && c.DataContext == action);
        Assert.Equal("", target.Text);
        return (vm, target);
    }

    /// <summary>What the window does once things are quiet, and what the
    /// suggestions read in the background, both done.</summary>
    private static void Settle(MainViewModel vm)
    {
        WindowHarness.Pump();
        Assert.True(vm.Suggestions.Idle.Wait(TimeSpan.FromSeconds(10)));
        WindowHarness.Pump();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    private static FrameworkElement PopupContent(ComboBox box)
        => (FrameworkElement)((Popup)box.Template.FindName("PART_Popup", box)).Child;

    /// <summary>The rows the open list shows, top to bottom as drawn.</summary>
    private static List<ComboBoxItem> RowsDrawn(ComboBox box)
    {
        WindowHarness.Pump();
        var content = PopupContent(box);
        content.UpdateLayout();
        return Descendants<ComboBoxItem>(content)
            .Where(r => r.IsVisible)
            .OrderBy(r => r.TransformToAncestor(content).Transform(new Point(0, 0)).Y)
            .ToList();
    }

    private static string[] Names(IEnumerable<ComboBoxItem> rows) => rows.Select(r => (string)r.DataContext).ToArray();

    /// <summary>How many pixels across one line of the open list are drawn in
    /// the colour given.</summary>
    private static int InkAcross(ComboBox box, double y, Color ink)
    {
        var content = PopupContent(box);
        int width = (int)content.ActualWidth, height = (int)content.ActualHeight;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var px = new byte[width * 4];
        bitmap.CopyPixels(new Int32Rect(0, Math.Clamp((int)Math.Floor(y), 0, height - 1), width, 1), px, width * 4, 0);
        int n = 0;
        for (int i = 0; i < px.Length; i += 4)
            if (Math.Abs(px[i] - ink.B) < 6 && Math.Abs(px[i + 1] - ink.G) < 6 && Math.Abs(px[i + 2] - ink.R) < 6) n++;
        return n;
    }

    /// <summary>Where a row's bottom edge is drawn in the open list.</summary>
    private static double BottomOf(ComboBox box, ComboBoxItem row)
        => row.TransformToAncestor(PopupContent(box)).Transform(new Point(0, row.ActualHeight)).Y;

    private static bool Starred(ComboBoxItem row)
        => Descendants<TextBlock>(row).Any(t => t.Text == "" && t.IsVisible);

    private List<ComboBoxItem> Open(ComboBox box)
    {
        WindowHarness.SetOpen(box, false);
        WindowHarness.Pump();
        WindowHarness.SetOpen(box, true);
        var rows = RowsDrawn(box);
        _out.WriteLine(string.Join(", ", Names(rows).Select((n, i) => Starred(rows[i]) ? n + "*" : n)));
        return rows;
    }

    [Fact]
    public void AnEmptyBoxOpensWithTheLikelyScenesOnTopStarred()
    {
        WindowHarness.Run(window =>
        {
            var (vm, target) = Opened(window);
            var listBefore = vm.SceneKeyOptions.ToList();

            var rows = Open(target);
            Assert.Equal(MirasScenes, Names(rows.Take(3)));
            Assert.All(rows.Take(3), r => Assert.True(Starred(r), $"{r.DataContext} is not starred"));
            Assert.All(rows.Skip(3), r => Assert.False(Starred(r), $"{r.DataContext} is starred"));
            Assert.Equal(vm.SceneKeyOptions.Count, rows.Count);   // nothing hidden

            // A line under the suggestions, and between no other rows. It has
            // a gap of its own under the last suggestion, so no row - an EX row
            // paints its own background - can cover it.
            var border = ((SolidColorBrush)window.FindResource("Theme.Border")).Color;
            Assert.True(BottomOf(target, rows[3]) - rows[3].ActualHeight - BottomOf(target, rows[2]) >= 2,
                        "no gap under the suggestions");
            // Anywhere in the gap: a look may sit its rows a pixel or two apart.
            double gapTop = BottomOf(target, rows[2]), gapBottom = BottomOf(target, rows[3]) - rows[3].ActualHeight;
            int under = 0;
            for (double y = gapTop - 1; y <= gapBottom + 1; y++) under = Math.Max(under, InkAcross(target, y, border));
            int elsewhere = InkAcross(target, BottomOf(target, rows[5]) - 1, border);
            _out.WriteLine($"border-coloured pixels: under the suggestions {under}, between two other rows {elsewhere}");
            Assert.True(under > PopupContent(target).ActualWidth / 2, "no line under the suggestions");
            Assert.True(elsewhere < 10, "a line between rows that are not suggestions");

            // Only on screen, only in this box: the list is as it was, and
            // the selection untouched.
            Assert.Equal(listBefore, vm.SceneKeyOptions.ToList());
            Assert.Null(target.SelectedItem);
            Assert.Equal("", target.Text);

            WindowHarness.SetOpen(target, false);
            WindowHarness.Pump();
            Assert.Null(DropDownSuggestions.GetRanking(target));
        });
    }

    [Fact]
    public void ABoxHoldingAChoiceAndTheOffSwitchKeepTheListsOrder()
    {
        WindowHarness.Run(window =>
        {
            var (vm, target) = Opened(window);
            var inOrder = Names(Open(target).OrderBy(r => vm.SceneKeyOptions.IndexOf((string)r.DataContext)));
            Assert.NotEqual(inOrder, Names(Open(target)));   // the control: suggestions do reorder

            target.Text = "JunoHarborScene01";
            WindowHarness.Pump();
            var rows = Open(target);
            Assert.DoesNotContain(rows, Starred);
            Assert.Null(DropDownSuggestions.GetRanking(target));

            target.Text = "";
            WindowHarness.Pump();
            try
            {
                vm.SmartSuggestionsOn = false;
                rows = Open(target);
                Assert.DoesNotContain(rows, Starred);
                Assert.Equal(inOrder, Names(rows));
            }
            finally { vm.SmartSuggestionsOn = true; }
            Settle(vm);
            Assert.Equal(MirasScenes, Names(Open(target).Take(3)));   // and back on
        });
    }

    [Fact]
    public void ACommitChangesWhatIsLikely()
    {
        WindowHarness.Run(window =>
        {
            var (vm, target) = Opened(window);
            Assert.Equal(MirasScenes, Names(Open(target).Take(3)));
            WindowHarness.SetOpen(target, false);

            // The lines become Juno's, and the field is left - a commit.
            foreach (var node in vm.SelectedDialogue!.Nodes) node.Actor = "Juno";
            vm.Undo.Checkpoint();
            Settle(vm);

            // Juno at the dialogue's place first now, and Juno's scenes
            // starred; the dialogue is still called MiraEvening, so Mira's at
            // that place stay, but her scene elsewhere is no longer likely.
            var rows = Open(target);
            Assert.Equal("JunoNightmarketScene01", rows[0].DataContext);
            Assert.All(rows.Where(r => ((string)r.DataContext).StartsWith("Juno")), r => Assert.True(Starred(r)));
            Assert.False(Starred(rows.Single(r => (string)r.DataContext == "MiraHarborScene01")));
        });
    }

    [Fact]
    public void UnderFolderHeadingsTheHeadingWithTheLikelyOnesComesFirst()
    {
        WindowHarness.Run(window =>
        {
            // Filed by who they are about: "Juno" is the first heading in
            // the list's own order.
            var (_, target) = Opened(window, pack =>
            {
                foreach (var who in new[] { "Juno", "Kai", "Lena", "Mira" })
                    pack.SceneFolders.Add(new UnitFolderDef { Name = who, Items = pack.Scenes.Select(s => s.Key).Where(k => k.StartsWith(who)).ToList() });
            });
            var rows = Open(target);
            Assert.Equal(MirasScenes, Names(rows.Take(3)));
            var headings = Descendants<GroupItem>(PopupContent(target)).Where(g => g.IsVisible)
                .OrderBy(g => g.TransformToAncestor(PopupContent(target)).Transform(new Point(0, 0)).Y)
                .Select(g => ((System.Windows.Data.CollectionViewGroup)g.DataContext).Name?.ToString()).ToList();
            _out.WriteLine("headings: " + string.Join(", ", headings));
            Assert.Equal("Mira", headings[0]);
            Assert.Equal(new[] { "Juno", "Kai", "Lena" }, headings.Skip(1));   // the rest in their own order
        });
    }

    [Fact]
    public void TheArrowKeysGoDownTheListAsItIsShown()
    {
        WindowHarness.Run(window =>
        {
            var (_, target) = Opened(window);
            var shown = Names(Open(target));

            var source = PresentationSource.FromVisual(target);
            var picked = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Down) { RoutedEvent = Keyboard.KeyDownEvent });
                WindowHarness.Pump();
                picked.Add(target.SelectedItem as string ?? "(none)");
            }
            _out.WriteLine("down x4: " + string.Join(", ", picked));
            Assert.Equal(shown.Take(4), picked);

            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Up) { RoutedEvent = Keyboard.KeyDownEvent });
            WindowHarness.Pump();
            Assert.Equal(shown[2], target.SelectedItem);
        });
    }
}
