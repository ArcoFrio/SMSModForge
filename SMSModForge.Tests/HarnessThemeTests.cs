using System;
using SMSModForge.Services;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The window tests run in the theme the run asks for: Light (App.xaml's
/// defaults) unless <c>SMSMODFORGE_TEST_THEME</c> names another, which is how
/// -WithEx runs them in EX. The app's own start-up runs inside the harness too,
/// after the run's theme is on, and its putting back the saved theme undid it:
/// the EX pass ran in Light until 2026-10-24, and nothing failed - found only
/// when the default became an EX theme and the Normal pass turned EX instead.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class HarnessThemeTests
{
    [Fact]
    public void TheWindowTestsRunInTheThemeTheRunAsksFor()
    {
        string asked = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") is { Length: > 0 } t ? t : "Light";
        WindowHarness.Run(_ =>
        {
            WindowHarness.Pump();
            Assert.Equal(asked, ThemeManager.Current.Name, ignoreCase: true);
        });
    }
}
