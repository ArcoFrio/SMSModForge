using System;
using System.Linq;
using System.Windows.Controls;
using SMSModForge.Localization;
using SMSModForge.View;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Language ▸ Edit texts on screen: a pencil beside every text of the editor's
/// that is showing, found from the texts' own bindings, and none left behind
/// when it is turned off.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UiTextMarksTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    public UiTextMarksTests(ITestOutputHelper o) => _out = o;

    public void Dispose() => Loc.Use(Loc.EnglishCode);

    [Fact]
    public void EveryTextShowingHasAPencil_NoneOnATabNotShowing_AndTheyAllComeOff()
    {
        WindowHarness.Run(window =>
        {
            Loc.Use("de");
            WindowHarness.Pump();

            UiTextMarks.Set(window, true);
            WindowHarness.Pump();
            var marks = UiTextMarks.For(window);
            Assert.NotNull(marks);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            marks!.FindNow();
            _out.WriteLine($"one look took {clock.ElapsedMilliseconds} ms");
            var keys = marks.KeysShown;
            _out.WriteLine($"{keys.Count} texts with a pencil");

            // The menu bar and the tab headers are always showing.
            Assert.Contains("menu.file", keys);
            Assert.Contains("quests.quests", keys);
            // A button inside the Quests tab is not, until the tab is chosen.
            Assert.DoesNotContain("quests.addQuest", keys);

            var tabs = (TabControl)window.FindName("MainTabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (t.Header as string) == Loc.T("quests.quests"));
            WindowHarness.Pump();
            window.UpdateLayout();
            marks.FindNow();
            Assert.Contains("quests.addQuest", marks.KeysShown);

            UiTextMarks.Set(window, false);
            Assert.Null(UiTextMarks.For(window));
            Assert.False(UiTextMarks.IsOn(window));
        });
    }

    [Fact]
    public void SwitchingToEnglishTakesThePencilsOff()
    {
        WindowHarness.Run(window =>
        {
            Loc.Use("de");
            UiTextMarks.Set(window, true);
            Assert.True(UiTextMarks.IsOn(window));

            ((MainWindow)window).SwitchLanguage(Loc.EnglishCode);
            WindowHarness.Pump();
            Assert.False(UiTextMarks.IsOn(window));
        });
    }
}
