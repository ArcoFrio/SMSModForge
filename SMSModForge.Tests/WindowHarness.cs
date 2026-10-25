using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SMSModForge.Tests;

/// <summary>
/// Runs the real MainWindow on a real dispatcher, on the run's own desktop
/// (<see cref="TestDesktop"/>) where nobody can see it or lose focus to it.
/// <para/>
/// The rest of the suite drives view models, which is enough for almost
/// everything and costs nothing. It was not enough for the tab that switches by
/// itself: the view model's SelectedTabIndex never moves, so whatever writes the
/// tab lives in the view — in a binding, in focus, in the TabControl's own
/// selection logic — and none of that exists until a window does.
/// <para/>
/// One Application per process is a WPF rule, so the harness keeps a single STA
/// thread and hands work to it. Tests already run one at a time (see
/// Parallelism.cs), so there is nothing to contend with.
/// </summary>
internal static class WindowHarness
{
    private static Dispatcher? _dispatcher;
    private static Exception? _startFailure;
    private static readonly object Gate = new();

    /// <summary>The STA thread's dispatcher, starting the thread on first use.</summary>
    private static Dispatcher Ui
    {
        get
        {
            lock (Gate)
            {
                if (_dispatcher != null) return _dispatcher;
                if (_startFailure != null)
                    throw new InvalidOperationException(_startFailure.Message, _startFailure);

                var ready = new ManualResetEventSlim();
                var thread = new Thread(() =>
                {
                    // First of all: a thread belongs to the desktop of its
                    // first window, and every window here must be on the
                    // run's own, never on the author's. If it cannot be, no
                    // window is made at all and every window test says why.
                    try { TestDesktop.Enter(); }
                    catch (Exception e)
                    {
                        _startFailure = e;
                        ready.Set();
                        return;
                    }

                    // Before anything else, and before any window exists: this
                    // is what keeps the harness from writing over the pane
                    // layout, prompting about unsaved changes, and filling the
                    // tab-change log with its own noise, on the machine of
                    // whoever ran the tests.
                    SMSModForge.Services.TestMode.Active = true;

                    // Resources next: MainWindow's DynamicResource theme brushes
                    // and every StaticResource style live in App.xaml, and a
                    // window built without them throws on the first lookup.
                    var app = new SMSModForge.App();
                    app.InitializeComponent();

                    // The theme the run is in (1.8.0): the window tests run a
                    // second time with an EX theme on, whose templates are its
                    // own (Tools/Testing/run-tests.ps1 -WithEx). Unset, the
                    // App.xaml defaults - Normal, Light - as always.
                    string? theme = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME");
                    if (!string.IsNullOrWhiteSpace(theme))
                        SMSModForge.Services.ThemeManager.Apply(SMSModForge.Services.ThemeManager.All.First(
                            t => string.Equals(t.Name, theme, StringComparison.OrdinalIgnoreCase)));

                    _dispatcher = Dispatcher.CurrentDispatcher;
                    ready.Set();
                    Dispatcher.Run();
                })
                {
                    IsBackground = true,   // never hold the test run open
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                ready.Wait(TimeSpan.FromSeconds(30));
                if (_startFailure != null)
                    throw new InvalidOperationException(_startFailure.Message, _startFailure);

                // Shut the UI thread down properly when the run ends, rather
                // than letting the process take a background thread down with
                // WPF halfway through its deferred binding cleanup - which
                // threw inside DataBindEngine.DoCleanup at exit, now and then,
                // in short runs that ended on a window, and printed as an
                // unhandled exception after every test had passed.
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    try
                    {
                        _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal);
                        thread.Join(TimeSpan.FromSeconds(5));
                    }
                    catch { /* the run is over either way */ }
                };
                return _dispatcher!;
            }
        }
    }

    /// <summary>Run <paramref name="body"/> on the UI thread and rethrow whatever
    /// it threw on this one, so a failure reads as an ordinary test failure.</summary>
    public static void Run(Action<MainWindow> body)
    {
        Exception? failure = null;
        Ui.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow
                {
                    // Asked to activate, as a real one is: on the run's own
                    // desktop that can take nobody's foreground, and the
                    // window still gets the keyboard focus a tab control's
                    // selection logic leans on - a harness without it cannot
                    // see a focus-driven jump.
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowInTaskbar = false,
                    Left = -32000,
                    Top = -32000,
                };
                window.Show();
                window.Activate();
                Pump();
                body(window);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                try { window?.Close(); } catch { /* closing is not the test */ }
            }
        }, DispatcherPriority.Normal);

        if (failure != null)
            throw new Exception("in the window: " + failure.Message, failure);
    }

    /// <summary>Let everything queued run: layout, bindings, container
    /// generation, and the Input-priority work the editor defers.</summary>
    public static void Pump()
    {
        foreach (var priority in new[]
                 {
                     DispatcherPriority.Render,
                     DispatcherPriority.Loaded,
                     DispatcherPriority.Input,
                     DispatcherPriority.Background,
                 })
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                new Action(() => frame.Continue = false), priority);
            Dispatcher.PushFrame(frame);
        }
    }

    /// <summary>
    /// Open or close a dropdown, and return once it has. With Windows'
    /// animations on, a list slides open and shut, and WPF tells the box it
    /// closed - which is what ends a search - only when the slide is over:
    /// measured 2026-10-24, a second after the change rather than within the
    /// <see cref="Pump"/> after it, which four tests had taken for granted.
    /// Opening waits for the popup to be up and its slide (WPF's popups
    /// animate for 150 ms) to be done, so what is drawn is the list at rest.
    /// </summary>
    public static void SetOpen(System.Windows.Controls.ComboBox box, bool open)
    {
        if (box.IsDropDownOpen == open) { Pump(); return; }
        var popup = box.Template?.FindName("PART_Popup", box) as System.Windows.Controls.Primitives.Popup;
        bool done = false;
        EventHandler happened = (_, _) => done = true;
        if (open) { if (popup != null) popup.Opened += happened; }
        else box.DropDownClosed += happened;
        try
        {
            box.IsDropDownOpen = open;
            Pump();
            Until(() => done || popup == null, TimeSpan.FromSeconds(3));
            if (open) Wait(TimeSpan.FromMilliseconds(200));
        }
        finally
        {
            if (popup != null) popup.Opened -= happened;
            box.DropDownClosed -= happened;
        }
    }

    /// <summary>Keep the dispatcher turning until <paramref name="done"/>, or
    /// <paramref name="atMost"/> has passed; whether it came true.</summary>
    public static bool Until(Func<bool> done, TimeSpan atMost)
    {
        var until = DateTime.UtcNow + atMost;
        while (!done() && DateTime.UtcNow < until) Wait(TimeSpan.FromMilliseconds(10));
        return done();
    }

    /// <summary>
    /// Let real time pass with the dispatcher still running.
    /// <para/>
    /// <see cref="Pump"/> drains what is already queued and returns, which
    /// proves nothing about anything on a clock: a DispatcherTimer has not
    /// posted its tick yet, so there is nothing there to drain. Tests of
    /// something that animates need the queue to keep turning while the wall
    /// clock moves, which is what this does.
    /// </summary>
    public static void Wait(TimeSpan howLong)
    {
        var until = DateTime.UtcNow + howLong;
        while (DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            var tick = new DispatcherTimer(TimeSpan.FromMilliseconds(5),
                                           DispatcherPriority.Background,
                                           (_, _) => frame.Continue = false,
                                           Dispatcher.CurrentDispatcher);
            tick.Start();
            Dispatcher.PushFrame(frame);
            tick.Stop();
        }
    }
}
