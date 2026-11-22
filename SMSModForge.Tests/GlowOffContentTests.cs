using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A glow is never laid on words, a control's content or a picture (the author, 2026-11-21: "Hovering over the
/// tutorial entries makes the art and text blurry"). An effect renders everything inside the element it is on
/// through a pass of its own, so EX drew a hovered card's art and text soft, and every hovered button's words. The
/// glow comes from a copy of the shape behind it instead. Read from the templates themselves - a hover cannot be
/// made on the test desktop, and a rule held by reading the source holds for every template, the next one too.
/// </summary>
public sealed class GlowOffContentTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>What an effect must not cover: a control's content, a picture, or words (an icon font's glyph is a
    /// mark, and may glow).</summary>
    private static string? Covers(XElement e)
    {
        foreach (var d in new[] { e }.Concat(e.Descendants()))
        {
            string name = d.Name.LocalName;
            if (name is "ContentPresenter" or "Image" or "ImageBrush" or "ScrollContentPresenter" or "ItemsPresenter")
                return name;
            if (name == "TextBlock" && !((string?)d.Attribute("FontFamily") ?? "").Contains("MDL2", StringComparison.Ordinal))
                return "TextBlock" + ((string?)d.Attribute("Text") is { } t ? " \"" + t + "\"" : "");
        }
        return null;
    }

    private static List<string> Offenders(string xaml)
    {
        var doc = XDocument.Parse(xaml);
        var found = new List<string>();
        foreach (var template in doc.Descendants().Where(d => d.Name.LocalName == "ControlTemplate"))
        {
            string key = (string?)template.Attribute(X + "Key") ?? (string?)template.Parent?.Parent?.Parent?.Attribute(X + "Key") ?? "(a template)";
            var named = template.Descendants().Where(d => d.Attribute(X + "Name") != null)
                                .GroupBy(d => (string)d.Attribute(X + "Name")!).ToDictionary(g => g.Key, g => g.First());
            // A glow set by a trigger on a named part...
            foreach (var setter in template.Descendants().Where(d => d.Name.LocalName == "Setter"
                                                                     && (string?)d.Attribute("Property") == "Effect"
                                                                     && d.Attribute("TargetName") != null))
            {
                string target = (string)setter.Attribute("TargetName")!;
                if (named.TryGetValue(target, out var part) && Covers(part) is { } what)
                    found.Add($"{key}: the glow on \"{target}\" covers a {what}");
            }
            // ... or written on an element itself.
            foreach (var element in template.Descendants().Where(d => d.Attribute("Effect") != null))
                if (Covers(element) is { } what)
                    found.Add($"{key}: the effect on a {element.Name.LocalName} covers a {what}");
        }
        return found;
    }

    [Fact]
    public void NoLookLaysAGlowOnWordsContentOrAPicture()
    {
        string dir = TranslationCoverageTests.EditorDir;
        var offenders = Offenders(File.ReadAllText(Path.Combine(dir, "View", "Themes", "Ex.xaml")))
                        .Concat(Offenders(File.ReadAllText(Path.Combine(dir, "App.xaml")))).ToList();
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    /// <summary>
    /// And the glow is still there, round the shape: the copy it comes from clips itself to its cut outline, and
    /// on the copy itself the clip cut the glow away (the first try, 2026-11-21). Measured beside the Home page's
    /// Continue button, a primary button that always glows: just outside it the colour leans to the accent, further
    /// out it does not. Control: Frost, an EX look with no glow, shows no lean.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void APrimaryButtonStillGlowsRoundItsShape()
    {
        double Lean(string theme)
        {
            double lean = 0;
            WindowHarness.Run(window =>
            {
                var was = SMSModForge.Services.ThemeManager.Current;
                try
                {
                    SMSModForge.Services.ThemeManager.Apply(SMSModForge.Services.ThemeManager.All.First(t => t.Name == theme));
                    window.Width = 1400; window.Height = 900;
                    var vm = (SMSModForge.ViewModel.MainViewModel)window.DataContext;
                    ((System.Windows.Controls.TabControl)window.FindName("MainTabs")).SelectedIndex = SMSModForge.ViewModel.MainTab.ModForge;
                    vm.HomePage = SMSModForge.ViewModel.HomePage.Home;
                    WindowHarness.Pump(); window.UpdateLayout(); WindowHarness.Pump();
                    var root = (System.Windows.FrameworkElement)System.Windows.Media.VisualTreeHelper.GetChild(window, 0);
                    var button = Descend(root).OfType<System.Windows.Controls.Button>()
                                 .First(b => b.IsVisible && ReferenceEquals(b.Command, vm.ContinueLearningCommand));
                    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96,
                                                                                  System.Windows.Media.PixelFormats.Pbgra32);
                    bmp.Render(root);
                    int w = bmp.PixelWidth;
                    var px = new byte[w * bmp.PixelHeight * 4];
                    bmp.CopyPixels(px, w * 4, 0);
                    var box = button.TransformToAncestor(root).TransformBounds(new System.Windows.Rect(button.RenderSize));
                    var accent = ((System.Windows.Media.SolidColorBrush)window.FindResource("Theme.Accent")).Color;
                    // Below the button, along its middle third: 2 px out, and 24 px out.
                    double Distance(int y)
                    {
                        double sum = 0; int n = 0;
                        for (int x = (int)(box.Left + box.Width / 3); x < (int)(box.Right - box.Width / 3); x++, n++)
                        {
                            int i = (y * w + x) * 4;
                            sum += Math.Abs(px[i + 2] - accent.R) + Math.Abs(px[i + 1] - accent.G) + Math.Abs(px[i] - accent.B);
                        }
                        return sum / n;
                    }
                    lean = Distance((int)box.Bottom + 24) - Distance((int)box.Bottom + 2);
                }
                finally { SMSModForge.Services.ThemeManager.Apply(was); }
            });
            return lean;
        }
        double neon = Lean("Neon"), frost = Lean("Frost");
        Assert.True(neon > 8, $"no glow round the button in Neon (it leans {neon:0.0} to the accent)");
        Assert.True(frost < 4, $"control: Frost has no glow, yet the edge leans {frost:0.0} to the accent");
    }

    private static IEnumerable<System.Windows.DependencyObject> Descend(System.Windows.DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descend(child)) yield return d;
        }
    }

    [Fact]
    public void TheRuleSeesAGlowOnContent()
    {
        // The control: the shape the templates had until 2026-11-21 must be caught.
        const string before = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ControlTemplate x:Key="Card" TargetType="ContentControl">
                <Grid x:Name="frame"><Border><ContentPresenter/></Border></Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="frame" Property="Effect" Value="{x:Null}"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
              <ControlTemplate x:Key="Glyph" TargetType="Button">
                <TextBlock x:Name="arrow" FontFamily="Segoe MDL2 Assets" Text="x" Effect="{x:Null}"/>
              </ControlTemplate>
            </ResourceDictionary>
            """;
        var found = Offenders(before);
        Assert.Single(found);
        Assert.Contains("Card", found[0]);
    }
}
