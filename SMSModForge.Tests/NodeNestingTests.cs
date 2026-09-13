using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What a node is inside, and the switch between the two ways of showing a row.
/// <para/>
/// A conversation is a tree shown as a flat list, and depth used to be said
/// only by how far in a row started. That reads at one level and stops reading
/// at three: two rows indented differently are related SOMEHOW, and a row
/// halfway down a long branch is anyone's guess. A line per ancestor answers
/// it.
/// <para/>
/// Measured off what is DRAWN. A guide control that is in the tree, sized, and
/// painting nothing looks exactly like one that is working, from every angle
/// except the only one that matters.
/// </summary>
public sealed class NodeNestingTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly bool _was = SMSModForge.Services.EditorPrefs.GameLookNodeRows;

    public NodeNestingTests(ITestOutputHelper o) => _out = o;

    /// <summary>Somebody is running this on their own machine.</summary>
    public void Dispose() => SMSModForge.Services.EditorPrefs.GameLookNodeRows = _was;

    // ── The guides themselves ────────────────────────────────────────

    /// <summary>A rail on a window, its own size, with a colour that shows.</summary>
    private static NodeDepthRail Rail(int depth)
    {
        var rail = new NodeDepthRail
        {
            Depth = depth,
            Height = 24,
            Rail = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = new Window
        {
            Width = 300, Height = 120, Left = -10000, Top = -10000,
            ShowInTaskbar = false, Background = Brushes.Black, Content = rail,
        };
        window.Show();
        window.UpdateLayout();
        WindowHarness.Pump();
        return rail;
    }

    /// <summary>How many separate vertical lines the rail drew, counted as runs
    /// of lit columns rather than lit pixels — a line is one guide however many
    /// pixels wide the display makes it.</summary>
    private static int Guides(NodeDepthRail rail, out int width)
    {
        width = Math.Max(1, (int)Math.Ceiling(rail.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(rail.ActualHeight));

        var render = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        render.Render(rail);

        var pixels = new byte[width * height * 4];
        render.CopyPixels(pixels, width * 4, 0);

        int lines = 0;
        bool inside = false;
        for (int x = 0; x < width; x++)
        {
            bool lit = false;
            for (int y = 0; y < height && !lit; y++)
                if (pixels[(y * width + x) * 4 + 3] > 40) lit = true;

            if (lit && !inside) lines++;
            inside = lit;
        }
        return lines;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void ANodeGetsOneGuidePerNodeItIsInside(int depth)
    {
        WindowHarness.Run(host =>
        {
            var rail = Rail(depth);
            int lines = Guides(rail, out int width);

            _out.WriteLine($"depth {depth}: {lines} guide(s) across {width}px");
            Assert.Equal(depth, lines);
        });
    }

    [Fact]
    public void ARootGetsNoneAtAll()
    {
        // The control. A rail that always drew something would make every row
        // look nested, which says less than saying nothing.
        WindowHarness.Run(host =>
        {
            var rail = Rail(0);
            Assert.Equal(0, rail.DesiredSize.Width);
            Assert.Equal(0, Guides(rail, out _));
            _out.WriteLine("a root draws nothing and takes no room");
        });
    }

    [Fact]
    public void TheGuidesTakeExactlyTheRoomTheIndentUsedTo()
    {
        // Two numbers meaning one thing. The guides are drawn in space the row
        // was already leaving blank, so if they disagree with the view model's
        // indent the rows step one way and their guides the other.
        WindowHarness.Run(host =>
        {
            var node = new DialogueNodeViewModel(new SMSModForge.Model.DialogueNodeDef { Id = 1 });
            var rail = new NodeDepthRail();

            for (int depth = 0; depth <= 5; depth++)
            {
                node.Depth = depth;
                rail.Depth = depth;
                rail.InvalidateMeasure();
                rail.Measure(new Size(1000, 1000));

                _out.WriteLine($"depth {depth}: indent {node.IndentMargin.Left}, "
                               + $"guides {rail.DesiredSize.Width}");
                Assert.Equal(node.IndentMargin.Left, rail.DesiredSize.Width);
            }
        });
    }

    [Fact]
    public void TheNearestGuideIsDrawnStrongerThanTheOnesFurtherOut()
    {
        // A row hangs off ONE node; the rest are branches it happens to be
        // under. Drawn all the same, four levels deep reads as a barcode.
        WindowHarness.Run(host =>
        {
            var rail = Rail(3);
            int width = Math.Max(1, (int)Math.Ceiling(rail.ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(rail.ActualHeight));

            var render = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            render.Render(rail);
            var pixels = new byte[width * height * 4];
            render.CopyPixels(pixels, width * 4, 0);

            // Read at the column each guide is drawn down, rather than
            // wherever the darkest pixel happens to be: the parent's guide also
            // carries a short arm into the row, and the antialiased tip of that
            // arm is not the guide.
            int At(int level)
            {
                double centre = level * rail.Step + rail.Step / 2;
                int most = 0;
                for (int x = (int)centre - 1; x <= (int)centre + 1; x++)
                {
                    if (x < 0 || x >= width) continue;
                    for (int y = 0; y < height; y++)
                        most = Math.Max(most, pixels[(y * width + x) * 4 + 3]);
                }
                return most;
            }

            int parent = At(rail.Depth - 1);
            var ancestors = Enumerable.Range(0, rail.Depth - 1).Select(At).ToList();

            _out.WriteLine($"parent's guide {parent}, the ones further out "
                           + string.Join(", ", ancestors));

            Assert.True(parent > 200, $"the parent's own guide is only {parent}");
            Assert.All(ancestors, a => Assert.True(a > 40, $"an ancestor's guide is {a} - invisible"));
            Assert.True(parent > ancestors.Max() + 20,
                        $"the parent's guide is {parent} against {ancestors.Max()} for the rest");
        });
    }

    // ── Both row styles, in the real window ──────────────────────────

    private static void ShowDialogues(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues")
            { tabs.SelectedIndex = i; WindowHarness.Pump(); return; }

        throw new Xunit.Sdk.XunitException("no Dialogues tab");
    }

    private static void Collect<T>(DependencyObject root, List<T> into) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) into.Add(hit);
            Collect(child, into);
        }
    }

    private static List<T> In<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();
        Collect(root, found);
        return found;
    }

    /// <summary>A conversation with a child hanging off a root, on screen.</summary>
    private static MainViewModel WithAChild(MainWindow window)
    {
        var vm = (MainViewModel)window.DataContext;
        ShowDialogues(window);

        vm.AddDialogueCommand.Execute(null);
        WindowHarness.Pump();
        if (vm.SelectedDialogue!.Nodes.Count == 0)
        {
            vm.AddDialogueRootNodeCommand.Execute(null);
            WindowHarness.Pump();
        }

        vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
        vm.SelectedNode.Text = "Finally. There you are.";
        vm.AddDialogueChildNodeCommand.Execute(null);
        WindowHarness.Pump();

        window.UpdateLayout();
        WindowHarness.Pump();
        return vm;
    }

    [Fact]
    public void BothKindsOfRowShowTheNesting()
    {
        // One control, two palettes. The plain rows follow the theme; the
        // game-look rows do not, because they sit on a fixed dark panel that
        // the theme knows nothing about.
        WindowHarness.Run(window =>
        {
            var vm = WithAChild(window);

            foreach (bool gameLook in new[] { false, true })
            {
                vm.GameLookNodeRows = gameLook;
                WindowHarness.Pump();
                window.UpdateLayout();
                WindowHarness.Pump();

                var rails = In<NodeDepthRail>(window).Where(r => r.IsVisible).ToList();
                var nested = rails.Where(r => r.Depth > 0).ToList();

                _out.WriteLine($"{(gameLook ? "game-look" : "plain")}: {rails.Count} rail(s), "
                               + $"{nested.Count} of them nested, "
                               + $"colour {nested.FirstOrDefault()?.Rail?.ToString() ?? "-"}");

                Assert.NotEmpty(nested);
                foreach (var rail in nested)
                {
                    Assert.NotNull(rail.Rail);
                    Assert.True(rail.ActualWidth > 0, "a nested row left no room for its guides");

                    if (gameLook) Assert.Same(DialogueLinePreview.Rail, rail.Rail);
                    else Assert.NotSame(DialogueLinePreview.Rail, rail.Rail);
                }
            }
        });
    }

    // ── The switch ───────────────────────────────────────────────────

    [Fact]
    public void TheRootIdsAreNoLongerPrinted()
    {
        // They were internal numbers nobody acts on, and the roots are the rows
        // at the left edge of the list anyway.
        WindowHarness.Run(window =>
        {
            WithAChild(window);

            var printed = In<TextBlock>(window)
                .Where(t => t.IsVisible && t.Text.StartsWith("Roots:", StringComparison.Ordinal))
                .ToList();

            _out.WriteLine($"{printed.Count} block(s) still printing root ids");
            Assert.Empty(printed);
        });
    }

    [Fact]
    public void TheSwitchIsWhereTheRootIdsWere()
    {
        WindowHarness.Run(window =>
        {
            WithAChild(window);

            var toggle = (ToggleButton)window.FindName("GameLookSwitch");
            Assert.NotNull(toggle);
            Assert.True(toggle.IsVisible, "the switch is not on screen");

            // A switch, not a tick box: it carries its own template with a
            // track and a thumb, which a CheckBox does not.
            toggle.ApplyTemplate();
            _out.WriteLine($"template parts: {toggle.Template.FindName("Track", toggle)}, "
                           + $"{toggle.Template.FindName("Thumb", toggle)}");

            Assert.IsNotType<CheckBox>(toggle);
            Assert.NotNull(toggle.Template.FindName("Track", toggle));
            Assert.NotNull(toggle.Template.FindName("Thumb", toggle));
        });
    }

    [Fact]
    public void FlippingTheSwitchChangesTheRows()
    {
        WindowHarness.Run(window =>
        {
            var vm = WithAChild(window);
            var toggle = (ToggleButton)window.FindName("GameLookSwitch");

            vm.GameLookNodeRows = false;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();
            Assert.False(toggle.IsChecked);
            Assert.DoesNotContain(In<DialogueLinePreview>(window), p => p.IsVisible);

            toggle.IsChecked = true;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var fancy = In<DialogueLinePreview>(window).Where(p => p.IsVisible).ToList();
            _out.WriteLine($"after flipping it: {fancy.Count} game-look row(s), "
                           + $"setting is {vm.GameLookNodeRows}");

            Assert.True(vm.GameLookNodeRows, "the switch did not reach the setting");
            Assert.NotEmpty(fancy);
        });
    }

    [Fact]
    public void TheThumbMovesWhenItIsOn()
    {
        // What makes it read as a switch rather than as a button that happens
        // to change colour.
        WindowHarness.Run(window =>
        {
            WithAChild(window);
            var toggle = (ToggleButton)window.FindName("GameLookSwitch");

            double Thumb()
            {
                window.UpdateLayout();
                WindowHarness.Pump();
                var thumb = (FrameworkElement)toggle.Template.FindName("Thumb", toggle);
                return thumb.TranslatePoint(new Point(0, 0), toggle).X;
            }

            toggle.IsChecked = false;
            double off = Thumb();
            toggle.IsChecked = true;
            double on = Thumb();

            _out.WriteLine($"thumb at x{off:0.#} off, x{on:0.#} on");
            Assert.True(on > off + 5, $"the thumb sits at x{off:0.#} either way");
        });
    }
}
