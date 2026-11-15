using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Services;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The mask studio's painting area and tools (1.9.0; before, the mask editor
/// window's): panning with the middle button anywhere on the dark area, the
/// tools a mask needs - mirror, invert, soften, the strength under the pointer -
/// each measured on the mask's own bytes, and labels that read on every theme.
/// Hosted here in a test window of its own; MaskStudioInWindowTests opens it in
/// the main window.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class MaskEditorCanvasTests
{
    private readonly ITestOutputHelper _out;
    public MaskEditorCanvasTests(ITestOutputHelper o) => _out = o;

    private static void WithStudio(Action<MaskStudio> body, IMaskEditorHost? host = null)
    {
        WindowHarness.Run(_ =>
        {
            host ??= new OutfitViewModel(new OutfitDef { Key = "o", GameObjectName = "O" });
            var studio = new MaskStudio(host, Path.GetTempPath(), "Mask: o", "test", null);
            var window = new Window
            {
                Content = studio, Width = 1300, Height = 820,
                Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
            };
            try
            {
                window.Show();
                WindowHarness.Pump();
                window.UpdateLayout();
                body(studio);
            }
            finally
            {
                studio.AnswerUnsaved = () => MessageBoxResult.No;
                studio.TryClose();
                window.Close();
                WindowHarness.Pump();
                Mouse.OverrideCursor = null;
            }
        });
    }

    private static MouseButtonEventArgs Button(MouseButton which, RoutedEvent routed)
        => new(Mouse.PrimaryDevice, Environment.TickCount, which) { RoutedEvent = routed };

    private static MouseEventArgs Move()
        => new(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseMoveEvent };

    [Fact]
    public void HoldingTheMiddleButtonAnywhereOnTheAreaPans()
    {
        WithStudio(studio =>
        {
            var area = (Border)studio.FindName("CanvasArea");
            var image = (FrameworkElement)studio.FindName("CanvasHost");
            var pan = (TranslateTransform)studio.FindName("PanTransform");
            pan.X = pan.Y = 0;

            var at = new Point(100, 100);
            studio.PointerOnArea = _ => at;

            var down = Button(MouseButton.Middle, UIElement.MouseDownEvent);
            area.RaiseEvent(down);
            Assert.True(studio.IsPanning, "the middle button on the area around the image does not pan");
            Assert.True(down.Handled);
            Assert.Same(Cursors.SizeAll, Mouse.OverrideCursor);

            at = new Point(160, 70);
            area.RaiseEvent(Move());
            _out.WriteLine($"pan {pan.X},{pan.Y}");
            Assert.Equal(60, pan.X, 6);
            Assert.Equal(-30, pan.Y, 6);

            area.RaiseEvent(Button(MouseButton.Middle, UIElement.MouseUpEvent));
            Assert.False(studio.IsPanning);
            Assert.Null(Mouse.OverrideCursor);

            at = new Point(0, 0);
            area.RaiseEvent(Move());
            Assert.Equal(60, pan.X, 6);

            // Pressed over the image, it reaches the area the same way.
            at = new Point(10, 10);
            image.RaiseEvent(Button(MouseButton.Middle, UIElement.MouseDownEvent));
            Assert.True(studio.IsPanning, "the middle button over the image does not pan");
            at = new Point(20, 30);
            area.RaiseEvent(Move());
            Assert.Equal(70, pan.X, 6);
            Assert.Equal(-10, pan.Y, 6);
            area.RaiseEvent(Button(MouseButton.Middle, UIElement.MouseUpEvent));
            Assert.False(studio.IsPanning);

            // The other buttons do not pan.
            area.RaiseEvent(Button(MouseButton.Left, UIElement.MouseDownEvent));
            area.RaiseEvent(Button(MouseButton.Right, UIElement.MouseDownEvent));
            Assert.False(studio.IsPanning);
        });
    }

    [Fact]
    public void ClosingMidPanPutsTheCursorBack()
    {
        WithStudio(studio =>
        {
            studio.PointerOnArea = _ => new Point(0, 0);
            ((Border)studio.FindName("CanvasArea")).RaiseEvent(Button(MouseButton.Middle, UIElement.MouseDownEvent));
            Assert.Same(Cursors.SizeAll, Mouse.OverrideCursor);
            Assert.True(studio.TryClose());
            WindowHarness.Pump();
            Assert.Null(Mouse.OverrideCursor);
        });
    }

    /// <summary>Mirror paints the stroke and its twin across the middle; off, only
    /// the stroke (the control). Read off the mask's bytes.</summary>
    [Fact]
    public void MirrorPaintsBothHalves_AndOffPaintsOne()
    {
        WithStudio(studio =>
        {
            byte At(int x, int y) => studio.ActiveChannelBytes[y * MaskBuffer.Size + x];
            const int left = 40, right = MaskBuffer.Size - 1 - 40, row = 100;

            studio.BeginStrokeAt(left + 0.5, row + 0.5);
            studio.EndStroke();
            _out.WriteLine($"mirror off: left {At(left, row)}, right {At(right, row)}");
            Assert.True(At(left, row) > 0);
            Assert.Equal(0, At(right, row));

            studio.SetMirror(true);
            studio.BeginStrokeAt(left + 0.5, row + 60.5);
            studio.StrokeTo(left + 10.5, row + 60.5);
            studio.EndStroke();
            _out.WriteLine($"mirror on: left {At(left, row + 60)}, right {At(right, row + 60)}");
            Assert.True(At(left, row + 60) > 0);
            Assert.Equal(At(left, row + 60), At(right, row + 60));
            Assert.Equal(At(left + 10, row + 60), At(right - 10, row + 60));
            Assert.True(studio.IsDirty);
        });
    }

    /// <summary>Invert swaps strong and faint; Soften spreads a point over its 3x3
    /// and keeps its total; both undo in one step.</summary>
    [Fact]
    public void InvertAndSoften_ChangeTheLayerAsSaid_AndUndo()
    {
        WithStudio(studio =>
        {
            var ch = studio.ActiveChannelBytes;
            Array.Clear(ch);
            int mid = 100 * MaskBuffer.Size + 100;
            ch[mid] = 255;

            studio.SoftenActiveChannel();
            ch = studio.ActiveChannelBytes;
            _out.WriteLine($"softened: centre {ch[mid]}, beside {ch[mid + 1]}, two away {ch[mid + 2]}");
            Assert.Equal(28, ch[mid]);              // 255 / 9, rounded
            Assert.Equal(28, ch[mid + 1]);
            Assert.Equal(28, ch[mid + MaskBuffer.Size + 1]);
            Assert.Equal(0, ch[mid + 2]);           // the control: two away, untouched

            studio.InvertActiveChannel();
            ch = studio.ActiveChannelBytes;
            Assert.Equal(255 - 28, ch[mid]);
            Assert.Equal(255, ch[mid + 2]);

            ApplicationCommands.Undo.Execute(null, studio);
            Assert.Equal(28, studio.ActiveChannelBytes[mid]);
            ApplicationCommands.Undo.Execute(null, studio);
            Assert.Equal(255, studio.ActiveChannelBytes[mid]);
            Assert.Equal(0, studio.ActiveChannelBytes[mid + 1]);
        });
    }

    /// <summary>
    /// The soften brush (the author, 1.9.0): across a hard edge it leaves what it
    /// fully covers at the 5x5 average, what it does not reach untouched, and
    /// undoes in one step. The control is the brush along the same stroke, which
    /// paints the edge's dark side full instead.
    /// </summary>
    [Fact]
    public void TheSoftenBrushSmoothsWhereItPasses_AndNowhereElse()
    {
        WithStudio(studio =>
        {
            int size = MaskBuffer.Size;
            var ch = studio.ActiveChannelBytes;
            // Strong on the left of column 100, nothing on its right.
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    ch[y * size + x] = (byte)(x < 100 ? 255 : 0);
            var before = (byte[])ch.Clone();
            var blurred = BrushEngine.Blurred(before, size, BrushEngine.SoftenRadius);

            ((Slider)studio.FindName("SizeSlider")).Value = 8;
            ((Slider)studio.FindName("HardnessSlider")).Value = 1;
            ((Slider)studio.FindName("OpacitySlider")).Value = 1;
            studio.SetTool(MaskStudio.Tool.Soften);
            Assert.True(((System.Windows.Controls.Primitives.ToggleButton)studio.FindName("ToolSoftenBtn")).IsChecked);
            Assert.False(((System.Windows.Controls.Primitives.ToggleButton)studio.FindName("ToolBrushBtn")).IsChecked);

            studio.BeginStrokeAt(100, 60.5);
            studio.StrokeTo(100, 140.5);
            studio.EndStroke();
            ch = studio.ActiveChannelBytes;
            byte At(int x, int y) => ch[y * size + x];
            _out.WriteLine($"across the edge at row 100: {At(97, 100)} {At(98, 100)} {At(99, 100)} | {At(100, 100)} {At(101, 100)} {At(102, 100)}");

            // Fully covered: exactly the average of the 5x5 around.
            for (int x = 96; x <= 103; x++)
                Assert.Equal(blurred[100 * size + x], At(x, 100));
            Assert.Equal(153, At(99, 100));        // 3 of 5 columns strong: 255 * 15 / 25
            Assert.Equal(102, At(100, 100));       // 2 of 5
            // Out of reach - above the stroke, and far along the row - untouched.
            Assert.Equal(255, At(99, 20));
            Assert.Equal(0, At(100, 20));
            Assert.Equal(before[100 * size + 50], At(50, 100));
            Assert.True(studio.IsDirty);

            // One Ctrl+Z puts the edge back.
            ApplicationCommands.Undo.Execute(null, studio);
            Assert.Equal(255, studio.ActiveChannelBytes[100 * size + 99]);
            Assert.Equal(0, studio.ActiveChannelBytes[100 * size + 100]);

            // The control: the brush along the same stroke paints, it does not soften.
            studio.SetTool(MaskStudio.Tool.Brush);
            studio.BeginStrokeAt(100, 60.5);
            studio.StrokeTo(100, 140.5);
            studio.EndStroke();
            Assert.Equal(255, studio.ActiveChannelBytes[100 * size + 100]);
        });
    }

    /// <summary>Alt-click takes the strength under the pointer as the opacity.</summary>
    [Fact]
    public void TheStrengthUnderThePointerBecomesTheOpacity()
    {
        WithStudio(studio =>
        {
            var ch = studio.ActiveChannelBytes;
            ch[50 * MaskBuffer.Size + 60] = 128;
            var opacity = (Slider)studio.FindName("OpacitySlider");
            opacity.Value = 0.9;
            studio.SampleAt(60.4, 50.4);
            _out.WriteLine($"opacity {opacity.Value}");
            Assert.Equal(128 / 255.0, opacity.Value, 6);
            // Nothing there: the slider's least.
            studio.SampleAt(10, 10);
            Assert.Equal(opacity.Minimum, opacity.Value, 6);
        });
    }

    /// <summary>The view fits the room it has, and the buttons zoom about the middle.</summary>
    [Fact]
    public void TheWholeMaskFitsTheRoom_AndTheButtonsZoom()
    {
        WithStudio(studio =>
        {
            var area = (Border)studio.FindName("CanvasArea");
            var host = (FrameworkElement)studio.FindName("CanvasHost");
            studio.FitView();
            double shown = host.Width * studio.Zoom;
            _out.WriteLine($"area {area.ActualWidth}x{area.ActualHeight}, canvas {host.Width} at {studio.Zoom:0.###} = {shown:0}");
            Assert.True(shown <= Math.Min(area.ActualWidth, area.ActualHeight), "the fitted mask is bigger than its room");
            Assert.True(shown >= Math.Min(area.ActualWidth, area.ActualHeight) - 30, "the fitted mask leaves room unused");

            double fitted = studio.Zoom;
            studio.ZoomBy(1.25);
            Assert.Equal(fitted * 1.25, studio.Zoom, 6);
            studio.ZoomBy(1 / 1.25);
            Assert.Equal(fitted, studio.Zoom, 6);
        });
    }

    [Fact]
    public void TheCopyLayersLabelReadsOnEveryTheme()
    {
        // The label takes the theme's colours rather than a fixed grey: measured
        // as drawn, its own colour against the colour of the panel it sits on, in
        // the lightest Normal theme, the darkest, and an EX one.
        WindowHarness.Run(_ =>
        {
            var was = ThemeManager.Current;
            try
            {
                foreach (var theme in ThemeManager.All.Where(t => t.Name is "Light" or "Carbon" or "Neon"))
                {
                    ThemeManager.Apply(theme);
                    WindowHarness.Pump();

                    var outfit = new OutfitViewModel(new OutfitDef { Key = "o", GameObjectName = "O" });
                    var studio = new MaskStudio(outfit, Path.GetTempPath(), "t", "s", null);
                    var window = new Window
                    {
                        Content = studio, Width = 1300, Height = 820,
                        Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
                    };
                    try
                    {
                        window.Show();
                        ((FrameworkElement)studio.FindName("CopyFromPanel")).Visibility = Visibility.Visible;
                        WindowHarness.Pump();
                        window.UpdateLayout();

                        foreach (string name in new[] { "CopyFromLabel", "TitleText", "SubtitleText", "StatusText" })
                        {
                            var label = (TextBlock)studio.FindName(name);
                            var ink = ((SolidColorBrush)label.Foreground).Color;
                            var panel = BackgroundBehind(label);
                            double ratio = Contrast(ink, panel);
                            _out.WriteLine($"{theme.Name,-10} {name,-14} {ink} on {panel}: {ratio:0.00}");
                            Assert.True(ratio >= 4.5, $"{name} is {ratio:0.00}:1 on {theme.Name}");
                        }
                    }
                    finally
                    {
                        studio.TryClose();
                        window.Close();
                        WindowHarness.Pump();
                    }
                }
            }
            finally
            {
                ThemeManager.Apply(was);
                WindowHarness.Pump();
            }
        });
    }

    /// <summary>The first solid background above an element.</summary>
    private static Color BackgroundBehind(DependencyObject from)
    {
        for (var at = VisualTreeHelper.GetParent(from); at != null; at = VisualTreeHelper.GetParent(at))
        {
            Brush? brush = at switch
            {
                Border b => b.Background,
                Panel p => p.Background,
                Control c => c.Background,
                _ => null,
            };
            if (brush is SolidColorBrush solid && solid.Color.A == 255) return solid.Color;
        }
        throw new InvalidOperationException("nothing solid behind the label");
    }

    /// <summary>WCAG contrast ratio.</summary>
    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte v)
        {
            double c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
