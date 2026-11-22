using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SMSModForge.Services;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The controls' own small movements (View/Motions, 1.10.0), measured from what is drawn, in Classic (Light) and EX
/// (Neon): a button gives under the pointer - EX further - and comes back; a tick comes on from clear - EX's also from
/// small; a tree item's arrow turns rather than jumps; the open tab's bar grows out; the issue count swells when it
/// goes up - EX's further. Controls: a tick or an arrow changed on a control nobody is using (a row a list recycled
/// as it scrolled) is simply there, and with Options ▸ Animations off nothing moves at all.
/// Every movement runs four times slower here (<see cref="Motions.SlowForTests"/>), so a frame that takes as long as a
/// tick's fade does not swallow it.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
[Trait("Timing", "Alone")]   // reads movements against the clock: beside eleven other testers it measured their load
public sealed class ControlMotionsTests
{
    private readonly ITestOutputHelper _out;
    public ControlMotionsTests(ITestOutputHelper o) => _out = o;

    private static ThemeDef Named(string name) => ThemeManager.All.First(t => t.Name == name);

    private static readonly MethodInfo SetIsPressed =
        typeof(ButtonBase).GetMethod("SetIsPressed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private sealed class Bench
    {
        public Window Window = null!;
        public Button Button = null!;
        public CheckBox Used = null!, Unused = null!;
        public TreeViewItem Item = null!, Other = null!;
        public TabControl Tabs = null!;
    }

    private static Bench Build()
    {
        var b = new Bench();
        b.Button = new Button { Content = "Press me", Width = 120 };
        b.Used = new CheckBox { Content = "Used" };
        b.Unused = new CheckBox { Content = "Unused" };
        b.Item = new TreeViewItem { Header = "Item" };
        b.Item.Items.Add(new TreeViewItem { Header = "Child" });
        b.Other = new TreeViewItem { Header = "Other" };
        b.Other.Items.Add(new TreeViewItem { Header = "Child" });
        var tree = new TreeView();
        tree.Items.Add(b.Item);
        tree.Items.Add(b.Other);
        b.Tabs = new TabControl { TabStripPlacement = Dock.Left };   // EX's rail: where its badge shows
        b.Tabs.Items.Add(new TabItem { Header = "One", Content = new TextBlock { Text = "1" } });
        b.Tabs.Items.Add(new TabItem { Header = "Two", Content = new TextBlock { Text = "2" } });
        var panel = new StackPanel();
        foreach (UIElement e in new UIElement[] { b.Button, b.Used, b.Unused, tree, b.Tabs }) panel.Children.Add(e);
        b.Window = new Window { Width = 400, Height = 400, Left = -10000, Top = -10000, ShowInTaskbar = false, Content = panel };
        b.Window.Show();
        b.Window.Activate();
        WindowHarness.Pump();
        return b;
    }

    /// <summary>
    /// A list row a person chooses takes its colour in - the theme's own brush back on it once done, so what stays is
    /// what a contrast check reads - and a row chosen with nobody using it (a list scrolled, an undo going to it) is
    /// simply coloured. Panels that replace each other (the mask studio and the tabs, Motion.Slide) slide across:
    /// Classic a third of the way while fading, EX the whole width without - measured from where each is drawn. The
    /// one going out is put away once gone, takes no clicks meanwhile, and their parent clips only while they move.
    /// </summary>
    [Fact]
    public void AChosenRowTakesItsColourIn_AndPanelsSlideAcrossEachLooksOwnWay()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _out.WriteLine("Windows' own Animation effects are off on this machine: nothing moves, as it should - nothing to hold");
            return;
        }
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var before = ThemeManager.Current;
            bool was = EditorPrefs.Animations;
            var slid = new System.Collections.Generic.Dictionary<string, (double Start, double Faded)>();
            Motion.ForTests = true;
            Motions.SlowForTests = Slow;
            vm.Animations = true;
            try
            {
                foreach (var look in new[] { "Light", "Neon" })
                {
                    ThemeManager.Apply(Named(look));
                    WindowHarness.Pump();
                    var list = new ListBox { ItemsSource = new[] { "One", "Two", "Three" } };
                    var going = new Border { Background = Brushes.SteelBlue, Height = 120 };
                    var coming = new Border { Background = Brushes.Tomato, Height = 120, Visibility = Visibility.Collapsed };
                    var stage = new Grid { Width = 600 };
                    stage.Children.Add(going);
                    stage.Children.Add(coming);
                    var panel = new StackPanel();
                    panel.Children.Add(list);
                    panel.Children.Add(stage);
                    var bench = new Window { Width = 700, Height = 400, Left = -10000, Top = -10000, ShowInTaskbar = false, Content = panel };
                    bench.Show();
                    bench.Activate();
                    WindowHarness.Pump();
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(400));   // the window's first frames drawn before anything is timed
                    try
                    {
                        // The row a person chooses: its colour comes in.
                        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
                        Assert.True(row.Focus() && row.IsKeyboardFocusWithin, "the row could not take the keyboard");
                        var bd = Part<Border>(row, "bd");
                        list.SelectedIndex = 1;
                        double alpha = Over(T(90), () => (bd.Background as SolidColorBrush)?.Color.A ?? 255, lowest: true);
                        WindowHarness.Wait(T(400));
                        var theme = (SolidColorBrush)Application.Current.Resources["Theme.Selection"];
                        _out.WriteLine($"{look}: the chosen row's colour was seen at {alpha:0} of 255 on its way in");
                        Assert.True(alpha < 200, "the row's colour did not come in");
                        Assert.Equal(DependencyProperty.UnsetValue, bd.ReadLocalValue(Border.BackgroundProperty));
                        Assert.Equal(theme.Color, ((SolidColorBrush)bd.Background).Color);
                        // Control: a row chosen while nobody uses it is simply coloured.
                        var other = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(2);
                        var otherBd = Part<Border>(other, "bd");
                        list.SelectedIndex = 2;
                        Assert.Equal(255, Over(T(90), () => (otherBd.Background as SolidColorBrush)?.Color.A ?? 255, lowest: true), 0);

                        // Panels replacing each other slide across.
                        coming.Visibility = Visibility.Visible;
                        bool gone = false;
                        Motion.Slide(coming, going, fromRight: true, () => { gone = true; going.Visibility = Visibility.Collapsed; });
                        Assert.True(stage.ClipToBounds, "the panels' parent does not clip while they move");
                        Assert.False(going.IsHitTestVisible, "the panel going out still takes clicks");
                        double start = 0, faded = 1;
                        var until = DateTime.UtcNow + T(60);
                        while (DateTime.UtcNow < until)
                        {
                            WindowHarness.Wait(TimeSpan.FromMilliseconds(8));
                            start = Math.Max(start, coming.TransformToAncestor(stage).Transform(new Point()).X);
                            faded = Math.Min(faded, coming.Opacity);
                        }
                        WindowHarness.Wait(T(450));
                        slid[look] = (start / stage.ActualWidth, faded);
                        _out.WriteLine($"{look}: the incoming panel was seen {start / stage.ActualWidth:0.00} of the width across, at opacity {faded:0.00}");
                        Assert.True(gone, "the panel going out was never put away");
                        Assert.Equal(Visibility.Collapsed, going.Visibility);
                        Assert.Equal(0, coming.TransformToAncestor(stage).Transform(new Point()).X, 1);
                        Assert.Equal(1.0, coming.Opacity);
                        Assert.False(stage.ClipToBounds, "the parent still clips after the move");
                        Assert.True(going.IsHitTestVisible, "the panel that went out takes no clicks any more");
                    }
                    finally { bench.Close(); }
                }
                // Classic: a third of the way, fading; EX: the whole width, no fade.
                Assert.InRange(slid["Light"].Start, 0.15, 0.34);
                Assert.True(slid["Light"].Faded < 0.7, "Classic's panel did not fade in");
                Assert.True(slid["Neon"].Start > 0.6, "EX's panel did not come from across the width");
                Assert.Equal(1.0, slid["Neon"].Faded);

