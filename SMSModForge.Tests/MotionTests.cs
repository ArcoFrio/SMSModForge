using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The pages' short movements (Services.Motion, 1.10.0): a tab's page comes in from clear - and in EX from a few
/// pixels lower - and is as it was a moment later, its own transform back. Control: with Options ▸ Animations off,
/// and under the harness (where nothing moves unless a test asks), the page is simply there.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class MotionTests
{
    private readonly ITestOutputHelper _out;
    public MotionTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void ATabsPageComesInAndSettles_AndWithAnimationsOffItIsSimplyThere()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _out.WriteLine("Windows' own Animation effects are off on this machine: nothing moves, as it should - nothing to hold");
            return;
        }
        bool was = EditorPrefs.Animations;
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            UIElement Page() => (UIElement)tabs.Template.FindName("PART_SelectedContentHost", tabs);

            // Under the harness nothing moves unless a test asks.
            tabs.SelectedIndex = MainTab.Characters;
            Assert.Equal(1.0, Page().Opacity);

            Motion.ForTests = true;
            try
            {
                vm.Animations = true;
                tabs.SelectedIndex = MainTab.Places;
                WindowHarness.Pump();
                double during = Page().Opacity;
                var shift = Page().RenderTransform;
                _out.WriteLine($"{ThemeManager.Current.Name}: right after the switch the page is at {during:0.00} opacity, transform {shift?.GetType().Name}");
                Assert.True(during < 1, "the page did not fade in");
                if (ThemeManager.Current.IsEx)
                    Assert.True(shift is TranslateTransform { Y: > 0 }, "in EX the page does not rise");
                else
                    Assert.True(shift == null || shift.Value.IsIdentity, "the Normal looks moved the page");

                WindowHarness.Wait(TimeSpan.FromMilliseconds(500));
                Assert.Equal(1.0, Page().Opacity);
                Assert.True(Page().RenderTransform == null || Page().RenderTransform.Value.IsIdentity, "the page's transform was not put back");

                // Control: Options ▸ Animations off - simply there.
                vm.Animations = false;
                tabs.SelectedIndex = MainTab.Dialogues;
                WindowHarness.Pump();
                Assert.Equal(1.0, Page().Opacity);
            }
            finally
            {
                Motion.ForTests = false;
                vm.Animations = was;
            }
        });
    }
}
