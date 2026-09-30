using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The NPCs category (the author, 1.6.3): a level, then one of the NPCs placed
/// in it - on SetGameObjectActive, SetSprite, the GameObjectActive condition and
/// the Fade / Move / Spin actions that share the GameObjects list. The NPCs
/// themselves leave the GameObjects list; the groups they stand in stay.
/// </summary>
public sealed class NpcCategoryListsTests
{
    [Fact]
    public void EveryRowThatOffersGameObjectsOffersNpcs()
    {
        Assert.Contains(NodeActionViewModel.CatNpcs, NodeActionViewModel.SetActiveCategories);
        Assert.Contains(NodeActionViewModel.CatNpcs, NodeActionViewModel.SetSpriteCategories);
        Assert.Contains(NodeActionViewModel.CatNpcs, NodeActionViewModel.GoCategories);
        Assert.Contains(NodeActionViewModel.CatNpcs, NodeConditionViewModel.GoCategories);
        // Stored as the game reads it, and shown in words.
        Assert.Equal("NPCs", NodeActionViewModel.CatNpcs);
        Assert.Equal("NPCs", ParamSchema.ChoiceText("kind", NodeActionViewModel.CatNpcs));
    }

    [Fact]
    public void TheGamesOwnNpcsAreKnownByTheMaterialTheyAreDrawnWith()
    {
        var downtown = VanillaLevelCatalog.FindLevel("26_Downtown");
        if (downtown == null) return;   // no extraction shipped beside the tests

        Assert.True(VanillaLevelCatalog.IsNpc(VanillaLevelCatalog.FindNode(downtown, "NPCs/Group_1/NPC")));
        Assert.True(VanillaLevelCatalog.IsNpc(VanillaLevelCatalog.FindNode(downtown, "NPCs/Group_1/NPC (2)")));
        // Not the group they stand in, nor their floor reflection.
        Assert.False(VanillaLevelCatalog.IsNpc(VanillaLevelCatalog.FindNode(downtown, "NPCs/Group_1")));
        Assert.False(VanillaLevelCatalog.IsNpc(VanillaLevelCatalog.FindNode(downtown, "NPCs/Group_1/NPC (2)/Square (1)")));
        Assert.False(VanillaLevelCatalog.IsNpc(null));
    }
}

