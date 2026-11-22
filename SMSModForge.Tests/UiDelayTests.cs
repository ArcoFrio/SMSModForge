using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;
using SMSModForge.Tutorials;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// How long the editor keeps a person waiting (1.10.0, the author: "can we do a full measurement of UI delays? I want
/// to find and fix anything that makes the UI feel slow ... the little delay after clicking a tab or view for the
/// first time and actually seeing it"). Each click is timed until the UI thread has nothing left to do for it - its
/// layout, its drawing and the work the editor puts off to Background - on the tutorials' practice pack, saved and
/// opened again as a person opens a pack. Reported (SMSMODFORGE_UI_DELAYS names a file for the table), and held to
/// what a person notices: no tab, page or choice may keep the thread busy longer than <see cref="Noticeable"/>.
/// </summary>
[Trait("Speed", "Slow")]
[Trait("Timing", "Alone")]  // times the UI thread: run by a tester alone (run-tests.ps1)
public sealed class UiDelayTests
{
    private readonly ITestOutputHelper _out;
    public UiDelayTests(ITestOutputHelper o) => _out = o;

    /// <summary>Until the UI thread is idle: everything a click set off, done.</summary>
    internal static double Busy(Action click)
    {
        var sw = Stopwatch.StartNew();
        click();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>What a person notices as a delay after a click (the usual figure for "instant" is 100 ms).</summary>
    internal const double Noticeable = 300;

    internal sealed record Row(string What, double First, double Again);

    internal static List<Row> Measure(MainWindow window, string folder, ITestOutputHelper? log = null)
    {
        var vm = (MainViewModel)window.DataContext;
        var rows = new List<Row>();
        void Twice(string what, Action click, Action? away = null)
        {
            double first = Busy(click);
            away?.Invoke();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            double again = Busy(click);
            rows.Add(new Row(what, first, again));
        }

        // Every tab, first then again; between, the ModForge tab (the start of any visit).
        for (int tab = 0; tab <= MainTab.Integration; tab++)
        {
            int t = tab;
            if (t == MainTab.ModForge) continue;
            Twice("tab " + MainTab.Name(t), () => vm.SelectedTabIndex = t, () => Busy(() => vm.SelectedTabIndex = MainTab.ModForge));
        }
        vm.SelectedTabIndex = MainTab.ModForge;
        foreach (var page in new[] { HomePage.Tutorials, HomePage.Guide, HomePage.Issues, HomePage.Home })
        {
            var p = page;
            Twice("ModForge page " + p, () => vm.HomePage = p, () => Busy(() => vm.HomePage = HomePage.Home));
        }
        // The guide: one topic after another.
        vm.HomePage = HomePage.Guide;
        var entries = vm.GuideEntries.Take(6).ToList();
        for (int i = 1; i < entries.Count; i++)
        {
            var e = entries[i];
            rows.Add(new Row("guide topic " + e.Title, Busy(() => vm.SelectedGuideEntry = e), 0));
        }
        // Choosing things in the lists.
        vm.SelectedTabIndex = MainTab.Characters;
        Busy(() => { });
        foreach (var c in vm.Characters.Where(c => !c.Model.IsVanillaCharacter).Take(3))
            rows.Add(new Row("character " + c.DisplayName, Busy(() => vm.SelectedCharacter = c), 0));
        vm.SelectedTabIndex = MainTab.Dialogues;
        Busy(() => { });
        foreach (var d in vm.Dialogues.Take(3))
            rows.Add(new Row("dialogue " + d.DisplayName, Busy(() => vm.SelectedDialogue = d), 0));
        if (vm.SelectedDialogue is { } open)
            foreach (var n in open.Nodes.Take(3))
                rows.Add(new Row("line " + n.Text[..Math.Min(20, n.Text.Length)], Busy(() => vm.SelectedNode = n), 0));
        vm.SelectedTabIndex = MainTab.Places;
        Busy(() => { });
        foreach (var p in vm.Places.Take(2))
            rows.Add(new Row("place " + p.DisplayName, Busy(() => vm.SelectedPlace = p), 0));
        vm.SelectedTabIndex = MainTab.Integration;
        Busy(() => { });
        foreach (var r in vm.IntegrationRules.Take(3))
            rows.Add(new Row("rule " + r.DisplayName, Busy(() => vm.SelectedIntegrationRule = r), 0));
        return rows;
    }

    [Fact]
    public void NothingTheEditorDoesOnAClickKeepsAPersonWaiting()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SMSModForge-delays-" + Guid.NewGuid().ToString("N"));
        var rows = new List<Row>();
        double open = 0, warmStep = 0;
        try
        {
            // The practice pack, made and saved once.
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                Assert.True(vm.MakePack(folder, StoryPack.Name));
                TutorialAssets.EnsureCopied(vm.PackRoot);
                foreach (var t in TutorialCatalog.All.Where(t => t.IsOnLadder).OrderBy(t => t.Level))
                    StoryPack.Replay(vm, t);
                vm.SavePackCommand.Execute(null);
                WindowHarness.Pump();
            });
            // Opened in a fresh window, as a person opens it, then clicked through.
            WindowHarness.Run(window =>
            {
                window.Width = 1500; window.Height = 940;
                var vm = (MainViewModel)window.DataContext;
                open = Busy(() => vm.OpenPackFromPath(folder));
                // The whole practice pack, not an empty one: what is measured has to be a real pack's worth.
                Assert.True(vm.Dialogues.Count >= 4 && vm.Places.Count >= 1 && vm.IntegrationRules.Count >= 5,
                            $"the pack did not open: {vm.Dialogues.Count} dialogues, {vm.Places.Count} places, {vm.IntegrationRules.Count} rules");
                // As a person would: the pack open, a moment before the first click - the views built meanwhile.
                Assert.True(WindowHarness.Until(() => window.WarmedUp, TimeSpan.FromSeconds(30)), "the views were not built ahead");
                warmStep = window.LongestWarmStepMs;
                rows = Measure(window, folder);
                // Profiling (dotnet-trace attached): stay up so its symbol map is taken before the process ends.
                if (Environment.GetEnvironmentVariable("SMSMODFORGE_UI_DELAYS_HOLD") == "1") WindowHarness.Wait(TimeSpan.FromSeconds(25));
                typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            });
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }

        var table = new List<string> { $"open the pack: {open,6:0} ms; longest view built ahead: {warmStep,6:0} ms",
                                       $"{"what",-48} {"first",8} {"again",8}" };
        table.AddRange(rows.Select(r => $"{r.What,-48} {r.First,8:0} {(r.Again > 0 ? r.Again.ToString("0") : ""),8}"));
        foreach (var l in table) _out.WriteLine(l);
        if (Environment.GetEnvironmentVariable("SMSMODFORGE_UI_DELAYS") is { Length: > 0 } file) File.WriteAllLines(file, table);
        var slow = rows.Where(r => Math.Max(r.First, r.Again) > Noticeable).Select(r => $"{r.What}: {Math.Max(r.First, r.Again):0} ms").ToList();
        Assert.True(slow.Count == 0, "noticeable waits:\n" + string.Join("\n", slow));
    }
}
