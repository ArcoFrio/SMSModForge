using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The menus, looked over again (the author, 1.8.0: "these menus are some of
/// the most outdated aspects of the tool"): Themes under Options, Options in
/// groups under headings, and Undo and Redo where an Edit menu has them.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class MenuLayoutTests
{
    private static Menu Bar(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Menu m) return m;
            if (Bar(child) is { } found) return found;
        }
        return null!;
    }

    private static MenuItem Top(MainWindow window, string key)
        => Bar(window).Items.OfType<MenuItem>().Single(m => m.Header as string == Loc.T(key));

    [Fact]
    public void ThemesAreUnderOptions_NotAMenuOfTheirOwn()
    {
        WindowHarness.Run(window =>
        {
            var tops = Bar(window).Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
            Assert.DoesNotContain(Loc.T("menu.themes"), tops);
            var themes = (MenuItem)window.FindName("ThemesMenu");
            Assert.Same(Top(window, "menu.options"), ItemsControl.ItemsControlFromItemContainer(themes));
        });
    }

    [Fact]
    public void OptionsAreGroupedUnderHeadings_EachSettingUnderItsOwn()
    {
        WindowHarness.Run(window =>
        {
            var options = Top(window, "menu.options");
            var heading = (Style)window.FindResource("Theme.MenuHeadingItem");
            var groups = new Dictionary<string, List<string>>();
            string? under = null;
            foreach (var item in options.Items.OfType<MenuItem>())
            {
                if (item.Style == heading) { under = (string)item.Header; groups[under] = new List<string>(); continue; }
                Assert.True(under != null, $"{item.Header} comes before any heading");
                groups[under!].Add((string)item.Header);
            }

            Assert.Equal(new[] { "menu.heading.look", "menu.heading.writing", "menu.heading.saving", "menu.heading.game", "menu.heading.updates" }
                             .Select(Loc.T), groups.Keys);
            Assert.Contains(Loc.T("menu.themes"), groups[Loc.T("menu.heading.look")]);
            Assert.Contains(Loc.T("menu.previewQuality"), groups[Loc.T("menu.heading.look")]);
            Assert.Contains(Loc.T("menu.spellCheckNodeText"), groups[Loc.T("menu.heading.writing")]);
            Assert.Contains(Loc.T("menu.confirmChangesSave"), groups[Loc.T("menu.heading.saving")]);
            Assert.Contains(Loc.T("menu.setGameFolder"), groups[Loc.T("menu.heading.game")]);
            Assert.Contains(Loc.T("menu.checkForUpdates"), groups[Loc.T("menu.heading.updates")]);
            // The fourteen it had, none lost, and Themes.
            Assert.Equal(15, groups.Values.Sum(g => g.Count));
        });
    }

    [Fact]
    public void EditStartsWithUndoAndRedo()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var items = Top(window, "menu.edit").Items.OfType<MenuItem>().ToList();
            Assert.Same(vm.UndoCommand, items[0].Command);
            Assert.Same(vm.RedoCommand, items[1].Command);
            Assert.Equal(Loc.T("menu.undo.keys"), items[0].InputGestureText);
        });
    }
}
