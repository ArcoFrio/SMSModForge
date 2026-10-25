using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.Rendering;
using SMSModForge.Services;
using SMSModForge.View;
using SMSModForge.View.Controls;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The game's colours in a plain node row (the author, 1.8.0: in EX dark,
/// "are color formattings (character name color, parentheses, etc) applying
/// correctly ... we could have some automatic conversion"). The row shows them
/// - words in brackets in the game's blue, what a &lt;color&gt; asks for, the
/// speaker's name colour - each moved in lightness until it reads on what is
/// behind it, its hue kept. Measured off the drawn runs against the fill under
/// them, on a near-black and on a white background.
/// </summary>
[Trait("Speed", "Slow")]   // builds windows on the harness thread
public sealed class ReadableRowColoursTests
{
    private readonly ITestOutputHelper _out;
    public ReadableRowColoursTests(ITestOutputHelper o) => _out = o;

    private const string Line = "Plain (softly) <color=#000080>navy</color> <color=#FFFF66>pale</color>";

    private static Color Of(Brush brush) => ((SolidColorBrush)brush).Color;

    private ((string Text, Color Ink)[] Runs, Color Name) Drawn(Color behind)
    {
        (string Text, Color Ink)[] runs = Array.Empty<(string, Color)>();
        Color name = default;
        WindowHarness.Run(_ =>
        {
            var box = new MarkupTextBox { HidesTags = true, ReadableColours = true, Text = Line, Background = Brushes.Transparent,
                                          Foreground = new SolidColorBrush(ThemeManager.Contrast(behind, Colors.White) > 4.5 ? Colors.White : Colors.Black) };
            var speaker = new TextBlock { Text = "Kiki: " };
            ReadableInk.SetHex(speaker, "#202020");
            var row = new DockPanel { Background = new SolidColorBrush(behind) };
            DockPanel.SetDock(speaker, Dock.Left);
            row.Children.Add(speaker);
            row.Children.Add(box);
            var window = new Window { Width = 600, Height = 120, Left = -10000, Top = -10000, ShowInTaskbar = false, Content = row };
            window.Show();
            window.UpdateLayout();
            WindowHarness.Pump();
            WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
            runs = box.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>())
                      .Select(r => (r.Text, Of(r.Foreground))).ToArray();
            name = Of(speaker.Foreground);
            window.Close();
        });
        foreach (var r in runs) _out.WriteLine($"'{r.Text}' {r.Ink} = {ThemeManager.Contrast(r.Ink, behind):0.00}");
        _out.WriteLine($"name {name} = {ThemeManager.Contrast(name, behind):0.00}");
        return (runs, name);
    }

    [Theory]
    [InlineData("#121212")]
    [InlineData("#FFFFFF")]
    public void TheGamesColoursReadOnTheRow(string hex)
    {
        var behind = (Color)ColorConverter.ConvertFromString(hex)!;
        var (runs, name) = Drawn(behind);
        Color Piece(string words) => runs.Single(r => r.Text.Contains(words)).Ink;

        foreach (var words in new[] { "(softly)", "navy", "pale" })
            Assert.True(ThemeManager.Contrast(Piece(words), behind) >= 4.5, $"'{words}' does not read");
        Assert.True(ThemeManager.Contrast(name, behind) >= 4.5, "the name does not read");

        // Still the colour it was: the brackets blue, the navy still blue.
        var bracket = Piece("(softly)");
        Assert.True(bracket.B > bracket.R, $"the brackets are no longer blue: {bracket}");
        var navy = Piece("navy");
        Assert.True(navy.B > navy.R && navy.B > navy.G, $"navy is no longer blue: {navy}");
    }

    /// <summary>The control: a colour that already reads is left as it is.</summary>
    [Fact]
    public void AColourThatReadsIsLeftAlone()
    {
        var (runs, _) = Drawn(Color.FromRgb(0x12, 0x12, 0x12));
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0x66), runs.Single(r => r.Text.Contains("pale")).Ink);
        Assert.Equal((Color)ColorConverter.ConvertFromString(DialogueLook.BracketHex)!, runs.Single(r => r.Text.Contains("(softly)")).Ink);
    }
}
