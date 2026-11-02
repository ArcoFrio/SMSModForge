using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using SMSModForge.View;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Copy all, Paste and Overwrite show only while the mouse - or the keyboard -
/// is on their list (the author, 1.8.0). The test desktop has no mouse, so the
/// keyboard is what moves here; the mouse goes through the same switch.
/// </summary>
[Trait("Speed", "Slow")]   // builds a window on the harness thread
public sealed class HoverToolsTests
{
    [Fact]
    public void TheToolsShowOnlyWhileTheListIsInUse()
    {
        WindowHarness.Run(_ =>
        {
            var inList = new TextBox { Text = "a condition" };
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            tools.Children.Add(new Button { Content = "Copy all" });
            HoverTools.SetReveal(tools, true);
            var plain = new StackPanel { Orientation = Orientation.Horizontal };
            plain.Children.Add(new Button { Content = "Add" });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(plain);
            row.Children.Add(tools);
            var list = new StackPanel();
            list.Children.Add(inList);
            list.Children.Add(row);
            var elsewhere = new TextBox { Text = "another panel" };
            var page = new StackPanel();
            page.Children.Add(list);
            page.Children.Add(elsewhere);
            var window = new Window { Width = 400, Height = 200, Left = -10000, Top = -10000, ShowInTaskbar = false, Content = page };
            window.Show();
            window.UpdateLayout();
            WindowHarness.Pump();

            elsewhere.Focus();
            Settle();
            Assert.Equal(0, tools.Opacity, 2);
            Assert.False(tools.IsHitTestVisible, "hidden tools can still be clicked");
            // The control: the row's other buttons are always there.
            Assert.Equal(1, plain.Opacity, 2);
            // Hidden, they keep their room: nothing under the list moves.
            Assert.True(tools.ActualWidth > 0, "the hidden tools gave up their room");

            inList.Focus();
            Settle();
            Assert.Equal(1, tools.Opacity, 2);
            Assert.True(tools.IsHitTestVisible);

            elsewhere.Focus();
            Settle();
            Assert.Equal(0, tools.Opacity, 2);
            window.Close();
        });
    }

    [Fact]
    public void TheGapsBetweenTheButtonsAreStillOverTheList()
    {
        // The author: they showed on "+ Add condition" and went the moment the
        // mouse moved across to them - the gap between the buttons had no
        // background, so it was over nothing and the list said the mouse had
        // left. Hit-tested at the gap, as the mouse would be.
        WindowHarness.Run(_ =>
        {
            var add = new Button { Content = "+ Add condition", Margin = new Thickness(0, 0, 8, 0) };
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            tools.Children.Add(new Button { Content = "Copy all" });
            HoverTools.SetReveal(tools, true);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(add);
            row.Children.Add(tools);
            var list = new StackPanel();
            list.Children.Add(new TextBox { Text = "a condition" });
            list.Children.Add(row);
            var page = new StackPanel();
            page.Children.Add(list);
            var window = new Window { Width = 400, Height = 200, Left = -10000, Top = -10000, ShowInTaskbar = false, Content = page };
            window.Show();
            window.UpdateLayout();
            WindowHarness.Pump();

            // In the margin right of "+ Add condition", and in the empty room
            // right of the row.
            var addCorner = add.TranslatePoint(new Point(add.ActualWidth, add.ActualHeight / 2), page);
            var gap = new Point(addCorner.X + 4, addCorner.Y);
            var beside = new Point(page.ActualWidth - 5, addCorner.Y);
            foreach (var point in new[] { gap, beside })
            {
                var hit = VisualTreeHelper.HitTest(page, point)?.VisualHit;
                Assert.True(hit != null && IsWithin(hit, list), $"at {point} the mouse is over {hit?.GetType().Name ?? "nothing"}, not the list");
            }
            // Control: the button itself is still what is hit on the button.
            var onAdd = add.TranslatePoint(new Point(add.ActualWidth / 2, add.ActualHeight / 2), page);
            Assert.True(IsWithin(VisualTreeHelper.HitTest(page, onAdd)!.VisualHit, add));
            window.Close();
        });
    }

    [Fact]
    public void InTheEditorTheGapBeforeEachListsToolsIsOverItsList()
    {
        // The same, measured in the window as it is drawn - a new dialogue's
        // start conditions on the Dialogues tab, in each look the suite runs.
        WindowHarness.Run(window =>
        {
            var vm = (SMSModForge.ViewModel.MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = SMSModForge.ViewModel.MainTab.Dialogues;
            vm.AddDialogueCommand.Execute(null);
            vm.SelectedNode = vm.SelectedDialogue?.Nodes.FirstOrDefault();
            window.UpdateLayout();
            WindowHarness.Pump();

            var toolbars = Descendants(window).OfType<StackPanel>()
                .Where(p => HoverTools.GetReveal(p) && p.IsVisible && p.ActualWidth > 0).ToList();
            Assert.True(toolbars.Count >= 1, "no list with copy buttons on screen to measure");
            foreach (var tools in toolbars)
            {
                var area = VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(tools));
                // Before the divider each toolbar starts with: the space the mouse
                // crosses on its way from "+ Add" to the buttons, where nothing is drawn.
                var gap = tools.TranslatePoint(new Point(4, tools.ActualHeight / 2), window);
                var hit = VisualTreeHelper.HitTest(window, gap)?.VisualHit;
                Assert.True(hit != null && IsWithin(hit, area), $"at {gap} the mouse is over {hit?.GetType().Name ?? "nothing"}, not the list");
            }
        });
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static bool IsWithin(DependencyObject hit, DependencyObject ancestor)
    {
        for (var d = hit; d != null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, ancestor)) return true;
        return false;
    }

    private static void Settle()
    {
        WindowHarness.Pump();
        WindowHarness.Wait(TimeSpan.FromMilliseconds(300));
    }

    /// <summary>Every Copy / Paste / Overwrite under a list in the window is in
    /// such a row - a list added later does not bring the always-there ones back.</summary>
    [Fact]
    public void EveryListsCopyPasteAndOverwriteHide()
    {
        XNamespace view = "clr-namespace:SMSModForge.View";
        var doc = XDocument.Load(Xaml.MainWindowPath);
        var overwrites = doc.Descendants()
            .Where(e => e.Name.LocalName == "Button"
                        && ((string?)e.Attribute("Command") ?? "").Contains("Overwrite"))
            .ToList();
        Assert.True(overwrites.Count >= 12, $"only {overwrites.Count} Overwrite buttons found: the scan is not reading the window");
        foreach (var button in overwrites)
        {
            var row = button.Parent!;
            Assert.True((string?)row.Attribute(view + "HoverTools.Reveal") == "True",
                $"{button.Attribute("Command")} is always on screen");
            foreach (var sibling in row.Elements().Where(e => e.Name.LocalName == "Button"))
            {
                string command = (string?)sibling.Attribute("Command") ?? "";
                Assert.True(command.Contains("Copy") || command.Contains("Paste") || command.Contains("Overwrite"),
                    $"{command} hides with the copy buttons");
            }
        }
    }
}
