using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// How long the editor takes over a real pack, and what it produces from it -
/// for a performance pass (1.9.0): measured before and after, the fingerprints
/// must not move. Not a check: runs only when SMSMODFORGE_PERF_PACK names a
/// pack folder, which it COPIES first (the folder named is only read), and
/// writes timings.tsv and fingerprints.txt to SMSMODFORGE_PERF_OUT.
/// <para/>
/// Fingerprints: every file the save writes (SHA-256 of its bytes), every
/// validation issue in order (code, where, words), and every file inside the
/// export (by its bytes, not the zip's dates).
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PerfBenchTests
{
    private readonly ITestOutputHelper _out;
    public PerfBenchTests(ITestOutputHelper o) => _out = o;

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 16);

    private static void Copy(string from, string to)
    {
        foreach (string dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
    }

    [Fact]
    public void TimeAndFingerprintARealPack()
    {
        string? source = Environment.GetEnvironmentVariable("SMSMODFORGE_PERF_PACK");
        string? outDir = Environment.GetEnvironmentVariable("SMSMODFORGE_PERF_OUT");
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(outDir))
        {
            _out.WriteLine("SMSMODFORGE_PERF_PACK / SMSMODFORGE_PERF_OUT not set; nothing measured.");
            return;
        }
        Directory.CreateDirectory(outDir);
        string work = Path.Combine(Path.GetTempPath(), "smsmodforge-perf-" + Guid.NewGuid().ToString("N"));
        Copy(source, work);
        var times = new List<(string What, double Ms)>();
        var prints = new List<string>();
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                var clock = new Stopwatch();
                double Time(string what, Action act)
                {
                    clock.Restart();
                    act();
                    WindowHarness.Pump();
                    window.UpdateLayout();
                    clock.Stop();
                    times.Add((what, clock.Elapsed.TotalMilliseconds));
                    return clock.Elapsed.TotalMilliseconds;
                }

                Time("open pack", () => vm.OpenPackFromPath(work));
                Time("validate", () => vm.ValidateCommand.Execute(null));
                foreach (var issue in vm.Issues)
                    prints.Add("issue\t" + issue.Code + "\t" + issue.Where + "\t" + issue.Message);

                // Every tab, twice: the first visit builds it.
                var tabs = (TabControl)window.FindName("MainTabs");
                for (int pass = 1; pass <= 2; pass++)
                    for (int i = 0; i < tabs.Items.Count; i++)
                    {
                        string name = (tabs.Items[i] as TabItem)?.Header as string ?? i.ToString();
                        Time($"tab {pass} {name}", () => tabs.SelectedIndex = i);
                    }

                // Dialogues: every dialogue, then every node of the largest.
                for (int i = 0; i < tabs.Items.Count; i++)
                    if ((tabs.Items[i] as TabItem)?.Header as string == "Dialogues") { tabs.SelectedIndex = i; break; }
                WindowHarness.Pump();
                var perDialogue = new List<double>();
                foreach (var d in vm.Dialogues.ToList())
                    perDialogue.Add(Time("dialogue", () => { vm.SelectedDialogue = d; if (d.Nodes.Count > 0) vm.SelectedNode = d.Nodes[0]; }));
                times.RemoveAll(t => t.What == "dialogue");
                if (perDialogue.Count > 0)
                {
                    times.Add(("dialogue select avg", perDialogue.Average()));
                    times.Add(("dialogue select max", perDialogue.Max()));
                    times.Add(("dialogue select total", perDialogue.Sum()));
                }
                var biggest = vm.Dialogues.OrderByDescending(d => d.Nodes.Count).FirstOrDefault();
                if (biggest != null)
                {
                    vm.SelectedDialogue = biggest;
                    WindowHarness.Pump();
                    var perNode = new List<double>();
                    foreach (var n in biggest.Nodes.Take(150).ToList())
                        perNode.Add(Time("node", () => vm.SelectedNode = n));
                    times.RemoveAll(t => t.What == "node");
                    times.Add(($"node select avg ({perNode.Count} of '{biggest.Model.Key}')", perNode.Average()));
                    times.Add(("node select max", perNode.Max()));
                }

                // Save and export: what they write is fingerprinted.
                Time("save", () => vm.SavePack());
                foreach (string file in Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories)
                                                 .OrderBy(f => f, StringComparer.Ordinal))
                    if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                        prints.Add("saved\t" + Path.GetRelativePath(work, file) + "\t" + Sha(File.ReadAllBytes(file)));
                string export = Path.Combine(work + "-export", "pack.smspack");
                Directory.CreateDirectory(Path.GetDirectoryName(export)!);
                Time("export", () => PackExporter.Export(work, export));
                using (var zip = ZipFile.OpenRead(export))
                    foreach (var e in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
                    {
                        using var s = e.Open();
                        using var m = new MemoryStream();
                        s.CopyTo(m);
                        prints.Add("exported\t" + e.FullName + "\t" + Sha(m.ToArray()));
                    }

                // The big dropdowns, opened once each (changes the pack after the save).
                var node = vm.SelectedNode!;
                long Open(Func<ComboBox, bool> which, object row)
                {
                    window.UpdateLayout();
                    WindowHarness.Pump();
                    var box = Descendants(window).First(c => c.IsVisible && ReferenceEquals(c.DataContext, row) && which(c));
                    var c2 = Stopwatch.StartNew();
                    box.IsDropDownOpen = true;
                    WindowHarness.Pump();
                    box.UpdateLayout();
                    c2.Stop();
                    box.IsDropDownOpen = false;
                    WindowHarness.Pump();
                    return c2.ElapsedMilliseconds;
                }
                var packVar = node.AddCondition();
                packVar.DisplayType = NodeConditionViewModel.VariableFamilyType;
                packVar.VarSource = "Pack";
                WindowHarness.Pump();
                times.Add(("open pack variables list", Open(c => OptionNotes.GetKind(c) == OptionNotes.PackVariable, packVar)));
                var gameVar = node.AddCondition();
                gameVar.DisplayType = NodeConditionViewModel.VariableFamilyType;
                gameVar.VarSource = "Vanilla";
                WindowHarness.Pump();
                times.Add(("open game variables list", Open(c => OptionNotes.GetKind(c) == OptionNotes.GameVariable, gameVar)));
                var show = node.AddActionOnFinish();
                show.DisplayType = NodeActionTypes.SetGameObjectActive;
                show.Category = NodeActionViewModel.CatScene;
                WindowHarness.Pump();
                times.Add(("open scene targets list", Open(c => c.IsEditable && c.Items.Count > 1000, show)));
            });
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
            try { Directory.Delete(work + "-export", true); } catch (IOException) { }
        }

        var sb = new StringBuilder();
        foreach (var (what, ms) in times) sb.Append(what).Append('\t').Append(ms.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        File.WriteAllText(Path.Combine(outDir, "timings.tsv"), sb.ToString());
        File.WriteAllText(Path.Combine(outDir, "fingerprints.txt"), string.Join("\n", prints) + "\n");
        foreach (var (what, ms) in times.Where(t => !t.What.StartsWith("tab 2"))) _out.WriteLine($"{ms,9:0.0} ms  {what}");
        _out.WriteLine($"{prints.Count} fingerprints");

        // A tracer attached to this process stops cleanly only while it lives.
        if (int.TryParse(Environment.GetEnvironmentVariable("SMSMODFORGE_PERF_HOLD"), out int hold) && hold > 0)
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(hold));
    }

    private static IEnumerable<ComboBox> Descendants(System.Windows.DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ComboBox c) yield return c;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
