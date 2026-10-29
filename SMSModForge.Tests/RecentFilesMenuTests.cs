using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// File ▸ Recent files, each pack with a ✕ that takes it off the list (the
/// author, after 1.8.0: "a small x to recent opened packs so we can remove them
/// from the list"). Measured in the real window, so the EX pass checks it in
/// EX too.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class RecentFilesMenuTests
{
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    /// <summary>The run's desktop has no mouse, so a menu cannot open: its
    /// popup's content is laid out instead (as UiBindingTests does).</summary>
    private static FrameworkElement? OpenSubmenu(MenuItem menu)
    {
        menu.IsSubmenuOpen = true;
        WindowHarness.Pump();
        menu.ApplyTemplate();
        if (menu.Template.FindName("PART_Popup", menu) is System.Windows.Controls.Primitives.Popup { Child: FrameworkElement content })
        {
            content.Measure(new Size(1000, 1000));
            content.Arrange(new Rect(content.DesiredSize));
            WindowHarness.Pump();
            return content;
        }
        return null;
    }

    /// <summary>A picture of the open list, when SMSMODFORGE_SNAPSHOTS names a folder.</summary>
    private static void Snap(FrameworkElement? content, Window window)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(folder) || content == null) return;
        System.IO.Directory.CreateDirectory(folder);
        string theme = SMSModForge.Services.ThemeManager.Current.Name;
        ThemeSnapshotTests.Save(ThemeSnapshotTests.Render(content, window.Background),
                                System.IO.Path.Combine(folder, "recent-files-" + theme + ".png"));
    }

    [Fact]
    public void Each_recent_pack_has_a_cross_that_takes_it_off_the_list_and_nothing_else()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var packs = new[] { @"C:\Packs\Beach_Day", @"C:\Packs\Second", @"C:\Packs\Third" };
            vm.RecentFiles.Clear();
            foreach (var p in packs) vm.RecentFiles.Add(p);

            var file = Descendants<MenuItem>(window).First(m => SMSModForge.View.TutorialAnchor.GetId(m) == "menu:file");
            OpenSubmenu(file);
            var recent = file.Items.OfType<MenuItem>().Single(m => m.Header as string == Loc.T("menu.recentFiles"));
            Snap(OpenSubmenu(recent), window);

            var rows = Enumerable.Range(0, packs.Length)
                                 .Select(i => (MenuItem)recent.ItemContainerGenerator.ContainerFromIndex(i))
                                 .ToList();
            Button? middle = null;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                Assert.Same(vm.OpenRecentCommand, row.Command);     // the row still opens the pack
                // The whole path, underscores and all - a plain header took one
                // as a menu access key and dropped it.
                // Laid out rather than "visible": the popup is arranged by hand
                // on a desktop with no mouse, so nothing in it is ever shown.
                Assert.Contains(Descendants<TextBlock>(row), t => t.ActualWidth > 0 && t.Text == packs[i]);
                var cross = Assert.Single(Descendants<Button>(row));
                Assert.True(cross.Visibility == Visibility.Visible && cross.ActualWidth > 0 && cross.ActualHeight > 0,
                            $"no cross drawn on {packs[i]}");
                Assert.Equal("✕", cross.Content as string);
                Assert.Equal(Loc.T("menu.recentFiles.remove.tip"), cross.ToolTip as string);
                Assert.False(cross.Focusable, "the cross takes the arrow keys away from the packs");
                if (i == 1) middle = cross;
            }

            ((IInvokeProvider)new ButtonAutomationPeer(middle!).GetPattern(PatternInterface.Invoke)!).Invoke();
            WindowHarness.Pump();

            Assert.Equal(new[] { packs[0], packs[2] }, vm.RecentFiles);    // that one, and only that one
            Assert.Null(vm.PackRoot);                                       // and nothing was opened
        });
    }
}
