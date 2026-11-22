using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SMSModForge.Tutorials;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// Draws the running tutorial over the editor: the dim with a hole cut where the step's control is, and the ring
/// round it. The step's words and buttons are in the coach docked beside the editor (<see cref="TutorialCoach"/>,
/// 1.10.0), which this keeps clear of.
/// <para/>
/// The one job worth describing is keeping the hole on the control. A control moves for all sorts of reasons that
/// raise no event of their own — a splitter dragged, a list scrolled, a panel expanded — so the position is
/// re-measured on <see cref="FrameworkElement.LayoutUpdated"/>, which fires for all of them. That is a hot event,
/// so the work is skipped whenever the rectangle has not actually changed, which is almost always.
/// <para/>
/// Everything is measured with <c>TransformToVisual</c>, so the numbers are device-independent units. Display
/// scaling and resolution therefore need no handling at all.
/// </summary>
public partial class TutorialOverlay : UserControl
{
    private TutorialRunner? _runner;
    private Window? _window;

    /// <summary>The step's primary control — the one that gets the ring and the scroll-into-view.</summary>
    private FrameworkElement? _anchor;

    /// <summary>Everything else the step lights, from <see cref="TutorialStep.AlsoAllow"/>.</summary>
    private readonly List<FrameworkElement> _alsoAllowed = new();

    /// <summary>The step showing, so what it lights can be looked for again (<see cref="LookAgain"/>).</summary>
    private TutorialStep? _step;

    /// <summary>A quarter second, as often as the runner checks a step.</summary>
    private readonly DispatcherTimer _lookAgain = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private List<Rect> _lastHoles = new();
    private Size _lastWindow = Size.Empty;
    private bool _forceRefresh;

    /// <summary>Switches to the tab a step names, before its control is looked for. Supplied by MainWindow.</summary>
    public Action<int>? SwitchTab { get; set; }

    /// <summary>Flashes a control that appears in the middle of a step (MainWindow's "look here" flash).</summary>
    public Action<UIElement?>? Flash { get; set; }

