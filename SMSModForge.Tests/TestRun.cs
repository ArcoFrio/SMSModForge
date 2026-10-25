using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace SMSModForge.Tests;

/// <summary>
/// The run is a test run from its first line: <see cref="Services.TestMode"/>
/// is on before any test builds anything.
/// <para/>
/// It was switched on by the window harness and by some tests themselves, so a
/// test that opened a pack without either went through as the author: its
/// temporary folder went into their File > Recent list, and the suite read
/// their preferences - a test failed on their machine because of the size they
/// had last picked for the bust preview (2026-09-27).
/// </summary>
internal static class TestRun
{
    [ModuleInitializer]
    internal static void Begin()
    {
        Services.TestMode.Active = true;
        TakeComsMainApartment();
    }

    /// <summary>
    /// The first thread in a process to join a single-threaded COM apartment
    /// is COM's main one, and COM gives it a hidden window of its own
    /// (OleMainThreadWndClass). A thread with a window cannot move to another
    /// desktop, so if the window harness's thread were first it could never
    /// reach the run's own desktop (<see cref="TestDesktop"/>: error 170,
    /// busy). In an ordinary run the progress window's thread always happened
    /// to be first, which hid this for weeks; a parallel run's other testers
    /// have no progress window, and every window test in them stopped.
    /// <para/>
    /// So a thread of no other use takes the role before anything else can,
    /// and keeps a message loop going: COM sends the main apartment work of
    /// its own, and a main apartment that never answered would hang the run.
    /// </summary>
    private static void TakeComsMainApartment()
    {
        var joined = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            OleInitialize(IntPtr.Zero);
            joined.Set();
            System.Windows.Forms.Application.Run();
        })
        {
            IsBackground = true,   // never holds the run open
            Name = "COM's main apartment",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        joined.Wait(TimeSpan.FromSeconds(10));
    }

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr reserved);
}
