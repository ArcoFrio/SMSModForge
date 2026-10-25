using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Services;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Themes of one's own (the author, 1.8.0: "How about giving users the
/// ability to create custom themes?"): made from the theme in use, coloured
/// and named in their window with the editor showing each change, kept between
/// sessions under a name apart from the built-in ones, listed in the Themes
/// menu, and never unreadable whatever colours are picked.
/// </summary>
[Trait("Speed", "Slow")]   // builds real windows; see CLAUDE.md
public sealed class CustomThemesTests
{
    private readonly ITestOutputHelper _out;
    public CustomThemesTests(ITestOutputHelper o) => _out = o;

    private static Color Resource(string key) => ((SolidColorBrush)Application.Current.Resources[key]).Color;

    [Fact]
    public void AThemeOfOnesOwnIsMadeShownKeptAndListed()
    {
        WindowHarness.Run(window =>
        {
            var was = ThemeManager.Current;
            foreach (var t in ThemeManager.Custom.ToList()) ThemeManager.RemoveCustom(t);
            try
            {
                var themes = new CustomThemesWindow();
                themes.Show();
                WindowHarness.Pump();
                var vm = themes.ViewModel;
                Assert.False(vm.HasSelection);

                // A new one starts as the theme in use, and is in use itself.
                vm.NewCommand.Execute(null);
                WindowHarness.Pump();
                var mine = Assert.Single(ThemeManager.Custom);
                Assert.Same(mine, ThemeManager.Current);
                Assert.Equal(was.Accent, mine.Accent);
                Assert.Equal(was.IsEx, mine.IsEx);

                // A colour typed in shows on the editor at once.
                vm.AccentColour = "#ff00aa";
                WindowHarness.Pump();
                Assert.Equal("#FF00AA", vm.AccentColour);
                Assert.Equal(Color.FromRgb(0xFF, 0x00, 0xAA), Resource(ThemeManager.KeyAccent));
                // Not a colour: nothing changes (the control).
                vm.AccentColour = "not a colour";
                Assert.Equal("#FF00AA", vm.AccentColour);

                // The look: Classic and back, the window's own templates following.
                vm.IsEx = false;
                WindowHarness.Pump();
                Assert.False(ThemeManager.Current.IsEx);
                vm.IsEx = true;

                // Light or dark follows the background.
                vm.WindowColour = "#101014";
                Assert.True(vm.IsDark);

                // Words are moved until they read: white text on white panels
                // and boxes, every other colour light, comes out dark - and
                // nothing is said, because nothing is unreadable.
                vm.WindowColour = "#F3F3F3";
                vm.ChromeColour = "#E8E8E8";
                vm.SurfaceColour = "#FFFFFF";
                vm.ControlColour = "#FFFFFF";
                vm.TextColour = "#FFFFFF";
                WindowHarness.Pump();
                double contrast = ThemeManager.Contrast(Resource(ThemeManager.KeyText), Resource(ThemeManager.KeySurface));
                _out.WriteLine($"text on panels: {contrast:0.00}");
                Assert.True(contrast >= ThemeManager.ReadableContrast);
                Assert.False(vm.HasUnreadable, string.Join("; ", ThemeManager.Unreadable(vm.Selected!)));
                Assert.False(((TextBlock)themes.FindName("UnreadableNote")).IsVisible);

                // The control: a mid-grey panel and a near-black box - no one
                // text colour reads on both - and the window says so.
                vm.SurfaceColour = "#808080";
                vm.ControlColour = "#101014";
                WindowHarness.Pump();
                _out.WriteLine("unreadable: " + string.Join("; ", ThemeManager.Unreadable(vm.Selected!)));
                Assert.True(vm.HasUnreadable);
                Assert.True(((TextBlock)themes.FindName("UnreadableNote")).IsVisible);

                // Named anything - a built-in theme's name too - and a second
                // of the same name gets a number.
                vm.Name = "Light";
                vm.DuplicateCommand.Execute(null);
                Assert.Equal(new[] { "Light", "Light 2" }, ThemeManager.Custom.Select(t => t.Name));
                var light = ThemeManager.Custom[0];
                Assert.Equal("custom:Light", light.SavedAs);
                Assert.Same(light, ThemeManager.Find("custom:Light"));
                Assert.Equal("Light", ThemeManager.Find("Light")!.Name);
                Assert.False(ThemeManager.Find("Light")!.IsCustom);   // the built-in one keeps its name
                Assert.Equal((light, false), ThemeManager.ThemeAtStart("custom:Light", movedBefore: false));
                Assert.Equal(ThemeManager.Default, ThemeManager.ThemeAtStart("custom:Gone", movedBefore: true).Theme);

                // Kept between sessions.
                ThemeManager.LoadCustom();
                Assert.Equal(new[] { "Light", "Light 2" }, ThemeManager.Custom.Select(t => t.Name));
                Assert.Equal("#FF00AA", ThemeManager.Custom[0].Accent);
                Assert.Equal("#F3F3F3", ThemeManager.Custom[0].Window);
                Assert.False(ThemeManager.Custom[0].IsDark);   // the light background it was given last

                // Listed in the Themes menu, under Yours.
                var menu = (MenuItem)window.FindName("CustomThemesMenu");
                Assert.Equal(Visibility.Visible, menu.Visibility);
                Assert.Equal(new[] { "Light", "Light 2" }, menu.Items.Cast<ThemeDef>().Select(t => t.DisplayName));
                Assert.NotNull(window.FindName("MakeThemesMenuItem"));

                // Removed while in use: the editor goes back to its default.
                vm.Selected = ThemeManager.Custom[1];
                vm.RemoveCommand.Execute(null);
                WindowHarness.Pump();
                Assert.Single(ThemeManager.Custom);
                vm.RemoveCommand.Execute(null);
                WindowHarness.Pump();
                Assert.Empty(ThemeManager.Custom);
                Assert.Same(ThemeManager.Default, ThemeManager.Current);
                Assert.Equal(Visibility.Collapsed, menu.Visibility);
                themes.Close();
            }
            finally
            {
                foreach (var t in ThemeManager.Custom.ToList()) ThemeManager.RemoveCustom(t);
                ThemeManager.Apply(was);
            }
        });
    }
}
