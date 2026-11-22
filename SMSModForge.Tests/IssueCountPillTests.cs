using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The issue count as a notification (1.10.0, the author: "can we have some notification-like highlighting to the
/// number of issues if there are any? Just to bring some more attention to them"): a pill beside Issues on the
/// ModForge tab's strip, and on the ModForge tab itself - red with an error, amber with only warnings, none when the
/// pack is clean. Read from the window as drawn, in the run's look (Classic, or EX with -WithEx).
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class IssueCountPillTests
{
    private static Color ColorOf(Brush? b) => (b as SolidColorBrush)?.Color ?? Colors.Transparent;

    [Fact]
    public void TheIssueCountShowsAsANotification_RedForErrors_AmberForWarnings_NoneWhenClean()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SMSModForge-pill-" + Guid.NewGuid().ToString("N"));
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                Assert.True(vm.MakePack(folder, "Pill test"));
                vm.SelectedTabIndex = MainTab.ModForge;
                vm.HomePage = HomePage.Home;
                var pill = (Border)window.FindName("IssueCountPill");
                var tabs = (TabControl)window.FindName("MainTabs");
                var modforge = tabs.Items.OfType<TabItem>().First(t => View.TabIcon.GetBadge(t) == vm.Issues.Count && t.Header as string == Localization.Loc.T("home.modforge"));
                Border TabBadge() { modforge.ApplyTemplate(); return (Border)modforge.Template.FindName("badge", modforge); }
                var danger = ColorOf((Brush)window.FindResource("Theme.Danger"));
                var warning = ColorOf((Brush)window.FindResource("Theme.Warning"));

                void Settle() { WindowHarness.Pump(); window.UpdateLayout(); WindowHarness.Pump(); }

                // Clean: nothing shouts.
                vm.ValidateCommand.Execute(null);
                Settle();
                Assert.Empty(vm.Issues.Where(i => i.Severity != Validation.Severity.Info));
                if (vm.Issues.Count == 0)
                {
                    Assert.False(pill.IsVisible);
                    Assert.NotEqual(Visibility.Visible, TabBadge().Visibility);
                }

                // A character with no pronouns is an error: red, with the count.
                vm.SelectedTabIndex = MainTab.Characters;
                vm.AddCharacterCommand.Execute(null);
                vm.SelectedTabIndex = MainTab.ModForge;
                vm.HomePage = HomePage.Home;
                vm.ValidateCommand.Execute(null);
                Settle();
                Assert.Contains(vm.Issues, i => i.Severity == Validation.Severity.Error);
                Assert.True(pill.IsVisible, "the pill shows when there are issues");
                Assert.Equal(danger, ColorOf(pill.Background));
                Assert.Equal(vm.Issues.Count.ToString(), ((TextBlock)pill.Child).Text);
                Assert.Equal(Visibility.Visible, TabBadge().Visibility);
                Assert.Equal(danger, ColorOf(TabBadge().Background));

                // Only warnings: amber. (A quest nothing starts is a warning.)
                vm.Characters.First(c => !c.IsPlayer && !c.Model.IsVanillaCharacter).Pronouns = Model.Pronouns.Female;
                vm.SelectedTabIndex = MainTab.Quests;
                vm.AddQuestCommand.Execute(null);
                vm.SelectedTabIndex = MainTab.ModForge;
                vm.HomePage = HomePage.Home;
                vm.ValidateCommand.Execute(null);
                Settle();
                Assert.DoesNotContain(vm.Issues, i => i.Severity == Validation.Severity.Error);
                Assert.Contains(vm.Issues, i => i.Severity == Validation.Severity.Warning);
                Assert.Equal(warning, ColorOf(pill.Background));
                Assert.Equal(warning, ColorOf(TabBadge().Background));
            });
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
