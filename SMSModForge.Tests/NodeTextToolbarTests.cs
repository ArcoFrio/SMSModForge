using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The formatting buttons over a dialogue line, and the shortcuts beside them.
/// <para/>
/// Writing markup by hand means remembering that the game reads
/// <c>&lt;b&gt;</c> and prints <c>&lt;bold&gt;</c>, and typing the closing half
/// in the right place. These do both.
/// <para/>
/// The hazard the tests here are mostly about is the SELECTION. A toolbar
/// button is pressed with the mouse, and a button that takes focus takes the
/// selection with it — at which point the tag wraps nothing and lands wherever
/// the caret was reset to. That is a silent wrong answer, not an error, so it
/// is driven end to end through the real window.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NodeTextToolbarTests
{
    private readonly ITestOutputHelper _out;
    public NodeTextToolbarTests(ITestOutputHelper o) => _out = o;

    /// <summary>A realised box on a window, the way the editor has it.</summary>
    private static MarkupTextBox Box(string text = "")
    {
        var box = new MarkupTextBox { Width = 300, MinHeight = 60, Text = text };
        var window = new Window
        {
            Width = 400, Height = 200, Left = -10000, Top = -10000,
            ShowInTaskbar = false, Content = box,
        };
        window.Show();
        WindowHarness.Pump();
        return box;
    }

    // ── Wrapping ─────────────────────────────────────────────────────

    [Fact]
    public void ATagGoesRoundTheSelectedWords()
    {
        WindowHarness.Run(_ =>
        {
            var box = Box("say this now");
            box.Focus();
            box.Select(4, 4);                       // "this"
            WindowHarness.Pump();

            box.Surround(MarkupTextBox.Markup.OpenBold, MarkupTextBox.Markup.CloseBold);
            WindowHarness.Pump();

            _out.WriteLine($"'{box.Text}'");
            Assert.Equal("say <b>this</b> now", box.Text);
        });
    }

    [Fact]
    public void TheSameWordsStaySelectedAfterwards()
    {
        // So a second tag can be put round the same phrase. Losing the
        // selection would mean re-selecting between every tag, and the words
        // have MOVED by the length of the opening tag, so "leave it alone" is
        // not the same as getting it right.
        WindowHarness.Run(_ =>
        {
            var box = Box("say this now");
            box.Focus();
            box.Select(4, 4);
            WindowHarness.Pump();

            box.Surround(MarkupTextBox.Markup.OpenBold, MarkupTextBox.Markup.CloseBold);
            WindowHarness.Pump();

            _out.WriteLine($"selection {box.SelectionStart}..{box.SelectionStart + box.SelectionLength}");
            Assert.Equal("this", box.Text.Substring(box.SelectionStart, box.SelectionLength));

            box.Surround(MarkupTextBox.Markup.OpenItalic, MarkupTextBox.Markup.CloseItalic);
            WindowHarness.Pump();

            _out.WriteLine($"'{box.Text}'");
            Assert.Equal("say <b><i>this</i></b> now", box.Text);
        });
    }

    [Fact]
    public void WithNothingSelectedThePairOpensAtTheCaret()
    {
        WindowHarness.Run(_ =>
        {
            var box = Box("say  now");
            box.Focus();
            box.CaretIndex = 4;
            WindowHarness.Pump();

            box.Surround(MarkupTextBox.Markup.OpenBold, MarkupTextBox.Markup.CloseBold);
            WindowHarness.Pump();

            _out.WriteLine($"'{box.Text}', caret at {box.CaretIndex}");
            Assert.Equal("say <b></b> now", box.Text);

            // Between the tags, which is where the next keystroke belongs.
            Assert.Equal(7, box.CaretIndex);
        });
    }

    [Fact]
    public void WithTheCaretNowhereTheTagsGoAtTheEnd()
    {
        // The case that has no right answer, only a predictable one: the box
        // has never been in focus, so WPF reports a caret at the very start of
        // the document - which is not where anybody meant. A pair opening in
        // front of the first word every time would be worse than useless.
        WindowHarness.Run(_ =>
        {
            var box = Box("say this now");
            Assert.False(box.IsKeyboardFocusWithin);

            box.Surround(MarkupTextBox.Markup.OpenBold, MarkupTextBox.Markup.CloseBold);
            WindowHarness.Pump();

            _out.WriteLine($"'{box.Text}'");
            Assert.Equal("say this now<b></b>", box.Text);
        });
    }

    [Fact]
    public void AnEmptyLineTakesThePairAndNothingElse()
    {
        WindowHarness.Run(_ =>
        {
            var box = Box("");
            box.Surround(MarkupTextBox.Markup.OpenSize, MarkupTextBox.Markup.CloseSize);
            WindowHarness.Pump();

            _out.WriteLine($"'{box.Text}'");
            Assert.Equal("<size=70%></size>", box.Text);
        });
    }

    // ── The shortcuts ────────────────────────────────────────────────

    [Fact]
    public void CtrlBWritesTheTagRatherThanApplyingItToTheDocument()
    {
        WindowHarness.Run(_ =>
        {
            var box = Box("say this now");
            box.Focus();
            box.Select(4, 4);
            WindowHarness.Pump();

            Assert.True(box.ApplyShortcut(Key.B, ModifierKeys.Control));
            WindowHarness.Pump();

            _out.WriteLine($"'{box.Text}'");
            Assert.Equal("say <b>this</b> now", box.Text);
        });
    }

    [Theory]
    [InlineData(Key.I, false, "say <i>this</i> now")]
    [InlineData(Key.C, true, "say <color=#FF6666>this</color> now")]   // no picker: the colour it would open on
    [InlineData(Key.S, true, "say <size=70%>this</size> now")]
    public void TheOtherChordsWriteTheirOwnTag(Key key, bool shift, string expected)
    {
        WindowHarness.Run(_ =>
        {
            var box = Box("say this now");
            box.Focus();
            box.Select(4, 4);
            WindowHarness.Pump();

            var mods = ModifierKeys.Control | (shift ? ModifierKeys.Shift : ModifierKeys.None);
            Assert.True(box.ApplyShortcut(key, mods));
            WindowHarness.Pump();

            _out.WriteLine($"'{box.Text}'");
            Assert.Equal(expected, box.Text);
        });
    }

    [Theory]
    [InlineData(Key.B, ModifierKeys.None)]
    [InlineData(Key.B, ModifierKeys.Shift)]
    [InlineData(Key.B, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.C, ModifierKeys.Control)]          // copy, and it must stay copy
    [InlineData(Key.V, ModifierKeys.Control)]
    [InlineData(Key.Z, ModifierKeys.Control)]
    public void EveryOtherChordIsLeftAlone(Key key, ModifierKeys mods)
    {
        // The control for all of the above, and the one that keeps the box
        // usable: a handler that took a bare B is a text box you cannot type
        // in, and one that took Ctrl+C is a box you cannot copy out of.
        WindowHarness.Run(_ =>
        {
            var box = Box("say this now");
            box.Focus();
            WindowHarness.Pump();

            _out.WriteLine($"{mods}+{key}");
            Assert.False(box.ApplyShortcut(key, mods));
            Assert.Equal("say this now", box.Text);
        });
    }

    [Fact]
    public void WpfsOwnBoldCannotGetIntoTheLine()
    {
        // The reason the chords are taken at all. A RichTextBox answers Ctrl+B
        // itself by applying WPF's bold to the DOCUMENT - which is not markup,
        // is not in Text, and is wiped the next time the line is drawn. It
        // looks like it worked right up until the pack is built.
        WindowHarness.Run(_ =>
        {
            var box = Box("say this now");
            box.Focus();
            box.Select(4, 4);
            WindowHarness.Pump();

            System.Windows.Documents.EditingCommands.ToggleBold.Execute(null, box);
            WindowHarness.Pump();

            // Nothing reached the line...
            Assert.Equal("say this now", box.Text);

            // ...and nothing survives a redraw either, so what is on screen and
            // what is in the pack still say the same thing.
            box.Text = "say this now!";
            WindowHarness.Pump();

            var runs = ((Paragraph)box.Document.Blocks.FirstBlock).Inlines.OfType<Run>();
            foreach (var run in runs)
                _out.WriteLine($"   '{run.Text}' weight={run.FontWeight}");
            Assert.All(runs, r => Assert.Equal(FontWeights.Normal, r.FontWeight));
        });
    }

    // ── The buttons, in the real window ──────────────────────────────

    [Fact]
    public void TheButtonsSitOverTheTextBoxAndCannotTakeItsSelection()
    {
        WindowHarness.Run(window =>
        {
            var box = (MarkupTextBox)window.FindName("NodeTextBox");
            var bar = (Panel)window.FindName("NodeTextToolbar");
            Assert.NotNull(box);
            Assert.NotNull(bar);

            var buttons = bar.Children.OfType<Button>().ToList();
            _out.WriteLine($"{buttons.Count} button(s) over the line");
            Assert.Equal(4, buttons.Count);

            // Not focusable, every one of them: see the class doc.
            foreach (var button in buttons)
                Assert.False(button.Focusable, "a formatting button can take focus");
        });
    }

    [Fact]
    public void ClickingABoldButtonWrapsWhatIsSelectedInTheEditor()
    {
        // End to end, through the window the editor actually builds: the
        // button, its handler, the box's selection and the binding back to the
        // node. Any of the five could be wired wrong on its own.
        WindowHarness.Run(window =>
        {
            var vm = WithALine(window);
            var box = (MarkupTextBox)window.FindName("NodeTextBox");
            var button = (Button)window.FindName("MarkupBoldButton");

            box.Focus();
            box.Select(0, 7);                       // "Finally"
            WindowHarness.Pump();

            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives
                                                        .ButtonBase.ClickEvent));
            WindowHarness.Pump();

            _out.WriteLine($"box '{box.Text}'; node '{vm.SelectedNode!.Text}'");
            Assert.Equal("<b>Finally</b>. There you are.", box.Text);
            Assert.Equal("<b>Finally</b>. There you are.", vm.SelectedNode.Text);
        });
    }

    /// <summary>A conversation with one selected line in it, on screen.</summary>
    private static MainViewModel WithALine(MainWindow window)
    {
        var vm = (MainViewModel)window.DataContext;

        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues")
            { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();

        vm.AddDialogueCommand.Execute(null);
        WindowHarness.Pump();
        if (vm.SelectedDialogue!.Nodes.Count == 0)
        {
            vm.AddDialogueRootNodeCommand.Execute(null);
            WindowHarness.Pump();
        }

        vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
        vm.SelectedNode.Text = "Finally. There you are.";
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
        return vm;
    }
}
