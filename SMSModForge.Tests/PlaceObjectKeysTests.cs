using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The place preview's keys and its recycle bin (after 1.10.0, the author: "copy/paste/duplicate (duplicating
/// just duplicates at the same hierarchy level, while pasting pastes under the selected GO). Deleting directly
/// from there should also be an option", "deleting the GOs actually sends them there instead just in case the
/// user needs them again later", and "copying/duplicating a GO with children should also paste the
/// children"). The rows are clicked as a mouse clicks them and the keys really pressed (RealKeys), in the main
/// window; what they did is read from the pack, the hierarchy as drawn and the page.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceObjectKeysTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-placekeys-" + Guid.NewGuid().ToString("N"));

    public PlaceObjectKeysTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Locations"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    // ───────────────────────── the pack

    private static void Png(string path, int w, int h, Color c)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>A place: a table with a cup on it and a spoon in the cup, a lamp, a bound object of the game's,
    /// and a maid standing on the NPCs object.</summary>
    private ModPack MakePack()
    {
        Png(Path.Combine(_dir, "Locations", "Room.png"), 64, 36, Colors.SaddleBrown);
        Png(Path.Combine(_dir, "Locations", "RoomB.png"), 64, 36, Colors.SteelBlue);
        var pack = PackRepository.CreateEmpty("placekeys");
        pack.Npcs.Add(new NpcDef { Key = "maid", DisplayName = "Maid" });
        var place = new PlaceDef { Key = "room", InternalName = "Room", BaseSprite = "Locations/Room.png", SecondarySprite = "Locations/RoomB.png" };
        place.GameObjects.Add(new GameObjectDef
        {
            Name = "Table", X = -2,
            Children = { new GameObjectDef { Name = "Cup", Children = { new GameObjectDef { Name = "Spoon" } } } },
        });
        place.GameObjects.Add(new GameObjectDef { Name = "Lamp", X = 3 });
        place.GameObjects.Add(new GameObjectDef { Name = "Sign", Bind = true });
        place.GameObjects.Add(new GameObjectDef
        {
            Name = "NPCs", Role = GameObjectDef.RoleNpcRoot,
            Npcs = { new NpcPlacementDef { Npc = "maid", Name = "Maid" } },
        });
        pack.Places.Add(place);
        return pack;
    }

    private void Run(Action<MainWindow, MainViewModel, PlacePreview, PlaceViewModel> body)
    {
        PackRepository.Save(MakePack(), _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            try
            {
                vm.OpenPackFromPath(_dir);
                WindowHarness.Pump();
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                WindowHarness.Pump();
                var place = vm.Places.Single();
                vm.SelectedPlace = place;
                Settle(window);
                RealKeys.Watch(window);
                var preview = (PlacePreview)window.FindName("PlacePreviewMain");
                body(window, vm, preview, place);
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }

    private static void Settle(Window window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
    }

    // ───────────────────────── the hierarchy, as drawn

    /// <summary>The rows of the hierarchy as drawn: the name each shows, whether it shows picked, and the row.</summary>
    private static List<(string Name, bool Picked, Button Row)> Rows(PlacePreview preview)
    {
        var rows = new List<(string, bool, Button)>();
        foreach (var b in preview.HierarchyList.Children.OfType<Button>())
        {
            if (b.Content is not StackPanel line || line.Children.OfType<TextBlock>().LastOrDefault() is not { } label) continue;
            string text = label.Text;
            int gap = text.IndexOf("  ", StringComparison.Ordinal);
            rows.Add((gap >= 0 ? text[(gap + 2)..] : text, ReferenceEquals(b.Background, PlacePreview.PickedRowBrush), b));
        }
        return rows;
    }

    private static string Shown(PlacePreview preview)
        => string.Join(", ", Rows(preview).Select(r => r.Picked ? "[" + r.Name + "]" : r.Name));

    /// <summary>A click on a row of the hierarchy, as the mouse makes it: pressed (the list hears it first), then
    /// clicked. The <paramref name="nth"/> row of that name.</summary>
    private static void ClickRow(Window window, PlacePreview preview, string name, int nth = 0)
    {
        var row = Rows(preview).Where(r => r.Name == name).Select(r => r.Row).ElementAtOrDefault(nth)
                  ?? throw new InvalidOperationException($"no row '{name}' in the hierarchy: {Shown(preview)}");
        Press(row);
        Settle(window);
    }

    /// <summary>The left button pressed on an element as the mouse presses it: the press goes down to it from the
    /// window (each element on the way hears its left-button press), then comes back up.</summary>
    private static void MouseDownOn(UIElement on)
    {
        on.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
        on.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseDownEvent });
    }

    private static void Press(ButtonBase button)
    {
        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static void Key(MainWindow window, Key key, ModifierKeys held = ModifierKeys.None)
    {
        RealKeys.Press(window, key, held);
        Settle(window);
    }

    private static string Names(IEnumerable<GameObjectDef> defs)
        => string.Join(", ", defs.Select(d => d.Name + (d.Children.Count > 0 ? "{" + Names(d.Children) + "}" : "")));

    /// <summary>The place's top objects but the template level's light, which the editor puts in every pack
    /// place when the game's catalog is there (TemplateLight).</summary>
    private static List<GameObjectDef> Own(PlaceViewModel place) => place.Model.GameObjects.Where(g => !TemplateLight.Is(g)).ToList();

    private static IEnumerable<string> Tops(PlaceViewModel place) => Own(place).Select(g => g.Name);

    private static ScrollViewer PageOf(FrameworkElement inside)
    {
        for (DependencyObject? at = VisualTreeHelper.GetParent(inside); at != null; at = VisualTreeHelper.GetParent(at))
            if (at is ScrollViewer sv) return sv;
        throw new InvalidOperationException("not in a scrolled page");
    }

    // ───────────────────────── the tests

    /// <summary>
    /// A row clicked puts the keyboard on the preview without moving the page; Ctrl+D duplicates the object
    /// picked beside itself, with everything under it, and picks the copy - not the place (the window's own
    /// Ctrl+D); Ctrl+C and Ctrl+V put a copy, everything under it with it, under the object picked.
    /// </summary>
    [Fact]
    public void DuplicateGoesBeside_PasteGoesUnder_BothWithEverythingUnder()
    {
        Run((window, vm, preview, place) =>
        {
            var page = PageOf(preview);
            page.ScrollToVerticalOffset(Math.Min(page.ScrollableHeight, 60));
            Settle(window);
            double scrolled = page.VerticalOffset;

            ClickRow(window, preview, "Table");
            _out.WriteLine($"Table clicked: keyboard on {Keyboard.FocusedElement?.GetType().Name}, page at {page.VerticalOffset} (was {scrolled}); {Shown(preview)}");
            Assert.Same(preview, Keyboard.FocusedElement);
            Assert.Equal(scrolled, page.VerticalOffset, 1);
            Assert.Contains(Rows(preview), r => r.Name == "Table" && r.Picked);

            int places = vm.Places.Count;
            Key(window, System.Windows.Input.Key.D, ModifierKeys.Control);
            _out.WriteLine($"Ctrl+D: {Names(Own(place))}; {Shown(preview)}");
            Assert.Equal(places, vm.Places.Count);
            Assert.Equal(new[] { "Table", "Table_copy", "Lamp", "Sign", "NPCs" }, Tops(place));
            var copy = Own(place)[1];
            Assert.NotSame(Own(place)[0], copy);
            Assert.Equal("Cup{Spoon}", Names(copy.Children));
            Assert.NotSame(Own(place)[0].Children[0], copy.Children[0]);
            Assert.Equal(new[] { "Table_copy" }, Rows(preview).Where(r => r.Picked).Select(r => r.Name));

            // Copied, then pasted under another: last among its children, everything under it with it.
            ClickRow(window, preview, "Table");
            Key(window, System.Windows.Input.Key.C, ModifierKeys.Control);
            ClickRow(window, preview, "Lamp");
            Key(window, System.Windows.Input.Key.V, ModifierKeys.Control);
            _out.WriteLine($"Table copied, Lamp picked, Ctrl+V: {Names(Own(place))}; {Shown(preview)}");
            var lamp = place.Model.GameObjects.Single(g => g.Name == "Lamp");
            Assert.Equal("Table{Cup{Spoon}}", Names(lamp.Children));
            Assert.Equal(5, Own(place).Count);
            Assert.Equal(new[] { "Table" }, Rows(preview).Where(r => r.Picked).Select(r => r.Name));
            Assert.True(Rows(preview).FindIndex(r => r.Picked) > Rows(preview).FindIndex(r => r.Name == "Lamp"), "the pasted table is not shown under the lamp");

            // Pasted under the cup, where a "Spoon" already is: renamed beside it.
            ClickRow(window, preview, "Cup");
            ClickRow(window, preview, "Spoon");
            Key(window, System.Windows.Input.Key.C, ModifierKeys.Control);
            ClickRow(window, preview, "Cup");
            Key(window, System.Windows.Input.Key.V, ModifierKeys.Control);
            _out.WriteLine($"Spoon copied, Cup picked, Ctrl+V: {Names(Own(place))}; {Shown(preview)}");
            Assert.Equal("Spoon, Spoon_copy", Names(Own(place)[0].Children[0].Children));

            // Nothing picked (a click on the picture's empty room): among the place's own, before the NPCs object.
            ClickRow(window, preview, "Lamp");
            Key(window, System.Windows.Input.Key.C, ModifierKeys.Control);
            MouseDownOn(FindCanvas(preview));
            Settle(window);
            Assert.DoesNotContain(Rows(preview), r => r.Picked);
            _out.WriteLine($"keyboard on {Keyboard.FocusedElement?.GetType().Name} before Ctrl+V; clipboard {SMSModForge.Services.EditorClipboard.Has<GameObjectDef>()}");
            Key(window, System.Windows.Input.Key.V, ModifierKeys.Control);
            _out.WriteLine($"nothing picked, Ctrl+V: {Names(Own(place))}");
            Assert.Equal(new[] { "Table", "Table_copy", "Lamp", "Sign", "Lamp_copy", "NPCs" }, Tops(place));
        });
    }

    /// <summary>The picture's own canvas: what a click on empty room lands on.</summary>
    private static Canvas FindCanvas(PlacePreview preview)
    {
        static IEnumerable<DependencyObject> Below(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var deeper in Below(child)) yield return deeper;
            }
        }
        // The biggest canvas with a background is the room; the layers over it have none.
        return Below(preview).OfType<Canvas>().Where(c => c.Background != null).OrderByDescending(c => c.ActualWidth * c.ActualHeight).First();
    }

    /// <summary>
    /// Delete sends the object picked to the recycle bin with everything under it, and the bin shows it under the
    /// hierarchy; Ctrl+Z brings it back where it was, picked, and empties the bin; Ctrl+Y sends it again; the
    /// bin's Restore puts it back where it was; and one deep in the tree goes back under the same object.
    /// </summary>
    [Fact]
    public void DeleteBinsIt_UndoAndRestoreBringItBackWhereItWas()
    {
        Run((window, vm, preview, place) =>
        {
            ClickRow(window, preview, "Table");
            Key(window, System.Windows.Input.Key.Delete);
            _out.WriteLine($"Table deleted: {Names(Own(place))}; bin {place.Model.RecycleBin.Count}; {Shown(preview)}");
            Assert.Equal(new[] { "Lamp", "Sign", "NPCs" }, Tops(place));
            var binned = Assert.Single(place.Model.RecycleBin);
            Assert.Equal("Table", binned.Object!.Name);
            Assert.Equal("Cup{Spoon}", Names(binned.Object.Children));
            Assert.DoesNotContain(Rows(preview), r => r.Name == "Table");
            Assert.DoesNotContain(Rows(preview), r => r.Picked);

            // Shown under the hierarchy, what it holds, when opened.
            Assert.Contains(preview.HierarchyList.Children.OfType<Button>(), b => (b.Content as TextBlock)?.Text.Contains("(1)") == true);
            preview.ShowBin(true);
            Settle(window);
            Assert.Equal(new[] { "Table" }, preview.BinRowsShown.Select(b => b.Display));
            Snapshot(preview, "place-bin");

            Key(window, System.Windows.Input.Key.Z, ModifierKeys.Control);
            Settle(window);
            _out.WriteLine($"Ctrl+Z: {Names(Own(place))}; bin {place.Model.RecycleBin.Count}; {Shown(preview)}");
            Assert.Equal(new[] { "Table", "Lamp", "Sign", "NPCs" }, Tops(place));
            Assert.Equal("Cup{Spoon}", Names(Own(place)[0].Children));
            Assert.Empty(place.Model.RecycleBin);
            Assert.Empty(preview.BinRowsShown);
            Assert.Equal(new[] { "Table" }, Rows(preview).Where(r => r.Picked).Select(r => r.Name));

            Key(window, System.Windows.Input.Key.Y, ModifierKeys.Control);
            Settle(window);
            Assert.Equal(new[] { "Lamp", "Sign", "NPCs" }, Tops(place));
            Assert.Single(place.Model.RecycleBin);

            // The bin's Restore, as clicked.
            var restore = preview.HierarchyList.Children.OfType<DockPanel>().Single().Children.OfType<Button>()
                                 .Single(b => (b.Content as TextBlock)?.Text == "↶");
            Press(restore);
            Settle(window);
            _out.WriteLine($"restored: {Names(Own(place))}; bin {place.Model.RecycleBin.Count}; {Shown(preview)}");
            Assert.Equal(new[] { "Table", "Lamp", "Sign", "NPCs" }, Tops(place));
            Assert.Equal("Cup{Spoon}", Names(Own(place)[0].Children));
            Assert.Empty(place.Model.RecycleBin);
            Assert.Equal(new[] { "Table" }, Rows(preview).Where(r => r.Picked).Select(r => r.Name));

            // Deep in the tree, with the place changed since: back under the same object, where it was.
            ClickRow(window, preview, "Spoon");
            Key(window, System.Windows.Input.Key.Delete);
            var spoon = Assert.Single(place.Model.RecycleBin);
            Assert.Equal("Table/Cup", spoon.UnderNames);
            Assert.Equal("Cup", Names(Own(place)[0].Children));
            place.AddGameObject().Name = "Rug";          // the top objects moved along
            Settle(window);
            ClickRow(window, preview, "Rug");
            Key(window, System.Windows.Input.Key.Delete);
            Assert.Equal(2, place.Model.RecycleBin.Count);
            Settle(window);
            var spoonRow = preview.HierarchyList.Children.OfType<DockPanel>().Single(r => ((BinnedObjectViewModel)r.Tag).Display == "Spoon");
            Press(spoonRow.Children.OfType<Button>().Single(b => (b.Content as TextBlock)?.Text == "↶"));
            Settle(window);
            Assert.Equal("Table{Cup{Spoon}}, Lamp, Sign, NPCs", Names(Own(place)));

            // For good: the cross, and Empty the bin.
            var rug = preview.HierarchyList.Children.OfType<DockPanel>().Single();
            Press(rug.Children.OfType<Button>().Single(b => (b.Content as TextBlock)?.Text == "✕"));
            Settle(window);
            Assert.Empty(place.Model.RecycleBin);
            Assert.DoesNotContain(place.Model.GameObjects, g => g.Name == "Rug");
        });
    }

    /// <summary>
    /// A character picked: Ctrl+D stands a copy beside it on the same object, Delete bins it, Restore stands it
    /// back where it was. The GameObjects list's own remove buttons send to the bin too. An object the author bound
    /// by name, with nothing known of it, is one of the pack's records like any other. The forced NPCs object is
    /// neither copied nor deleted. And keys typed in a box beside the picture are the box's: Delete there deletes
    /// a letter, not the object.
    /// </summary>
    [Fact]
    public void CharactersAndTheGamesObjects_AndKeysInABoxStayTheBoxs()
    {
        Run((window, vm, preview, place) =>
        {
            var npcs = place.Model.GameObjects.Single(g => g.IsNpcRoot);
            ClickRow(window, preview, "Maid");
            Key(window, System.Windows.Input.Key.D, ModifierKeys.Control);
            Assert.Equal(new[] { "Maid", "Maid_copy" }, npcs.Npcs.Select(n => n.Name));
            Assert.Equal(new[] { "Maid_copy" }, Rows(preview).Where(r => r.Picked).Select(r => r.Name));
            ClickRow(window, preview, "Maid");
            Key(window, System.Windows.Input.Key.Delete);
            Assert.Equal(new[] { "Maid_copy" }, npcs.Npcs.Select(n => n.Name));
            var binned = Assert.Single(place.Model.RecycleBin);
            Assert.Equal("maid", binned.Character!.Npc);
            Assert.True(place.Objects.Restore(place.Objects.Bin.Single()) is NpcPlacementViewModel);
            Assert.Equal(new[] { "Maid", "Maid_copy" }, npcs.Npcs.Select(n => n.Name));

            // The forced NPCs object: neither duplicated nor deleted.
            ClickRow(window, preview, "NPCs");
            Key(window, System.Windows.Input.Key.D, ModifierKeys.Control);
            Key(window, System.Windows.Input.Key.Delete);
            Assert.Single(place.Model.GameObjects, g => g.IsNpcRoot);
            Assert.Empty(place.Model.RecycleBin);

            // Bound by name, nothing known of it: one of the pack's records - duplicated, and binned.
            ClickRow(window, preview, "Sign");
            Key(window, System.Windows.Input.Key.D, ModifierKeys.Control);
            Assert.Equal(new[] { "Sign", "Sign_copy" }, Tops(place).Where(n => n.StartsWith("Sign", StringComparison.Ordinal)));
            Key(window, System.Windows.Input.Key.Delete);
            Assert.DoesNotContain(place.Model.GameObjects, g => g.Name == "Sign_copy");
            Assert.Equal("Sign_copy", Assert.Single(place.Model.RecycleBin).Object!.Name);
            place.Objects.EmptyBin();

            // The list's own − button: to the bin.
            place.GameObjects.Single(g => g.Name == "Lamp").RemoveCommand.Execute(null);
            Settle(window);
            Assert.Equal("Lamp", Assert.Single(place.Model.RecycleBin).Object!.Name);

            // A box beside the picture: Delete deletes a letter.
            ClickRow(window, preview, "Table");
            var name = Below(preview.SettingsPanel).OfType<TextBox>().First(b => b.IsVisible && b.Text == "Table");
            name.Focus();
            Keyboard.Focus(name);
            name.CaretIndex = 0;
            Settle(window);
            Key(window, System.Windows.Input.Key.Delete);
            _out.WriteLine($"Delete in the name box: box '{name.Text}', {Names(Own(place))}");
            Assert.Equal("able", name.Text);
            Assert.Contains(place.GameObjects, g => ReferenceEquals(g.Model, Own(place)[0]));
            Assert.Single(place.Model.RecycleBin);
        });
    }

    /// <summary>A picture of the preview to look at, when asked for (SMSMODFORGE_SNAPSHOTS): one per theme.</summary>
    private static void Snapshot(FrameworkElement what, string name)
    {
        string? shots = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(shots)) return;
        Directory.CreateDirectory(shots);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(what.ActualWidth), (int)Math.Ceiling(what.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(what);
        ThemeSnapshotTests.Save(bmp, Path.Combine(shots, name + "-" + (Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light") + ".png"));
    }

    /// <summary>
    /// The game's own light in a pack place (Light 2D, the template level's): Ctrl+D makes a light of the pack's
    /// own beside it, with its settings, standing where it does - the handles of the copy drawn where the
    /// original's are; Delete switches the game's light off, its row staying, and puts it in the bin; the bin's
    /// Restore switches it back on as it was, and so does Ctrl+Z.
    /// </summary>
    [Fact]
    public void TheGamesLight_CopiedAsALightOfThePacks_AndSwitchedOffToTheBin()
    {
        Run((window, vm, preview, place) =>
        {
            var game = place.GameObjects.SingleOrDefault(g => g.IsGameLightOnly);
            Assert.True(game != null, "no light of the game's in the place: the game's catalog is not beside the tests");
            game!.Light!.Color = "#FFAA44";
            Settle(window);
            ClickRow(window, preview, game.Name);
            var stands = preview.HandlesAt;
            Assert.NotNull(stands);
            Key(window, System.Windows.Input.Key.D, ModifierKeys.Control);
            int at = place.Model.GameObjects.IndexOf(game.Model);
            var copy = place.Model.GameObjects[at + 1];
            _out.WriteLine($"{game.Name} at {stands}; Ctrl+D: '{copy.Name}' bind {copy.Bind}, light {copy.Light?.Color}, at {preview.HandlesAt}");
            Assert.Equal(game.Name + "_copy", copy.Name);
            Assert.False(copy.Bind);
            Assert.Equal("#FFAA44", copy.Light?.Color);
            Assert.Equal(new[] { copy.Name }, Rows(preview).Where(r => r.Picked).Select(r => r.Name));
            Assert.True(preview.HandlesAt is { } there && (there - stands!.Value).Length < 1.0,
                        $"the copy stands at {preview.HandlesAt}, the game's light at {stands}");

            ClickRow(window, preview, game.Name);
            Key(window, System.Windows.Input.Key.Delete);
            _out.WriteLine($"Delete: removeLight {game.Model.RemoveLight}, light {game.Model.Light?.Color ?? "none"}, bin {place.Model.RecycleBin.Count}");
            Assert.True(game.Model.RemoveLight);
            Assert.Contains(place.GameObjects, g => ReferenceEquals(g, game));
            var binned = Assert.Single(place.Model.RecycleBin);
            Assert.True(binned.SwitchedOff);

            Key(window, System.Windows.Input.Key.Z, ModifierKeys.Control);
            Settle(window);
            Assert.False(place.GameObjects.Single(g => g.IsGameLightOnly).Model.RemoveLight);
            Assert.Empty(place.Model.RecycleBin);
            Key(window, System.Windows.Input.Key.Y, ModifierKeys.Control);
            Settle(window);
            game = place.GameObjects.Single(g => g.IsGameLightOnly);
            Assert.True(game.Model.RemoveLight);

            Assert.Same(game, place.Objects.Restore(place.Objects.Bin.Single()));
            Assert.False(game.Model.RemoveLight);
            Assert.Equal("#FFAA44", game.Model.Light?.Color);
            Assert.Empty(place.Model.RecycleBin);
        });
    }

    /// <summary>
    /// The game's own objects in one of its levels: Ctrl+D makes a copy of the game's object (CopyOf its path in the
    /// game's scene) beside it, drawn as the original is - its picture known, its handles where the original's
    /// are; Delete switches the game's object off where it stands and puts it in the bin, Restore switches it on.
    /// </summary>
    [Fact]
    public void AGameLevelsObjects_CopiedAsCopiesOfTheGames_AndSwitchedOffToTheBin()
    {
        var pack = MakePack();
        pack.VanillaExtensions.Add(new VanillaPlaceExtensionDef { Source = "vanilla:26_Downtown" });
        PackRepository.Save(pack, _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            try
            {
                vm.OpenPackFromPath(_dir);
                WindowHarness.Pump();
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                WindowHarness.Pump();
                var level = vm.VanillaExtensions.Single();
                vm.SelectedVanillaExtension = level;
                Settle(window);
                RealKeys.Watch(window);
                var preview = (PlacePreview)window.FindName("ExtPreview");
                var game = level.GameObjects.FirstOrDefault(g => g.Model.Bind && g.Model.Baseline?.SpriteRenderer != null && !g.IsGameLightOnly
                                                                 && g.StartActive && !string.IsNullOrEmpty(g.Model.VanillaArtPath));
                Assert.True(game != null, "no drawn object of the game's at the top of Downtown: the game's catalog is not beside the tests");

                Assert.True(preview.Pick(game!), $"'{game!.Name}' is not in the level's preview");
                preview.TakeKeyboard();
                Settle(window);
                var stands = preview.HandlesAt;
                Key(window, System.Windows.Input.Key.D, ModifierKeys.Control);
                int at = level.Model.GameObjects.IndexOf(game.Model);
                var copy = level.Model.GameObjects[at + 1];
                _out.WriteLine($"'{game.Name}' at {stands}; Ctrl+D: '{copy.Name}' copyOf '{copy.CopyOf}', art '{copy.VanillaArtPath}', at {preview.HandlesAt}");
                Assert.Equal(game.Name + "_copy", copy.Name);
                Assert.True(copy.IsCopy);
                Assert.Equal("5_Levels/26_Downtown/" + game.Name, copy.CopyOf);
                Assert.Equal(game.Model.VanillaArtPath, copy.VanillaArtPath);
                Assert.True(preview.HandlesAt is { } there && stands is { } was && (there - was).Length < 1.0,
                            $"the copy stands at {preview.HandlesAt}, the game's object at {stands}");

                Assert.True(preview.Pick(game));
                preview.TakeKeyboard();
                Key(window, System.Windows.Input.Key.Delete);
                _out.WriteLine($"Delete: startActive {game.StartActive}, override {game.OverrideActive}, bin {level.Model.RecycleBin.Count}");
                Assert.False(game.StartActive);
                Assert.True(game.OverrideActive);
                Assert.Contains(level.GameObjects, g => ReferenceEquals(g, game));
                Assert.True(Assert.Single(level.Model.RecycleBin).SwitchedOff);
                Assert.Same(game, level.Objects.Restore(level.Objects.Bin.Single()));
                Assert.True(game.StartActive);
                Assert.False(game.OverrideActive);
                Assert.Empty(level.Model.RecycleBin);
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }

    private static IEnumerable<DependencyObject> Below(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Below(child)) yield return deeper;
        }
    }

    /// <summary>
    /// The bin is the pack's: saved with it and there when it is opened again - and left out of what is exported,
    /// the place's objects in it as they are. A pack with nothing in a bin exports its manifest byte for byte.
    /// </summary>
    [Fact]
    public void TheBinIsSaved_AndLeftOutOfTheExport()
    {
        // Nothing binned: the manifest goes in as it is.
        PackRepository.Save(MakePack(), _dir);
        string plain = Path.Combine(_dir, "plain.smspack");
        PackExporter.Export(_dir, plain);
        Assert.Equal(File.ReadAllBytes(Path.Combine(_dir, PackExporter.ManifestEntryName)), Entry(plain));

        Run((window, vm, preview, place) =>
        {
            ClickRow(window, preview, "Table");
            Key(window, System.Windows.Input.Key.Delete);
            Assert.Single(place.Model.RecycleBin);
            vm.SavePackCommand.Execute(null);
            Settle(window);
        });

        string saved = File.ReadAllText(Path.Combine(_dir, PackExporter.ManifestEntryName));
        Assert.Contains("\"recycleBin\"", saved);
        var read = PackRepository.Load(_dir);
        var bin = Assert.Single(read.Places.Single().RecycleBin);
        Assert.Equal("Cup{Spoon}", Names(bin.Object!.Children));

        string exported = Path.Combine(_dir, "binned.smspack");
        PackExporter.Export(_dir, exported);
        string inside = System.Text.Encoding.UTF8.GetString(Entry(exported));
        _out.WriteLine($"saved {saved.Length} chars with the bin; exported {inside.Length} without");
        Assert.DoesNotContain("recycleBin", inside);
        Assert.DoesNotContain("Spoon", inside);
        Assert.Contains("\"Lamp\"", inside);
    }

    private static byte[] Entry(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        using var s = zip.GetEntry(PackExporter.ManifestEntryName)!.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
