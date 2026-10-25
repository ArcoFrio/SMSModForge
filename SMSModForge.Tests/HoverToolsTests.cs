using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
