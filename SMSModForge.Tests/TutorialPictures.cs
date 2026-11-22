using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Tutorials;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Pictures of a tutorial's box (1.10.0: the step list, the fold-out, Do it for me, Next tutorial), to look at in
/// each look: nothing is asserted. Only when SMSMODFORGE_SHOT_DIR names a folder; the theme is
/// SMSMODFORGE_TEST_THEME's.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class TutorialPictures
{
    private static void Save(Visual what, double w, double h, string file)
    {
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(what);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(file);
        png.Save(f);
    }

    private static T? Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t && t.Name == name) return t;
            if (Find<T>(c, name) is { } found) return found;
        }
        return null;
    }

    /// <summary>
    /// A picture of every step of every tutorial, as a reader meets it - the story brought in first when it is
    /// offered, each step pictured on arrival, then done for the reader ("Do it for me") and gone past. For the
    /// review of the tutorials (each step's words read with its picture) and the tutorials' page. Only when
    /// SMSMODFORGE_SHOT_DIR names a folder and SMSMODFORGE_TUTORIAL_SHOTS is set (to a tutorial's id for one).
    /// </summary>
    [Fact]
    public void EveryStep()
    {
        string? dir = Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR");
        string? which = Environment.GetEnvironmentVariable("SMSMODFORGE_TUTORIAL_SHOTS");
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(which)) return;
        Directory.CreateDirectory(dir);
        string theme = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light";
        foreach (var tutorial in TutorialCatalog.All.Where(t => t.IsOnLadder && (which == "1" || which == "all" || which == t.Id)))
        {
            WindowHarness.Run(window =>
            {
                window.Width = 1400; window.Height = 900;
                var vm = (MainViewModel)window.DataContext;
                using var walker = new TutorialWalker(vm);
                var runner = vm.TutorialRunner;
                var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
                runner.Start(tutorial);
                WindowHarness.Wait(TimeSpan.FromMilliseconds(500));
                if (runner.NeedsPreparing)
                {
                    Save(root, root.ActualWidth, root.ActualHeight, Path.Combine(dir, $"{tutorial.Level:00}-{tutorial.Id}-00-prepare-{theme}.png"));
                    runner.PrepareCommand.Execute(null);
                }
                // Each step's words beside its picture, from the step itself (for a reader of the pictures: a list
                // made by reading the source missed the steps a helper builds, and numbered the pictures wrongly).
                var words = new System.Text.StringBuilder();
                words.AppendLine($"# Tutorial {tutorial.Level}: {tutorial.Title}  (id {tutorial.Id})");
                for (int i = 0; i < tutorial.Steps.Count; i++)
                {
                    var step = tutorial.Steps[i];
                    words.AppendLine().AppendLine($"## Step {i + 1} of {tutorial.Steps.Count} - {step.Title}  [{step.Kind}{(step.Solve != null && step.Kind != StepKind.Read ? ", has Do it for me" : "")}]  key: {step.TitleKey}");
                    words.AppendLine($"Picture on arrival: {Path.Combine(dir, $"{tutorial.Level:00}-{tutorial.Id}-{i + 1:00}-{theme}.png")}");
                    words.AppendLine("Text: " + step.Body);
                    if (!string.IsNullOrEmpty(step.More)) words.AppendLine("Fold-out (Tell me more): " + step.More);
                    if (!string.IsNullOrEmpty(step.Hint)) words.AppendLine("Hint (after waiting): " + step.Hint);
                    words.AppendLine($"(Lit: {step.Anchor}{(step.AlsoAllow.Length > 0 ? " + " + string.Join(", ", step.AlsoAllow) : "")})");
                }
                File.WriteAllText(Path.Combine(dir, $"{tutorial.Level:00}-{tutorial.Id}.md"), words.ToString());
                for (int i = 0; i < tutorial.Steps.Count; i++)
                {
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(700));
                    window.UpdateLayout();
                    WindowHarness.Pump();
                    Save(root, root.ActualWidth, root.ActualHeight, Path.Combine(dir, $"{tutorial.Level:00}-{tutorial.Id}-{i + 1:00}-{theme}.png"));
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
    }

    [Fact]
    public void TakeThem()
    {
        string? dir = Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        string theme = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light";
        WindowHarness.Run(window =>
        {
            window.Width = 1400; window.Height = 900;
            var vm = (MainViewModel)window.DataContext;
            using var walker = new TutorialWalker(vm);
            var first = TutorialCatalog.All.First(t => t.Id == "first-steps");
            vm.TutorialRunner.Start(first);
            WindowHarness.Pump();
            // Through the pack and the art, by Do it for me, to "Name her": a step with a fold-out, waiting.
            while (vm.TutorialRunner.Step?.Title != Localization.Loc.T("tutorial.firstSteps.giveThemName.title"))
            {
                if (vm.TutorialRunner.CanDoItForMe) vm.TutorialRunner.DoItForMeCommand.Execute(null);
                WindowHarness.Wait(TimeSpan.FromMilliseconds(400));
                vm.TutorialRunner.NextCommand.Execute(null);
                WindowHarness.Wait(TimeSpan.FromMilliseconds(300));
            }
            WindowHarness.Wait(TimeSpan.FromMilliseconds(500));
            if (Find<Expander>(window, "PART_More") is { } more) more.IsExpanded = true;
            window.UpdateLayout();
            WindowHarness.Pump();
            var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
            Save(root, root.ActualWidth, root.ActualHeight, Path.Combine(dir, $"tutorial-step-{theme}.png"));

            // The last step: Next tutorial.
            vm.TutorialRunner.GoToCommand.Execute(first.Steps.Count - 1);
            WindowHarness.Wait(TimeSpan.FromMilliseconds(500));
            window.UpdateLayout();
            WindowHarness.Pump();
            Save(root, root.ActualWidth, root.ActualHeight, Path.Combine(dir, $"tutorial-last-{theme}.png"));
            vm.TutorialRunner.ExitCommand.Execute(null);
            typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        });
    }
}