    public TutorialOverlay()
    {
        InitializeComponent();
        _lookAgain.Tick += (_, _) => LookAgain();
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);
            if (_window != null)
            {
                _window.SizeChanged += (_, _) => Refresh();
                _window.DpiChanged += (_, _) => Refresh();
            }
            LayoutUpdated += (_, _) => Refresh();
        };
    }

    /// <summary>Binds the overlay to the runner it should draw.</summary>
    public void Attach(TutorialRunner runner)
    {
        _runner = runner;
        runner.StepChanged += OnStepChanged;
        runner.Ended += OnEnded;
    }

    private void OnEnded()
    {
        Visibility = Visibility.Collapsed;
        _lookAgain.Stop();
        _step = null;
        _anchor = null;
        _alsoAllowed.Clear();
        _lastHoles = new List<Rect>();
    }

    private void OnStepChanged(TutorialStep step)
    {
        // The ring glides from the last step's control to this one's (1.10.0); the same step shown again (done for
        // the reader) stays still.
        bool newStep = !ReferenceEquals(step, _step) || Visibility != Visibility.Visible;
        Visibility = Visibility.Visible;
        if (newStep) _glideRing = true;
        _step = step;
        _lookAgain.Start();

        // Get onto the right tab first, or the control the step points at has not been rendered and cannot be found.
        if (step.Tab >= 0) SwitchTab?.Invoke(step.Tab);

        // Then let that tab lay out before measuring.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _anchor = step.Anchor.Length > 0 && _window != null ? TutorialAnchor.Find(_window, step.Anchor) : null;
            _alsoAllowed.Clear();
            if (_window != null)
                foreach (var id in step.AlsoAllow)
                    if (!string.IsNullOrEmpty(id) && TutorialAnchor.Find(_window, id) is { } extra) _alsoAllowed.Add(extra);
            _anchor?.BringIntoView();
            _forceRefresh = true;
            Refresh();
        }), DispatcherPriority.Background);

        // And again once the step has picked what it shows (TutorialRunner runs OnShown when the window is idle,
        // after this): picking another conversation kept the page's scroll, and the ringed Identity group of the
        // one picked was above the view (found by the review of every tutorial step, 1.10.0).
        if (step.OnShown != null)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(_step, step) || _window == null) return;
                if (step.Anchor.Length > 0 && TutorialAnchor.Find(_window, step.Anchor) is { } anchor)
                {
                    _anchor = anchor;
                    anchor.BringIntoView();
                }
                _forceRefresh = true;
                Refresh();
            }), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Whether a lit control is off the screen: never found, hidden, or thrown away as its template was
    /// rebuilt.</summary>
    private static bool Gone(FrameworkElement? el)
        => el is not { IsVisible: true } || el.ActualWidth <= 0 || el.ActualHeight <= 0
           || PresentationSource.FromVisual(el) == null;

    /// <summary>
    /// Look again for what the step lights and is not on screen: a line's editor appears only once a line is
    /// chosen, and found only at the step's start it stayed dimmed when it did appear (1.8.0). Finding walks the
    /// whole window, so it is done only while something is missing, at the runner's own pace.
    /// </summary>
    private void LookAgain()
    {
        if (_step == null || _window == null || _runner is not { IsRunning: true }) return;
        bool changed = false;

        if (_step.Anchor.Length > 0 && Gone(_anchor)
            && TutorialAnchor.Find(_window, _step.Anchor) is { } found && !ReferenceEquals(found, _anchor))
        {
            _anchor = found;
            changed = true;
            // It has arrived: shown and flashed - a control lower down a long panel is otherwise lit out of sight.
            found.BringIntoView();
            Flash?.Invoke(found);
        }

        var wanted = _step.AlsoAllow.Where(id => !string.IsNullOrEmpty(id)).ToList();
        if (_alsoAllowed.Count < wanted.Count || _alsoAllowed.Any(Gone))
        {
            var now = wanted.Select(id => TutorialAnchor.Find(_window, id)).OfType<FrameworkElement>().ToList();
            if (!now.SequenceEqual(_alsoAllowed))
            {
                _alsoAllowed.Clear();
                _alsoAllowed.AddRange(now);
                changed = true;
            }
        }

        if (!changed) return;
        _forceRefresh = true;
        Refresh();
    }

    /// <summary>Re-measures and redraws, cheaply skipping when nothing moved.</summary>
    private void Refresh()
    {
        if (_runner is not { IsRunning: true } || _window == null) return;

        var size = new Size(ActualWidth, ActualHeight);
        if (size.Width <= 0 || size.Height <= 0) return;

        Rect hole = MeasureAnchor(_anchor);
        var holes = new List<Rect>();
        if (!hole.IsEmpty) holes.Add(hole);
        foreach (var extra in _alsoAllowed)
        {
            var r = MeasureAnchor(extra);
            if (!r.IsEmpty) holes.Add(r);
        }

        if (!_forceRefresh && SameHoles(holes, _lastHoles) && size == _lastWindow) return;
        _forceRefresh = false;
        _lastHoles = holes;
        _lastWindow = size;

        Dim.Data = SpotlightGeometry.BuildMask(size, holes);

        var spot = SpotlightGeometry.SpotlightRect(size, hole);
        if (spot.IsEmpty || spot.Width <= 0)
        {
            Ring.Visibility = Visibility.Collapsed;
            return;
        }
        bool glide = _glideRing && Ring.Visibility == Visibility.Visible && Services.Motion.Enabled;
        _glideRing = false;
        var from = new Rect(Ring.Margin.Left, Ring.Margin.Top, double.IsNaN(Ring.Width) ? 0 : Ring.Width, double.IsNaN(Ring.Height) ? 0 : Ring.Height);
        Ring.Visibility = Visibility.Visible;
        Ring.Width = spot.Width;
        Ring.Height = spot.Height;
        Ring.Margin = new Thickness(spot.X, spot.Y, 0, 0);
        if (glide && from.Width > 0 && from != spot) Glide(from, spot);
    }

    /// <summary>
    /// Where a lit control sits, in this control's own coordinates: only the part of it that is on screen. A panel
    /// scrolled half out of view was lit whole, and its ring and bright area ran up over the tabs and off the
    /// window's edge (found by the review of every step's picture, 1.10.0). <see cref="Rect.Empty"/> when it is not
    /// on screen to be measured.
    /// </summary>
    private Rect MeasureAnchor(FrameworkElement? el)
    {
        if (el is not { IsVisible: true } || el.ActualWidth <= 0 || el.ActualHeight <= 0)
            return Rect.Empty;
        try
        {
            var origin = el.TransformToVisual(this).Transform(new Point(0, 0));
            var slot = new Rect(origin, new Size(el.ActualWidth, el.ActualHeight));
            // Cut to what each scrolling viewport it sits in shows.
            for (DependencyObject? p = VisualTreeHelper.GetParent(el); p != null && p != _window; p = VisualTreeHelper.GetParent(p))
            {
                if (p is not ScrollContentPresenter view || view.ActualWidth <= 0 || view.ActualHeight <= 0) continue;
                var at = view.TransformToVisual(this).Transform(new Point(0, 0));
                slot.Intersect(new Rect(at, new Size(view.ActualWidth, view.ActualHeight)));
                if (slot.IsEmpty) return Rect.Empty;
            }
            // Quantised, because an animated control (the previews) shifts by fractions of a pixel every frame.
            return SpotlightGeometry.Quantize(slot);
        }
        catch (InvalidOperationException)
        {
            // Left the tree between layout passes (a tab switch mid-step): the next pass picks it up.
            return Rect.Empty;
        }
    }

    /// <summary>The lit areas now, in this control's coordinates - what a click can reach. For tests
    /// (TutorialReachTests: every control a step's words name is one of them).</summary>
    internal IReadOnlyList<Rect> Holes => _lastHoles;

    private static bool SameHoles(List<Rect> a, List<Rect> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>
    /// Let the wheel through the dimmed area. The dim swallows clicks on purpose, but swallowing SCROLLING stranded
    /// people: a reader scrolled past the control a step wants could not scroll back to it. Only the dimmed area is
    /// forwarded; over a lit hole the control gets the wheel itself.
    /// </summary>
    private void Overlay_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_window == null || e.Handled) return;

        var atWindow = e.GetPosition(this);
        foreach (var hole in _lastHoles)
            if (hole.Contains(atWindow))
                return;   // lit: the control underneath is already getting this

        bool wasHitTestable = IsHitTestVisible;
        IsHitTestVisible = false;
        var under = _window.InputHitTest(e.GetPosition(_window)) as UIElement;
        IsHitTestVisible = wasHitTestable;
        if (under == null) return;

        under.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = under,
        });
        e.Handled = true;
    }

    /// <summary>The next measuring moves the ring from where it was rather than jumping (a new step).</summary>
    private bool _glideRing;

    /// <summary>The ring from the last step's control to this one's, quickly (Services.Motion's time, twice).</summary>
    private void Glide(Rect from, Rect to)
    {
        var d = new Duration(Services.Motion.Duration + Services.Motion.Duration);
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var stop = System.Windows.Media.Animation.FillBehavior.Stop;
        Ring.BeginAnimation(MarginProperty, new System.Windows.Media.Animation.ThicknessAnimation(
            new Thickness(from.X, from.Y, 0, 0), new Thickness(to.X, to.Y, 0, 0), d) { EasingFunction = ease, FillBehavior = stop });
        Ring.BeginAnimation(WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(from.Width, to.Width, d) { EasingFunction = ease, FillBehavior = stop });
        Ring.BeginAnimation(HeightProperty, new System.Windows.Media.Animation.DoubleAnimation(from.Height, to.Height, d) { EasingFunction = ease, FillBehavior = stop });
    }
}
