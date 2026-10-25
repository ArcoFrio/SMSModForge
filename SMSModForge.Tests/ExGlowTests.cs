using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using SMSModForge.Services;
using SMSModForge.View;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// EX glows on its dark themes only (the author, 1.8.0: on the light ones
/// the glowing things "look more like blurs than actual glow ... this might
/// affect legibility"). A light theme keeps the accent borders and lines that
/// say the same thing, and its words their sharp edges.
/// </summary>
[Trait("Speed", "Slow")]   // applies themes to the running app
public sealed class ExGlowTests
{
    private static ThemeDef Named(string name) => ThemeManager.All.Single(t => t.Name == name);

    private static Effect? NameGlowIn(string theme)
    {
        ThemeManager.Apply(Named(theme));
        var speaker = new TextBlock { Text = "Kiki: " };
        ReadableInk.SetHex(speaker, "#E8A0C0");
        return speaker.Effect;
    }

    [Fact]
    public void ALightExThemeHasNoGlow_ADarkOneDoes()
    {
        WindowHarness.Run(_ =>
        {
            var was = ThemeManager.Current;
            try
            {
                Assert.False(Named("Frost").IsDark);
                Assert.Null(NameGlowIn("Frost"));
                Assert.Null(Application.Current.Resources[ThemeManager.KeyGlow]);
                Assert.Null(Application.Current.Resources[ThemeManager.KeyGlowSoft]);

                // The control: a dark EX theme still glows.
                Assert.True(Named("Neon").IsDark);
                Assert.IsType<DropShadowEffect>(NameGlowIn("Neon"));
                Assert.IsType<DropShadowEffect>(Application.Current.Resources[ThemeManager.KeyGlow]);
            }
            finally { ThemeManager.Apply(was); }
        });
    }
}
