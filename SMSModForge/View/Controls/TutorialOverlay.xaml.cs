using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SMSModForge.Tutorials;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// Draws the running tutorial over the whole window: the dim with a hole cut
/// where the step's control is, and the callout beside it.
/// <para/>
/// The one job worth describing is keeping the hole on the control. A control
/// moves for all sorts of reasons that raise no event of their own — a splitter
/// dragged, a list scrolled, a panel expanded — so the position is re-measured
/// on <see cref="FrameworkElement.LayoutUpdated"/>, which fires for all of
/// them. That is a hot event, so the work is skipped whenever the rectangle has
/// not actually changed, which is almost always.
/// <para/>
/// Everything is measured with <c>TransformToVisual</c>, so the numbers are
/// device-independent units. Display scaling and resolution therefore need no
/// handling at all: WPF has already applied them by the time a rectangle
/// arrives here, and a DPI change simply triggers another layout pass.
/// </summary>
public partial class TutorialOverlay : UserControl
{
    private TutorialRunner? _runner;
    private Window? _window;

    /// <summary>The step's primary control — the one that gets the ring, the
    /// arrival flash and the scroll-into-view.</summary>
    private FrameworkElement? _anchor;

    /// <summary>Everything else the step lights, from
    /// <see cref="TutorialStep.AlsoAllow"/>. Held separately from
    /// <see cref="_anchor"/> because only the primary drives the ring.</summary>
    private readonly List<FrameworkElement> _alsoAllowed = new();

    private List<Rect> _lastHoles = new();
    private Size _lastWindow = Size.Empty;
    private bool _forceRefresh;

    /// <summary>Corner the callout is currently using, kept across redraws so an
    /// animated anchor cannot make it hop. Reset per step.</summary>
    private int _corner = -1;

    /// <summary>Size the callout was last placed at, so a redraw can tell
    /// whether anything about it actually moved.</summary>
    private Size _lastCallout = Size.Empty;

    /// <summary>Switches to the tab a step names, before its control is looked
    /// for. Supplied by MainWindow, which owns the tab control.</summary>
    public Action<int>? SwitchTab { get; set; }

    /// <summary>Flashes the step's control on arrival. Supplied by MainWindow so
    /// the tutorial reuses the same highlight as jumping to a validation issue,
    /// rather than inventing a second visual language for "look here".</summary>
    public Action<UIElement?>? Flash { get; set; }

