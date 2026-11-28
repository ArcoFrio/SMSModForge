using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A character picked in the place preview stays picked when Ctrl+Z takes back a change to what it is called: its
/// name typed in the panel beside the picture, or - a character with no name of its own goes by its NPC's - the NPC
/// chosen there. Found by the walk over every control once it picked things in the preview (after 1.10.0): the undo
/// made the character's row again, the preview looked for it by its path of names, and the path had the name the
/// undo had just taken away - so the panel and the handles went, and the person's next click picked something else.
/// Control: a character the undo takes out of the place is not swapped for another.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlacePickAfterUndoTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-pickundo-" + Guid.NewGuid().ToString("N"));

    public PlacePickAfterUndoTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private void MakePack()
    {
        var pack = PackRepository.CreateEmpty("pickundo.pack");
        pack.Npcs.Add(new NpcDef { Key = "guard", DisplayName = "Guard" });
        pack.Npcs.Add(new NpcDef { Key = "maid", DisplayName = "Maid" });
        var place = new PlaceDef { Key = "p", InternalName = "P", DisplayName = "P" };
        new PlaceViewModel(place);   // its NPCs node
        var npcs = place.GameObjects.Single(g => g.IsNpcRoot).Npcs;
        npcs.Add(new NpcPlacementDef { Npc = "guard", Name = "Gate", Body = new NpcTransform { X = -2 } });
        npcs.Add(new NpcPlacementDef { Npc = "guard", Name = "", Body = new NpcTransform { X = 0 } });   // goes by its NPC's key
        npcs.Add(new NpcPlacementDef { Npc = "maid", Name = "Hall", Body = new NpcTransform { X = 2 } });
        pack.Places.Add(place);
        PackRepository.Save(pack, _dir);
    }

    private static IEnumerable<T> Within<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Within<T>(child)) yield return d;
        }
    }

    [Fact]
    public void A_picked_character_stays_picked_when_its_rename_or_its_NPC_is_taken_back()
    {
        MakePack();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            vm.SelectedPlace = vm.Places.Single();
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");
            NpcPlacementViewModel Placed(int i) => vm.SelectedPlace!.NpcsNode!.Npcs[i];
            try
            {
                // What the preview shows beside the picture: every button there (its words, whether it can be pressed).
                string Shown() => string.Join(" | ", Within<Button>(preview).Where(b => b.IsVisible)
                                                         .Select(b => (b.Content as string ?? (b.Content as TextBlock)?.Text ?? "") + (b.IsEnabled ? "" : " (off)")));
                string before = "";
                // The change made as a person makes it: in the panel beside the picture, the keyboard taken away after.
                void Change(int i, Func<FrameworkElement, bool> which, Action<FrameworkElement> edit)
                {
                    Assert.True(preview.Pick(Placed(i)), $"character {i} is not in the picture");
                    WindowHarness.Pump();
                    before = Shown();
                    vm.Undo.Checkpoint();
                    var control = Within<FrameworkElement>(preview).First(c => c.DataContext == Placed(i) && which(c) && c.IsVisible);
                    control.Focus();
                    WindowHarness.Pump();
                    edit(control);
                    WindowHarness.Pump();
                    RealKeys.Elsewhere(window);
                    WindowHarness.Pump();
                }
                void TakeBack(int i, string what)
                {
                    RealKeys.Press(window, Key.Z, ModifierKeys.Control);
                    WindowHarness.Pump();
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
                    _out.WriteLine($"{what}, taken back ({vm.LastRestore}): picked {(preview.PickedRow as NpcPlacementViewModel)?.Name ?? preview.PickedRow?.ToString() ?? "nothing"}");
                    Assert.Same(Placed(i), preview.PickedRow);
                    Assert.True(preview.HasHandles, what + ": taken back, the character has no handles");
                    // And beside the picture what was there before the change: its chips, how it drifts, its settings.
                    Assert.Equal(before, Shown());
                }

                // Renamed in the panel's name box.
                var rowWas = Placed(0);
                Change(0, c => c is TextBox t && BindingPath(t, TextBox.TextProperty) == "Name", c => ((TextBox)c).Text = "Gatehouse");
                Assert.Equal("Gatehouse", Placed(0).Name);
                TakeBack(0, "renamed");
                // The undo made the character's row (and its record) again: the one picked is the new one.
                Assert.NotSame(rowWas, Placed(0));
                Assert.Equal("Gate", Placed(0).Name);

                // One going by its NPC's key, given another NPC.
                Change(1, c => c is ComboBox b && BindingPath(b, System.Windows.Controls.Primitives.Selector.SelectedValueProperty) == "Npc",
                       c => ((ComboBox)c).SelectedValue = "maid");
                Assert.Equal("maid", Placed(1).Npc);
                TakeBack(1, "its NPC changed");
                Assert.Equal("guard", Placed(1).Npc);

                // Control: one the undo takes out of the place is not swapped for another.
                Assert.True(preview.Pick(Placed(2)));
                WindowHarness.Pump();
                vm.Undo.Checkpoint();
                int count = vm.SelectedPlace!.NpcsNode!.Npcs.Count;
                vm.SelectedPlace.AddNpcCommand.Execute(null);
                WindowHarness.Pump();
                var added = vm.SelectedPlace.NpcsNode!.Npcs.Last();
                Assert.True(preview.Pick(added));
                WindowHarness.Pump();
                RealKeys.Elsewhere(window);
                RealKeys.Press(window, Key.Z, ModifierKeys.Control);
                WindowHarness.Pump();
                WindowHarness.Wait(TimeSpan.FromMilliseconds(300));
                WindowHarness.Pump();
                Assert.Equal(count, vm.SelectedPlace!.NpcsNode!.Npcs.Count);
                _out.WriteLine($"one added and taken back: picked {preview.PickedRow?.ToString() ?? "nothing"}");
                Assert.Null(preview.PickedRow);
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }

    /// <summary>
    /// As the walk over every control met it: in its pack's first place, every character with its Reflection part
    /// picked (where its NPC has one), its NPC moved to the next one its list offers (as the walk picks) - one without
    /// a reflection shows the body instead - then Ctrl+Z: the character still picked, its reflection back with its
    /// NPC, and beside the picture what was there before (the part stayed Body, Rotate and Scale on where a reflection
    /// has neither); then Ctrl+Y: still picked, as it was once edited (it was unpicked: the reflection asked of an NPC
    /// that had none).
    /// </summary>
    [Fact]
    public void Every_character_of_the_walks_place_stays_as_it_was_when_its_NPC_is_taken_back_and_brought_back()
    {
        PackRepository.Save(UndoCrawlPack.Make(_dir), _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            vm.SelectedPlace = vm.Places[0];
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");
            string Shown() => string.Join(" | ", Within<Button>(preview).Where(b => b.IsVisible)
                                                     .Select(b => (b.Content as string ?? (b.Content as TextBlock)?.Text ?? "") + (b.IsEnabled ? "" : " (off)")));
            static IEnumerable<NpcPlacementViewModel> Placed(IEnumerable<GameObjectViewModel> nodes)
            {
                foreach (var n in nodes)
                {
                    foreach (var pl in n.Npcs) { yield return pl; foreach (var c in Placed(pl.Children)) yield return c; }
                    foreach (var c in Placed(n.Children)) yield return c;
                }
            }
            int tried = 0;
            try
            {
                int count = Placed(vm.Places[0].GameObjects).Count();
                for (int i = 0; i < count; i++)
                {
                    var placed = Placed(vm.SelectedPlace!.GameObjects).ElementAt(i);
                    Assert.True(preview.Pick(placed), $"character {i} is not in the picture");
                    WindowHarness.Pump();
                    preview.SetPart(PlacePreview.GizmoPart.Reflection);
                    WindowHarness.Pump();
                    var combo = Within<ComboBox>(preview).FirstOrDefault(c => c.DataContext == placed && c.IsVisible
                        && BindingPath(c, System.Windows.Controls.Primitives.Selector.SelectedValueProperty) == "Npc");
                    if (combo == null || combo.Items.Count < 2) continue;
                    string before = Shown();
                    string npcWas = placed.Npc;
                    vm.Undo.Checkpoint();
                    combo.Focus();
                    WindowHarness.Pump();
                    int next = -1;
                    for (int k = 1; k < combo.Items.Count && next < 0; k++)
                    {
                        int j = (Math.Max(combo.SelectedIndex, 0) + k) % combo.Items.Count;
                        if (combo.Items[j] is not Separator && combo.Items[j] is not ComboBoxItem { IsEnabled: false } && j != combo.SelectedIndex) next = j;
                    }
                    if (next < 0) continue;
                    combo.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty, next);
                    WindowHarness.Pump();
                    RealKeys.Elsewhere(window);
                    WindowHarness.Pump();
                    string npcEdited = Placed(vm.SelectedPlace!.GameObjects).ElementAt(i).Npc;
                    if (npcEdited == npcWas) continue;
                    string edited = Shown();
                    tried++;
                    RealKeys.Press(window, Key.Z, ModifierKeys.Control);
                    WindowHarness.Pump();
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
                    WindowHarness.Pump();
                    var now = Placed(vm.SelectedPlace!.GameObjects).ElementAt(i);
                    _out.WriteLine($"character {i} ({npcWas}): taken back ({vm.LastRestore}), picked {(ReferenceEquals(now, preview.PickedRow) ? "it" : "something else")}");
                    Assert.Equal(npcWas, now.Npc);
                    Assert.Same(now, preview.PickedRow);
                    Assert.Equal(before, Shown());
                    // Ctrl+Y: the NPC chosen again, the character still picked, beside the picture as it was once edited.
                    RealKeys.Press(window, Key.Y, ModifierKeys.Control);
                    WindowHarness.Pump();
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
                    WindowHarness.Pump();
                    now = Placed(vm.SelectedPlace!.GameObjects).ElementAt(i);
                    _out.WriteLine($"character {i}: brought back ({vm.LastRestore}), picked {(ReferenceEquals(now, preview.PickedRow) ? "it" : preview.PickedRow == null ? "nothing" : "something else")}");
                    Assert.Equal(npcEdited, now.Npc);
                    Assert.Same(now, preview.PickedRow);
                    Assert.Equal(edited, Shown());
                    RealKeys.Press(window, Key.Z, ModifierKeys.Control);
                    WindowHarness.Pump();
                }
                Assert.True(tried > 0, "no character of the place had another NPC to choose");
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }

    private static string BindingPath(DependencyObject d, DependencyProperty p)
        => (System.Windows.Data.BindingOperations.GetBindingBase(d, p) as System.Windows.Data.Binding)?.Path?.Path ?? "";
}
