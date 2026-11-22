using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using Polygon = System.Windows.Shapes.Polygon;
using Rectangle = System.Windows.Shapes.Rectangle;
using Path = System.IO.Path;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A place's lights are made and edited in the preview window (1.10.0, the
/// author: "of course all should be doable via the preview window"), and the
/// light every pack place starts with - the template level's - is one of them,
/// changed or removed like any other (the author: it "should be kept as is but
/// allowing the user to customize or remove it the same way a normal light
/// would"). Everything is driven through what a person clicks (the buttons'
/// own invoke, the panel's sliders) and measured from what is drawn: the
/// place's lit picture (the look's own pass, without the post), the handles'
/// shapes, the suns.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceLightEditingTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-placelights-" + Guid.NewGuid().ToString("N"));

    public PlaceLightEditingTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>A flat grey place (a light shows on it), saved.</summary>
    private void WritePack()
    {
        string art = Path.Combine(_dir, "Places", "grey.png");
        Directory.CreateDirectory(Path.GetDirectoryName(art)!);
        int w = 2048, h = 1136;
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = 90; px[i + 1] = 90; px[i + 2] = 90; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using (var fs = File.Create(art)) enc.Save(fs);
        var pack = PackRepository.CreateEmpty("lights.pack");
        pack.Places.Add(new PlaceDef { Key = "hall", InternalName = "Hall", DisplayName = "Hall", BaseSprite = "Places/grey.png" });
        PackRepository.Save(pack, _dir);
    }

    private static T Field<T>(object o, string name)
        => (T)o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(o)!;

    private static void Click(Button b) => ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)!).Invoke();

    private static string Words(Button b) => b.Content is TextBlock t ? t.Text : b.Content?.ToString() ?? "";

    /// <summary>The hierarchy's rows and buttons, as drawn.</summary>
    private static Button[] MenuButtons(PlacePreview p) => Field<StackPanel>(p, "_objectMenuList").Children.OfType<Button>().ToArray();

    private static string RowText(Button b)
        => b.Content is Panel sp ? string.Concat(sp.Children.OfType<TextBlock>().Select(t => t.Text)) : Words(b);

    /// <summary>The green channel of the lit place (the look's pass without the post), canvas pixels, rows from the top.</summary>
    private static byte[] Lit(PlacePreview p, out int w)
    {
        WindowHarness.Pump();
        var px = p.RenderStill(look: false, lights: true)!;
        w = 1920;
        return px;
    }

    private static int G(byte[] px, int w, double x, double y) => px[((int)y * w + (int)x) * 4 + 1];

    [Fact]
    public void A_light_is_added_and_edited_in_the_preview_window_and_the_template_light_removed_and_brought_back()
    {
        WritePack();
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            vm.SelectedPlace = vm.Places.First();
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");
            var place = vm.SelectedPlace!;

            // The template's light is in the hierarchy, as a light.
            var templateRow = MenuButtons(preview).FirstOrDefault(b => RowText(b).Contains("☼") && RowText(b).Contains(TemplateLight.ObjectName));
            Assert.True(templateRow != null, "the place's starting light is not in the preview's hierarchy: "
                        + string.Join(" | ", MenuButtons(preview).Select(RowText)));

            // "+ Light": a light object, picked, its settings beside the picture.
            var add = Descendants(Field<StackPanel>(preview, "_objectMenuList")).OfType<Button>().Single(b => Words(b) == Loc.T("preview.light.add"));
            var withoutNew = Lit(preview, out int w);
            Click(add);
            WindowHarness.Pump();
            var made = place.GameObjects.SingleOrDefault(g => g.Name == "Light");
            Assert.True(made?.Light != null, "+ Light made no light object");
            Assert.Equal(Visibility.Visible, Field<StackPanel>(preview, "_lightPanel").Visibility);
            Assert.Equal(Visibility.Visible, Field<StackPanel>(preview, "_lightFields").Visibility);

            // Where it is drawn: its sun, and its reach (the handles' outer circle) - both as drawn.
            var sun = Field<Canvas>(preview, "_lightLayer").Children.OfType<FrameworkElement>()
                .Single(e => e.ToolTip as string == made!.Display);
            double cx = Canvas.GetLeft(sun) + sun.Width / 2, cy = Canvas.GetTop(sun) + sun.Height / 2;
            var rings = Field<Canvas>(preview, "_gizmoLayer").Children.OfType<Ellipse>()
                .Where(e => !e.IsHitTestVisible && e.Opacity == 1 && e.StrokeDashArray.Count == 0).ToList();
            Assert.True(rings.Count >= 1, "the light's reach is not drawn");
            var ring = rings.OrderByDescending(e => e.Width).First();
            double ringR = ring.Width / 2;
            Assert.InRange(Canvas.GetLeft(ring) + ringR - cx, -1.0, 1.0);
            _out.WriteLine($"the new light's sun at {cx:F0},{cy:F0}, its reach drawn {ringR:F0} canvas px");

            // The light in the picture: brighter inside its reach, untouched outside it - where the handles say.
            var with = Lit(preview, out _);
            int Change(double dx, double dy) => G(with, w, cx + dx, cy + dy) - G(withoutNew, w, cx + dx, cy + dy);
            int atMiddle = Change(0, 0), inside = Change(ringR * 0.7, 0), outside = Change(ringR * 1.08, 0);
            _out.WriteLine($"lit by it: at its middle +{atMiddle}, at 0.7 of its reach +{inside}, just past it +{outside}");
            Assert.True(atMiddle > 10, "the new light does not light its middle");
            Assert.True(inside > 0, "the new light does not reach where its circle says");
            Assert.Equal(0, outside);

            // Stronger, through the panel's slider: brighter at its middle (control: as it was).
            var sliders = Field<StackPanel>(preview, "_lightFields").Children.OfType<Panel>()
                .SelectMany(Descendants).OfType<Slider>().ToList();
            var strength = sliders.First(s => s.GetBindingExpression(Slider.ValueProperty)?.ParentBinding.Path.Path == nameof(LightViewModel.Strength));
            strength.Value = 1.0;
            WindowHarness.Pump();
            Assert.Equal(1.0f, made!.Light!.Strength);
            var stronger = Lit(preview, out _);
            Assert.True(G(stronger, w, cx, cy) > G(with, w, cx, cy) + 5, "a stronger light is no brighter");

            // A cone, through its slider: it lights above its middle and no longer below it.
            var cone = sliders.First(s => s.GetBindingExpression(Slider.ValueProperty)?.ParentBinding.Path.Path == nameof(LightViewModel.OuterAngle));
            cone.Value = 90;
            WindowHarness.Pump();
            var coned = Lit(preview, out _);
            int above = G(coned, w, cx, cy - ringR * 0.5) - G(withoutNew, w, cx, cy - ringR * 0.5);
            int below = G(coned, w, cx, cy + ringR * 0.5) - G(withoutNew, w, cx, cy + ringR * 0.5);
            int belowBefore = G(stronger, w, cx, cy + ringR * 0.5) - G(withoutNew, w, cx, cy + ringR * 0.5);
            _out.WriteLine($"a 90 degree cone: above its middle +{above}, below it +{below} (+{belowBefore} before the cone)");
            Assert.True(above > 5, "the cone does not light above its middle");
            Assert.True(belowBefore > 5, "control: the light did not reach below its middle before the cone");
            Assert.Equal(0, below);

            // A free shape, through its chip: a square of four corners with a handle each and a "+" on each side,
            // lighting inside its outline and not well outside it.
            var freeChip = Descendants(Field<StackPanel>(preview, "_lightPanel")).OfType<Button>().Single(b => Words(b) == Loc.T("preview.light.shape.free"));
            Click(freeChip);
            WindowHarness.Pump();
            Assert.True(made.Light!.IsFree, "the Free shape chip did not make the light free");
            var gizmo = Field<Canvas>(preview, "_gizmoLayer");
            var outline = gizmo.Children.OfType<Polygon>().Single(p => p.Opacity == 1 && !p.IsHitTestVisible);
            var corners = gizmo.Children.OfType<Ellipse>().Where(e => e.ToolTip as string == Loc.T("preview.light.point.tip")).ToList();
            var pluses = gizmo.Children.OfType<Rectangle>().Where(e => e.ToolTip as string == Loc.T("preview.light.addPoint.tip")).ToList();
            Assert.Equal(4, outline.Points.Count);
            Assert.Equal(4, corners.Count);
            Assert.Equal(4, pluses.Count);
            // The handles sit on the outline's corners, as drawn.
            foreach (var k in corners)
            {
                var at = new Point(Canvas.GetLeft(k) + k.Width / 2, Canvas.GetTop(k) + k.Height / 2);
                Assert.Contains(outline.Points, q => (q - at).Length < 1.0);
            }
            double minX = outline.Points.Min(q => q.X), maxX = outline.Points.Max(q => q.X), midY = outline.Points.Average(q => q.Y);
            double midX = (minX + maxX) / 2, across = maxX - minX;
            var free = Lit(preview, out _);
            int insideFree = G(free, w, midX, midY) - G(withoutNew, w, midX, midY);
            int farOutside = G(free, w, maxX + across, midY) - G(withoutNew, w, maxX + across, midY);
            _out.WriteLine($"the free square {across:F0} canvas px across: lit +{insideFree} inside, +{farOutside} a width beyond it");
            Assert.True(insideFree > 5, "the free shape does not light inside its outline");
            Assert.Equal(0, farOutside);
            // A corner right-clicked away: three left; and no fewer than three.
            var rightClick = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
            { RoutedEvent = UIElement.MouseRightButtonDownEvent };
            corners[0].RaiseEvent(rightClick);
            WindowHarness.Pump();
            Assert.Equal(3, made.Light!.Points.Count);
            var three = Field<Canvas>(preview, "_gizmoLayer").Children.OfType<Ellipse>().First(e => e.ToolTip as string == Loc.T("preview.light.point.tip"));
            three.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
            { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            WindowHarness.Pump();
            Assert.Equal(3, made.Light!.Points.Count);
            // Back to round for what follows.
            Click(Descendants(Field<StackPanel>(preview, "_lightPanel")).OfType<Button>().Single(b => Words(b) == Loc.T("preview.light.shape.round")));
            WindowHarness.Pump();
            Assert.True(made.Light!.IsRound);

            // The template's light: picked from the hierarchy, removed, then brought back.
            Click(MenuButtons(preview).First(b => RowText(b).Contains(TemplateLight.ObjectName)));
            WindowHarness.Pump();
            var template = place.GameObjects.Single(g => g.IsTemplateLight);
            Assert.NotNull(template.Light);
            var remove = Descendants(Field<StackPanel>(preview, "_lightPanel")).OfType<Button>().Single(b => Words(b) == Loc.T("preview.light.remove"));
            var withTemplate = Lit(preview, out _);
            Click(remove);
            WindowHarness.Pump();
            Assert.True(template.LightRemoved && template.Light == null, "Remove light left the template's light on");
            var withoutTemplate = Lit(preview, out _);
            // The template's light is cyan, over the whole place: far from the new light, the place is less blue without it.
            int Blue(byte[] px) => px[(100 * w + 100) * 4];
            _out.WriteLine($"a corner's blue: {Blue(withTemplate)} with the template's light, {Blue(withoutTemplate)} without");
            Assert.True(Blue(withTemplate) > Blue(withoutTemplate), "removing the template's light changed nothing drawn");
            vm.SavePack();
            string saved = File.ReadAllText(Path.Combine(_dir, "modpack.json"));
            Assert.Contains("\"removeLight\": true", saved);

            var restore = Descendants(Field<StackPanel>(preview, "_lightPanel")).OfType<Button>().Single(b => Words(b) == Loc.T("preview.light.restore"));
            Assert.Equal(Visibility.Visible, restore.Visibility);
            Click(restore);
            WindowHarness.Pump();
            Assert.True(!template.LightRemoved && template.Light != null, "Back to the game's did not bring the light back");
            vm.SavePack();
            saved = File.ReadAllText(Path.Combine(_dir, "modpack.json"));
            Assert.DoesNotContain(TemplateLight.ObjectName, saved);
            Assert.Contains("\"light\"", saved);   // the new light is saved
            Assert.Equal(Blue(withTemplate), Blue(Lit(preview, out _)));

            if (Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR") is { Length: > 0 } shots)
            {
                // Picked again, for a person to look at the panel and the handles.
                Click(MenuButtons(preview).First(b => RowText(b).Contains("☼") && !RowText(b).Contains(TemplateLight.ObjectName)));
                WindowHarness.Pump();
                preview.UpdateLayout();
                var bmp = new RenderTargetBitmap((int)preview.ActualWidth, (int)preview.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(preview);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(Path.Combine(shots, "place-lights-" + (Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light") + ".png")))
                    enc.Save(fs);
                // And as a free shape.
                Click(Descendants(Field<StackPanel>(preview, "_lightPanel")).OfType<Button>().Single(b => Words(b) == Loc.T("preview.light.shape.free")));
                WindowHarness.Pump();
                preview.UpdateLayout();
                var free2 = new RenderTargetBitmap((int)preview.ActualWidth, (int)preview.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                free2.Render(preview);
                var enc2 = new PngBitmapEncoder();
                enc2.Frames.Add(BitmapFrame.Create(free2));
                using (var fs2 = File.Create(Path.Combine(shots, "place-lights-free-" + (Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light") + ".png")))
                    enc2.Save(fs2);
            }
        });
    }

    /// <summary>
    /// "+ Darkness" (1.10.0): the place's own base light, made from the preview window, picked, its panel the whole
    /// place's (no falloff or glow, its tick box about darkening the characters); the place drawn darker, measured
    /// against the same place with the darkness switched off (the control).
    /// </summary>
    [Fact]
    public void A_place_is_darkened_from_the_preview_window()
    {
        WritePack();
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            vm.SelectedPlace = vm.Places.First();
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");
            var day = Lit(preview, out int w);

            var add = Descendants(Field<StackPanel>(preview, "_objectMenuList")).OfType<Button>().Single(b => Words(b) == Loc.T("preview.light.addDarkness"));
            Click(add);
            WindowHarness.Pump();
            var dark = vm.SelectedPlace!.GameObjects.SingleOrDefault(g => g.Name == "Darkness");
            Assert.True(dark?.Light?.IsAmbient == true, "+ Darkness made no whole-place light");
            Assert.Equal(Visibility.Collapsed, Field<StackPanel>(preview, "_lampFields").Visibility);
            Assert.Equal(Loc.T("preview.light.ambientCharacters"), Field<TextBlock>(preview, "_charactersLabel").Text);

            var night = Lit(preview, out _);
            int dayG = G(day, w, 960, 540), nightG = G(night, w, 960, 540);
            dark!.StartActive = false;
            WindowHarness.Pump();
            int offG = G(Lit(preview, out _), w, 960, 540);
            _out.WriteLine($"the place's middle (green): {dayG} by day, {nightG} with the darkness, {offG} with it switched off");
            Assert.True(nightG < dayG - 20, "the darkness does not darken the place");
            Assert.Equal(dayG, offG);   // control: switched off, the place is as it was
        });
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root is Panel p)
            foreach (UIElement c in p.Children)
            {
                yield return c;
                foreach (var d in Descendants(c)) yield return d;
            }
        else if (root is Decorator dec && dec.Child != null)
        {
            yield return dec.Child;
            foreach (var d in Descendants(dec.Child)) yield return d;
        }
        else if (root is ContentControl cc && cc.Content is DependencyObject content)
        {
            yield return content;
            foreach (var d in Descendants(content)) yield return d;
        }
    }
}
