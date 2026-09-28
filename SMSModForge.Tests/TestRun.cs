using System.Runtime.CompilerServices;

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
    internal static void Begin() => Services.TestMode.Active = true;
}
