using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SMSModForge.Tests;

/// <summary>
/// Runs the real MainWindow on a real dispatcher, off screen.
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
    private static readonly object Gate = new();

    /// <summary>The STA thread's dispatcher, starting the thread on first use.</summary>
    private static Dispatcher Ui
    {
        get
        {
            lock (Gate)
            {
                if (_dispatcher != null) return _dispatcher;

                var ready = new ManualResetEventSlim();
                var thread = new Thread(() =>
                {
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
                    // Off screen, but ACTIVATED. An unactivated window has no
                    // keyboard focus at all, and focus is half of what a tab
                    // control does with a selection - a harness that skips it
                    // cannot see a focus-driven jump, which is the shape of the
                    // bug being chased.
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
