using System.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// "Replace textures on this bust" says what the pack does, so it has to still
/// say it after the pack is opened again.
/// <para/>
/// Reported as the tick resetting on reload. It was editor state and nothing
/// else — a bool that started false every time a view model was built — and the
/// panel it opens is the only way to reach the rows underneath, so the author
/// came back to a bust whose replacements were still in the manifest, still
/// applied by the runtime, and nowhere on screen.
/// <para/>
/// Nothing new is saved to fix it. The pack already records the answer: an
/// outfit that carries a <c>spriteOverrides</c> entry is a bust this pack
/// replaces textures on. The tick reads that instead of starting blank, which
/// also keeps it honest — it cannot claim the pack is doing something it is
/// not, or hide something it is.
/// </summary>
public sealed class OverridePanelReopensTests
{
    private readonly ITestOutputHelper _out;
    public OverridePanelReopensTests(ITestOutputHelper o) => _out = o;

    /// <summary>A pack with one texture replaced on one of the game's busts,
    /// written to text and read back the way opening it does.</summary>
    private static (ModPack Pack, CharacterViewModel Them, OutfitViewModel Bust) Reloaded()
    {
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);

        var def = pack.Characters.First(c => !string.IsNullOrEmpty(c.VanillaCharacter));
        var outfit = def.Outfits[0];
        outfit.SpriteOverrides.Add(new SpriteOverrideDef
        {
            Slot = SpriteSlotNames.Mouth[0],
            Sprite = "art/mouth1.png",
        });

        string json = PackRepository.SerializeAsSaved(pack);
        var back = PackRepository.Deserialize(json)!;

