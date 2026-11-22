using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;
using Path = System.IO.Path;

namespace SMSModForge.Tests;

/// <summary>
/// Undo and redo of every way a light is changed beside the place preview
/// (1.10.0, the author: "the undo/redo controls on lighting aren't working as
/// intended"). What was wrong: the pack was put back, but the panel beside the
/// picture went on showing the light that had been replaced - its sliders at
/// the values the undo had taken back, and moving them changed nothing.
/// For each edit - the light added, each slider, each box, the tick, each
/// shape, a corner taken away, the light moved, removed, given back:
/// <list type="bullet">
/// <item>undo makes the pack what it was before the edit, redo what it was
/// after it;</item>
/// <item>after each, the panel shows the light of the object picked, that
/// light is the pack's, and every slider of the panel is at the pack's value;</item>
/// <item>a slider moved after an undo changes the pack (the panel is not
/// showing a light the undo replaced).</item>
/// </list>
/// Control: the panel given another light is told apart.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceLightUndoTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-lightundo-" + Guid.NewGuid().ToString("N"));

    public PlaceLightUndoTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Places"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static T Field<T>(object o, string name)
        => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static void Click(Button b) => ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static string Words(Button b) => b.Content is TextBlock t ? t.Text : b.Content?.ToString() ?? "";

    private static IEnumerable<GameObjectViewModel> Walk(IEnumerable<GameObjectViewModel> nodes)
    {
        foreach (var n in nodes) { yield return n; foreach (var c in Walk(n.Children)) yield return c; }
    }

    [Fact]
    public void EveryEditOfALightIsUndoneAndRedone_AndThePanelShowsThePacksLight()
    {
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Places", "grey.png"), 2048, 1136, Color.FromRgb(0x50, 0x50, 0x50));
        var pack = PackRepository.CreateEmpty("lightundo.pack");
        pack.Places.Add(new PlaceDef { Key = "hall", InternalName = "Hall", DisplayName = "Hall", BaseSprite = "Places/grey.png" });
        PackRepository.Save(pack, _dir);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());

        var wrong = new List<string>();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            vm.SelectedPlace = vm.Places.First();
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");

            string State() => PackRepository.Serialize(vm.Pack);
            GameObjectViewModel? Light() => vm.SelectedPlace?.GameObjects.FirstOrDefault(g => g.Name == "Light");

            // What is wrong with the panel, or null: the object picked is one of the place shown, the panel's
            // light is that object's and the pack's, and every slider is at the pack's value.
            string? Panel()
            {
                var picked = Field<GameObjectViewModel?>(preview, "_selNode");
                var shown = Field<StackPanel>(preview, "_lightPanel").DataContext as LightViewModel;
                if (picked != null && (vm.SelectedPlace == null || !Walk(vm.SelectedPlace.GameObjects).Contains(picked)))
                    return "the object picked is not one of the place shown";
                if (shown == null) return null;
                if (!ReferenceEquals(shown, picked?.Light)) return "the panel shows another light than the picked object's";
                if (picked == null || !ReferenceEquals(shown.Model, picked.Model.Light)) return "the panel's light is not the pack's";
                foreach (var slider in Descendants(Field<StackPanel>(preview, "_lightFields")).OfType<Slider>().Where(s => s.IsVisible))
                {
                    string? path = slider.GetBindingExpression(RangeBase.ValueProperty)?.ParentBinding.Path.Path;
                    if (path == null || typeof(LightViewModel).GetProperty(path)?.GetValue(shown) is not { } value) continue;
                    double packs = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (Math.Abs(packs - slider.Value) > 1e-4) return $"the {path} slider is at {slider.Value:0.###}, the pack's light at {packs:0.###}";
                }
                return null;
            }

            void Undo() { typeof(MainWindow).GetMethod("CommitPendingEdits", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null); vm.UndoCommand.Execute(null); WindowHarness.Pump(); }
            void Redo() { typeof(MainWindow).GetMethod("CommitPendingEdits", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null); vm.RedoCommand.Execute(null); WindowHarness.Pump(); }

            Slider SliderOf(string path) => Descendants(Field<StackPanel>(preview, "_lightFields")).OfType<Slider>()
                .First(s => s.GetBindingExpression(RangeBase.ValueProperty)?.ParentBinding.Path.Path == path);
            TextBox BoxOf(string path) => Descendants(Field<StackPanel>(preview, "_lightFields")).OfType<TextBox>()
                .First(s => s.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == path);
            Button Chip(string key) => Descendants(Field<StackPanel>(preview, "_lightPanel")).OfType<Button>().First(b => Words(b) == Loc.T(key));
            void Drag(Slider s, double to)
            {
                // The thumb dragged (its value moving through the binding), then let go.
                s.Value = (s.Value + to) / 2;
                s.Value = to;
                s.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            }
            void Type(TextBox box, string text)
            {
                box.Focus();
                Keyboard.Focus(box);
                box.Text = text;
                box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, 0, Key.Enter) { RoutedEvent = UIElement.KeyDownEvent });
            }
            void TypeAndLeave(TextBox box, string text, UIElement other)
            {
                box.Focus();
                Keyboard.Focus(box);
                box.Text = text;
                other.Focus();
                Keyboard.Focus(other);
            }
            void Pick()
            {
                var row = Field<StackPanel>(preview, "_objectMenuList").Children.OfType<Button>()
                    .FirstOrDefault(b => b.Content is Panel sp && sp.Children.OfType<TextBlock>().Any(t => t.Text.EndsWith("Light", StringComparison.Ordinal) && !t.Text.Contains("2D")));
                if (row != null) { Click(row); WindowHarness.Pump(); }
            }

            void Try(string what, Action act)
            {
                WindowHarness.Pump();
                vm.Undo.Checkpoint();
                string before = State();
                int depth = vm.Undo.Depth;
                act();
                WindowHarness.Pump();
                string after = State();
                if (after == before) { wrong.Add(what + ": changed nothing of the pack"); return; }
                Undo();
                if (State() != before) wrong.Add(what + ": undo did not make the pack what it was");
                else if (vm.Undo.Depth != depth) wrong.Add($"{what}: after undo the history is {vm.Undo.Depth} steps deep, was {depth}");
                if (Panel() is { } afterUndo) wrong.Add(what + ", after undo: " + afterUndo);
                Redo();
                if (State() != after) wrong.Add(what + ": redo did not make the pack what the edit had");
                if (Panel() is { } afterRedo) wrong.Add(what + ", after redo: " + afterRedo);
                _out.WriteLine($"{what}: undone and redone ({vm.LastRestore})");
            }

            var add = Descendants(Field<StackPanel>(preview, "_objectMenuList")).OfType<Button>().Single(b => Words(b) == Loc.T("preview.light.add"));
            Try("+ Light", () => Click(add));
            Assert.NotNull(Light());
            Pick();
            Assert.NotNull(Field<StackPanel>(preview, "_lightPanel").DataContext as LightViewModel);
            Assert.Null(Panel());

            // Control: the panel given another light is told apart.
            var real = Field<StackPanel>(preview, "_lightPanel").DataContext;
            Field<StackPanel>(preview, "_lightPanel").DataContext = new LightViewModel(new LightDef(), () => { });
            Assert.NotNull(Panel());
            Field<StackPanel>(preview, "_lightPanel").DataContext = real;
            Assert.Null(Panel());

            Try("strength slider dragged", () => Drag(SliderOf(nameof(LightViewModel.Strength)), 1.3));
            Pick();
            Try("falloff slider dragged", () => Drag(SliderOf(nameof(LightViewModel.Falloff)), 0.8));
            Pick();
            Try("glow slider dragged", () => Drag(SliderOf(nameof(LightViewModel.Glow)), 0.6));
            Pick();
            Try("reach typed, Enter", () => Type(BoxOf(nameof(LightViewModel.OuterRadius)), "5"));
            Pick();
            Try("full-strength reach typed, box left", () => TypeAndLeave(BoxOf(nameof(LightViewModel.InnerRadius)), "1.5", BoxOf(nameof(LightViewModel.OuterRadius))));
            Pick();
            Try("colour typed, Enter", () => Type(BoxOf(nameof(LightViewModel.Color)), "#FF8800"));
            Pick();
            Try("cone slider dragged", () => Drag(SliderOf(nameof(LightViewModel.OuterAngle)), 90));
            Pick();
            Try("lights the characters ticked", () =>
            {
                var tick = Descendants(Field<StackPanel>(preview, "_lightFields")).OfType<CheckBox>().Single();
                typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tick, null);
            });
            Pick();
            Try("shape: free", () => Click(Chip("preview.light.shape.free")));
            Pick();
            Try("a corner right-clicked away", () =>
            {
                var corner = Field<Canvas>(preview, "_gizmoLayer").Children.OfType<System.Windows.Shapes.Ellipse>().First(e => e.ToolTip as string == Loc.T("preview.light.point.tip"));
                corner.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonDownEvent });
            });
            Pick();
            Try("shape: whole place", () => Click(Chip("preview.light.shape.ambient")));
            Pick();
            Try("shape: round", () => Click(Chip("preview.light.shape.round")));
            Pick();
            Try("the light moved", () => { var l = Light()!; l.X += 1; preview.RaiseEvent(new RoutedEventArgs(PlacePreview.EditCommittedEvent, preview)); });
            Pick();
            Try("Remove light", () => Click(Chip("preview.light.remove")));
            Pick();
            Try("Give it a light", () => Click(Chip("preview.light.give")));
            Pick();

            // After an undo, the panel still changes the pack's light.
            vm.Undo.Checkpoint();
            string rest = State();
            Drag(SliderOf(nameof(LightViewModel.Strength)), 0.9);
            WindowHarness.Pump();
            Undo();
            Assert.Equal(rest, State());
            Pick();
            Drag(SliderOf(nameof(LightViewModel.Strength)), 0.7);
            WindowHarness.Pump();
            Assert.NotEqual(rest, State());
            Assert.Equal(0.7f, Light()!.Model.Light!.Strength, 3);
            Undo();
            Assert.Equal(rest, State());
            Pick();

            // Three edits, then undone one by one and redone one by one.
            string s0 = State();
            Drag(SliderOf(nameof(LightViewModel.Strength)), 0.4); WindowHarness.Pump();
            string s1 = State();
            Pick();
            Drag(SliderOf(nameof(LightViewModel.Glow)), 0.2); WindowHarness.Pump();
            string s2 = State();
            Pick();
            Type(BoxOf(nameof(LightViewModel.OuterRadius)), "2"); WindowHarness.Pump();
            string s3 = State();
            var seen = new List<string>();
            foreach (var step in new Action[] { Undo, Undo, Undo, Redo, Redo, Redo })
            {
                step();
                string now = State();
                seen.Add(now == s0 ? "0" : now == s1 ? "1" : now == s2 ? "2" : now == s3 ? "3" : "?");
                if (Panel() is { } off) wrong.Add("three edits walked back and forward, at " + seen[^1] + ": " + off);
            }
            _out.WriteLine("three edits (1, 2, 3 from 0), undone three times and redone three times, went through: " + string.Join(" ", seen));
            Assert.Equal("2 1 0 1 2 3", string.Join(" ", seen));

            typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        });
        foreach (string w in wrong) _out.WriteLine("WRONG: " + w);
        Assert.Empty(wrong);
    }
}
