using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A dropdown holding the pack's word for something says what it names, faintly at its right (1.10.0): the review
/// of the tutorials found beginners reading place:TheGallery in a Level box and HerFirstPose in a Target box while
/// the lists said "The Gallery" and "Her first pose". Measured from what is drawn: the note's ink, where it is, and
/// that it keeps clear of the words.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class TokenNoteTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-tokennote-" + Guid.NewGuid().ToString("N"));

    public TokenNoteTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private MainViewModel OnALine(MainWindow window, ModPack pack)
    {
        PackRepository.Save(pack, _dir);
        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        vm.SelectedTabIndex = MainTab.Dialogues;
        WindowHarness.Pump();
        vm.AddDialogueCommand.Execute(null);
        WindowHarness.Pump();
        if (vm.SelectedDialogue!.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
        vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
        WindowHarness.Pump();
        return vm;
    }

    private static void Settle(MainWindow window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
    }

    /// <summary>The visible box of this row showing this text.</summary>
    private static ComboBox BoxWith(MainWindow window, object row, string text)
        => Assert.Single(Descendants<ComboBox>(window).Where(c => c.IsVisible && ReferenceEquals(c.DataContext, row) && c.Text == text));

    /// <summary>How many pixels the note's adorner put down inside <paramref name="area"/> (the box's coordinates).</summary>
    private static int InkIn(ComboBox box, Rect area)
    {
        var adorner = TokenNote.AdornerOf(box);
        Assert.NotNull(adorner);
        int w = (int)Math.Ceiling(box.ActualWidth), h = (int)Math.Ceiling(box.ActualHeight);
        // What the adorner drew, in the box's coordinates: rendered as itself it would carry its offset in the
        // adorner layer, and land outside the picture.
        var drawing = VisualTreeHelper.GetDrawing(adorner!);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            if (drawing != null) dc.DrawDrawing(drawing);
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);
        int ink = 0;
        for (int y = Math.Max(0, (int)area.Top); y < Math.Min(h, (int)Math.Ceiling(area.Bottom)); y++)
            for (int x = Math.Max(0, (int)area.Left); x < Math.Min(w, (int)Math.Ceiling(area.Right)); x++)
                if (px[(y * w + x) * 4 + 3] > 40) ink++;
        return ink;
    }

    [Fact]
    public void ABoxHoldingThePacksWordForAPlaceOrASceneSaysWhatItNames_ClearOfTheWords()
    {
        WindowHarness.Run(window =>
        {
            window.Width = 1600;
            var pack = PackRepository.CreateEmpty("tokennote.pack");
            pack.Places.Add(new PlaceDef
            {
                Key = "TheGallery", InternalName = "TheGallery", DisplayName = "The Gallery",
                GameObjects = { new GameObjectDef { Name = "Vase" } },
            });
            pack.Scenes.Add(new SceneDef { Key = "HerFirstPose", DisplayName = "Her first pose" });
            var vm = OnALine(window, pack);
            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.SetGameObjectActive;
            action.Category = NodeActionViewModel.CatOverlay;
            action.OverlayLevel = "place:TheGallery";
            Settle(window);

            var level = BoxWith(window, action, "place:TheGallery");
            // The target picker's Level list labels a place by its name alone.
            Assert.Equal("The Gallery", TokenNote.GetNote(level));
            var drawn = TokenNote.DrawnOn(level);
            _out.WriteLine($"box {level.ActualWidth:0} wide, note drawn at {drawn}");
            Assert.False(drawn.IsEmpty, "the note was not drawn");
            var words = (TextBox)level.Template.FindName("PART_EditableTextBox", level);
            double wordsEnd = words.TranslatePoint(new Point(words.GetRectFromCharacterIndex(words.Text.Length).Right, 0), level).X;
            Assert.True(drawn.Left > wordsEnd + 4, $"the note (from {drawn.Left:0}) runs into the words (to {wordsEnd:0})");
            Assert.True(InkIn(level, drawn) > 20, "the note's place has no ink in it");
            // Nothing of the note over the words.
            Assert.Equal(0, InkIn(level, new Rect(0, 0, wordsEnd, level.ActualHeight)));
            Picture(window, level, "level");

            // The control: a word no option names gets no note.
            action.OverlayLevel = "place:Nowhere";
            Settle(window);
            var nowhere = BoxWith(window, action, "place:Nowhere");
            Assert.Equal("", TokenNote.GetNote(nowhere));
            Assert.True(TokenNote.DrawnOn(nowhere).IsEmpty);
            Assert.Equal(0, InkIn(nowhere, new Rect(0, 0, nowhere.ActualWidth, nowhere.ActualHeight)));

            // A list of bare keys (the Scene category's): the pack's scene, by its name.
            action.Category = "Scene";
            action.Target = "HerFirstPose";
            Settle(window);
            var target = BoxWith(window, action, "HerFirstPose");
            Assert.Equal("Her first pose", TokenNote.GetNote(target));
            Assert.False(TokenNote.DrawnOn(target).IsEmpty, "the scene's name was not drawn");
            Picture(window, target, "scene");
        });
    }

    /// <summary>The rows around a box, adorners and all, to look at: only when SMSMODFORGE_SHOT_DIR names a folder.</summary>
    private static void Picture(MainWindow window, FrameworkElement near, string name)
    {
        string? dir = Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        var bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        var at = near.TranslatePoint(new Point(0, 0), root);
        int x = Math.Max(0, (int)at.X - 160), y = Math.Max(0, (int)at.Y - 80);
        int w = Math.Min(760, bmp.PixelWidth - x), h = Math.Min(200, bmp.PixelHeight - y);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(new CroppedBitmap(bmp, new Int32Rect(x, y, w, h))));
        string theme = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light";
        using var f = File.Create(Path.Combine(dir, $"tokennote-{name}-{theme}.png"));
        png.Save(f);
    }

    [Theory]
    [InlineData("This pack — The Gallery (place:TheGallery)", "place:TheGallery", "This pack — The Gallery")]
    [InlineData("Vanilla — My Room (5_MyRoom)", "vanilla:5_MyRoom", "Vanilla — My Room")]
    [InlineData("Portfolio (portfolio)", "portfolio", "Portfolio")]
    [InlineData("HerFirstPose", "HerFirstPose", "")]                     // says no more than the box
    [InlineData("Anna (in her room)", "Anna_Room", "Anna (in her room)")]  // brackets that are not the word stay
    public void TheLabelLosesOnlyTheWordTheBoxAlreadyShows(string label, string token, string note)
        => Assert.Equal(note, TokenNote.Shorten(label, token));
}