                // Control: with Options ▸ Animations off, the panel going out is put away at once.
                vm.Animations = false;
                var a = new Border(); var b = new Border();
                bool now = false;
                Motion.Slide(a, b, fromRight: true, () => now = true);
                Assert.True(now);
                Assert.True(a.RenderTransform == null || a.RenderTransform.Value.IsIdentity);
            }
            finally
            {
                Motion.ForTests = false;
                Motions.SlowForTests = 1;
                vm.Animations = was;
                ThemeManager.Apply(before);
                WindowHarness.Pump();
            }
        });
    }

    /// <summary>How big a part is drawn against its own size: below 1 while it gives.</summary>
    private static double Drawn(UIElement part, Visual root)
    {
        var drawn = part.TransformToAncestor(root).TransformBounds(new Rect(part.RenderSize));
        return Math.Min(drawn.Width / part.RenderSize.Width, drawn.Height / part.RenderSize.Height);
    }

    /// <summary>The furthest a reading goes over a stretch of real time, the dispatcher turning.</summary>
    private static double Over(TimeSpan time, Func<double> read, bool lowest)
    {
        double best = read();
        var until = DateTime.UtcNow + time;
        while (DateTime.UtcNow < until)
        {
            WindowHarness.Wait(TimeSpan.FromMilliseconds(8));
            best = lowest ? Math.Min(best, read()) : Math.Max(best, read());
        }
        return best;
    }

    private const double Slow = 4;
    private static TimeSpan T(double ms) => TimeSpan.FromMilliseconds(ms * Slow);

    private static T Part<T>(Control c, string name) where T : class => (T)c.Template.FindName(name, c);

    [Fact]
    public void ControlsMoveInEachLooksOwnWay_AndNothingMovesWhatNobodyUsedOrWithAnimationsOff()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _out.WriteLine("Windows' own Animation effects are off on this machine: nothing moves, as it should - nothing to hold");
            return;
        }
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var before = ThemeManager.Current;
            bool was = EditorPrefs.Animations;
            var pressed = new System.Collections.Generic.Dictionary<string, double>();
            var swelled = new System.Collections.Generic.Dictionary<string, double>();
            Motion.ForTests = true;
            Motions.SlowForTests = Slow;
            vm.Animations = true;
            try
            {
                foreach (var look in new[] { "Light", "Neon" })
                {
                    ThemeManager.Apply(Named(look));
                    WindowHarness.Pump();
                    var b = Build();
                    try
                    {
                        // A button gives under the pointer and comes back.
                        var face = (UIElement)VisualTreeHelper.GetChild(b.Button, 0);
                        Assert.Equal(1.0, Drawn(face, b.Window), 3);
                        SetIsPressed.Invoke(b.Button, new object[] { true });
                        pressed[look] = Over(T(300), () => Drawn(face, b.Window), lowest: true);
                        SetIsPressed.Invoke(b.Button, new object[] { false });
                        WindowHarness.Wait(T(450));
                        _out.WriteLine($"{look}: a 120 px button pressed is drawn at {pressed[look]:0.000} of its size, back at {Drawn(face, b.Window):0.000}");
                        Assert.InRange(pressed[look], 0.94, 0.995);
                        Assert.Equal(1.0, Drawn(face, b.Window), 2);

                        // A tick comes on from clear, on the box being used...
                        Assert.True(b.Used.Focus() && b.Used.IsKeyboardFocused, "the tick box could not take the keyboard");
                        var mark = Part<UIElement>(b.Used, "mark");
                        // A frame that takes longer than the movement (the first after a look is put on, here) shows its end:
                        // the window's first frames drawn before anything is timed.
                        WindowHarness.Wait(TimeSpan.FromMilliseconds(400));
                        b.Used.IsChecked = true;
                        // An animation's clock starts at the next frame: what is drawn over its first moments.
                        double markScale = 1;
                        double start = Over(T(60), () =>
                        {
                            markScale = Math.Min(markScale, (mark.RenderTransform as ScaleTransform)?.ScaleX ?? 1);
                            return mark.Opacity;
                        }, lowest: true);
                        _out.WriteLine($"{look}: the tick starts at opacity {start:0.00}, scale {markScale:0.00}");
                        Assert.True(start < 0.5, "the tick did not come in");
                        if (look == "Neon") Assert.True(markScale < 0.5, "EX's tick did not spring up from small");
                        else Assert.Equal(1.0, markScale);
                        WindowHarness.Wait(T(350));
                        Assert.Equal(1.0, mark.Opacity);
                        // ...and is simply there on one nobody is using.
                        b.Window.Focus();
                        var otherMark = Part<UIElement>(b.Unused, "mark");
                        b.Unused.IsChecked = true;
                        Assert.Equal(1.0, otherMark.Opacity);

                        // A tree item's arrow turns, on the item being used; on another it is simply turned.
                        Assert.True(b.Item.Focus() && b.Item.IsKeyboardFocused, "the tree item could not take the keyboard");
                        var arrow = Part<UIElement>(Part<ToggleButton>(b.Item, "Expander"), "arrow");
                        b.Item.IsExpanded = true;
                        double angle = Over(T(40), () => ((RotateTransform)arrow.RenderTransform).Angle, lowest: true);
                        _out.WriteLine($"{look}: the arrow was seen at {angle:0} degrees on its way to 90");
                        Assert.True(angle < 80, "the arrow jumped rather than turned");
                        WindowHarness.Wait(T(400));
                        Assert.Equal(90, ((RotateTransform)arrow.RenderTransform).Angle, 1);
                        b.Window.Focus();
                        var otherArrow = Part<UIElement>(Part<ToggleButton>(b.Other, "Expander"), "arrow");
                        b.Other.IsExpanded = true;
                        Assert.Equal(90, ((RotateTransform)otherArrow.RenderTransform).Angle, 1);

                        // The open tab's bar grows out.
                        var second = (TabItem)b.Tabs.Items[1];
                        b.Tabs.SelectedIndex = 1;
                        var bar = Part<UIElement>(second, "bar");
                        double grown = Over(T(40), () => Drawn(bar, b.Window), lowest: true);
                        Assert.True(grown < 0.5, $"the tab's bar did not grow out ({grown:0.00})");
                        WindowHarness.Wait(T(450));
                        Assert.Equal(1.0, Drawn(bar, b.Window), 2);

                        // The issue count swells as it goes up.
                        second.SetValue(TabIcon.BadgeProperty, 1);
                        WindowHarness.Pump();
                        var badge = Part<UIElement>(second, "badge");
                        Assert.True(badge.IsVisible, "the badge is not shown");
                        second.SetValue(TabIcon.BadgeProperty, 3);
                        swelled[look] = Over(T(450), () => Drawn(badge, b.Window), lowest: false);
                        _out.WriteLine($"{look}: the badge swelled to {swelled[look]:0.00}, back at {Drawn(badge, b.Window):0.00}");
                        Assert.True(swelled[look] > 1.05, "the badge did not swell");
                        Assert.Equal(1.0, Drawn(badge, b.Window), 2);
                        // Going down says nothing.
                        second.SetValue(TabIcon.BadgeProperty, 2);
                        Assert.Equal(1.0, Over(T(150), () => Drawn(badge, b.Window), lowest: false), 2);
                    }
                    finally { b.Window.Close(); }
                }

                // The looks move differently: EX gives further and swells further.
                Assert.True(pressed["Neon"] < pressed["Light"] - 0.005, $"EX pressed {pressed["Neon"]:0.000}, Classic {pressed["Light"]:0.000}");
                Assert.True(swelled["Neon"] > swelled["Light"] + 0.05, $"EX swelled {swelled["Neon"]:0.00}, Classic {swelled["Light"]:0.00}");

                // Control: Options ▸ Animations off - nothing moves.
                vm.Animations = false;
                var off = Build();
                try
                {
                    var face = (UIElement)VisualTreeHelper.GetChild(off.Button, 0);
                    SetIsPressed.Invoke(off.Button, new object[] { true });
                    Assert.Equal(1.0, Over(T(200), () => Drawn(face, off.Window), lowest: true), 3);
                    SetIsPressed.Invoke(off.Button, new object[] { false });
                    off.Used.Focus();
                    off.Used.IsChecked = true;
                    Assert.Equal(1.0, Part<UIElement>(off.Used, "mark").Opacity);
                    off.Item.Focus();
                    off.Item.IsExpanded = true;
                    var arrow = Part<UIElement>(Part<ToggleButton>(off.Item, "Expander"), "arrow");
                    Assert.Equal(90, ((RotateTransform)arrow.RenderTransform).Angle, 1);
                    off.Tabs.SelectedIndex = 1;
                    Assert.Equal(1.0, Drawn(Part<UIElement>((TabItem)off.Tabs.Items[1], "bar"), off.Window), 3);
                }
                finally { off.Window.Close(); }
            }
            finally
            {
                Motion.ForTests = false;
                Motions.SlowForTests = 1;
                vm.Animations = was;
                ThemeManager.Apply(before);
                WindowHarness.Pump();
            }
        });
    }
}
