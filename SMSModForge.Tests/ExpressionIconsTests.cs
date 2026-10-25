using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The expression icons (the author, 1.8.0: "very basic ones that cover all
/// human facial expressions"): every one drawn, no two alike, and a face's
/// name picking the one it means.
/// </summary>
public class ExpressionIconsTests
{
    private readonly ITestOutputHelper _out;
    public ExpressionIconsTests(ITestOutputHelper o) => _out = o;

    private static byte[] Render(string icon, int side = 48)
    {
        byte[] pixels = Array.Empty<byte>();
        var thread = new Thread(() =>
        {
            TestDesktop.Enter();
            var view = new ExpressionIconView { Icon = icon, Foreground = Brushes.Black, Width = side, Height = side };
            view.Measure(new Size(side, side));
            view.Arrange(new Rect(0, 0, side, side));
            var bitmap = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            pixels = new byte[side * side * 4];
            bitmap.CopyPixels(pixels, side * 4, 0);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return pixels;
    }

    [Fact]
    public void EveryIconIsDrawnAndNoTwoAreAlike()
    {
        var drawn = ExpressionIcons.All.ToDictionary(i => i, i => Render(i));
        foreach (var (icon, px) in drawn)
        {
            int ink = 0;
            for (int i = 3; i < px.Length; i += 4) if (px[i] > 64) ink++;
            Assert.True(ink > 200, $"{icon} draws almost nothing ({ink} pixels)");
        }
        var same = new List<string>();
        var icons = ExpressionIcons.All.ToList();
        for (int a = 0; a < icons.Count; a++)
            for (int b = a + 1; b < icons.Count; b++)
            {
                int differ = 0;
                var pa = drawn[icons[a]]; var pb = drawn[icons[b]];
                for (int i = 3; i < pa.Length; i += 4) if (Math.Abs(pa[i] - pb[i]) > 64) differ++;
                if (differ < 20) same.Add($"{icons[a]} / {icons[b]} ({differ})");
            }
        Assert.True(same.Count == 0, "these look the same: " + string.Join(", ", same));

        // A sheet to look at, when asked for one.
        string? dir = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOTS");
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        int cols = icons.Count, side = 48;
        var sheet = new WriteableBitmap(cols * side, side, 96, 96, PixelFormats.Pbgra32, null);
        for (int i = 0; i < cols; i++)
            sheet.WritePixels(new Int32Rect(i * side, 0, side, side), drawn[icons[i]], side * 4, 0);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sheet));
        using var file = File.Create(Path.Combine(dir, "expression-icons.png"));
        encoder.Save(file);
        _out.WriteLine(string.Join(" ", icons));
    }

    [Theory]
    [InlineData("Happy", "happy")]
    [InlineData("Angry", "angry")]
    [InlineData("Sad", "sad")]
    [InlineData("Flirty", "flirty")]
    [InlineData("neutral", "neutral")]
    [InlineData("talk", "talk")]
    [InlineData("BigLaugh", "laugh")]
    [InlineData("crying_hard", "cry")]
    [InlineData("Blushing", "embarrassed")]
    [InlineData("Smirk", "smug")]
    [InlineData("so_tired", "sleepy")]
    [InlineData("Expression7", "neutral")]
    [InlineData("NewFace", "neutral")]   // no stray match inside a word
    public void AFacesNamePicksTheIconItMeans(string name, string icon)
        => Assert.Equal(icon, ExpressionIcons.Guess(name));

    [Fact]
    public void EveryMeaningLeadsToAnIconThatExists()
    {
        foreach (var name in ExpressionIcons.SuggestedNames)
            Assert.True(ExpressionIcons.IsKnown(ExpressionIcons.Guess(name)), name);
        // ...and each suggested name means its own icon.
        Assert.All(ExpressionIcons.SuggestedNames, n => Assert.Equal(n, ExpressionIcons.Guess(n)));
    }
}
