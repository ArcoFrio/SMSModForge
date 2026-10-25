using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Node panel's fields (the author, 1.8.0): ID and Children on the line's
/// row instead, Kind / Then / Duration as radio buttons, Tag behind a tick box,
/// an empty Actor handled like the player's lines, and faces in the Actor
/// picker, which is as wide as its longest name. Measured on the window.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NodeFieldsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-nodefields-" + Guid.NewGuid().ToString("N"));

    public NodeFieldsTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private MainViewModel Opened(MainWindow window)
    {
        var pack = PackRepository.CreateEmpty("nodefields.pack");
        pack.Characters.Add(new CharacterDef { Key = "kiki", DisplayName = "Kiki" });
        var d = new DialogueDef { Key = "chat", DisplayName = "Chat" };
        var first = new DialogueNodeDef { Id = 1, Actor = "kiki", Text = "Hello!", Tag = "start" };
        first.Children.Add(2);
        d.Nodes.Add(first);
        d.Nodes.Add(new DialogueNodeDef { Id = 2, Text = "The tide comes in." });
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _dir);

        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        vm.SelectedTabIndex = 5;
        vm.SelectedDialogue = vm.Dialogues.Single();
        WindowHarness.Pump();
        return vm;
    }

    private static void Select(MainViewModel vm, int id)
    {
        vm.SelectedNode = vm.SelectedDialogue!.Nodes.Single(n => n.Id == id);
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

    private static T Named<T>(Window window, string name) where T : class => (T)window.FindName(name);

    private static List<RadioButton> Radios(ListBox row) => Descendants<RadioButton>(row).ToList();

    [Fact]
    public void KindThenAndDurationAreRowsOfRadioButtons()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            Select(vm, 1);
            var node = vm.SelectedNode!;
            var kind = Named<ListBox>(window, "NodeKindRow");
            var then = Named<ListBox>(window, "NodeThenRow");
            var duration = Named<ListBox>(window, "NodeDurationRow");

            foreach (var row in new[] { kind, then, duration })
            {
                var radios = Radios(row);
                Assert.True(radios.Count >= 2);
                // In a row, side by side: one baseline, left to right.
                var tops = radios.Select(r => Math.Round(r.TranslatePoint(new Point(0, 0), row).Y)).Distinct().ToList();
                Assert.Single(tops);
                Assert.Single(radios.Where(r => r.IsChecked == true));
            }
            Assert.Equal(Loc.T("enum.DialogueNodeKind.Text"), Radios(kind).Single(r => r.IsChecked == true).Content is ContentPresenter
                ? Descendants<TextBlock>(Radios(kind).Single(r => r.IsChecked == true)).First().Text : "");

            // Picking one sets the line, as the dropdown did.
            Radios(kind)[1].IsChecked = true;
            WindowHarness.Pump();
            Assert.Equal(DialogueNodeKind.Choice, node.Kind);

            var jumpTo = Descendants<ComboBox>(window).First(c => c.GetBindingExpression(ComboBox.TextProperty)?.ParentBinding.Path.Path == "JumpTargetTag");
            Assert.False(jumpTo.IsVisible);
            Radios(then).Last().IsChecked = true;
            WindowHarness.Pump();
            Assert.Equal(JumpMode.Jump, node.JumpMode);
            Assert.True(jumpTo.IsVisible, "Jump to did not appear for Jump");

            Radios(duration).Last().IsChecked = true;
            WindowHarness.Pump();
            Assert.Equal(NodeDurationMode.Timeout, node.Duration);
        });
    }

    [Fact]
    public void TheTagHidesBehindATickBox()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var tick = Named<CheckBox>(window, "NodeTagSwitch");
            var box = Named<TextBox>(window, "NodeTagBox");

            Select(vm, 1);   // tagged
            Assert.True(tick.IsChecked == true);
            Assert.True(box.IsVisible);
            // Beside the tick box, not under it.
            Assert.True(Math.Abs(box.TranslatePoint(new Point(0, box.ActualHeight / 2), window).Y
                                 - tick.TranslatePoint(new Point(0, tick.ActualHeight / 2), window).Y) < 6);

            Select(vm, 2);   // not tagged
            Assert.True(tick.IsChecked == false);
            Assert.False(box.IsVisible);
            tick.IsChecked = true;
            WindowHarness.Pump();
            Assert.True(box.IsVisible);
            box.Text = "tide";
            WindowHarness.Pump();
            Assert.Equal("tide", vm.SelectedNode!.Tag);

            tick.IsChecked = false;
            WindowHarness.Pump();
            Assert.False(box.IsVisible);
            Assert.Equal("", vm.SelectedNode!.Tag);
        });
    }

    private static string TipOf(ListBoxItem row)
    {
        var tip = Assert.IsType<ToolTip>(row.ToolTip);
        tip.DataContext = row.DataContext;   // what opening it does
        WindowHarness.Pump();
        var panel = (FrameworkElement)tip.Content;
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return string.Join(" | ", Descendants<TextBlock>(panel).DefaultIfEmpty()
            .Concat(LogicalTextBlocks(panel))
            .Where(t => t != null).Distinct()
            .Select(t => string.Concat(t!.Inlines.OfType<Run>().Select(r => r.Text))));
    }

    private static IEnumerable<TextBlock> LogicalTextBlocks(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TextBlock t) yield return t;
            foreach (var inner in LogicalTextBlocks(child)) yield return inner;
        }
    }

    [Fact]
    public void IdAndChildrenAreOnTheLinesRowNotInTheNodeBox()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            Select(vm, 1);
            var list = Named<ListBox>(window, "NodeList");
            foreach (bool gameLook in new[] { false, true })
            {
                vm.GameLookNodeRows = gameLook;
                WindowHarness.Pump();
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(vm.SelectedNode);
                string tip = TipOf(row);
                _out.WriteLine($"game look {gameLook}: {tip}");
                Assert.Contains(Loc.T("dialogues.id") + " 1", tip.Replace("  ", " "));
                Assert.Contains(Loc.T("dialogues.children") + " 2", tip.Replace("  ", " "));
            }

            // Nothing in the Node box says ID any more.
            var editor = Named<ScrollViewer>(window, "NodeEditorPane");
            Assert.DoesNotContain(Descendants<TextBlock>(editor), t => t.IsVisible && t.Text == Loc.T("dialogues.id"));
            Assert.DoesNotContain(Descendants<TextBlock>(editor), t => t.IsVisible && t.Text == Loc.T("dialogues.children"));
        });
    }

    [Fact]
    public void ALineWithNobodySpeakingIsSaidSoAndHasNoBustFields()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var note = Named<TextBlock>(window, "NoSpeakerNote");
            var expression = Named<FrameworkElement>(window, "NodeExpressionPicker");

            Select(vm, 2);   // nobody speaking
            _out.WriteLine($"empty {vm.SelectedNodeActorIsEmpty}, note {note.Visibility}/{note.IsVisible}, expression {expression.Visibility}");
            Assert.True(note.IsVisible);
            Assert.False(expression.IsVisible);

            // Neutral is no face: nothing to clear, so nothing shown (the
            // author, 1.8.0 - it could not be cleared, being the same button
            // as no face).
            vm.SelectedNode!.Expression = "neutral";
            WindowHarness.Pump();
            Assert.False(expression.IsVisible);

            // A face left on it stays in sight, to be cleared.
            vm.SelectedNode!.Expression = "Happy";
            WindowHarness.Pump();
            Assert.True(expression.IsVisible);
            vm.SelectedNode!.Expression = "";

            vm.SelectedNode!.Actor = "kiki";
            WindowHarness.Pump();
            Assert.False(note.IsVisible);
            Assert.True(expression.IsVisible);
        });
    }

    [Fact]
    public void TheActorPickerShowsFacesAndIsAsWideAsItsLongestName()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            Select(vm, 1);
            var picker = Named<ComboBox>(window, "NodeActorPicker");
            picker.IsDropDownOpen = true;
            WindowHarness.Pump();

            // Faces are cut on a worker thread: give them a moment to arrive.
            List<Image> faces = new();
            for (int i = 0; i < 40; i++)
            {
                faces = picker.Items.Cast<object>()
                    .Select(item => picker.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement)
                    .Where(c => c != null)
                    .SelectMany(c => Descendants<Image>(c!))
                    .Where(img => img.Source != null && img.ActualWidth > 0)
                    .ToList();
                if (faces.Count >= 5) break;
                System.Threading.Thread.Sleep(100);
                WindowHarness.Pump();
            }
            var rows = picker.Items.Cast<object>()
                .Select(item => picker.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement)
                .Where(c => c != null).ToList();
            _out.WriteLine($"{rows.Count} rows drawn, {faces.Count} with a face");
            Assert.True(faces.Count >= 5, "the game's cast shows no faces");
            Assert.All(faces, f => Assert.Equal(24, f.ActualWidth, 1));

            // Every name drawn fits, face and all; and the box is not the column.
            double widest = rows.SelectMany(r => Descendants<TextBlock>(r!))
                                .Where(t => t.FontFamily.Source != "Segoe MDL2 Assets")
                                .Max(t => t.ActualWidth);
            picker.IsDropDownOpen = false;
            WindowHarness.Pump();
            double column = ((FrameworkElement)picker.Parent).ActualWidth;
            _out.WriteLine($"widest name drawn {widest:0}, picker {picker.ActualWidth:0}, column {column:0}");
            Assert.True(picker.ActualWidth >= widest + 30, "a name in the list is wider than the box");
            Assert.True(picker.ActualWidth < column - 20, "the box still stretches across the column");
        });
    }
}
