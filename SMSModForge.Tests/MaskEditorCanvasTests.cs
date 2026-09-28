using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Mask Editor's working area and side panels: panning with the middle
/// button anywhere on the dark area, and labels that stay readable on every
/// theme.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class MaskEditorCanvasTests
{
    private readonly ITestOutputHelper _out;
    public MaskEditorCanvasTests(ITestOutputHelper o) => _out = o;

    private static void WithEditor(Action<MaskEditorWindow> body)
    {
        WindowHarness.Run(_ =>
        {
            var outfit = new OutfitViewModel(new OutfitDef { Key = "o", GameObjectName = "O" });
            var editor = new MaskEditorWindow(outfit, System.IO.Path.GetTempPath())
            {
                Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
            };
            bool closed = false;
            editor.Closed += (_, _) => closed = true;
            try
            {
                editor.Show();
                WindowHarness.Pump();
                body(editor);
            }
            finally
            {
                if (!closed) editor.Close();
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
        WithEditor(editor =>
        {
            var area = (Border)editor.FindName("CanvasArea");
            var image = (FrameworkElement)editor.FindName("CanvasHost");
            var pan = (TranslateTransform)editor.FindName("PanTransform");

            // The pointer, as the test says it is.
            var at = new Point(100, 100);
            editor.PointerOnArea = _ => at;

            // Pressed on the dark area, away from the image.
            var down = Button(MouseButton.Middle, UIElement.MouseDownEvent);
            area.RaiseEvent(down);
            Assert.True(editor.IsPanning, "the middle button on the area around the image does not pan");
            Assert.True(down.Handled);
            Assert.Same(Cursors.SizeAll, Mouse.OverrideCursor);

            at = new Point(160, 70);
            area.RaiseEvent(Move());
            _out.WriteLine($"pan {pan.X},{pan.Y}");
            Assert.Equal(60, pan.X, 6);
            Assert.Equal(-30, pan.Y, 6);

            area.RaiseEvent(Button(MouseButton.Middle, UIElement.MouseUpEvent));
            Assert.False(editor.IsPanning);
            Assert.Null(Mouse.OverrideCursor);

            // Moving afterwards moves nothing.
            at = new Point(0, 0);
            area.RaiseEvent(Move());
            Assert.Equal(60, pan.X, 6);

            // Pressed over the image, it reaches the area the same way, and
            // carries on from where the last pan left the image.
            at = new Point(10, 10);
            image.RaiseEvent(Button(MouseButton.Middle, UIElement.MouseDownEvent));
            Assert.True(editor.IsPanning, "the middle button over the image does not pan");
            at = new Point(20, 30);
            area.RaiseEvent(Move());
            Assert.Equal(70, pan.X, 6);
            Assert.Equal(-10, pan.Y, 6);
            area.RaiseEvent(Button(MouseButton.Middle, UIElement.MouseUpEvent));
            Assert.False(editor.IsPanning);

            // The other buttons do not pan.
            area.RaiseEvent(Button(MouseButton.Left, UIElement.MouseDownEvent));
            area.RaiseEvent(Button(MouseButton.Right, UIElement.MouseDownEvent));
            Assert.False(editor.IsPanning);
        });
    }

    [Fact]
    public void ClosingMidPanPutsTheCursorBack()
    {
        WithEditor(editor =>
        {
            editor.PointerOnArea = _ => new Point(0, 0);
            ((Border)editor.FindName("CanvasArea")).RaiseEvent(Button(MouseButton.Middle, UIElement.MouseDownEvent));
            Assert.Same(Cursors.SizeAll, Mouse.OverrideCursor);
            editor.Close();
            WindowHarness.Pump();
            Assert.Null(Mouse.OverrideCursor);
        });
    }

    [Fact]
    public void TheCopyLayersLabelReadsOnEveryTheme()
    {
        // Reported on the light themes: a fixed light grey on the panel's
        // light background. Measured as drawn: the label's own colour against
        // the colour of the panel it sits on, for every theme.
        WindowHarness.Run(_ =>
        {
            var was = ThemeManager.Current;
            try
            {
                foreach (var theme in ThemeManager.All)
                {
                    ThemeManager.Apply(theme);
                    WindowHarness.Pump();

                    var outfit = new OutfitViewModel(new OutfitDef { Key = "o", GameObjectName = "O" });
                    var editor = new MaskEditorWindow(outfit, System.IO.Path.GetTempPath())
                    {
                        Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
                    };
                    try
                    {
                        editor.Show();
                        ((FrameworkElement)editor.FindName("CopyFromPanel")).Visibility = Visibility.Visible;
                        WindowHarness.Pump();
                        editor.UpdateLayout();

                        var label = (TextBlock)editor.FindName("CopyFromLabel");
                        var ink = ((SolidColorBrush)label.Foreground).Color;
                        var panel = BackgroundBehind(label);
                        double ratio = Contrast(ink, panel);
                        _out.WriteLine($"{theme.Name,-10} {ink} on {panel}: {ratio:0.00}");
                        Assert.True(ratio >= 4.5, $"'Copy layers from' is {ratio:0.00}:1 on {theme.Name}");
                    }
                    finally
                    {
                        editor.Close();
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
