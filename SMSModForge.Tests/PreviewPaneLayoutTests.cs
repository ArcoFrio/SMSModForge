using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Characters and NPCs previews' panes (1.9.1, the author: "only the
/// backdrop ones were really supposed to be aligned to the bottom. The rest of
/// the layout should have stayed like before"): the backdrop buttons at the
/// pane's bottom, and everything else directly under the preview as in 1.9.0 -
/// with room to spare, the room is between them and the backdrop buttons, not
/// between the preview and its options. In a short window the preview still
/// shrinks to leave its options inside. Measured where each is drawn; control:
/// the tall window has room to spare, so the check can tell the two layouts apart.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PreviewPaneLayoutTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-panelayout-" + Guid.NewGuid().ToString("N"));
    public PreviewPaneLayoutTests(ITestOutputHelper o) { _out = o; Directory.CreateDirectory(_dir); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static Rect Drawn(Window window, FrameworkElement e)
        => e.TransformToVisual((Visual)window.Content).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++) { WindowHarness.Pump(); window.UpdateLayout(); }
        WindowHarness.Pump();
    }

    /// <summary>The preview, what is directly under it, the last of that, the
    /// backdrop buttons and the pane, as drawn; with how it was laid out.</summary>
    private (Rect preview, Rect next, Rect last, Rect backdrop, Rect pane) Measure(Window window, string preview, string next, string last, string backdrop, string pane)
    {
        Rect At(string name) => Drawn(window, (FrameworkElement)window.FindName(name));
        var r = (At(preview), At(next), At(last), At(backdrop), At(pane));
        _out.WriteLine($"  {preview} {r.Item1}; {next} {r.Item2}; {last} {r.Item3}; {backdrop} {r.Item4}; {pane} {r.Item5}");
        return r;
    }

    private void Check(Window window, string preview, string next, string last, string backdrop, string pane)
    {
        var content = (FrameworkElement)window.Content;
        window.Width = 1400; window.Height = 1800;
        Settle(window);
        _out.WriteLine($"tall window, content {content.ActualWidth:0}x{content.ActualHeight:0}:");
        var (p, n, l, b, pn) = Measure(window, preview, next, last, backdrop, pane);
        // The room between the preview and the backdrop buttons that its options do not take, wherever it is.
        double spare = b.Top - p.Bottom - (l.Bottom - n.Top);
        Assert.True(spare > 150, $"control: the tall window has no room to spare ({spare:0}) - the check cannot tell");
        Assert.True(n.Top - p.Bottom is >= 0 and <= 16, $"{next} is {n.Top - p.Bottom:0} under the preview, not directly under it");
        Assert.True(b.Top - l.Bottom >= spare - 20, $"of {spare:0} to spare, only {b.Top - l.Bottom:0} is between {last} and the backdrop buttons");
        Assert.True(Math.Abs(b.Bottom - pn.Bottom) <= 2, $"the backdrop buttons end at {b.Bottom:0}, not at the pane's bottom ({pn.Bottom:0})");

        window.Width = 1400; window.Height = 640;
        Settle(window);
        _out.WriteLine($"short window, content {content.ActualWidth:0}x{content.ActualHeight:0}:");
        (p, n, l, b, pn) = Measure(window, preview, next, last, backdrop, pane);
        Assert.True(p.Bottom <= n.Top + 0.5, $"the preview ({p.Bottom:0}) is drawn over {next} ({n.Top:0})");
        Assert.True(l.Bottom <= b.Top + 0.5, $"{last} ({l.Bottom:0}) is drawn over the backdrop buttons ({b.Top:0})");
        Assert.True(b.Bottom <= content.ActualHeight + 0.5, $"the backdrop buttons end at {b.Bottom:0}, past a window {content.ActualHeight:0} tall");
    }

    [Fact]
    public void Characters_TheOptionsAreDirectlyUnderTheBust_AndTheBackdropButtonsAtTheBottom()
    {
        WindowHarness.Run(window =>
        {
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Characters;
            WindowHarness.Pump();
            ((PreviewFrame)window.FindName("BustPreviewResizer")).Scale = 1;
            Check(window, "BustPreviewResizer", "BustQualityBar", "BustPreviewOptions", "BustBackdropBar", "BustPreviewPane");
        });
    }

    [Fact]
    public void Npcs_TheQualityAndNoteAreDirectlyUnderTheNpc_AndTheBackdropButtonsAtTheBottom()
    {
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Npcs", "guard.png"), 200, 400, Color.FromRgb(0xFF, 0x00, 0xFF));
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Npcs", "mask.png"), 200, 400, Color.FromRgb(0, 0, 0));
        var pack = PackRepository.CreateEmpty("panelayout.pack");
        pack.Npcs.Add(new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Npcs/guard.png", Mask = "Npcs/mask.png" });
        PackRepository.Save(pack, _dir);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Npcs;
            WindowHarness.Pump();
            vm.SelectedNpc = vm.Npcs.First();
            WindowHarness.Pump();
            ((PreviewFrame)window.FindName("NpcPreviewResizer")).Scale = 1;
            Check(window, "NpcPreviewResizer", "NpcQualityBar", "NpcPreviewNote", "NpcBackdropBar", "NpcPreviewPane");
        });
    }
}
