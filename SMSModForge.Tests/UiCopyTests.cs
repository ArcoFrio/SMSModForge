using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Starting a UI as a copy of one the game already has.
/// <para/>
/// The distinction that matters: building ON a screen changes the game's own,
/// while copying leaves it alone. A pack that wants a second shop rather than a
/// changed general store needs the second, and the difference between them is
/// whether the nodes carry a bind.
/// </summary>
public sealed class UiCopyTests
{
    private readonly ITestOutputHelper _out;
    public UiCopyTests(ITestOutputHelper output) => _out = output;

    private const string Screen = "vanillaui:9_MainCanvas/Quitagme";

    private static VanillaUiSurface.Node? Base()
    {
        var entry = VanillaUiCatalog.Find(Screen);
        return entry == null ? null : VanillaUiLibrary.Node(entry);
    }

    private static int Count(UiNodeDef n, System.Func<UiNodeDef, bool> want)
        => (want(n) ? 1 : 0) + n.Children.Sum(c => Count(c, want));

    [Fact]
    public void A_copy_owns_every_object_it_took()
    {
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var vanilla = Base();
        if (vanilla == null) { _out.WriteLine("no base - skipping"); return; }

        var extension = VanillaUiSeed.FromBase(vanilla)!;
        var copy = VanillaUiSeed.CopyOf(vanilla)!;

        int total = Count(copy, _ => true);
        _out.WriteLine($"{total} objects; extension binds {Count(extension, n => n.IsBound)}, " +
                       $"copy binds {Count(copy, n => n.IsBound)}");

        // Same objects either way - it is the same screen.
        Assert.Equal(Count(extension, _ => true), total);
        Assert.True(total > 10, "the screen came across nearly empty");

        // An extension names the game's objects; a copy names none of them.
        Assert.True(Count(extension, n => n.IsBound) > 10);
        Assert.Equal(0, Count(copy, n => n.IsBound));
    }

    [Fact]
    public void A_copy_keeps_what_the_screen_looks_like()
    {
        // Owning the objects is no use if they arrive blank.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var vanilla = Base();
        if (vanilla == null) return;

        var copy = VanillaUiSeed.CopyOf(vanilla, VanillaUiLibrary.Assets.NameForKey)!;

        Assert.True(Count(copy, n => n.Image != null) > 3, "no pictures came across");
        Assert.True(Count(copy, n => n.Text != null) > 0, "no text came across");

        // And it draws.
        var report = new UiRenderReport();
        var pixels = UiAuthoredRenderer.Render(copy, 1920, 1080, VanillaUiLibrary.Assets, report);
        Assert.NotEmpty(pixels);
        Assert.Empty(report.MissingSprites);
    }

    [Fact]
    public void Everything_in_a_copy_can_be_removed()
    {
        // The point of owning them. A bound object refuses to be deleted,
        // because a pack cannot delete the game's objects out of its screen.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var vanilla = Base();
        if (vanilla == null) return;

        var copy = VanillaUiSeed.CopyOf(vanilla)!;
        var vm = new UiNodeViewModel(copy);

        Assert.NotEmpty(vm.Children);
        Assert.All(vm.Children, child => Assert.True(child.IsMine));
    }

    [Fact]
    public void A_copy_brought_into_a_screen_is_the_packs_own()
    {
        // Copying is an add, so it lives on the tree beside the other things
        // that add objects - not on the list, which only makes and removes
        // whole screens.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));

            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            ui.SelectedNode = ui.Nodes[0];

            var entry = VanillaUiCatalog.Find(Screen);
            ui.AddCopyChildCommand.Execute(entry);
            WindowHarness.Pump();

            var added = ui.Nodes[0].Children.Last();
            _out.WriteLine($"{added.Model.Name}: {Count(added.Model, _ => true)} objects, " +
                           $"binds {Count(added.Model, n => n.IsBound)}");

            // Nothing bound: it is the pack's, and the game's screen is untouched.
            Assert.Equal(0, Count(added.Model, n => n.IsBound));
            Assert.True(added.IsMine);
            Assert.False(ui.IsVanillaBased);
        });
    }

    [Fact]
    public void Copying_a_screen_does_not_use_it_up()
    {
        // Two extensions of one screen collide; two copies do not, because
        // neither is touching it. The copy picker must not filter.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            vm.AddVanillaUiCommand.Execute(null);
            vm.Uis.Last().Source = Screen;

            // Asked from a DIFFERENT row: the list deliberately keeps a row's
            // own choice in its own dropdown, so asking from the row that made
            // the choice would always find it.
            vm.AddVanillaUiCommand.Execute(null);
            vm.SelectedUi = vm.Uis.Last();
            WindowHarness.Pump();

            // Off the extension list, as it should be...
            Assert.DoesNotContain(Screen, vm.AvailableUiScreens.Select(b => b.Token));

            // ...and still copyable.
            Assert.Contains(Screen, vm.CopyableUiScreens.Select(b => b.Token));
        });
    }

    [Fact]
    public void A_copy_survives_being_saved_and_reopened()
    {
        // An extension stores a delta and reseeds; a copy has nothing to reseed
        // from, so every object has to be in the file.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var vanilla = Base();
        if (vanilla == null) return;

        var pack = new ModPack();
        var def = new UiDef { Name = "Shop", Id = "shop", Template = "copy" };
        def.Nodes.Add(VanillaUiSeed.CopyOf(vanilla, VanillaUiLibrary.Assets.NameForKey)!);
        pack.Uis.Add(def);

        int before = Count(def.Nodes[0], _ => true);

        var reopened = PackRepository.Deserialize(PackRepository.SerializeAsSaved(pack));
        var back = reopened.Uis.Single();

        int after = Count(back.Nodes[0], _ => true);
        _out.WriteLine($"{before} objects saved, {after} came back");

        Assert.Equal("", back.Source);
        Assert.Equal(before, after);
    }
}