    public TutorialOverlay()
    {
        InitializeComponent();
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
        runner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TutorialRunner.IsSatisfied)
                               or nameof(TutorialRunner.ShowHint)) UpdateCallout();
        };
    }

    private void OnEnded()
    {
        Visibility = Visibility.Collapsed;
        _anchor = null;
        _alsoAllowed.Clear();
        _lastHoles = new List<Rect>();
    }

    private void OnStepChanged(TutorialStep step)
    {
        Visibility = Visibility.Visible;
        UpdateCallout();

        // Get onto the right tab first, or the control the step points at has
        // not been rendered and cannot be found.
        if (step.Tab >= 0) SwitchTab?.Invoke(step.Tab);

        // Then let that tab lay out before measuring — the same deferral issue
        // navigation uses, for the same reason.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _anchor = step.Anchor.Length > 0 && _window != null
                ? TutorialAnchor.Find(_window, step.Anchor)
                : null;

            // Anything else the step needs reachable. A missing id is skipped
            // rather than throwing: a pane that is not on screen simply
            // contributes no hole, and the step still works through the rest.
            _alsoAllowed.Clear();
            if (_window != null)
            {
                foreach (var id in step.AlsoAllow)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (TutorialAnchor.Find(_window, id) is { } extra) _alsoAllowed.Add(extra);
                }
            }

            _anchor?.BringIntoView();
            _corner = -1;            // a new step may want a different corner
            // A step the reader shoved aside does not decide where the NEXT
            // step's instructions go: the geometry places each one afresh.
            _userPlaced = null;
            _forceRefresh = true;
            Refresh();
            if (_anchor != null) Flash?.Invoke(_anchor);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Re-measures and redraws, cheaply skipping when nothing moved.</summary>
    private void Refresh()
    {
        if (_runner is not { IsRunning: true } || _window == null) return;

        var size = new Size(ActualWidth, ActualHeight);
        if (size.Width <= 0 || size.Height <= 0) return;

        Rect hole = MeasureAnchor(_anchor);

        // The primary first: the ring follows it, and PlaceCallout keeps its
        // corner by index, so the order has to be stable between passes.
        var holes = new List<Rect>();
        if (!hole.IsEmpty) holes.Add(hole);
        foreach (var extra in _alsoAllowed)
        {
            var r = MeasureAnchor(extra);
            if (!r.IsEmpty) holes.Add(r);
        }

        // A step with a longer body makes the callout taller, and nothing about
        // the holes changes to announce that, so its size is part of what counts
        // as "something moved".
        var callout = CalloutSize(size);
        if (!_forceRefresh && SameHoles(holes, _lastHoles) &&
            size == _lastWindow && callout == _lastCallout)
            return;
        _forceRefresh = false;
        _lastHoles = holes;
        _lastWindow = size;

        Dim.Data = SpotlightGeometry.BuildMask(size, holes);

        var spot = SpotlightGeometry.SpotlightRect(size, hole);
        if (spot.IsEmpty || spot.Width <= 0)
        {
            Ring.Visibility = Visibility.Collapsed;
        }
        else
        {
            Ring.Visibility = Visibility.Visible;
            Ring.Width = spot.Width;
            Ring.Height = spot.Height;
            Ring.Margin = new Thickness(spot.X, spot.Y, 0, 0);
        }

        PlaceCallout(holes, size);
    }

    /// <summary>Where a lit control sits, in this control's own coordinates.
    /// <see cref="Rect.Empty"/> when it is not on screen to be measured.</summary>
    private Rect MeasureAnchor(FrameworkElement? el)
    {
        if (el is not { IsVisible: true } || el.ActualWidth <= 0 || el.ActualHeight <= 0)
            return Rect.Empty;
        try
        {
            var origin = el.TransformToVisual(this).Transform(new Point(0, 0));
            // Quantised, because an animated control (the previews) shifts by
            // fractions of a pixel every frame, and that would otherwise count
            // as a change and force a redraw on every layout pass.
            return SpotlightGeometry.Quantize(
                new Rect(origin, new Size(el.ActualWidth, el.ActualHeight)));
        }
        catch (InvalidOperationException)
        {
            // Left the tree between layout passes (a tab switch mid-step). No
            // hole this pass; the next one picks it up.
            return Rect.Empty;
        }
    }

    private static bool SameHoles(List<Rect> a, List<Rect> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>
    /// The size to place the callout at.
    /// <para/>
    /// Read from what it was actually ARRANGED at, not from a fresh Measure.
    /// Measuring here was the bug behind the callout drifting up and down:
    /// calling Measure on an element already in the tree, from inside a
    /// LayoutUpdated handler, invalidates its layout and forces another pass —
    /// and the DesiredSize that comes back is not the size the parent then
    /// arranges it at. Because the bottom corners are computed as
    /// <c>window.Height - callout.Height</c>, that discrepancy moved the
    /// callout vertically on every single layout pass, for ever.
    /// <para/>
    /// Measure is still the fallback for the very first pass, when nothing has
    /// arranged it yet and there is no actual size to read.
    /// </summary>
    private Size CalloutSize(Size window)
    {
        var size = new Size(Callout.ActualWidth, Callout.ActualHeight);
        if (size.Width <= 0 || size.Height <= 0)
        {
            Callout.Measure(new Size(Math.Min(Callout.MaxWidth, window.Width), window.Height));
            size = Callout.DesiredSize;
        }
        // Whole units, so a hair of text-layout difference is not movement.
        return new Size(Math.Ceiling(size.Width), Math.Ceiling(size.Height));
    }

    // ── Dragging the instructions out of the way ─────────────────────
    //
    // Automatic placement puts the callout in the least-blocked corner. That is
    // right nearly always, and cannot be right when a step lights something big
    // enough to reach all four — the map button list, for one, where the
    // callout came to rest over the dropdown the step was asking the reader to
    // open. There is no corner to move to in that case, so the reader gets to
    // move it themselves.

    private bool _dragging;
    private Point _dragFrom;
    private Thickness _dragStartMargin;

    /// <summary>Where the reader has put the callout, if they have moved it.
    /// Cleared whenever the step changes, so each step starts placed by the
    /// geometry rather than wherever the last one was shoved.</summary>
    private Thickness? _userPlaced;

    private void Callout_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragFrom = e.GetPosition(this);
        _dragStartMargin = Callout.Margin;
        Callout.CaptureMouse();
        e.Handled = true;
    }

    private void Callout_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var now = e.GetPosition(this);

        // Kept inside the window: a callout dragged off the edge is a callout
        // the reader cannot get back without restarting the tutorial.
        double maxLeft = Math.Max(0, ActualWidth - Callout.ActualWidth);
        double maxTop = Math.Max(0, ActualHeight - Callout.ActualHeight);
        double left = Math.Clamp(_dragStartMargin.Left + (now.X - _dragFrom.X), 0, maxLeft);
        double top = Math.Clamp(_dragStartMargin.Top + (now.Y - _dragFrom.Y), 0, maxTop);

        var m = new Thickness(left, top, 0, 0);
        Callout.Margin = m;
        _userPlaced = m;
        e.Handled = true;
    }

    private void Callout_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Callout.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PlaceCallout(IReadOnlyList<Rect> holes, Size window)
    {
        // A reader who has moved it has said where they want it. Re-placing on
        // the next layout pass would drag it back and read as the window
        // fighting them.
        if (_userPlaced is { } placed)
        {
            if (Callout.Margin != placed) Callout.Margin = placed;
            return;
        }

        var wanted = CalloutSize(window);
        _lastCallout = wanted;

        var (at, corner) = SpotlightGeometry.PlaceCallout(holes, wanted, window, _corner);
        _corner = corner;

        var margin = new Thickness(at.X, at.Y, 0, 0);
        if (Callout.Margin != margin) Callout.Margin = margin;
    }

    private void UpdateCallout()
    {
        if (_runner?.Step is not TutorialStep s) return;
        Title.Text = s.Title;
        Body.Text = s.Body;
        Progress.Text = _runner.Progress;
        NextBtn.Content = _runner.NextLabel;
        NextBtn.IsEnabled = _runner.CanAdvance;
        BackBtn.IsEnabled = _runner.Index > 0;
        Waiting.Text = _runner.WaitingLabel;
        Hint.Text = s.Hint;
        HintBox.Visibility = _runner.ShowHint && s.Hint.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;

        // The callout changes size with its text, so it has to be re-placed or
        // a taller step can end up covering its own spotlight.
        if (_lastWindow.Width > 0) PlaceCallout(_lastHoles, _lastWindow);
    }

    private void Next_Click(object sender, RoutedEventArgs e) => _runner?.NextCommand.Execute(null);
    private void Back_Click(object sender, RoutedEventArgs e) => _runner?.BackCommand.Execute(null);
    private void Exit_Click(object sender, RoutedEventArgs e) => _runner?.ExitCommand.Execute(null);
}
