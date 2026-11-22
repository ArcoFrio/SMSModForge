using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Tutorials;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The guide's pictures (1.10.0): each tab of the editor with the tutorials' story pack in it, one item chosen, for
/// the reader on the ModForge tab - a picture of the part of the editor a topic is about. Taken in the look of the
/// run (SMSMODFORGE_TEST_THEME; an EX theme writes &lt;topic&gt;.EX.png) into SMSMODFORGE_GUIDE_SHOTS, and only when
/// that names a folder; Tools/Docs/guide-pictures.py makes them the editor's Resources/GuidePictures.
/// <para/>
/// Only the pack's own art is in them: the bust preview's backdrop is set to None first, so none of the game's
/// pictures go out inside the editor's pictures.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class GuidePictureShots
{
    private static FrameworkElement? Named(DependencyObject root, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is FrameworkElement f && f.Name == name) return f;
            if (Named(c, name) is { } found) return found;
        }
        return null;
    }

    private static void Save(FrameworkElement what, string file)
    {
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(what.ActualWidth), (int)Math.Ceiling(what.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(GuideSectionShots.Exact(what, what.RenderSize), null, new Rect(0, 0, what.ActualWidth, what.ActualHeight));
        bmp.Render(dv);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(file);
        png.Save(f);
    }

    /// <summary>Each tab's page of the guide: the tab, the topic's picture name, and what to choose on it first.</summary>
    internal static List<(int Tab, string Topic, Action Pick)> Picks(MainViewModel vm)
    {
        var her = StoryPack.Dummy(vm);
        return new()
        {
            (MainTab.Characters, "characters", () =>
            {
                if (her == null) return;
                vm.SelectedCharacter = her;
                her.IsExpanded = true;
                if (her.Outfits.Count > 0) vm.SelectedOutfit = her.Outfits[^1];
            }),
            (MainTab.Npcs, "npcs", () => { if (vm.Npcs.Count > 0) vm.SelectedNpc = vm.Npcs[0]; }),
            (MainTab.Places, "places", () => { if (StoryPack.Gallery(vm) is { } g) vm.SelectedPlace = g; }),
            (MainTab.MapButtons, "mapButtons", () => { if (vm.MapButtons.Count > 0) vm.SelectedMapButton = vm.MapButtons[0]; }),
            (MainTab.Dialogues, "dialogues", () => TutorialsPart2.ShowHerLine(vm, TutorialsPart2.SweetOf)),
            (MainTab.Scenes, "scenes", () => { if (vm.Scenes.Count > 0) vm.SelectedScene = vm.Scenes[0]; }),
            (MainTab.Music, "music", () => { if (vm.Music.Count > 0) vm.SelectedMusic = vm.Music[0]; }),
            (MainTab.Sfx, "sfx", () => { if (vm.Sfx.Count > 0) vm.SelectedSfx = vm.Sfx[0]; }),
            (MainTab.Wallpapers, "wallpapers", () => { if (vm.Wallpapers.Count > 0) vm.SelectedWallpaper = vm.Wallpapers[^1]; }),
            (MainTab.Fx, "fx", () => { if (vm.Effects.Count > 0) vm.SelectedEffect = vm.Effects[0]; }),
            (MainTab.Ui, "ui", () => { if (vm.Uis.Count > 0) vm.SelectedUi = vm.Uis[0]; }),
            (MainTab.Quests, "quests", () => { if (vm.Quests.Count > 0) vm.SelectedQuest = vm.Quests[^1]; }),
            (MainTab.Variables, "variables", () =>
            {
                if (vm.Variables.FirstOrDefault(v => v.Name == TutorialsPart2.Confidence) is { } v) vm.SelectedVariable = v;
            }),
            (MainTab.Integration, "integration", () =>
            {
                if (vm.IntegrationRules.FirstOrDefault(r => r.Model.Branches.Count > 0) is { } r) vm.SelectedIntegrationRule = r;
            }),
            (MainTab.ModForge, "modforgeTab", () => vm.HomePage = HomePage.Home),
        };
    }

    [Fact]
    public void TakeThem()
    {
        string? dir = Environment.GetEnvironmentVariable("SMSMODFORGE_GUIDE_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1500; window.Height = 940;
            var vm = (MainViewModel)window.DataContext;
            using var walker = new TutorialWalker(vm);
            // The whole story: everything the tutorials make, as Bring in does it.
            var last = TutorialCatalog.All.Last(t => t.IsOnLadder);
            StoryPack.BringIn(vm, last);
            StoryPack.Replay(vm, last);
            Services.PreviewBackdropManager.Apply(Services.PreviewBackdropManager.All.First(b => b.IsNone));
            string look = Services.ThemeManager.Current.IsEx ? ".EX" : "";
            var tabs = (TabControl)window.FindName("MainTabs");

            void Shot(int tab, string topic, Action pick)
            {
                vm.SelectedTabIndex = tab;
                WindowHarness.Wait(TimeSpan.FromMilliseconds(300));
                pick();
                WindowHarness.Wait(TimeSpan.FromMilliseconds(1200));
                window.UpdateLayout();
                WindowHarness.Pump();
                var host = Named(tabs, "PART_SelectedContentHost") ?? tabs;
                Save(host, Path.Combine(dir, topic + look + ".png"));
            }

            foreach (var (tab, topic, pick) in Picks(vm)) Shot(tab, topic, pick);

            typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        });
    }
}
