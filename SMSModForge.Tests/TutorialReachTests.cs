using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Tutorials;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Every control a tutorial step's words name can be clicked (1.10.0). The tutorial dims the window and the dim
/// swallows every click outside the areas the step lights (TutorialStep.Anchor and AlsoAllow), so a step that says
/// "name it" or "tick Wait for Talk button" without lighting that box cannot be done at all - the skeptical
/// reviewers of 2026-11-22 found seven such steps by looking at pictures, which finds them by luck.
/// <para/>
/// Walked as a person walks it: each tutorial in the real window, every step as it arrives (Do it for me to go on).
/// On a step that asks something of the reader, every control on screen whose own words are in the step's text or
/// hint - a button's or a tick's words, the label beside a box, a group's heading - must sit in a lit area. What is
/// named but scrolled out of sight is listed apart: the reader would have to find it, and the step does not show it.
/// <para/>
/// Long (every tutorial, every step), so it runs when asked: SMSMODFORGE_TUTORIAL_REACH=a file for the report.
/// </summary>
[Trait("Speed", "Slow")]
public sealed class TutorialReachTests
{
    private readonly ITestOutputHelper _out;
    public TutorialReachTests(ITestOutputHelper o) => _out = o;

    /// <summary>Words too common to be a control's name in a sentence ("type a name", "the Text").</summary>
    private static readonly HashSet<string> TooCommon = new(StringComparer.Ordinal) { "OK", "Cancel", "Add", "More", "Remove" };

