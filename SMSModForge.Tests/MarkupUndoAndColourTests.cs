using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Rendering;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The dialogue line's own undo, and its colours.
/// <para/>
/// Undo is driven through the real window with real keystrokes, because the
/// fault was an interaction: the box redraws its document whenever the styling
/// moves, WPF recorded every redraw as an edit, and so every other Ctrl+Z did
/// nothing and a redo never survived an undo. Nothing short of typing, a
/// formatting button and the undo command together shows that.
/// <para/>
/// Colour is measured on both places a line is drawn - the Text box's runs and
/// the game-look row's pixels - because the fault there was the two reading the
/// same tag differently.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class MarkupUndoAndColourTests
{
    private readonly ITestOutputHelper _out;
    public MarkupUndoAndColourTests(ITestOutputHelper o) => _out = o;

    private static void Keys(UIElement target, string text)
    {
        foreach (char c in text)
        {
            TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, target, c.ToString()));
            WindowHarness.Pump();
        }
    }

    /// <summary>A dialogue with an empty line selected, and the Text box
    /// focused with the keyboard in it.</summary>
    private static (MainViewModel Vm, MarkupTextBox Box) EmptyLine(MainWindow window)
    {
        var vm = (MainViewModel)window.DataContext;
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues") { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();

        vm.AddDialogueCommand.Execute(null);
        WindowHarness.Pump();
        if (vm.SelectedDialogue!.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
        vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
        WindowHarness.Pump();

        window.Activate();
        var box = (MarkupTextBox)window.FindName("NodeTextBox");
        box.Focus();
        WindowHarness.Pump();
        Assert.True(box.IsKeyboardFocusWithin, "the Text box never had the keyboard, so this proves nothing");
        return (vm, box);
    }

    private static void Click(MainWindow window, string button)
    {
        ((Button)window.FindName(button)).RaiseEvent(
            new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        WindowHarness.Pump();
    }

    [Fact]
    public void EachStepComesBackOnePressAtATime()
    {
        WindowHarness.Run(window =>
        {
            var (vm, box) = EmptyLine(window);

            Keys(box, "hello world");
            box.Select(6, 5);
            Click(window, "MarkupBoldButton");
            box.CaretIndex = box.Text.Length;
            Keys(box, "!");
            Assert.Equal("hello <b>world</b>!", box.Text);

            // Ctrl+Z, three times: the second run of typing, the tag, the first
            // run. Every press does something - the fault was every OTHER one.
            string[] back = { "hello <b>world</b>", "hello world", "" };
            foreach (string expected in back)
            {
                Assert.True(box.ApplyShortcut(Key.Z, ModifierKeys.Control), "Ctrl+Z did nothing");
                WindowHarness.Pump();
                _out.WriteLine($"undo -> '{box.Text}' (node '{vm.SelectedNode!.Text}')");
                Assert.Equal(expected, box.Text);
                Assert.Equal(expected, vm.SelectedNode.Text);
            }

            // ...and Ctrl+Y brings each one back, which the redraw used to
            // throw away the moment anything was undone.
            string[] forward = { "hello world", "hello <b>world</b>", "hello <b>world</b>!" };
            foreach (string expected in forward)
            {
                Assert.True(box.ApplyShortcut(Key.Y, ModifierKeys.Control), "Ctrl+Y did nothing");
                WindowHarness.Pump();
                _out.WriteLine($"redo -> '{box.Text}'");
                Assert.Equal(expected, box.Text);
            }
        });
    }

    [Fact]
    public void TheUndoCommandTakesTheSameSteps()
    {
        // The menu and anything else that sends Undo rather than the key.
        WindowHarness.Run(window =>
        {
            var (_, box) = EmptyLine(window);
            Keys(box, "one two");
            box.Select(4, 3);
            Click(window, "MarkupItalicButton");
            Assert.Equal("one <i>two</i>", box.Text);

            ApplicationCommands.Undo.Execute(null, box);
            WindowHarness.Pump();
            Assert.Equal("one two", box.Text);

            ApplicationCommands.Redo.Execute(null, box);
            WindowHarness.Pump();
            Assert.Equal("one <i>two</i>", box.Text);
        });
    }

    [Fact]
    public void WithNothingOfTheLinesToUndoTheKeyIsLeftForTheEditor()
    {
        // Not swallowed: from any other field Ctrl+Z reaches the editor's own
        // undo, and a box that ate it with nothing to show for it would be the
        // one place that did not.
        WindowHarness.Run(window =>
        {
            var (_, box) = EmptyLine(window);
            Assert.False(box.CanUndoLine);
            Assert.False(box.ApplyShortcut(Key.Z, ModifierKeys.Control));
            Assert.False(box.ApplyShortcut(Key.Y, ModifierKeys.Control));
        });
    }

    [Fact]
    public void AnotherLineStartsAHistoryOfItsOwn()
    {
        // Stepping back from here into the previous node's edits would write
        // them onto this one.
        WindowHarness.Run(window =>
        {
            var (vm, box) = EmptyLine(window);
            Keys(box, "first line");
            Assert.True(box.CanUndoLine);

            vm.AddDialogueRootNodeCommand.Execute(null);
            WindowHarness.Pump();
            vm.SelectedNode = vm.SelectedDialogue!.Nodes.Last();
            WindowHarness.Pump();

            _out.WriteLine($"now showing '{box.Text}', can undo {box.CanUndoLine}");
            Assert.False(box.CanUndoLine, "the history followed the box to a different line");
        });
    }

    [Fact]
    public void AnEditAfterAnUndoDropsWhatCouldHaveBeenRedone()
    {
        WindowHarness.Run(window =>
        {
            var (_, box) = EmptyLine(window);
            Keys(box, "abc");
            box.Select(0, 3);
            Click(window, "MarkupBoldButton");
            Assert.True(box.Undo());
            Assert.True(box.CanRedoLine);

            box.CaretIndex = box.Text.Length;
            Keys(box, "d");
            Assert.False(box.CanRedoLine, "a redo survived a new edit, and would overwrite it");
        });
    }

    // ── Colour ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("#f66", 0xFF, 0x66, 0x66, 0xFF)]
    [InlineData("#F66", 0xFF, 0x66, 0x66, 0xFF)]
    [InlineData("#ff6666", 0xFF, 0x66, 0x66, 0xFF)]
    [InlineData("#0f08", 0x00, 0xFF, 0x00, 0x88)]      // RGBA, not WPF's ARGB
    [InlineData("#00ff0080", 0x00, 0xFF, 0x00, 0x80)]  // alpha LAST
    [InlineData("red", 0xFF, 0x00, 0x00, 0xFF)]
    [InlineData("RED", 0xFF, 0x00, 0x00, 0xFF)]
    [InlineData("lightblue", 173, 216, 230, 0xFF)]
    [InlineData("orange", 255, 128, 0, 0xFF)]
    [InlineData("purple", 160, 32, 240, 0xFF)]
    [InlineData("yellow", 255, 235, 4, 0xFF)]
    [InlineData("#zzz", 0xFF, 0xFF, 0xFF, 0xFF)]       // not a digit counts as f
    public void AColourIsReadTheWayTheGameReadsIt(string value, byte r, byte g, byte b, byte a)
    {
        Assert.True(TmpColor.TryParse(value, out var c), $"{value} was not read as a colour");
        _out.WriteLine($"{value} -> {c.R},{c.G},{c.B},{c.A}");
        Assert.Equal((r, g, b, a), (c.R, c.G, c.B, c.A));
    }

    [Theory]
    [InlineData("crimson")]      // one of WPF's hundred and forty; not the game's
    [InlineData("#12345")]       // five digits
    [InlineData("#1234567")]     // seven
    [InlineData("ff6666")]       // no hash
    [InlineData("")]
    public void WhatTheGameDoesNotReadIsNoColour(string value)
    {
        Assert.False(TmpColor.TryParse(value, out _));
        Assert.Null(MarkupTextBox.BrushFor(value));
    }

    /// <summary>The Text box's colour for a line, or null for the ordinary one.</summary>
    private static Color? BoxColour(string line, string words)
    {
        var box = new MarkupTextBox { Width = 300, Text = line };
        var window = new Window { Width = 400, Height = 200, Left = -10000, Top = -10000, ShowInTaskbar = false, Content = box };
        window.Show();
        WindowHarness.Pump();
        var run = ((Paragraph)box.Document.Blocks.FirstBlock).Inlines.OfType<Run>().Single(r => r.Text == words);
        var local = run.ReadLocalValue(TextElement.ForegroundProperty) as SolidColorBrush;
        window.Close();
        return local?.Color;
    }

    /// <summary>How many pixels of the game-look row are near a colour.</summary>
    private static int RowPixelsNear(string line, Color want)
    {
        var preview = new DialogueLinePreview
        {
            Width = 420, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Line = line,
        };
        var window = new Window { Width = 500, Height = 200, Left = -10000, Top = -10000, ShowInTaskbar = false, Content = preview };
        window.Show();
        window.UpdateLayout();
        WindowHarness.Pump();

        var render = new RenderTargetBitmap(Math.Max(1, (int)preview.ActualWidth), Math.Max(1, (int)preview.ActualHeight),
                                            96, 96, PixelFormats.Pbgra32);
        render.Render(preview);
        var px = new byte[render.PixelWidth * render.PixelHeight * 4];
        render.CopyPixels(px, render.PixelWidth * 4, 0);
        window.Close();

        int hits = 0;
        for (int i = 0; i + 3 < px.Length; i += 4)
            if (Math.Abs(px[i] - want.B) <= 28 && Math.Abs(px[i + 1] - want.G) <= 28 && Math.Abs(px[i + 2] - want.R) <= 28)
                hits++;
        return hits;
    }

    private static bool FontAvailable
        => SMSModForge.Rendering.VanillaUiLibrary.IsAvailable;

    [Theory]
    [InlineData("#f66", "#FF6666")]     // the button's old value: red in the box, white on the row
    [InlineData("#0F0F", "#00FF00")]    // RGBA: green to the game, invisible magenta to WPF
    [InlineData("orange", "#FF8000")]   // a name both know, with the game's own value
    public void TheBoxAndTheRowPaintTheSameColour(string value, string expectedHex)
    {
        WindowHarness.Run(_ =>
        {
            if (!FontAvailable) { _out.WriteLine("no font extraction - skipping the row half"); }

            var expected = (Color)ColorConverter.ConvertFromString(expectedHex)!;
            var box = BoxColour($"say <color={value}>this</color> now", "this");
            _out.WriteLine($"{value}: box {box?.ToString() ?? "(ordinary)"}");
            Assert.Equal(expected, box);

            if (!FontAvailable) return;
            int tagged = RowPixelsNear($"say <color={value}>this</color> now", expected);
            int plain = RowPixelsNear("say this now", expected);
            _out.WriteLine($"{value}: row {tagged} pixel(s) near {expectedHex}, {plain} without the tag");
            Assert.True(tagged > 20, "the row did not paint the colour the box shows");
            Assert.True(plain < 5, "the row is that colour without the tag, so this proves nothing");
        });
    }

    [Fact]
    public void ANameOnlyWindowsKnowsIsTheOrdinaryColourInBoth()
    {
        WindowHarness.Run(_ =>
        {
            var crimson = (Color)ColorConverter.ConvertFromString("#DC143C")!;
            Assert.Null(BoxColour("say <color=crimson>this</color> now", "this"));
            if (!FontAvailable) return;
            int near = RowPixelsNear("say <color=crimson>this</color> now", crimson);
            _out.WriteLine($"row: {near} crimson pixel(s)");
            Assert.True(near < 5, "the row painted a colour the game does not know");
        });
    }

    // ── The colour button ─────────────────────────────────────────────

    private static MarkupTextBox Box(string text)
    {
        var box = new MarkupTextBox { Width = 300, MinHeight = 60, Text = text };
        var window = new Window { Width = 400, Height = 200, Left = -10000, Top = -10000, ShowInTaskbar = false, Content = box };
        window.Show();
        window.Activate();
        box.Focus();
        WindowHarness.Pump();
        return box;
    }

    [Fact]
    public void TheColourWrittenIsTheOnePicked()
    {
        WindowHarness.Run(_ =>
        {
            var box = Box("say this now");
            box.Select(4, 4);
            string? offered = null;
            box.PickColor = current => { offered = current; return "#12AB34"; };

            Assert.True(box.ApplyShortcut(Key.C, ModifierKeys.Control | ModifierKeys.Shift));
            WindowHarness.Pump();

            _out.WriteLine($"'{box.Text}', picker opened on {offered}");
            Assert.Equal("say <color=#12AB34>this</color> now", box.Text);
            Assert.Equal(MarkupTextBox.Markup.DefaultColor, offered);

            // The next one opens on the colour just used.
            box.Select(box.Text.Length, 0);
            box.ApplyShortcut(Key.C, ModifierKeys.Control | ModifierKeys.Shift);
            Assert.Equal("#12AB34", offered);

            // And it is one step, like the other buttons.
            Assert.True(box.Undo());
            Assert.True(box.Undo());
            Assert.Equal("say this now", box.Text);
        });
    }

    [Theory]
    [InlineData("#FF6666", "#FF6666", 0xFF, 0x66, 0x66, 0xFF)]
    [InlineData("#12AB34", "#12AB34", 0x12, 0xAB, 0x34, 0xFF)]
    [InlineData("#00FF00", "#00FF00", 0x00, 0xFF, 0x00, 0xFF)]
    [InlineData("#3366CC80", "#3366CC80", 0x33, 0x66, 0xCC, 0x80)]   // half transparent: eight digits
    public void WhatThePickerChoosesIsSomethingTheGameReads(string seed, string expectedValue,
                                                            byte r, byte g, byte b, byte a)
    {
        // The picker's OWN output, through the window's own conversion - not a
        // hex string typed into a test. The wheel is opened on a colour and
        // asked what it is holding, which is what pressing OK hands back.
        WindowHarness.Run(_ =>
        {
            var wheel = new SMSModForge.View.ColorPickerWindow();
            wheel.Seed(seed);
            string picked = wheel.Current;

            string? value = MainWindow.ColorTagValue(picked);
            _out.WriteLine($"seeded {seed} -> picker '{picked}' -> tag '{value}'");

            Assert.Equal(expectedValue, value);
            Assert.True(TmpColor.TryParse(value, out var read), $"the game would not read '{value}' as a colour");
            Assert.Equal((r, g, b, a), (read.R, read.G, read.B, read.A));
        });
    }

    [Theory]
    [InlineData("#12AB34")]
    [InlineData("#3366CC80")]
    public void AColourStraightFromThePickerIsDrawnInBothPlaces(string seed)
    {
        // End to end: the wheel, the button, the line it writes, and then both
        // places that draw a line. A value the box shows and the row does not is
        // exactly the fault this went looking for.
        WindowHarness.Run(window =>
        {
            var wheel = new SMSModForge.View.ColorPickerWindow();
            wheel.Seed(seed);
            string? value = MainWindow.ColorTagValue(wheel.Current);
            Assert.NotNull(value);

            var (_, box) = EmptyLine(window);
            Keys(box, "say this now");
            box.Select(4, 4);
            box.PickColor = _ => value;      // what the real picker would have handed back
            Click(window, "MarkupColorButton");
            WindowHarness.Pump();

            string line = box.Text;
            _out.WriteLine($"line '{line}'");
            Assert.Equal($"say <color={value}>this</color> now", line);

            Assert.True(TmpColor.TryParse(value, out var want));
            var expected = Color.FromRgb(want.R, want.G, want.B);

            // The Text box: the words it wrapped are painted that colour.
            var shown = BoxColour(line, "this");
            Assert.NotNull(shown);
            Assert.Equal(expected, Color.FromRgb(shown!.Value.R, shown.Value.G, shown.Value.B));
            Assert.Equal(want.A, shown.Value.A);

            // The game-look row: the same colour lands in the pixels. A half
            // transparent one is drawn over the panel, so it is looked for as
            // the colour it becomes there rather than as its own.
            if (!FontAvailable) { _out.WriteLine("no font extraction - skipping the row half"); return; }
            var panel = (Color)ColorConverter.ConvertFromString(DialogueLook.PanelHex)!;
            var onPanel = Color.FromRgb(
                (byte)((want.R * want.A + panel.R * (255 - want.A)) / 255),
                (byte)((want.G * want.A + panel.G * (255 - want.A)) / 255),
                (byte)((want.B * want.A + panel.B * (255 - want.A)) / 255));

            int tagged = RowPixelsNear(line, onPanel);
            int plain = RowPixelsNear("say this now", onPanel);
            _out.WriteLine($"row: {tagged} pixel(s) near {onPanel}, {plain} without the tag");
            Assert.True(tagged > 20, "the row did not draw the colour the picker chose");
            Assert.True(plain < 5, "the row is that colour without the tag, so this proves nothing");
        });
    }

    [Fact]
    public void CancellingThePickerWritesNothing()
    {
        WindowHarness.Run(_ =>
        {
            var box = Box("say this now");
            box.Select(4, 4);
            box.PickColor = _ => null;

            box.ApplyShortcut(Key.C, ModifierKeys.Control | ModifierKeys.Shift);
            WindowHarness.Pump();

            Assert.Equal("say this now", box.Text);
            Assert.False(box.CanUndoLine, "a cancelled pick left a step behind");
        });
    }

    [Fact]
    public void TheWindowsColourButtonNeverOpensAPickerUnderTheHarness()
    {
        // The window's picker is a dialog, and a dialog here stops the suite on
        // somebody's screen. Under the harness it answers as a cancel would.
        WindowHarness.Run(window =>
        {
            var (_, box) = EmptyLine(window);
            Keys(box, "a line");
            box.Select(0, 1);

            Click(window, "MarkupColorButton");
            Assert.Equal("a line", box.Text);
        });
    }

    [Fact]
    public void ThePickerIsTheWindowsOwn()
    {
        WindowHarness.Run(window =>
        {
            var box = (MarkupTextBox)window.FindName("NodeTextBox");
            Assert.NotNull(box.PickColor);
        });
    }
}
