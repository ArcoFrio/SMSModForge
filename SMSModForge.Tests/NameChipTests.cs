using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Shared;
using SMSModForge.Validation;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A character's name typed in a line becomes their name token by itself
/// (1.10.0, the author: "there should be some level of automatic detection
/// when the user types in a character's name, with changing the name in the
/// text field and previews to have the color of the character and a very
/// small x next to it if the user decides to put it as plain text instead. The
/// detection should happen as soon as the user finishes typing the name").
/// Held on the line box with real typing:
/// <list type="bullet">
/// <item>a name still being typed is left alone - control - and becomes the
/// token as the next word is typed; the box shows the name, the line holds the
/// token; undo gives the word back as typed;</item>
/// <item>typing beside a name goes beside it; Backspace or Delete takes the
/// whole name;</item>
/// <item>the x is drawn at the name's top right (measured in the picture, with
/// a control drawn without it) and takes the click there; it writes the plain
/// word back and the line keeps it plain;</item>
/// <item>leaving the box finishes a name; words not typed now are not looked
/// at;</item>
/// <item>in the editor: the line's step, Ctrl+Z and Ctrl+Y really pressed, the
/// x remembered on the line, and Validate leaving a kept name alone - control:
/// the same name in another line is still offered.</item>
/// </list>
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NameChipTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-namechips-" + Guid.NewGuid().ToString("N"));

    public NameChipTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private const string Token = "{name:anna}";

    private static void Keys(UIElement target, string text)
    {
        foreach (char c in text)
        {
            TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, target, c.ToString()));
            WindowHarness.Pump();
        }
    }

    /// <summary>What the box shows: its document's words, as a person reads them.</summary>
    private static string Shown(MarkupTextBox box)
        => string.Concat(((Paragraph)box.Document.Blocks.FirstBlock).Inlines.Select(i => i is Run r ? r.Text : i is LineBreak ? "\n" : ""));

    /// <summary>A line box on its own, in a window of its own, whose names are the game's Anna - found by the
    /// editor's own rule (NameMentions.Find).</summary>
    private static (Window Window, MarkupTextBox Box, TextBox Elsewhere, List<string> Kept) Box(string text = "")
    {
        var names = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Anna"] = "anna" };
        var kept = new List<string>();
        var box = new MarkupTextBox { Width = 400, MinHeight = 60, FontSize = 16, Text = text };
        box.FindNames = t => NameMentions.Find(t, names).Select(h => (h.Start, h.Length, NameTokens.Make(h.Key))).ToList();
        box.KeepPlain = kept.Add;
        var elsewhere = new TextBox { Width = 100 };
        // Room above the box: a name on its first line has its x over its top.
        var panel = new StackPanel { Background = Brushes.White, Margin = new Thickness(0, 24, 0, 0) };
        panel.Children.Add(box);
        panel.Children.Add(elsewhere);
        var window = new Window { Width = 500, Height = 220, Left = -10000, Top = -10000, ShowInTaskbar = false, Content = panel };
        window.Show();
        window.Activate();
        box.Focus();
        Keyboard.Focus(box);
        WindowHarness.Pump();
        box.CaretIndex = box.Text.Length;
        return (window, box, elsewhere, kept);
    }

    /// <summary>The game's Anna, named and coloured as the editor would, whatever another test left set.</summary>
    private static void WithAnna(Action body)
    {
        var name = DialogueMarkup.NameOf;
        var colour = DialogueMarkup.TokenColor;
        DialogueMarkup.NameOf = key => string.Equals(key, "anna", StringComparison.OrdinalIgnoreCase) ? "Anna" : null;
        DialogueMarkup.TokenColor = token => token == Token ? new UiColor(0xF0, 0x80, 0xC0, 0xFF) : null;
        try { body(); }
        finally { DialogueMarkup.NameOf = name; DialogueMarkup.TokenColor = colour; }
    }

    [Fact]
    public void ANameBecomesItsTokenOnceTheWordIsFinished_AndUndoGivesTheWordBack()
    {
        WindowHarness.Run(_ => WithAnna(() =>
        {
            var (window, box, _, _) = Box();
            Keys(box, "Hello Anna");
            // Control: still being typed - it could be Annabel.
            Assert.Equal("Hello Anna", box.Text);
            Assert.Equal(0, box.ChipCount);

            Keys(box, ",");
            _out.WriteLine($"line '{box.Text}', shown '{Shown(box)}'");
            Assert.Equal("Hello " + Token + ",", box.Text);
            Assert.Equal("Hello Anna,", Shown(box));
            Assert.Equal(1, box.ChipCount);
            Assert.Equal(box.Text.Length, box.CaretIndex);

            Keys(box, " how");
            Assert.Equal("Hello " + Token + ", how", box.Text);
            Assert.Equal("Hello Anna, how", Shown(box));

            // Back: the words after it, then the name as it was typed.
            Assert.True(box.Undo());
            Assert.Equal("Hello " + Token + ",", box.Text);
            Assert.True(box.Undo());
            Assert.Equal("Hello Anna,", box.Text);
            Assert.Equal(0, box.ChipCount);
            Assert.True(box.Redo());
            Assert.Equal("Hello " + Token + ",", box.Text);
            window.Close();
        }));
    }

    [Fact]
    public void TypingBesideANameGoesBesideIt_AndBackspaceTakesTheWholeName()
    {
        WindowHarness.Run(_ => WithAnna(() =>
        {
            var (window, box, _, _) = Box("Hi " + Token);
            RealKeys.Watch(window);
            Assert.Equal("Hi Anna", Shown(box));
            Assert.Equal(1, box.ChipCount);

            // Just after it, and just before it: beside, the name whole.
            box.CaretIndex = box.Text.Length;
            Keys(box, "!");
            Assert.Equal("Hi " + Token + "!", box.Text);
            box.CaretIndex = 3;
            Keys(box, "x");
            Assert.Equal("Hi x" + Token + "!", box.Text);
            Assert.Equal(1, box.ChipCount);

            // Backspace just after it: all of it.
            box.CaretIndex = 4 + Token.Length;
            RealKeys.Press(window, Key.Back);
            WindowHarness.Pump();
            Assert.Equal("Hi x!", box.Text);
            Assert.Equal(4, box.CaretIndex);

            // Delete just before one: the same.
            box.Text = Token + " waves";
            WindowHarness.Pump();
            box.CaretIndex = 0;
            RealKeys.Press(window, Key.Delete);
            WindowHarness.Pump();
            Assert.Equal(" waves", box.Text);

            // Control: Backspace in plain words takes one letter.
            box.CaretIndex = box.Text.Length;
            RealKeys.Press(window, Key.Back);
            WindowHarness.Pump();
            Assert.Equal(" wave", box.Text);
            window.Close();
        }));
    }

    /// <summary>Pixels of the picture near the name chip's dark colour, and the box they fill.</summary>
    private static (int Count, Int32Rect Bounds) ChipPixels(FrameworkElement root)
    {
        root.UpdateLayout();
        var picture = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        picture.Render(root);
        var px = new byte[picture.PixelWidth * picture.PixelHeight * 4];
        picture.CopyPixels(px, picture.PixelWidth * 4, 0);
        var dark = (Color)ColorConverter.ConvertFromString(DialogueLook.TokenChipHex)!;
        int count = 0, left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (int y = 0; y < picture.PixelHeight; y++)
            for (int x = 0; x < picture.PixelWidth; x++)
            {
                int i = (y * picture.PixelWidth + x) * 4;
                if (Math.Abs(px[i] - dark.B) > 6 || Math.Abs(px[i + 1] - dark.G) > 6 || Math.Abs(px[i + 2] - dark.R) > 6) continue;
                count++;
                left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
        return (count, count == 0 ? default : new Int32Rect(left, top, right - left + 1, bottom - top + 1));
    }

    [Fact]
    public void TheXIsDrawnAtTheNamesTopRight_AndAClickThereKeepsItPlainInThisLine()
    {
        WindowHarness.Run(_ => WithAnna(() =>
        {
            var (window, box, _, kept) = Box("Hello there, " + Token + ", hi");
            var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);   // with the layer the x is drawn on
            WindowHarness.Pump();

            // Measured in the picture: with the x, the dark of the name's chip reaches further up and to the right.
            var with = ChipPixels(root);
            box.IsReadOnly = true;                     // control: no x where the line cannot be edited
            WindowHarness.Pump();
            var without = ChipPixels(root);
            box.IsReadOnly = false;
            WindowHarness.Pump();
            _out.WriteLine($"the chip's dark without the x: {without.Bounds.X},{without.Bounds.Y} {without.Bounds.Width}x{without.Bounds.Height} ({without.Count}); with: {with.Bounds.X},{with.Bounds.Y} {with.Bounds.Width}x{with.Bounds.Height} ({with.Count})");
            Assert.True(without.Count > 30, "control: the name's chip is not drawn - this proves nothing");
            // (On the box's first line the x stays inside the box: at the name's top, not above it.)
            Assert.True(with.Count != without.Count, "nothing of the x was drawn");
            Assert.True(with.Bounds.Y <= without.Bounds.Y, "the x is below the name's top");
            Assert.True(with.Bounds.X + with.Bounds.Width >= without.Bounds.X + without.Bounds.Width + 2, "the x is not at the name's right");
            Assert.True(with.Bounds.X >= without.Bounds.X - 1, "something was drawn left of the name");

            // What is under that corner takes the click: the x, not the words.
            var spot = Assert.Single(box.CloserSpots);
            var corner = new Point(spot.Spot.X + spot.Spot.Width / 2, spot.Spot.Y + spot.Spot.Height / 2);
            var layer = AdornerLayer.GetAdornerLayer(box)!;
            var hit = VisualTreeHelper.HitTest(layer, box.TranslatePoint(corner, layer));
            Assert.NotNull(hit);
            Assert.IsAssignableFrom<Adorner>(hit!.VisualHit);
            // Control: in the middle of the words, nothing of the x's.
            var words = VisualTreeHelper.HitTest(layer, box.TranslatePoint(new Point(10, box.ActualHeight / 2), layer));
            Assert.False(words?.VisualHit is Adorner, "the x's layer takes clicks away from the words");

            Assert.True(box.ClickCloserAt(corner));
            WindowHarness.Pump();
            Assert.False(box.ClickCloserAt(corner), "a second click on where the x was did something");
            Assert.Equal("Hello there, Anna, hi", box.Text);
            Assert.Equal(new[] { "Anna" }, kept);
            Assert.Equal(0, box.ChipCount);
            Assert.Empty(box.CloserSpots);

            // Kept plain in this line: typed again, it stays words.
            box.CaretIndex = box.Text.Length;
            Keys(box, " Anna!");
            Assert.Equal("Hello there, Anna, hi Anna!", box.Text);

            // Control: another line is another line.
            box.Text = "Bye";
            WindowHarness.Pump();
            box.CaretIndex = box.Text.Length;
            Keys(box, " Anna.");
            Assert.Equal("Bye " + Token + ".", box.Text);
            window.Close();
        }));
    }

    [Fact]
    public void LeavingTheBoxFinishesAName_AndWordsNotTypedNowAreLeftAlone()
    {
        WindowHarness.Run(_ => WithAnna(() =>
        {
            var (window, box, elsewhere, _) = Box("Old Anna stays");
            Keys(box, " and");
            // Not typed now: left as it was written.
            Assert.Equal("Old Anna stays and", box.Text);
            Keys(box, " Anna");
            Assert.Equal("Old Anna stays and Anna", box.Text);

            elsewhere.Focus();
            Keyboard.Focus(elsewhere);
            WindowHarness.Pump();
            Assert.False(box.IsKeyboardFocusWithin);
            Assert.Equal("Old Anna stays and " + Token, box.Text);

            // A stutter is the words' own: the token cannot carry it.
            box.Text = "";
            WindowHarness.Pump();
            box.Focus();
            Keyboard.Focus(box);
            WindowHarness.Pump();
            Keys(box, "A-Anna, wait");
            Assert.Equal("A-Anna, wait", box.Text);
            window.Close();
        }));
    }

    [Fact]
    public void InTheEditor_TheNameIsAStepOfItsOwn_AndAKeptNameIsLeftAloneByValidate()
    {
        PackRepository.Save(UndoCrawlPack.Make(_dir), _dir);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Dialogues;
            WindowHarness.Pump();
            try
            {
                // A name of the game's that is one character's.
                var names = NameMentions.Detectable(vm.Pack);
                var (name, key) = names.Where(p => p.Value != null && vm.Pack.Characters.Any(c => c.IsVanillaCharacter && c.Key == p.Value))
                                       .Select(p => (p.Key, p.Value!)).OrderBy(p => p.Item1.Length).First();
                string token = NameTokens.Make(key);
                _out.WriteLine($"typing {name}, which becomes {token}");

                var dialogue = vm.Dialogues.First(d => d.Nodes.Count >= 2 && !d.Model.IsVanillaBased);
                vm.SelectedDialogue = dialogue;
                var line = dialogue.Nodes[0];
                vm.SelectedNode = line;
                WindowHarness.Pump();
                var box = (MarkupTextBox)window.FindName("NodeTextBox");
                box.Focus();
                Keyboard.Focus(box);
                WindowHarness.Pump();
                box.CaretIndex = box.Text.Length;
                vm.Undo.Checkpoint();
                string was = line.Text;

                Keys(box, " " + name + ",");
                Assert.Equal(was + " " + token + ",", line.Text);

                RealKeys.Press(window, Key.Z, ModifierKeys.Control);
                WindowHarness.Pump();
                Assert.Equal(was + " " + name + ",", line.Text);       // the word back, as typed
                RealKeys.Press(window, Key.Y, ModifierKeys.Control);
                WindowHarness.Pump();
                Assert.Equal(was + " " + token + ",", line.Text);

                // The x: plain words, and the line keeps it so.
                int start = line.Text.IndexOf(token, StringComparison.Ordinal);
                Assert.True(box.KeepNamePlain(start));
                WindowHarness.Pump();
                Assert.Equal(was + " " + name + ",", line.Text);
                Assert.Contains(name, line.Model.PlainNames);
                vm.Undo.Checkpoint();

                // Validate: the kept one is left alone; the same name in another line is offered.
                var other = dialogue.Nodes[1];
                other.Text = name + " is here.";
                int Offered()
                {
                    var issues = new List<ValidationIssue>();
                    NameMentions.Check(issues, vm.Pack);
                    return issues.Count(i => i.Code == NameMentions.PlainMention && i.Message.Contains(name, StringComparison.Ordinal)
                                             && i.Where.Contains(dialogue.Model.Key, StringComparison.Ordinal));
                }
                Assert.Equal(1, Offered());
                line.Model.PlainNames.Clear();                // control: not kept, offered too
                Assert.Equal(2, Offered());

                // And undo takes the x back with the words.
                line.Model.PlainNames.Add(name);
                vm.Undo.Checkpoint();
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }
}