        var theirs = back.Characters.Single(c => c.Key == def.Key);
        var vm = new CharacterViewModel(theirs);
        return (back, vm, vm.Outfits.Single(o => o.Model.Key == outfit.Key));
    }

    [Fact]
    public void ABustWithAReplacedTextureComesBackWithThePanelOpen()
    {
        var (_, _, bust) = Reloaded();

        _out.WriteLine($"{bust.Model.Key}: {bust.Model.SpriteOverrides.Count} override(s), "
                       + $"tick {bust.OverridesShown}");

        Assert.True(bust.OverridesShown,
                    "the pack replaces a texture on this bust and the tick says it does not");
        Assert.True(bust.ShowOverridesPanel, "...so the rows underneath are unreachable");
    }

    [Fact]
    public void TheReplacedTextureIsStillThereToBeShown()
    {
        // The other half, and the reason the tick mattered: the entry survived
        // the round trip perfectly well. Only the way in was missing.
        var (_, _, bust) = Reloaded();

        var row = bust.Overrides.Single(o => o.Slot == SpriteSlotNames.Mouth[0]);
        Assert.True(row.Replaced);
        Assert.Equal("art/mouth1.png", row.Path);
    }

    [Fact]
    public void ABustNobodyHasTouchedComesBackClosed()
    {
        // The control. Starting every one of the game's busts open would be the
        // same bug pointing the other way — a tick claiming the pack paints
        // over art it has never touched.
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);
        var them = new CharacterViewModel(
            pack.Characters.First(c => !string.IsNullOrEmpty(c.VanillaCharacter)));

        foreach (var bust in them.Outfits)
            Assert.False(bust.OverridesShown);
    }

    [Fact]
    public void TickingItStillOpensAnEmptyPanel()
    {
        // Nothing about reading the pack takes away the ordinary way in: a bust
        // with no replacements yet is exactly where an author starts.
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);
        var them = new CharacterViewModel(
            pack.Characters.First(c => !string.IsNullOrEmpty(c.VanillaCharacter)));
        var bust = them.Outfits[0];

        bust.OverridesShown = true;
        Assert.True(bust.ShowOverridesPanel);

        // ...and closing it again is the author's to do, even once there is
        // something in it. It hides the panel; it does not throw the pack's
        // replacements away, which is what the row ticks are for.
        var row = bust.Overrides.Single(o => o.Slot == SpriteSlotNames.Mouth[0]);
        row.Replaced = true;
        bust.OverridesShown = false;

        Assert.False(bust.ShowOverridesPanel);
        Assert.Single(bust.Model.SpriteOverrides);
    }

    [Fact]
    public void UntickingTheLastRowDoesNotSlamThePanelShut()
    {
        // The tick reads the pack until somebody says otherwise, so it has to
        // LATCH: clearing the one replacement a bust had would otherwise close
        // the panel from under an author who is in the middle of changing which
        // texture they meant to replace.
        var (_, _, bust) = Reloaded();
        Assert.True(bust.OverridesShown);

        var row = bust.Overrides.Single(o => o.Slot == SpriteSlotNames.Mouth[0]);
        row.Replaced = false;

        Assert.Empty(bust.Model.SpriteOverrides);
        Assert.True(bust.OverridesShown, "the panel closed while the author was working in it");
    }

    [Fact]
    public void PickingACharacterLandsOnTheBustTheyEnterIn()
    {
        // The author reached this panel by clicking the character rather than
        // the bust, on the understanding that doing so lands on their default
        // bust. It landed on whichever bust happened to be first instead, which
        // is the same one only when the pack has not chosen otherwise.
        var vm = new MainViewModel();
        var them = vm.Characters.First(c => c.IsVanillaBust && c.Outfits.Count > 1);

        // Somewhere other than the front of the list, or this passes on a
        // coincidence rather than on the rule. Named the way a manifest names
        // it - by GameObject name, which for one of the game's busts is not
        // the same string as the outfit key.
        var meant = them.Outfits[them.Outfits.Count - 1];
        them.DefaultOutfit = meant.Model.GameObjectName;
        Assert.NotEqual(meant.Model.Key, them.DefaultOutfit);

        vm.SelectedOutfit = null;
        vm.SelectedCharacter = them;

        _out.WriteLine($"default {them.DefaultOutfit} -> landed on "
                       + (vm.SelectedOutfit?.Model.GameObjectName ?? "(nothing)"));
        Assert.Same(meant, vm.SelectedOutfit);

        // ...and coming from another character, which is the path that already
        // repointed the selection and the one an author actually walks.
        var somebodyElse = vm.Characters.First(c => c != them && c.Outfits.Count > 0);
        vm.SelectedCharacter = somebodyElse;
        vm.SelectedCharacter = them;
        Assert.Same(meant, vm.SelectedOutfit);
    }

    [Fact]
    public void ClickingTheCharacterInTheSidebarLandsThereToo()
    {
        // The path the author actually took, and the one the view-model fix
        // does NOT cover on its own: the sidebar's selection handler used to
        // reach past the view model and take Outfits[0] itself, so the rule
        // lived in two places and the second one overwrote the first.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (System.Windows.Controls.TabControl)window.FindName("MainTabs");
            for (int i = 0; i < tabs.Items.Count; i++)
                if (tabs.Items[i] is System.Windows.Controls.TabItem t
                    && (t.Header as string) == "Characters")
                { tabs.SelectedIndex = i; break; }
            WindowHarness.Pump();

            var tree = (System.Windows.Controls.TreeView)window.FindName("CharacterTree");
            tree.UpdateLayout();
            WindowHarness.Pump();

            // Walked out of the visual tree rather than asked of the
            // ItemContainerGenerator, because this TreeView is GROUPED - its
            // top-level containers are the groups, and asking it for a
            // character returns null. A version of this test that did that
            // skipped itself, and a test that skips itself passes while the
            // bug it was written for is still there.
            // The characters are filed under collapsed group headings, so
            // nothing under them exists as a control until the headings open.
            OpenEveryGroup(tree);
            tree.UpdateLayout();
            WindowHarness.Pump();

            var rows = new System.Collections.Generic.List<System.Windows.Controls.TreeViewItem>();
            Collect(tree, rows);
            _out.WriteLine($"{rows.Count} tree row(s) on screen");

            System.Windows.Controls.TreeViewItem? row = null;
            CharacterViewModel? them = null;
            foreach (var candidate in rows)
                if (candidate.DataContext is CharacterViewModel c && c.Outfits.Count > 1)
                { row = candidate; them = c; break; }

            Assert.True(row != null,
                        $"no character row with more than one bust is on screen "
                        + $"({rows.Count} tree row(s) realised) - nothing was clicked");

            var meant = them!.Outfits[them.Outfits.Count - 1];
            them.DefaultOutfit = meant.Model.GameObjectName;

            row!.IsSelected = true;      // exactly what a click does
            WindowHarness.Pump();

            _out.WriteLine($"clicked {them.Key}, default {them.DefaultOutfit} -> "
                           + (vm.SelectedOutfit?.Model.GameObjectName ?? "(nothing)"));
            Assert.Same(them, vm.SelectedCharacter);
            Assert.Same(meant, vm.SelectedOutfit);
        });
    }

    /// <summary>Open every group heading, so the rows underneath are built.</summary>
    private static void OpenEveryGroup(System.Windows.DependencyObject root)
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.Expander open) open.IsExpanded = true;
            OpenEveryGroup(child);
        }
    }

    private static void Collect(System.Windows.DependencyObject root,
                                System.Collections.Generic.List<System.Windows.Controls.TreeViewItem> into)
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.TreeViewItem row) into.Add(row);
            Collect(child, into);
        }
    }
}
