using System;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The author's rules for what a test run may do on their screen (2026-10-21):
/// nothing a test builds appears on their monitors or takes their focus, and
/// the progress window is the one thing they do see, on the leftmost monitor.
/// See <see cref="TestDesktop"/> for why a desktop of the run's own.
/// </summary>
public sealed class TestWindowRulesTests
{
    private readonly ITestOutputHelper _out;
    public TestWindowRulesTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void TheHarnessWindowsAreOnTheRunsOwnDesktop()
    {
        string input = TestDesktop.InputName();
        if (input == "" || !Environment.UserInteractive) return;   // nobody's screen to keep

        // The control: this thread never entered the run's desktop, so it must
        // read as the author's - otherwise the comparison below proves nothing.
        string here = TestDesktop.CurrentName();
        _out.WriteLine($"author's desktop: {input}; this thread: {here}");
        Assert.Equal(input, here);

        string? harness = null;
        WindowHarness.Run(_ => harness = TestDesktop.CurrentName());
        _out.WriteLine($"harness: {harness}");
        Assert.Equal(TestDesktop.Name, harness);
        Assert.NotEqual(input, harness);
    }

    [Fact]
    public void EveryThreadThatMakesWindowsEntersTheRunsDesktop()
    {
        // A WPF or WinForms window needs an STA thread, so a test that starts
        // one is a test that can put a window in front of the author.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "SMSModForge.Tests")))
            dir = dir.Parent;
        Assert.True(dir != null, "no SMSModForge.Tests folder above " + AppContext.BaseDirectory);

        var offenders = Directory.GetFiles(Path.Combine(dir!.FullName, "SMSModForge.Tests"), "*.cs")
            .Where(f => Path.GetFileName(f) != "RunProgressWindow.cs")   // meant to be seen
            // COM's main apartment: holds a message-only window and shows nothing,
            // and must stay where it is - see TestRun.TakeComsMainApartment.
            .Where(f => Path.GetFileName(f) != "TestRun.cs")
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(f =>
            {
                string text = File.ReadAllText(f);
                return text.Contains("SetApartmentState(") && !text.Contains("TestDesktop.Enter()");
            })
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(offenders.Count == 0,
            "These start an STA thread without putting it on the run's own desktop (TestDesktop.Enter() first): "
            + string.Join(", ", offenders));
    }
}
