using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The EX themes (the author, 1.8.0: "a very different take on the app's UI
/// ... absolutely no functionality differences"): an EX theme puts its own
/// templates over every control and a Normal one puts App.xaml's back, live;
/// each EX template stands in for a Normal one; Windows draws the title bar to
/// match. That the window does the same things in EX is the rest of the suite's
/// job, run again in EX (run-tests.ps1 -WithEx).
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class ExThemeTests
{
    private readonly ITestOutputHelper _out;
    public ExThemeTests(ITestOutputHelper o) => _out = o;

    private static ThemeDef Named(string name) => ThemeManager.All.Single(t => t.Name == name);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    [Fact]
    public void AnExThemeDrawsTheSameControlsItsOwnWayAndNormalComesBack()
    {
        WindowHarness.Run(window =>
        {
            var before = ThemeManager.Current;
            try
            {
                ThemeManager.Apply(Named("Light"));
                WindowHarness.Pump();
                window.UpdateLayout();
                int buttons = Descendants<Button>(window).Count(b => b.IsVisible);
                Assert.DoesNotContain(Descendants<ChamferBorder>(window), c => c.IsVisible);   // the control: Normal has none

                ThemeManager.Apply(Named("Neon"));
                WindowHarness.Pump();
                window.UpdateLayout();
                Assert.True(ThemeManager.StructureIsEx);
                int cut = Descendants<ChamferBorder>(window).Count(c => c.IsVisible);
                _out.WriteLine($"{buttons} buttons; {cut} cut-corner borders in EX");
                Assert.True(cut >= buttons, "EX did not reach the buttons");
                // The same controls - and, in EX's header, Save, Validate and
                // Export (the author, 1.8.0), which are the File menu's own.
                var tabsHere = (TabControl)window.FindName("MainTabs");
                var commandBar = (Panel)tabsHere.Template.FindName("Commands", tabsHere);
                var extra = commandBar.Children.OfType<Button>().ToList();
                Assert.Equal(3, extra.Count);
                var vmHere = (MainViewModel)window.DataContext;
                Assert.Equal(new System.Windows.Input.ICommand[] { vmHere.SavePackCommand, vmHere.ValidateCommand, vmHere.ExportPackCommand },
                             extra.Select(b => b.Command));
                Assert.Equal(buttons, Descendants<Button>(window).Count(b => b.IsVisible && !extra.Contains(b)));
                Assert.Contains("Bahnschrift", window.FontFamily.Source);

                // Another arrangement, the same navigation: the window's tabs in a
                // rail down the side, each with its icon drawn, in the same order.
                var tabs = (TabControl)window.FindName("MainTabs");
                Assert.Equal(Dock.Left, tabs.TabStripPlacement);
                var headers = tabs.Items.OfType<TabItem>().ToList();
                Assert.Equal(14, headers.Count);
                double lastY = double.MinValue;
                foreach (var tab in headers)
                {
                    var origin = tab.TranslatePoint(new Point(0, 0), window);
                    Assert.True(origin.Y > lastY, $"{tab.Header} is not below the tab before it");
                    lastY = origin.Y;
                    Assert.Contains(Descendants<TextBlock>(tab), t => t.IsVisible && t.Text.Length > 0
                                                                      && t.Text == SMSModForge.View.TabIcon.GetGlyph(tab));
                }

                ThemeManager.Apply(Named("Dark"));
                WindowHarness.Pump();
                window.UpdateLayout();
                Assert.False(ThemeManager.StructureIsEx);
                Assert.DoesNotContain(Descendants<ChamferBorder>(window), c => c.IsVisible);
                Assert.Equal(Dock.Top, ((TabControl)window.FindName("MainTabs")).TabStripPlacement);
                Assert.DoesNotContain("Bahnschrift", window.FontFamily.Source);
            }
            finally { ThemeManager.Apply(before); }
        });
    }

    [Fact]
    public void EveryExTemplateStandsInForANormalOne()
    {
        WindowHarness.Run(_ =>
        {
            var ex = new ResourceDictionary { Source = new Uri("pack://application:,,,/SMSModForge;component/View/Themes/Ex.xaml", UriKind.Absolute) };
            var own = ex.Keys.OfType<string>().Where(k => k.StartsWith("Theme.", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(own);
            var before = ThemeManager.Current;
            try
            {
                ThemeManager.Apply(Named("Light"));
                foreach (string key in own)
                    Assert.True(Application.Current.Resources.Contains(key), key + " has no Normal counterpart in App.xaml");
            }
            finally { ThemeManager.Apply(before); }
            // The rest are EX's own helpers, named so nothing Normal meets them.
            Assert.All(ex.Keys.OfType<string>().Except(own), k => Assert.StartsWith("Ex.", k));
        });
    }

    [Fact]
    public void WindowsDrawsTheTitleBarToMatch()
    {
        WindowHarness.Run(window =>
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            // The control: a window that does not exist is refused, so a call
            // that does nothing cannot pass for one that worked.
            Assert.NotEqual(0, NativeWindowLook.Set(IntPtr.Zero, NativeWindowLook.BorderColour, 0));

            var before = ThemeManager.Current;
            try
            {
                var neon = Named("Neon");
                ThemeManager.Apply(neon);
                int hr = NativeWindowLook.DwmGetWindowAttribute(hwnd, NativeWindowLook.UseImmersiveDarkMode, out int dark, sizeof(int));
                Assert.Equal(0, hr);
                Assert.Equal(1, dark);

                // Windows takes the colours but will not say them back, so what
                // is checked is that it takes them - and, the control, that it
                // turns down an attribute it does not have.
                Assert.NotEqual(0, NativeWindowLook.Set(hwnd, 9999, 0));
                if (Environment.OSVersion.Version.Build < 22000)
                {
                    _out.WriteLine("before Windows 11: no caption colours, dark mode only");
                    return;
                }
                var p = ThemeManager.Palette(neon);
                Assert.Equal(0, NativeWindowLook.Set(hwnd, NativeWindowLook.BorderColour, NativeWindowLook.Ref(p[ThemeManager.KeyAccent])));
                Assert.Equal(0, NativeWindowLook.Set(hwnd, NativeWindowLook.CaptionColour, NativeWindowLook.Ref(p[ThemeManager.KeyChrome])));
                Assert.Equal(0, NativeWindowLook.Set(hwnd, NativeWindowLook.CaptionTextColour, NativeWindowLook.Ref(p[ThemeManager.KeyChromeText])));

                ThemeManager.Apply(Named("Light"));
                NativeWindowLook.DwmGetWindowAttribute(hwnd, NativeWindowLook.UseImmersiveDarkMode, out dark, sizeof(int));
                Assert.Equal(0, dark);
            }
            finally { ThemeManager.Apply(before); }
        });
    }
}
