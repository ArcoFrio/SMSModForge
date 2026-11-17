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

    // ───────────────────────── the soften brush (1.9.0; capped 1.9.1)

    /// <summary>Strong on the left of column 100, nothing on its right.</summary>
    private static void HardEdge(MaskStudio studio)
    {
        int size = MaskBuffer.Size;
        var ch = studio.ActiveChannelBytes;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                ch[y * size + x] = (byte)(x < 100 ? 255 : 0);
    }

    /// <summary>How many pixels of a row are part way, between 2% and 98%:
    /// the width of its fade, as the canvas shows it.</summary>
    private static int FadeWidth(MaskStudio studio, int row)
    {
        var ch = studio.ActiveChannelBytes;
        int n = 0;
        for (int x = 0; x < MaskBuffer.Size; x++)
        {
            byte v = ch[row * MaskBuffer.Size + x];
            if (v > 0.02 * 255 && v < 0.98 * 255) n++;
        }
        return n;
    }

    private string Row(MaskStudio studio, int row)
        => string.Join(" ", Enumerable.Range(88, 24).Select(x => studio.ActiveChannelBytes[row * MaskBuffer.Size + x]));

    private static void SoftenDownTheEdge(MaskStudio studio, double fromY = 60.5, double toY = 140.5)
    {
        studio.BeginStrokeAt(100, fromY);
        studio.StrokeTo(100, toY);
        studio.EndStroke();
    }

    /// <summary>
    /// Softened again, and no further: where the brush covers fully (rows 62 to
    /// 138 of the stroke down the edge) not one pixel differs from
    /// <paramref name="once"/>, and nowhere is softer than that fully covered
    /// fade - each pixel lies between the hard edge and row 100 at its column.
    /// The pixels at the round ends of the stroke, which the brush covers only
    /// in part, may still move towards that fade, never past it.
    /// </summary>
    private static void AssertSoftenedNoFurther(MaskStudio studio, byte[] once)
    {
        int size = MaskBuffer.Size;
        var now = studio.ActiveChannelBytes;
        for (int y = 62; y <= 138; y++)
            for (int x = 0; x < size; x++)
                Assert.True(once[y * size + x] == now[y * size + x],
                    $"({x},{y}) was {once[y * size + x]} after the first pass, is {now[y * size + x]}");
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hard = x < 100 ? 255 : 0, fade = once[100 * size + x], v = now[y * size + x];
                Assert.True(v >= Math.Min(hard, fade) && v <= Math.Max(hard, fade),
                    $"({x},{y}) is {v}, past the fade's {fade} from {hard}");
            }
    }

    private static void SetUpSoftening(MaskStudio studio, double softness)
    {
        ((Slider)studio.FindName("SizeSlider")).Value = 12;
        ((Slider)studio.FindName("HardnessSlider")).Value = 1;
        ((Slider)studio.FindName("SoftnessSlider")).Value = softness;
        studio.SetTool(MaskStudio.Tool.Soften);
    }

    /// <summary>
    /// The soften brush (the author, 1.9.1: "making the edges of the painted
    /// areas look smoother, to a max edge softness allowed in the slider").
    /// Across a hard edge it leaves a fade as wide as Edge softness; going over
    /// it again softens it no further; what it does not reach is untouched.
    /// The controls: a wider Edge softness on the same stroke DOES change it (so
    /// the second stroke ran), a narrower one tightens it back, and the brush on
    /// the same stroke paints instead.
    /// </summary>
    [Fact]
    public void TheSoftenBrushSoftensAnEdgeToTheSoftnessSet_AndNeverPast()
    {
        WithStudio(studio =>
        {
            int size = MaskBuffer.Size;
            HardEdge(studio);
            Assert.Equal(0, FadeWidth(studio, 100));
            SetUpSoftening(studio, 8);
            Assert.True(((System.Windows.Controls.Primitives.ToggleButton)studio.FindName("ToolSoftenBtn")).IsChecked);
            Assert.False(((System.Windows.Controls.Primitives.ToggleButton)studio.FindName("ToolBrushBtn")).IsChecked);

            SoftenDownTheEdge(studio);
            _out.WriteLine("softness 8:  " + Row(studio, 100));
            Assert.Equal(8, FadeWidth(studio, 100));
            var once = (byte[])studio.ActiveChannelBytes.Clone();
            // Out of reach - above the stroke, and far along the row - untouched.
            byte At(int x, int y) => studio.ActiveChannelBytes[y * size + x];
            Assert.Equal(255, At(99, 20));
            Assert.Equal(0, At(100, 20));
            Assert.Equal(255, At(50, 100));
            Assert.True(studio.IsDirty);

            // Again, and again, either way: no further.
            SoftenDownTheEdge(studio);
            SoftenDownTheEdge(studio, 140.5, 60.5);
            AssertSoftenedNoFurther(studio, once);
            Assert.Equal(8, FadeWidth(studio, 100));
            var settled = (byte[])studio.ActiveChannelBytes.Clone();

            // The control: more softness on the same stroke softens it further...
            ((Slider)studio.FindName("SoftnessSlider")).Value = 16;
            SoftenDownTheEdge(studio);
            _out.WriteLine("softness 16: " + Row(studio, 100));
            Assert.Equal(16, FadeWidth(studio, 100));
            // ...and less tightens it back, to that softness from the hard edge.
            ((Slider)studio.FindName("SoftnessSlider")).Value = 4;
            SoftenDownTheEdge(studio);
            _out.WriteLine("softness 4:  " + Row(studio, 100));
            Assert.Equal(4, FadeWidth(studio, 100));

            // Ctrl+Z goes back a stroke at a time, to the hard edge.
            ApplicationCommands.Undo.Execute(null, studio);
            Assert.Equal(16, FadeWidth(studio, 100));
            ApplicationCommands.Undo.Execute(null, studio);
            Assert.Equal(settled, studio.ActiveChannelBytes);
            for (int i = 0; i < 3; i++) ApplicationCommands.Undo.Execute(null, studio);
            Assert.Equal(255, At(99, 100));
            Assert.Equal(0, At(100, 100));

            // The control: the brush along the same stroke paints, it does not soften.
            studio.SetTool(MaskStudio.Tool.Brush);
            ((Slider)studio.FindName("OpacitySlider")).Value = 1;
            SoftenDownTheEdge(studio);
            Assert.Equal(255, At(100, 100));
        });
    }

    /// <summary>
    /// The bug of 1.9.0, through the canvas's own mouse handlers: picked with its
    /// button and dragged down the edge with the left button held, the soften
    /// brush softens it. In 1.9.0 the mouse began strokes by a copy of its own
    /// that never set the soften brush up, and painted with the brush; the
    /// tests began them the other way, and passed. The control is the brush by
    /// the same handlers, which paints.
    /// </summary>
    [Fact]
    public void TheSoftenBrushSoftensWhenDraggedWithTheMouse()
    {
        WithStudio(studio =>
        {
            int size = MaskBuffer.Size;
            var canvas = (FrameworkElement)studio.FindName("CanvasHost");
            double scale = (canvas.ActualWidth > 0 ? canvas.ActualWidth : canvas.Width) / size;
            _out.WriteLine($"canvas {canvas.ActualWidth} wide: {scale} per mask pixel");
            var at = new Point(100 * scale, 60.5 * scale);
            bool held = false;
            studio.PointerOnCanvas = _ => at;
            studio.LeftHeld = _ => held;

            void Drag()
            {
                at = new Point(100 * scale, 60.5 * scale);
                held = true;
                canvas.RaiseEvent(Button(MouseButton.Left, UIElement.MouseDownEvent));
                at = new Point(100 * scale, 100.5 * scale);
                canvas.RaiseEvent(Move());
                at = new Point(100 * scale, 140.5 * scale);
                canvas.RaiseEvent(Move());
                held = false;
                canvas.RaiseEvent(Button(MouseButton.Left, UIElement.MouseUpEvent));
            }

            HardEdge(studio);
            ((Slider)studio.FindName("SizeSlider")).Value = 12;
            ((Slider)studio.FindName("HardnessSlider")).Value = 1;
            ((Slider)studio.FindName("SoftnessSlider")).Value = 8;
            var softenButton = (System.Windows.Controls.Primitives.ToggleButton)studio.FindName("ToolSoftenBtn");
            softenButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(Visibility.Visible, ((FrameworkElement)studio.FindName("SoftnessRow")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)studio.FindName("OpacityRow")).Visibility);

            Drag();
            _out.WriteLine("soften by mouse: " + Row(studio, 100));
            Assert.Equal(8, FadeWidth(studio, 100));
            Assert.True(studio.ActiveChannelBytes[100 * size + 99] < 200, "the strong side of the edge was not softened");
            Assert.True(studio.IsDirty);

            // The control: the brush by the same handlers paints the edge's dark side.
            ApplicationCommands.Undo.Execute(null, studio);
            var brushButton = (System.Windows.Controls.Primitives.ToggleButton)studio.FindName("ToolBrushBtn");
            brushButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(Visibility.Visible, ((FrameworkElement)studio.FindName("OpacityRow")).Visibility);
            ((Slider)studio.FindName("OpacitySlider")).Value = 1;
            Drag();
            _out.WriteLine("brush by mouse:  " + Row(studio, 100));
            Assert.Equal(255, studio.ActiveChannelBytes[100 * size + 99]);
            Assert.Equal(255, studio.ActiveChannelBytes[100 * size + 100]);
        });
    }

    /// <summary>
    /// Painting between two passes of the soften brush does not let the second
    /// pass soften further: rows the paint did not reach come out exactly as the
    /// first pass left them, and where the paint crossed the edge the second
    /// pass softens the painting. Ctrl+Z on both puts the first pass back
    /// exactly, and softening again from there goes no further. The control
    /// is "Soften all" in between instead, which is meant to become the layer -
    /// the second pass then softens on from it, and those rows do change.
    /// </summary>
    [Fact]
    public void PaintingBetweenTwoPassesOfTheSoftenBrushDoesNotSoftenFurther()
    {
        WithStudio(studio =>
        {
            int size = MaskBuffer.Size;
            byte At(int x, int y) => studio.ActiveChannelBytes[y * size + x];
            HardEdge(studio);
            SetUpSoftening(studio, 8);
            SoftenDownTheEdge(studio);
            var once = (byte[])studio.ActiveChannelBytes.Clone();
            int[] farRows = { 66, 75, 125, 134 };

            // A hard line of paint across the softened edge at row 100.
            studio.SetTool(MaskStudio.Tool.Brush);
            ((Slider)studio.FindName("OpacitySlider")).Value = 1;
            ((Slider)studio.FindName("SizeSlider")).Value = 8;
            studio.BeginStrokeAt(80.5, 100.5);
            studio.StrokeTo(120.5, 100.5);
            studio.EndStroke();
            _out.WriteLine("painted across: " + Row(studio, 100));
            Assert.Equal(255, At(100, 100));

            SetUpSoftening(studio, 8);
            SoftenDownTheEdge(studio);
            _out.WriteLine("softened again: " + Row(studio, 100));
            foreach (int y in farRows)
                for (int x = 0; x < size; x++)
                    Assert.True(once[y * size + x] == At(x, y), $"({x},{y}) was {once[y * size + x]}, is {At(x, y)}");
            // The painting itself is what was softened: solid where it was painted.
            Assert.Equal(255, At(100, 100));

            // Ctrl+Z on both strokes, then softening: the first pass's result exactly.
            ApplicationCommands.Undo.Execute(null, studio);
            ApplicationCommands.Undo.Execute(null, studio);
            Assert.Equal(once, studio.ActiveChannelBytes);
            SoftenDownTheEdge(studio);
            AssertSoftenedNoFurther(studio, once);

            // The control: Soften all in between becomes the layer, and the second
            // pass softens on from it.
            studio.SoftenActiveChannel();
            var afterAll = (byte[])studio.ActiveChannelBytes.Clone();
            SoftenDownTheEdge(studio);
            _out.WriteLine("after Soften all, softened again: " + Row(studio, 75));
            Assert.NotEqual(afterAll[75 * size + 98], At(98, 75));
            Assert.True(FadeWidth(studio, 75) > 8, "Soften all and the brush did not add up");
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
