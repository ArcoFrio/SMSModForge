using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A dialogue's rows draw each line once (1.9.0, the performance pass). They drew
/// it three times - with its tags, without them, then in readable colours - as each
/// setting of the row arrived after the line did, and building a line into a
/// rich-text document is most of what showing a dialogue costs. Measured on the
/// rows of the real window; what each row shows is held to the line too.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NodeRowDrawsOnceTests
{
    private readonly ITestOutputHelper _out;
    public NodeRowDrawsOnceTests(ITestOutputHelper o) => _out = o;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    [Fact]
    public void EachRowDrawsItsLineOnce_AndShowsItWithoutItsTags()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            for (int i = 0; i < tabs.Items.Count; i++)
                if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues") { tabs.SelectedIndex = i; break; }
            WindowHarness.Pump();

            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();
            var first = vm.SelectedDialogue!;
            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();
            var lines = vm.SelectedDialogue!;
            if (lines.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
            for (int i = 1; i < 12; i++) { vm.SelectedNode = lines.Nodes.Last(); vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
            for (int i = 0; i < lines.Nodes.Count; i++)
                lines.Nodes[i].Text = $"Line {i}: <b>bold</b> and <color=#f66>red</color>\nsecond line";
            WindowHarness.Pump();

            // Away and back: the rows are built anew for the dialogue chosen.
            vm.SelectedDialogue = first;
            WindowHarness.Pump();
            window.UpdateLayout();
            vm.SelectedDialogue = lines;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var rows = Descendants<MarkupTextBlock>(window).Where(b => b.Name == "NodeRowText" && b.IsVisible).ToList();
            // Under a loaded machine the rows can still be coming: wait for them
            // (what each has drawn does not depend on when it is counted).
            for (int t = 0; t < 60 && rows.Count < lines.Nodes.Count; t++)
            {
                WindowHarness.Wait(TimeSpan.FromMilliseconds(50));
                window.UpdateLayout();
                rows = Descendants<MarkupTextBlock>(window).Where(b => b.Name == "NodeRowText" && b.IsVisible).ToList();
            }
            _out.WriteLine($"{rows.Count} rows; draws: {string.Join(",", rows.Select(r => r.Draws))}");
            Assert.True(rows.Count >= lines.Nodes.Count, "the rows were not built, so this proves nothing");
            Assert.All(rows, r => Assert.Equal(1, r.Draws));

            // Plain words in the theme's own text colour, as the line box the rows used drew them.
            var text = ((SolidColorBrush)window.FindResource(SMSModForge.Services.ThemeManager.KeyText)).Color;
            Assert.All(rows, r => Assert.Contains(r.LineRuns, run => run.Foreground is SolidColorBrush b && b.Color == text));

            foreach (var row in rows)
            {
                var node = (DialogueNodeViewModel)row.DataContext;
                string shown = new TextRange(row.ContentStart, row.ContentEnd).Text.TrimEnd('\r', '\n');
                Assert.DoesNotContain("<b>", shown);
                Assert.StartsWith(node.Text.Substring(0, node.Text.IndexOf(':') + 1), shown);
            }
        });
    }
}
