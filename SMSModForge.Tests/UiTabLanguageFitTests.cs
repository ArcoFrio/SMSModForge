using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Every word on the UI tab reaches the screen in every language. A label given
/// a width that suits its English is cut in German, and nothing else notices:
/// the text is all there, and a TextBlock's ActualWidth goes on reporting the
/// width of its text while layout clips it (1.8.3: "Gezeichnet a"). So this reads
/// the clip that is drawn with, from the label up through everything it is in.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UiTabLanguageFitTests
{
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private static bool InsideACombo(DependencyObject element)
    {
        for (var up = element; up != null; up = VisualTreeHelper.GetParent(up))
            if (up is ComboBox) return true;
        return false;
    }

    /// <summary>Words that do not all reach the screen. A text that trims says so
    /// with its own ellipsis and is not one of them.</summary>
    private static IEnumerable<string> CutWords(FrameworkElement root)
    {
        foreach (var label in Descendants<TextBlock>(root).ToList())
        {
            if (!label.IsVisible || string.IsNullOrWhiteSpace(label.Text) || InsideACombo(label)) continue;
            if (label.TextWrapping != TextWrapping.NoWrap || label.TextTrimming != TextTrimming.None) continue;

            var words = new FormattedText(label.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
                label.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(label).PixelsPerDip);
            double wide = words.WidthIncludingTrailingWhitespace + label.Padding.Left + label.Padding.Right;

            for (DependencyObject? up = label; up != null; up = ReferenceEquals(up, root) ? null : VisualTreeHelper.GetParent(up))
            {
                if (up is not Visual visual || VisualTreeHelper.GetClip(visual) is not { } clip) continue;
                var there = label.TransformToVisual(visual).TransformBounds(new Rect(0, 0, wide, 1));
                if (there.Right > clip.Bounds.Right + 0.5)
                {
                    yield return $"'{label.Text}' needs {wide:0}, drawn {wide - (there.Right - clip.Bounds.Right):0}";
                    break;
                }
            }
        }
    }

    private static IEnumerable<UiNodeViewModel> Every(IEnumerable<UiNodeViewModel> nodes)
        => nodes.SelectMany(node => new[] { node }.Concat(Every(node.Children)));

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    [InlineData("zh-Hans")]
    public void Nothing_on_the_UI_tab_is_cut_off(string language)
    {
        // A window reads its words as it is built, so the language comes first.
        Loc.Use(language);
        try
        {
            WindowHarness.Run(window =>
            {
                var cut = new HashSet<string>();
                var vm = (MainViewModel)window.DataContext;
                var tabs = (TabControl)window.FindName("MainTabs");
                tabs.SelectedIndex = MainTab.Ui;
                WindowHarness.Pump();

                vm.SelectedUi = null;
                WindowHarness.Pump();
                window.UpdateLayout();
                cut.UnionWith(CutWords((FrameworkElement)window.FindName("UiStartPage")));

                vm.StartWindowUiCommand.Execute(null);
                WindowHarness.Pump();
                var inspector = (FrameworkElement)window.FindName("UiInspector");
                var preview = (FrameworkElement)window.FindName("UiPreviewColumn");

                void Read()
                {
                    // Sections hold sections, and a closed one has no content to read.
                    for (int pass = 0; pass < 3; pass++)
                    {
                        foreach (var section in Descendants<Expander>(inspector).ToList()) section.IsExpanded = true;
                        WindowHarness.Pump();
                        window.UpdateLayout();
                    }
                    cut.UnionWith(CutWords(inspector));
                    cut.UnionWith(CutWords(preview));
                }

                var ui = vm.SelectedUi!;
                ui.ShowSettingsCommand.Execute(null);
                WindowHarness.Pump();
                Read();

                foreach (var node in Every(ui.Nodes).ToList())
                {
                    // Rows that only appear once they are switched on.
                    node.PulsesFade = true;
                    node.Sweeps = true;
                    ui.SelectedNode = node;
                    WindowHarness.Pump();
                    Read();
                }

                Assert.True(cut.Count == 0, language + ":\n" + string.Join("\n", cut.OrderBy(s => s)));

                // The control: a label given too little room must be caught, or
                // the empty list above says nothing.
                var fields = (StackPanel)window.FindName("UiNodeFields");
                var tooNarrow = new TextBlock { Text = "A label far too long for its width", Width = 20 };
                fields.Children.Add(tooNarrow);
                WindowHarness.Pump();
                window.UpdateLayout();
                Assert.Contains(CutWords(inspector), found => found.StartsWith("'" + tooNarrow.Text + "'"));
                fields.Children.Remove(tooNarrow);
            });
        }
        finally { Loc.Use(Loc.EnglishCode); }
    }
}