[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NpcCategoryTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-npccat-" + Guid.NewGuid().ToString("N"));

    public NpcCategoryTests(ITestOutputHelper o)
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

    private void WritePng(string relative, int width, int height)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>
    /// A pack open on a node, with a place "home" holding a lamp and NPCs in
    /// two groups - one named (AnisCouch, 70 wide), and one unnamed NPC placed
    /// twice (by its key, anisSwim, 60 wide) - a place "yard" with a lamp and no
    /// NPCs, a scene (256) and a bust (90).
    /// </summary>
    private MainViewModel APack(MainWindow window)
    {
        WritePng("NPCs/default.png", 70, 150);
        WritePng("NPCs/swim.png", 60, 140);
        WritePng("NPCs/other.png", 50, 130);
        WritePng("Objects/lamp.png", 64, 64);
        WritePng("Scenes/photo.png", 256, 256);
        WritePng("Busts/elf.png", 90, 240);

        var pack = PackRepository.CreateEmpty("npccat.pack");
        pack.Npcs.Add(new NpcDef { Key = "anisDefault", Sprite = "NPCs/default.png" });
        pack.Npcs.Add(new NpcDef { Key = "anisSwim", Sprite = "NPCs/swim.png" });
        var couch = new GameObjectDef { Name = "Couch" };
        couch.Npcs.Add(new NpcPlacementDef { Npc = "anisDefault", Name = "AnisCouch" });
        couch.Npcs.Add(new NpcPlacementDef { Npc = "anisSwim" });
        var bed = new GameObjectDef { Name = "Bed" };
        bed.Npcs.Add(new NpcPlacementDef { Npc = "anisSwim" });
        var root = new GameObjectDef { Name = "NPCs", Role = GameObjectDef.RoleNpcRoot, Children = { couch, bed } };
        pack.Places.Add(new PlaceDef
        {
            Key = "home", InternalName = "Home", DisplayName = "Home",
            GameObjects = { root, new GameObjectDef { Name = "Lamp", Sprite = "Objects/lamp.png" } },
        });
        pack.Places.Add(new PlaceDef
        {
            Key = "yard", InternalName = "Yard", DisplayName = "Yard",
            GameObjects = { new GameObjectDef { Name = "Lamp", Sprite = "Objects/lamp.png" } },
        });
        pack.Scenes.Add(new SceneDef { Key = "photo", DisplayName = "Photo", SceneSprite = "Scenes/photo.png" });
        var elf = new CharacterDef { Key = "elf", DisplayName = "Elf" };
        elf.Outfits.Add(new OutfitDef { Key = "default", GameObjectName = "Elf_Default", BaseSprite = "Busts/elf.png" });
        pack.Characters.Add(elf);
        PackRepository.Save(pack, _dir);

        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues") { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();
        vm.AddDialogueCommand.Execute(null);
        WindowHarness.Pump();
        if (vm.SelectedDialogue!.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
        vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
        WindowHarness.Pump();
        return vm;
    }

    private static readonly string[] TheNpcsOfHome =
        { "AnisCouch", "NPCs > Couch > anisSwim", "NPCs > Bed > anisSwim" };

    private static List<FrameworkElement> Previews(MainWindow window, object row)
        => Descendants<FrameworkElement>(window)
           .Where(e => (e is ScenePreview || e is ArtThumb) && e.IsVisible && ReferenceEquals(e.DataContext, row))
           .ToList();

    private static BitmapSource? Picture(ArtThumb thumb)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (thumb.Picture == null && thumb.Message.Length == 0 && DateTime.UtcNow < until)
            WindowHarness.Wait(TimeSpan.FromMilliseconds(20));
        return thumb.Picture as BitmapSource;
    }

    private static int ThumbWidth(MainWindow window, object row)
        => Picture(Assert.IsType<ArtThumb>(Assert.Single(Previews(window, row))))!.PixelWidth;

    /// <summary>What the Target field's label says on this row.</summary>
    private static List<string> Labels(MainWindow window, object row)
        => Descendants<TextBlock>(window)
           .Where(t => t.IsVisible && ReferenceEquals(t.DataContext, row))
           .Select(t => t.Text).ToList();

    [Fact]
    public void TheNpcsLeaveTheGameObjectsList_AndTheGroupsTheyStandInStay()
    {
        WindowHarness.Run(window =>
        {
            var vm = APack(window);
            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.SetGameObjectActive;
            action.Category = NodeActionViewModel.CatOverlay;
            action.OverlayLevel = "place:home";
            WindowHarness.Pump();

            var objects = action.OverlayOptions.ToList();
            _out.WriteLine("GameObjects: " + string.Join(" | ", objects));
            Assert.Contains("NPCs", objects);
            Assert.Contains("NPCs > Couch", objects);
            Assert.Contains("NPCs > Bed", objects);
            Assert.Contains("Lamp", objects);
            Assert.DoesNotContain(objects, o => o.EndsWith("AnisCouch") || o.EndsWith("anisSwim"));

            action.Category = NodeActionViewModel.CatNpcs;
            action.OverlayLevel = "place:home";
            WindowHarness.Pump();
            var npcs = action.NpcOptions.ToList();
            _out.WriteLine("NPCs: " + string.Join(" | ", npcs));
            // By the name the game gives each; a name used twice, by its path.
            Assert.Equal(TheNpcsOfHome, npcs);

            // Only levels with NPCs placed are offered for NPCs; both are for GameObjects.
            var npcLevels = action.OverlayLevelOptions.Select(o => o.Token).ToList();
            Assert.Contains("place:home", npcLevels);
            Assert.DoesNotContain("place:yard", npcLevels);
            action.Category = NodeActionViewModel.CatOverlay;
            WindowHarness.Pump();
            var objectLevels = action.OverlayLevelOptions.Select(o => o.Token).ToList();
            Assert.Contains("place:home", objectLevels);
            Assert.Contains("place:yard", objectLevels);
        });
    }

    [Fact]
    public void TheNpcsCategoryHasALevelAndAnNpcField_OnActionsAndConditions()
    {
        WindowHarness.Run(window =>
        {
            var vm = APack(window);
            string npcLabel = SMSModForge.Localization.Loc.T("action.npc");
            string targetLabel = SMSModForge.Localization.Loc.T("action.target");
            string levelLabel = SMSModForge.Localization.Loc.T("action.level");

            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.SetSprite;
            action.Category = NodeActionViewModel.CatNpcs;
            WindowHarness.Pump();
            var labels = Labels(window, action);
            _out.WriteLine("action labels: " + string.Join(" | ", labels));
            Assert.Contains(levelLabel, labels);
            Assert.Contains(npcLabel, labels);
            Assert.DoesNotContain(targetLabel, labels);
            // The NPC field waits for a level, as GameObjects' does.
            Assert.False(action.IsOverlayTargetEnabled);
            action.OverlayLevel = "place:home";
            Assert.True(action.IsOverlayTargetEnabled);

            // Back to GameObjects: "Target" again.
            action.Category = NodeActionViewModel.CatOverlay;
            WindowHarness.Pump();
            Assert.Contains(targetLabel, Labels(window, action));
            Assert.DoesNotContain(npcLabel, Labels(window, action));

            var cond = vm.SelectedNode.AddCondition();
            cond.Type = NodeConditionTypes.GameObjectActive;
            cond.GoCategory = NodeActionViewModel.CatNpcs;
            WindowHarness.Pump();
            var condLabels = Labels(window, cond);
            _out.WriteLine("condition labels: " + string.Join(" | ", condLabels));
            Assert.Contains(SMSModForge.Localization.Loc.T("condition.level"), condLabels);
            Assert.Contains(SMSModForge.Localization.Loc.T("condition.npc"), condLabels);
            Assert.False(cond.IsGoTargetEnabled);
            cond.GoOverlayLevel = "place:home";
            Assert.True(cond.IsGoTargetEnabled);
            Assert.Equal(TheNpcsOfHome, cond.GoNpcOptions);
            Assert.DoesNotContain("place:yard", cond.GoOverlayLevelOptions.Select(o => o.Token));

            // Fade / Move / Spin share the GameObjects list, so they lose the
            // NPCs from it too, and get the category.
            var fade = vm.SelectedNode.AddActionOnFinish();
            fade.Type = NodeActionTypes.FadeSprite;
            fade.GoCategory = NodeActionViewModel.CatNpcs;
            fade.GoOverlayLevel = "place:home";
            WindowHarness.Pump();
            Assert.True(fade.IsGoLevelScoped);
            Assert.Equal(TheNpcsOfHome, fade.GoNpcOptions);
            Assert.Contains(npcLabel, Labels(window, fade));
        });
    }

    [Fact]
    public void AnActionAimedAtAnNpcShowsItsPose_AndASwapShowsBeforeAndAfter()
    {
        WindowHarness.Run(window =>
        {
            var vm = APack(window);
            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.SetGameObjectActive;
            action.Category = NodeActionViewModel.CatNpcs;
            action.OverlayLevel = "place:home";
            action.Target = "AnisCouch";
            WindowHarness.Pump();
            Assert.Equal(70, ThumbWidth(window, action));

            action.Target = "NPCs > Bed > anisSwim";
            WindowHarness.Pump();
            Assert.Equal(60, ThumbWidth(window, action));

            // Found as the game finds it: case for case.
            action.Target = "aniscouch";
            WindowHarness.Pump();
            Assert.Empty(Previews(window, action));

            var swap = vm.SelectedNode.AddActionOnFinish();
            swap.DisplayType = NodeActionTypes.SetSprite;
            swap.Category = NodeActionViewModel.CatNpcs;
            swap.OverlayLevel = "place:home";
            swap.Target = "AnisCouch";
            swap.ParamRows.Single(r => r.Key == "sprite").Value = "NPCs/other.png";
            WindowHarness.Pump();
            var pair = Previews(window, swap).Cast<ArtThumb>().ToList();
            Assert.Equal(2, pair.Count);
            Assert.Equal(70, Picture(pair[0])!.PixelWidth);
            Assert.Equal(50, Picture(pair[1])!.PixelWidth);
        });
    }

    [Fact]
    public void TheGamesOwnNpcsMoveToTheNpcsCategoryToo_InALevelThePackExtends()
    {
        if (VanillaLevelCatalog.FindLevel("26_Downtown") == null)
        { _out.WriteLine("no extraction shipped beside the tests - skipping"); return; }

        WindowHarness.Run(window =>
        {
            var pack = PackRepository.CreateEmpty("npcgame.pack");
            pack.VanillaExtensions.Add(new VanillaPlaceExtensionDef { Source = "vanilla:26_Downtown" });
            PackRepository.Save(pack, _dir);
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            var tabs = (TabControl)window.FindName("MainTabs");
            for (int i = 0; i < tabs.Items.Count; i++)
                if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues") { tabs.SelectedIndex = i; break; }
            WindowHarness.Pump();
            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();
            if (vm.SelectedDialogue!.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
            vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
            WindowHarness.Pump();

            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.SetGameObjectActive;
            action.Category = NodeActionViewModel.CatOverlay;
            action.OverlayLevel = "vanilla:26_Downtown";
            WindowHarness.Pump();
            var objects = action.OverlayOptions.ToList();
            Assert.Contains("NPCs > Group_1", objects);             // the group stays
            Assert.DoesNotContain("NPCs > Group_1 > NPC", objects);  // the NPC goes...
            Assert.DoesNotContain("NPCs > Group_1 > NPC (2) > Square (1)", objects);   // ...with its reflection

            action.Category = NodeActionViewModel.CatNpcs;
            action.OverlayLevel = "vanilla:26_Downtown";
            WindowHarness.Pump();
            Assert.Contains("vanilla:26_Downtown", action.OverlayLevelOptions.Select(o => o.Token));
            var npcs = action.NpcOptions.ToList();
            _out.WriteLine($"{npcs.Count} NPCs, e.g. " + string.Join(" | ", npcs.Take(4)));
            // "NPC" is in every group, so each is listed by its path.
            Assert.Contains("NPCs > Group_1 > NPC", npcs);
            Assert.Contains("NPCs > Group_1 > NPC (2)", npcs);
            Assert.DoesNotContain("NPC", npcs);
            Assert.DoesNotContain(npcs, n => n.EndsWith("Square (1)"));

            // Its picture, from the game's art that comes with ModForge.
            action.Target = "NPCs > Group_1 > NPC (2)";
            WindowHarness.Pump();
            var thumb = Assert.IsType<ArtThumb>(Assert.Single(Previews(window, action)));
            _out.WriteLine($"preview: {thumb.Sprite}");
            Assert.NotNull(Picture(thumb));
            Assert.EndsWith(Path.Combine("_extra", "NPCs", "Group_1", "NPC (2).PNG"), thumb.Sprite);
        });
    }

    [Fact]
    public void AConditionShowsWhatItAsksAbout_InEveryCategoryThatHasAPicture()
    {
        WindowHarness.Run(window =>
        {
            var vm = APack(window);
            var cond = vm.SelectedNode!.AddCondition();
            cond.Type = NodeConditionTypes.GameObjectActive;
            WindowHarness.Pump();

            void Aim(string category, string target, string level = "")
            {
                cond.GoCategory = category;
                if (level.Length > 0) cond.GoOverlayLevel = level;
                cond.GoTarget = target;
                WindowHarness.Pump();
            }

            Aim(NodeActionViewModel.CatNpcs, "AnisCouch", "place:home");
            Assert.Equal(70, ThumbWidth(window, cond));

            Aim(NodeActionViewModel.CatOverlay, "Lamp", "place:yard");
            Assert.Equal(64, ThumbWidth(window, cond));
            Aim(NodeActionViewModel.CatOverlay, "lamp", "place:yard");   // not as the game spells it
            Assert.Empty(Previews(window, cond));

            Aim(NodeActionViewModel.CatBust, "Elf_Default");
            Assert.Equal(90, ThumbWidth(window, cond));

            Aim(NodeActionViewModel.CatScene, "photo");
            var scene = Assert.IsType<ScenePreview>(Assert.Single(Previews(window, cond)));
            Assert.Equal("Scenes/photo.png", scene.SceneSprite);

            // Nothing the editor can draw.
            Aim(NodeActionViewModel.CatUi, "photo");
            Assert.Empty(Previews(window, cond));
            Aim(NodeActionViewModel.CatPath, "Elf_Default");
            Assert.Empty(Previews(window, cond));

            // A condition never changes anything: no arrow, no "after".
            Aim(NodeActionViewModel.CatNpcs, "AnisCouch", "place:home");
            Assert.DoesNotContain(Descendants<System.Windows.Shapes.Path>(window),
                p => "swapArrow".Equals(p.Tag) && p.IsVisible && ReferenceEquals(p.DataContext, cond));
        });
    }
}