    [Fact]
    public void EveryControlAStepNamesIsLit()
    {
        string? report = Environment.GetEnvironmentVariable("SMSMODFORGE_TUTORIAL_REACH");
        if (string.IsNullOrWhiteSpace(report)) return;
        string? only = Environment.GetEnvironmentVariable("SMSMODFORGE_TUTORIAL_REACH_ONLY");
        var dimmed = new List<string>();
        var unseen = new List<string>();
        foreach (var tutorial in TutorialCatalog.All.Where(t => t.IsOnLadder && (string.IsNullOrEmpty(only) || t.Id == only)))
        {
            WindowHarness.Run(window =>
            {
                window.Width = 1400; window.Height = 900;
                var vm = (MainViewModel)window.DataContext;
                using var walker = new TutorialWalker(vm);
                var runner = vm.TutorialRunner;
                var overlay = (TutorialOverlay)window.FindName("Tutorial");
                var coach = (FrameworkElement)window.FindName("Coach");
                runner.Start(tutorial);
                WindowHarness.Wait(TimeSpan.FromMilliseconds(500));
                if (runner.NeedsPreparing) runner.PrepareCommand.Execute(null);
                for (int i = 0; i < tutorial.Steps.Count; i++)
                {
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(700));
                    window.UpdateLayout();
                    WindowHarness.Pump();
                    var step = tutorial.Steps[i];
                    if (step.Kind != StepKind.Read)
                        Look(tutorial, i, step, window, overlay, coach, dimmed, unseen);
                    if (runner.CanDoItForMe) runner.DoItForMeCommand.Execute(null);
                    var deadline = DateTime.Now.AddSeconds(3);
                    while (!runner.CanAdvance && DateTime.Now < deadline) WindowHarness.Pump();
                    if (!runner.CanAdvance || i == tutorial.Steps.Count - 1) break;
                    runner.NextCommand.Execute(null);
                }
                if (runner.IsRunning) runner.ExitCommand.Execute(null);
                typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            });
        }
        var lines = new List<string> { $"DIMMED ({dimmed.Count}): named by the step, on screen, not lit - a click there is swallowed" };
        lines.AddRange(dimmed);
        lines.Add("");
        lines.Add($"OUT OF SIGHT ({unseen.Count}): named by the step, scrolled out of view, not lit");
        lines.AddRange(unseen);
        File.WriteAllLines(report, lines);
        foreach (var l in lines.Take(80)) _out.WriteLine(l);
    }

    private static void Look(TutorialDef tutorial, int index, TutorialStep step, Window window, TutorialOverlay overlay,
                             FrameworkElement coach, List<string> dimmed, List<string> unseen)
    {
        string words = Loc.T(step.Body) + "\n" + (string.IsNullOrEmpty(step.Hint) ? "" : Loc.T(step.Hint));
        var holes = overlay.Holes;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lit = new HashSet<string>(StringComparer.Ordinal);
        var where = new Dictionary<string, (bool OnScreen, string Kind)>(StringComparer.Ordinal);
        var all = Named(window, coach, overlay).ToList();
        foreach (var (named, name, kind) in all)
        {
            if (name.Length < 3 || TooCommon.Contains(name) || name.All(c => !char.IsUpper(c) && !char.IsDigit(c) && c != '+')) continue;
            // Named mid-sentence: "Type The Gallery over it" is the verb, "set Type to Bool" the box.
            if (!Regex.Matches(words, @"(?<![\w+])" + Regex.Escape(name) + @"(?![\w])")
                      .Any(m => !SentenceStart(words, m.Index))) continue;
            // A label is reached through its box: the box beside it on its line. One with none is a heading.
            var el = kind == "label" ? BoxBeside(named, all, overlay) : named;
            if (el == null) continue;
            var visible = Visible(el, overlay);
            if (visible is not { } r) { if (!where.ContainsKey(name)) where[name] = (false, kind); continue; }
            seen.Add(name);
            where[name] = (true, kind);
            var centre = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            if (holes.Any(h => h.Contains(centre))) lit.Add(name);
        }
        string at = $"{tutorial.Level:00}-{tutorial.Id} step {index + 1} \"{Loc.T(step.Title)}\"";
        foreach (var (name, (onScreen, kind)) in where.OrderBy(w => w.Key))
        {
            if (lit.Contains(name)) continue;
            if (onScreen) dimmed.Add($"{at}: {kind} \"{name}\"");
            else if (!seen.Contains(name)) unseen.Add($"{at}: {kind} \"{name}\"");
        }
    }

    /// <summary>The controls on the window with words of their own: buttons and ticks by their words, a box by the
    /// label beside it, a group by its heading. Not the tutorial's own panel and dim.</summary>
    private static IEnumerable<(FrameworkElement El, string Name, string Kind)> Named(Window window, FrameworkElement coach, FrameworkElement overlay)
    {
        foreach (var f in GuideSectionShots.Below(window).OfType<FrameworkElement>())
        {
            if (!f.IsVisible || IsWithin(f, coach) || IsWithin(f, overlay)) continue;
            switch (f)
            {
                case ButtonBase b when b.TemplatedParent is not Control:
                    yield return (b, Clean(UiDriver.TextOf(b)), b is CheckBox ? "tick" : b is RadioButton ? "choice" : "button");
                    break;
                case TextBlock t when Label(t):
                    yield return (t, Clean(t.Text), "label");
                    break;
                case HeaderedContentControl h when h is not TabItem && h.Header is string header:
                    yield return (h, Clean(header), "group");
                    break;
            }
        }
    }

    /// <summary>A label: words of their own, not inside a control, and not a tab's name (a step goes to its tab).</summary>
    private static bool Label(TextBlock t)
    {
        if (t.Text.Length is < 3 or > 40) return false;
        for (var p = VisualTreeHelper.GetParent(t); p != null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is ButtonBase or TextBoxBase or ComboBox or ListBoxItem or TreeViewItem or ToolTip or TabItem) return false;
            if (p is Window) return true;
        }
        return true;
    }

    private static bool SentenceStart(string text, int at)
    {
        int i = at - 1;
        while (i >= 0 && text[i] == ' ') i--;
        return i < 0 || text[i] is '.' or '\n' or ':' or '!' or '?' or '"';
    }

    /// <summary>The box a label stands for: the nearest box, tick, dropdown or button to its right on its line.</summary>
    private static FrameworkElement? BoxBeside(FrameworkElement label, List<(FrameworkElement El, string Name, string Kind)> all, FrameworkElement overlay)
    {
        if (Visible(label, overlay) is not { } l)
            return GuideSectionShots.Below(VisualTreeHelper.GetParent(label) ?? label).OfType<Control>()
                       .FirstOrDefault(c => c is TextBoxBase or ComboBox or ButtonBase);   // off screen: its row's box, to say so
        FrameworkElement? best = null;
        double bestGap = double.MaxValue;
        foreach (var c in GuideSectionShots.Below(overlay.Parent as DependencyObject ?? overlay).OfType<Control>())
        {
            if (c is not (TextBoxBase or ComboBox or ButtonBase) || !c.IsVisible) continue;
            if (Visible(c, overlay) is not { } r) continue;
            double mid = l.Y + l.Height / 2;
            if (r.Top > mid || r.Bottom < mid || r.Left < l.Left + l.Width * 0.5) continue;
            double gap = r.Left - l.Right;
            if (gap > -2 && gap < bestGap && gap < 400) { best = c; bestGap = gap; }
        }
        return best;
    }

    private static string Clean(string s)
    {
        s = s.Trim().TrimEnd(':').Trim();
        int paren = s.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? s[..paren] : s;
    }

    private static bool IsWithin(DependencyObject d, DependencyObject ancestor)
    {
        for (var p = d; p != null; p = VisualTreeHelper.GetParent(p))
            if (ReferenceEquals(p, ancestor)) return true;
        return false;
    }

    /// <summary>The part of a control on screen, in the overlay's coordinates; null when none is (scrolled away).</summary>
    private static Rect? Visible(FrameworkElement el, FrameworkElement overlay)
    {
        try
        {
            var slot = new Rect(el.TransformToVisual(overlay).Transform(new Point(0, 0)), new Size(el.ActualWidth, el.ActualHeight));
            for (var p = VisualTreeHelper.GetParent(el); p != null; p = VisualTreeHelper.GetParent(p))
            {
                if (p is not ScrollContentPresenter view || view.ActualWidth <= 0 || view.ActualHeight <= 0) continue;
                slot.Intersect(new Rect(view.TransformToVisual(overlay).Transform(new Point(0, 0)), new Size(view.ActualWidth, view.ActualHeight)));
                if (slot.IsEmpty) return null;
            }
            slot.Intersect(new Rect(0, 0, overlay.ActualWidth, overlay.ActualHeight));
            return slot.IsEmpty || slot.Width < 2 || slot.Height < 2 ? null : slot;
        }
        catch (InvalidOperationException) { return null; }
    }
}
