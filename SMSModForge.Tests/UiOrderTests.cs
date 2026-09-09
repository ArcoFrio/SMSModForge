using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Sibling order, which on a canvas is draw order — there is no depth to sort
/// by, so an object is in front of another because it comes after it.
/// <para/>
/// The rule these check is that the pack stores an order only when the author
/// has actually made one. The game already puts its own objects where they
/// were and adds the pack's on the end; writing that down would be writing down
/// the default, and every one of those entries is a place the pack would pin a
/// screen to the shape it had when the pack was written.
/// </summary>
public sealed class UiOrderTests
{
    private readonly ITestOutputHelper _out;
    public UiOrderTests(ITestOutputHelper output) => _out = output;

    private const string Screen = "vanillaui:9_MainCanvas/Quitagme";

    /// <summary>A pack holding one seeded vanilla screen, the way the editor
    /// hands it over at save time.</summary>
    private static (ModPack Pack, UiViewModel Ui)? Seeded()
    {
        if (!VanillaUiLibrary.IsAvailable) return null;

        var pack = new ModPack();
        var def = new UiDef { Source = Screen };
        pack.Uis.Add(def);
        var ui = new UiViewModel(def);
        ui.Seed();
        return ui.Nodes.Count == 0 ? null : (pack, ui);
    }

    private static int Stamped(ModPack pack)
    {
        int found = 0;
        void Walk(UiNodeDef node)
        {
            if (node.SiblingIndex >= 0) found++;
            foreach (var child in node.Children) Walk(child);
        }
        foreach (var ui in pack.Uis)
            foreach (var node in ui.Nodes) Walk(node);
        return found;
    }

    [Fact]
    public void An_untouched_screen_stores_no_order_at_all()
    {
        var made = Seeded();
        if (made == null) { _out.WriteLine("no extraction - skipping"); return; }
        var (pack, _) = made.Value;

        var restore = VanillaUiDelta.PrepareForSave(pack);
        try
        {
            Assert.Equal(0, Stamped(pack));
        }
        finally { restore(); }
    }

    [Fact]
    public void Adding_an_object_at_the_end_stores_no_order()
    {
        // The game appends what a pack creates, so an object on the end is
        // already where it is going to be. Recording that is recording the
        // default.
        var made = Seeded();
        if (made == null) { _out.WriteLine("no extraction - skipping"); return; }
        var (pack, ui) = made.Value;

        ui.Nodes[0].AddChild("Badge");

        var restore = VanillaUiDelta.PrepareForSave(pack);
        try
        {
            Assert.Equal(0, Stamped(pack));
        }
        finally { restore(); }
    }

    [Fact]
    public void Moving_an_object_in_front_of_another_stores_the_whole_arrangement()
    {
        var made = Seeded();
        if (made == null) { _out.WriteLine("no extraction - skipping"); return; }
        var (pack, ui) = made.Value;

        var root = ui.Nodes[0];
        if (root.Children.Count < 3) { _out.WriteLine("too few children - skipping"); return; }
        int siblings = root.Children.Count;

        // Bring the first child to the front of the pile.
        var moved = root.Children[0];
        string movedName = moved.Model.Name;
        root.MoveTo(moved, siblings - 1);

        var restore = VanillaUiDelta.PrepareForSave(pack);
        try
        {
            // All of them, not just the one that moved: an order is only an
            // order if every place in it is stated.
            Assert.Equal(siblings, Stamped(pack));

            var saved = pack.Uis[0].Nodes[0].Children;
            Assert.Equal(siblings, saved.Count);
            Assert.Equal(movedName, saved[siblings - 1].Name);

            // And the places run 0..n-1 in the order they are written, so the
            // runtime can apply them lowest first.
            Assert.Equal(Enumerable.Range(0, siblings), saved.Select(n => n.SiblingIndex));
            _out.WriteLine($"{siblings} siblings stamped, '{movedName}' last");
        }
        finally { restore(); }
    }

    [Fact]
    public void An_added_object_placed_between_two_of_the_games_stores_the_order()
    {
        // Left alone this would be appended to the end - behind everything it
        // was meant to sit in front of.
        var made = Seeded();
        if (made == null) { _out.WriteLine("no extraction - skipping"); return; }
        var (pack, ui) = made.Value;

        var root = ui.Nodes[0];
        if (root.Children.Count < 3) { _out.WriteLine("too few children - skipping"); return; }

        var added = root.AddChild("Badge");
        root.MoveTo(added, 1);

        var restore = VanillaUiDelta.PrepareForSave(pack);
        try
        {
            Assert.True(Stamped(pack) > 0, "the added object's place was not recorded");
            Assert.Equal("Badge", pack.Uis[0].Nodes[0].Children[1].Name);
        }
        finally { restore(); }
    }

    [Fact]
    public void A_stored_order_survives_being_saved_and_reopened()
    {
        var made = Seeded();
        if (made == null) { _out.WriteLine("no extraction - skipping"); return; }
        var (pack, ui) = made.Value;

        var root = ui.Nodes[0];
        if (root.Children.Count < 3) { _out.WriteLine("too few children - skipping"); return; }

        var moved = root.Children[0];
        string movedName = moved.Model.Name;
        root.MoveTo(moved, root.Children.Count - 1);

        string json = PackRepository.SerializeAsSaved(pack);
        var reopened = PackRepository.Deserialize(json);
        var reseeded = new UiViewModel(reopened.Uis[0]);

        // Merged back over a fresh seed, the object is where it was left - not
        // where the game has it.
        var rows = reseeded.Nodes[0].Children;
        Assert.Equal(movedName, rows[rows.Count - 1].Model.Name);
        _out.WriteLine($"'{movedName}' still last of {rows.Count} after a round trip");
    }

    [Fact]
    public void Moving_a_row_moves_the_saved_object_with_it()
    {
        // The rows an author sees and the list that gets written are two lists,
        // and a reorder that moved only one of them would look right in the
        // editor and be wrong in the pack.
        var root = new UiNodeDef { Name = "Screen" };
        root.Children.Add(new UiNodeDef { Name = "A" });
        root.Children.Add(new UiNodeDef { Name = "B" });
        root.Children.Add(new UiNodeDef { Name = "C" });

        var vm = new UiNodeViewModel(root);
        vm.MoveBy(vm.Children[0], +2);

        Assert.Equal(new[] { "B", "C", "A" }, vm.Children.Select(c => c.Model.Name));
        Assert.Equal(new[] { "B", "C", "A" }, root.Children.Select(c => c.Name));
    }

    [Fact]
    public void A_row_cannot_be_moved_off_either_end()
    {
        var root = new UiNodeDef { Name = "Screen" };
        root.Children.Add(new UiNodeDef { Name = "A" });
        root.Children.Add(new UiNodeDef { Name = "B" });

        var vm = new UiNodeViewModel(root);

        Assert.False(vm.Children[0].MoveUpCommand.CanExecute(null));
        Assert.True(vm.Children[0].MoveDownCommand.CanExecute(null));
        Assert.True(vm.Children[1].MoveUpCommand.CanExecute(null));
        Assert.False(vm.Children[1].MoveDownCommand.CanExecute(null));

        // And asking anyway changes nothing.
        vm.MoveBy(vm.Children[0], -1);
        Assert.Equal(new[] { "A", "B" }, root.Children.Select(c => c.Name));
    }
}
