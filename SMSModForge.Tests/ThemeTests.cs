using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Every theme reads (the author, 2026-10-21: themes with white-on-white
/// selections, fields that looked greyed out, an open tab you could not pick
/// out, buttons the colour of the panel behind them).
/// </summary>
public sealed class ThemeTests
{
    private readonly ITestOutputHelper _out;
    public ThemeTests(ITestOutputHelper o) => _out = o;

    /// <summary>The pairs the editor draws words with: words, and what they
    /// sit on.</summary>

    [Fact]
    public void EveryThemesWordsReadOnEverythingTheyAreDrawnOn()
    {
        // The pairs, and what each must reach, are ThemeManager's (TextPairs,
        // NeededFor): Your themes reads the same list.
        var failures = new List<string>();
        foreach (var theme in ThemeManager.All)
            failures.AddRange(ThemeManager.Unreadable(theme).Select(f => theme.Name + ": " + f));
        foreach (var f in failures) _out.WriteLine(f);
        Assert.Empty(failures);
    }

    [Fact]
    public void TheThingsThatMustStandOutDo()
    {
        // Not words, so not 4.5 - but each must differ visibly from what is
        // around it: a button from its panel, the open tab from the strip, a
        // selected row from the list.
        var failures = new List<string>();
        foreach (var theme in ThemeManager.All)
        {
            var p = ThemeManager.Palette(theme);
            void Apart(string a, string b, double min)
            {
                double r = ThemeManager.Contrast(p[a], p[b]);
                if (r < min) failures.Add($"{theme.Name}: {a} vs {b} = {r:0.00}");
            }
            Apart(ThemeManager.KeyButtonBorder, ThemeManager.KeySurface, 1.5);
            Apart(ThemeManager.KeyAccent, ThemeManager.KeyTabInactive, 2.0);   // the open tab's bar
            Apart(ThemeManager.KeySelection, ThemeManager.KeyControl, 1.15);
            Apart(ThemeManager.KeyControlBorder, ThemeManager.KeyControl, 1.6);
            Apart(ThemeManager.KeyAccent, ThemeManager.KeyControl, 2.0);   // a ticked box, the focus border
        }
        foreach (var f in failures) _out.WriteLine(f);
        Assert.Empty(failures);
    }

    [Fact]
    public void ThemesAreListedLightThenDark_AndEverySavedNameStillFindsOne()
    {
        // Normal and EX (1.8.0), each light and dark; every theme in exactly one.
        Assert.Equal(ThemeManager.All.Where(t => !t.IsDark && !t.IsEx), ThemeManager.LightThemes);
        Assert.Equal(ThemeManager.All.Where(t => t.IsDark && !t.IsEx), ThemeManager.DarkThemes);
        Assert.Equal(ThemeManager.All.Where(t => !t.IsDark && t.IsEx), ThemeManager.ExLightThemes);
        Assert.Equal(ThemeManager.All.Where(t => t.IsDark && t.IsEx), ThemeManager.ExDarkThemes);
        Assert.Equal(ThemeManager.All.Count, ThemeManager.LightThemes.Count + ThemeManager.DarkThemes.Count
                                             + ThemeManager.ExLightThemes.Count + ThemeManager.ExDarkThemes.Count);
        Assert.NotEmpty(ThemeManager.ExLightThemes);
        Assert.NotEmpty(ThemeManager.ExDarkThemes);
        Assert.True(ThemeManager.All.Take(ThemeManager.LightThemes.Count).All(t => !t.IsDark));
        // The names ModForge has saved since 1.1 - a person's choice must
        // still find its theme after the rework.
        foreach (var old in new[] { "Light", "Slate", "Forest", "Plum", "Ocean", "Sandstone", "Rose", "Dark", "Midnight", "Carbon" })
            Assert.Contains(ThemeManager.All, t => t.Name == old);
        // Every theme has a name in the English file, and ids carry no spaces
        // (an id is part of a text key).
        foreach (var t in ThemeManager.All)
        {
            Assert.DoesNotContain(' ', t.Name);
            Assert.NotEqual(t.NameKey, t.DisplayName);
        }
    }
}

/// <summary>The themes in the real window: the menu, and what is drawn.</summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class ThemeWindowTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-themes-" + Guid.NewGuid().ToString("N"));

    public ThemeWindowTests(ITestOutputHelper o) => _out = o;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>Options ▸ Themes (1.8.0: a menu of its own before).</summary>
    private static MenuItem ThemesMenu(MainWindow window)
    {
        var themes = (MenuItem)window.FindName("ThemesMenu");
        var options = (MenuItem)ItemsControl.ItemsControlFromItemContainer(themes);
        Assert.Equal(SMSModForge.Localization.Loc.T("menu.options"), options.Header as string);
        return themes;
    }

    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            if (FindVisual<T>(child) is { } found) return found;
        }
        return null;
    }

    [Fact]
    public void TheThemesMenuIsExThenClassicEachLightThenDark_AndPickingOneAppliesIt()
    {
        WindowHarness.Run(window =>
        {
            // EX and Classic (1.8.0), each light then dark under its headings;
            // EX first, as the look a first start opens in.
            var menu = ThemesMenu(window);
            var groups = menu.Items.OfType<MenuItem>().Take(2).ToList();
            Assert.Equal(new[] { SMSModForge.Localization.Loc.T("theme.group.ex"), SMSModForge.Localization.Loc.T("theme.group.normal") },
                         groups.Select(g => (string)g.Header));
            string Describe(object o) => o switch
            {
                ThemeDef t => t.Name,
                MenuItem m => "[" + m.Header + "]",
                Separator => "----",
                _ => o.ToString() ?? "",
            };
            List<string> Expected(IEnumerable<ThemeDef> light, IEnumerable<ThemeDef> dark)
            {
                var expected = new List<string> { "[" + SMSModForge.Localization.Loc.T("theme.group.light") + "]" };
                expected.AddRange(light.Select(t => t.Name));
                expected.Add("----");
                expected.Add("[" + SMSModForge.Localization.Loc.T("theme.group.dark") + "]");
                expected.AddRange(dark.Select(t => t.Name));
                return expected;
            }
            foreach (var g in groups) _out.WriteLine(g.Header + ": " + string.Join(" ", g.Items.Cast<object>().Select(Describe)));
            Assert.Equal(Expected(ThemeManager.ExLightThemes, ThemeManager.ExDarkThemes), groups[0].Items.Cast<object>().Select(Describe));
            Assert.Equal(Expected(ThemeManager.LightThemes, ThemeManager.DarkThemes), groups[1].Items.Cast<object>().Select(Describe));

            var was = ThemeManager.Current;
            try
            {
                var vm = (MainViewModel)window.DataContext;
                var midnight = ThemeManager.All.First(t => t.Name == "Midnight");
                vm.ApplyThemeCommand.Execute(midnight);
                Assert.Same(midnight, ThemeManager.Current);
                Assert.True(midnight.IsCurrent);
                // Applied to the window's own resources, system colours included.
                var selection = (SolidColorBrush)Application.Current.Resources[SystemColors.InactiveSelectionHighlightBrushKey];
                Assert.Equal(ThemeManager.Palette(midnight)[ThemeManager.KeySelectionInactive], selection.Color);
            }
            finally { ThemeManager.Apply(was); }
        });
    }

    /// <summary>
    /// A first start opens in Frost; a Classic choice saved before 1.8.0 is
    /// moved once to the EX theme most like it - light for light, dark for
    /// dark, the nearest accent - and a Classic theme picked after that stays
    /// (the author, 1.8.0).
    /// </summary>
    [Fact]
    public void AStartThatMovesTheThemeSaysFromWhatToWhat()
    {
        // The window tells the person, once (the author, 1.8.0) - from what
        // ApplySaved leaves here. The run's own settings folder, not anyone's.
        WindowHarness.Run(_ =>
        {
            var was = ThemeManager.Current;
            string saved = SMSModForge.Services.AppDataFolder.PathOf("theme.json");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(saved)!);
            System.IO.File.WriteAllText(saved, "\"Midnight\"");
            try
            {
                ThemeManager.ApplySaved();
                var moved = ThemeManager.MovedAtStart;
                Assert.NotNull(moved);
                Assert.Equal("Midnight", moved!.Value.From.Name);
                Assert.Equal("Pulse", moved.Value.To.Name);
                Assert.Same(moved.Value.To, ThemeManager.Current);
                string said = SMSModForge.Localization.Loc.F("theme.moved.body", "from", moved.Value.From.DisplayName, "to", moved.Value.To.DisplayName);
                Assert.Contains(moved.Value.From.DisplayName, said);
                Assert.Contains(moved.Value.To.DisplayName, said);
            }
            finally
            {
                System.IO.File.Delete(saved);
                ThemeManager.Apply(was);
            }
        });
    }

    [Fact]
    public void FrostIsTheDefaultAndAClassicChoiceIsMovedToExOnce()
    {
        Assert.Equal("Frost", ThemeManager.ThemeAtStart(null, movedBefore: false).Theme.Name);
        Assert.Equal("Frost", ThemeManager.ThemeAtStart("NoSuchTheme", movedBefore: true).Theme.Name);
        Assert.True(ThemeManager.Default.IsEx);

        foreach (var classic in ThemeManager.All.Where(t => !t.IsEx))
        {
            var (theme, moved) = ThemeManager.ThemeAtStart(classic.Name, movedBefore: false);
            _out.WriteLine($"{classic.Name} -> {theme.Name}");
            Assert.True(moved);
            Assert.True(theme.IsEx);
            Assert.Equal(classic.IsDark, theme.IsDark);
            // The control: once moved, a Classic theme picked again stays.
            Assert.Equal((classic, false), ThemeManager.ThemeAtStart(classic.Name, movedBefore: true));
        }
        // An EX choice is never moved.
        Assert.Equal((ThemeManager.All.First(t => t.Name == "Neon"), false), ThemeManager.ThemeAtStart("neon", movedBefore: false));

        // Nearest accent: Ember's orange lands on Blaze, Moss's lime on Venom.
        Assert.Equal("Blaze", ThemeManager.ExEquivalent(ThemeManager.All.First(t => t.Name == "Ember")).Name);
        Assert.Equal("Venom", ThemeManager.ExEquivalent(ThemeManager.All.First(t => t.Name == "Moss")).Name);
    }

    [Fact]
    public void EveryThemeReadsInTheBusiestTabs()
    {
        // The automatic half of the snapshot tool (ThemeSnapshotTests), on the
        // three tabs with the most kinds of control: every enabled word on
        // screen against what is actually behind it.
        PackRepository.Save(ThemeSnapshotTests.Fixture(), _dir);
        var failures = new List<string>();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var was = ThemeManager.Current;
            window.Width = 1600;
            window.Height = 1000;
            vm.OpenPackFromPath(_dir);
            var tabs = (TabControl)window.FindName("MainTabs");
            try
            {
                foreach (var theme in ThemeManager.All)
                {
                    ThemeManager.Apply(theme);
                    foreach (int tab in new[] { 5, 8, 3 })   // Dialogues, SFX, Places
                    {
                        tabs.SelectedIndex = tab;
                        WindowHarness.Pump();
                        if (tab == 5)
                        {
                            vm.SelectedDialogue = vm.Dialogues.First(d => d.Key == "chat");
                            WindowHarness.Pump();
                            vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
                        }
                        window.UpdateLayout();
                        WindowHarness.Pump();
                        foreach (var f in ContrastAudit.Walk((Visual)window.Content)
                                     .Where(f => f.Enabled && f.Ratio < ContrastAudit.Needed(f.Large)))
                            failures.Add($"{theme.Name} tab {tab}: '{f.Text}' {ContrastAudit.Hex(f.Foreground)} on {ContrastAudit.Hex(f.Background)} = {f.Ratio:0.00} ({f.Path})");
                    }
                }
            }
            finally { ThemeManager.Apply(was); }
        });
        foreach (var f in failures.Distinct().Take(40)) _out.WriteLine(f);
        Assert.Empty(failures);
    }

    /// <summary>
    /// The Themes menu stays open while themes are tried (the author, 1.8.0:
    /// "to reduce frustration of having to reopen it"): every theme row stays
    /// open on a click, and a click on one - here a Classic theme picked from
    /// EX, which redraws the menu in the other look - puts its theme on.
    /// <para/>
    /// That the menu is still open afterwards cannot be seen here: the test
    /// desktop has no mouse, and without one a menu does not stay open at all
    /// (measured: closed again before the click). MainWindow.ThemeRow_Click
    /// opens the menus above the row again after the theme has been put on.
    /// </summary>
    [Fact]
    public void TheThemesMenuStaysOpenWhileThemesAreTried()
    {
        WindowHarness.Run(window =>
        {
            var was = ThemeManager.Current;
            try
            {
                ThemeManager.Apply(ThemeManager.All.First(t => t.Name == "Frost"));
                WindowHarness.Pump();
                var classic = (MenuItem)window.FindName("NormalThemesMenu");
                var themes = (MenuItem)ItemsControl.ItemsControlFromItemContainer(classic);
                var options = (MenuItem)ItemsControl.ItemsControlFromItemContainer(themes);
                Open(options);
                Open(themes);
                Open(classic);
                var rows = Enumerable.Range(0, classic.Items.Count)
                                     .Select(i => classic.ItemContainerGenerator.ContainerFromIndex(i) as MenuItem)
                                     .Where(r => r?.DataContext is ThemeDef).ToList();
                Assert.NotEmpty(rows);
                Assert.All(rows, r => Assert.True(r!.StaysOpenOnClick));

                var light = rows.First(r => ((ThemeDef)r!.DataContext).Name == "Light")!;
                ((System.Windows.Automation.Provider.IInvokeProvider)
                    new System.Windows.Automation.Peers.MenuItemAutomationPeer(light)).Invoke();
                WindowHarness.Pump();
                Assert.Equal("Light", ThemeManager.Current.Name);
            }
            finally { ThemeManager.Apply(was); }
        });

        static void Open(MenuItem menu)
        {
            menu.IsSubmenuOpen = true;
            WindowHarness.Pump();
            if (menu.Items.Count == 0 || menu.ItemContainerGenerator.ContainerFromIndex(0) != null) return;
            menu.ApplyTemplate();
            if (menu.Template.FindName("PART_Popup", menu) is System.Windows.Controls.Primitives.Popup { Child: FrameworkElement content })
            {
                content.Measure(new Size(1000, 1000));
                content.Arrange(new Rect(content.DesiredSize));
                WindowHarness.Pump();
            }
        }
    }

    /// <summary>
    /// Text over a picture is measured against the picture (1.8.0): black
    /// words on a white picture in a black window read, though what is behind
    /// them in the tree is black; pale grey words on the same picture do not,
    /// and must be found - the case that keeps the measurement honest.
    /// </summary>
    [Fact]
    public void TextOverAPictureIsMeasuredAgainstThePicture()
    {
        List<ContrastAudit.Finding> found = new();
        WindowHarness.Run(_ =>
        {
            var white = new byte[4 * 4 * 4];
            Array.Fill(white, (byte)255);
            var picture = new Image { Source = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, white, 16),
                                      Stretch = Stretch.Fill, Width = 300, Height = 90,
                                      HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var words = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8) };
            words.Children.Add(new TextBlock { Text = "Black on the picture", FontSize = 16, Foreground = Brushes.Black });
            words.Children.Add(new TextBlock { Text = "Pale on the picture", FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)) });
            var grid = new Grid { Background = Brushes.Black };
            grid.Children.Add(picture);
            grid.Children.Add(words);
            var window = new Window { Width = 400, Height = 200, Left = -10000, Top = -10000, ShowInTaskbar = false,
                                      Background = Brushes.Black, Content = grid };
            window.Show();
            window.UpdateLayout();
            WindowHarness.Pump();
            try { found = ContrastAudit.Walk(grid); }
            finally { window.Close(); }
        });
        foreach (var f in found) _out.WriteLine($"'{f.Text}' {f.Kind} {ContrastAudit.Hex(f.Foreground)} on {ContrastAudit.Hex(f.Background)} = {f.Ratio:0.00}");
        var black = Assert.Single(found, f => f.Text == "Black on the picture");
        var pale = Assert.Single(found, f => f.Text == "Pale on the picture");
        Assert.EndsWith("over a picture", black.Kind);
        Assert.True(black.Ratio >= ContrastAudit.Needed(black.Large), $"black on white measured {black.Ratio:0.00}");
        Assert.True(pale.Ratio < ContrastAudit.Needed(pale.Large), $"pale on white measured {pale.Ratio:0.00}");
    }
}
