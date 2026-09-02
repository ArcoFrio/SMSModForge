using System;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Editing a vanilla UI: what an author can do to it, what they cannot, and
/// what ends up in the pack.
/// <para/>
/// These need the extraction, because seeding reads the real screen. Without
/// it they say so and skip, which is the same bargain the renderer tests make.
/// </summary>
public class UiExtensionViewModelTests
{
    private readonly ITestOutputHelper _out;
    public UiExtensionViewModelTests(ITestOutputHelper o) => _out = o;

    private const string Quit = "vanillaui:9_MainCanvas/Quitagme";

    private UiViewModel? Open(string source = Quit)
    {
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return null; }
        var vm = new UiViewModel(new UiDef { Source = source });
        if (!vm.CanSeed) { _out.WriteLine("cannot seed " + source + " - skipping"); return null; }
        return vm;
    }

    private static int Count(UiNodeDef n) => 1 + n.Children.Sum(Count);

    // ── Opening one ──────────────────────────────────────────────────

    [Fact]
    public void Choosing_a_screen_fills_the_tree_with_the_screen()
    {
        var vm = Open();
        if (vm == null) return;

        var root = Assert.Single(vm.Nodes);
        Assert.Equal("Quitagme", root.Name);
        Assert.True(root.IsVanilla);
        Assert.Equal(".", root.Bind);
        Assert.NotEmpty(root.Children);
        _out.WriteLine($"{Count(root.Model)} objects, summary: {vm.Summary}");
        Assert.Contains("nothing changed yet", vm.Summary);
    }

    [Fact]
    public void A_vanilla_object_can_be_edited_but_not_deleted()
    {
        // The rule that keeps an extension honest. A pack cannot remove an
        // object from a screen it does not own, and an editor that offered to
        // would be promising something the runtime cannot do.
        var vm = Open();
        if (vm == null) return;

        var root = vm.Nodes[0];
        var child = root.Children.First(c => c.IsVanilla);

        Assert.False(child.RemoveCommand.CanExecute(null));
        Assert.False(root.RemoveCommand.CanExecute(null));

        // Renaming is refused too: the name is what the binding is built from.
        string was = child.Name;
        child.Name = "Something Else";
        Assert.Equal(was, child.Name);
    }

    [Fact]
    public void An_object_the_pack_adds_can_be_deleted_again()
    {
        var vm = Open();
        if (vm == null) return;

        var root = vm.Nodes[0];
        int before = root.Children.Count;

        var added = root.AddChild("My Button");
        Assert.True(added.IsMine);
        Assert.False(added.IsVanilla);
        Assert.Equal(before + 1, root.Children.Count);
        Assert.True(added.RemoveCommand.CanExecute(null));

        added.RemoveCommand.Execute(null);
        Assert.Equal(before, root.Children.Count);
        Assert.Equal(before, root.Model.Children.Count);
    }

    [Fact]
    public void Two_added_objects_do_not_end_up_sharing_a_name()
    {
        // Duplicate sibling names are how a bind path stops identifying one
        // object, and this is the cheapest place to prevent it.
        var vm = Open();
        if (vm == null) return;

        var root = vm.Nodes[0];
        var first = root.AddChild("Button");
        var second = root.AddChild("Button");

        Assert.NotEqual(first.Name, second.Name);
        _out.WriteLine($"{first.Name} / {second.Name}");
    }

    // ── What gets stored ─────────────────────────────────────────────

    [Fact]
    public void Opening_a_screen_and_closing_it_stores_nothing()
    {
        var vm = Open();
        if (vm == null) return;

        var pack = new ModPack { PackId = "test" };
        pack.Uis.Add(vm.Model);

        int seeded = vm.Model.Nodes.Sum(Count);
        var restore = VanillaUiDelta.PrepareForSave(pack);
        try
        {
            _out.WriteLine($"{seeded} objects seeded, {vm.Model.Nodes.Sum(Count)} stored");
            Assert.Empty(vm.Model.Nodes);
        }
        finally { restore(); }

        Assert.Equal(seeded, vm.Model.Nodes.Sum(Count));   // and it all came back
    }

    [Fact]
    public void Retyping_one_label_stores_one_label()
    {
        var vm = Open();
        if (vm == null) return;

        var label = vm.Nodes[0].Children.FirstOrDefault(c => c.HasText);
        if (label == null) { _out.WriteLine("no text on this screen - skipping"); return; }

        string was = label.Text;
        label.Text = "Totally Different";

        var pack = new ModPack { PackId = "test" };
        pack.Uis.Add(vm.Model);

        var restore = VanillaUiDelta.PrepareForSave(pack);
        try
        {
            int stored = vm.Model.Nodes.Sum(Count);
            _out.WriteLine($"changed '{was}' to '{label.Text}': {stored} objects stored");
            Assert.InRange(stored, 1, 4);          // the label and its line of descent

            var changed = vm.Model.Nodes[0].Children.Single();
            Assert.True(changed.OverrideText);
            Assert.Equal("Totally Different", changed.Text!.Value);
        }
        finally { restore(); }
    }

    [Fact]
    public void An_added_object_survives_a_reseed()
    {
        // Re-seeding refreshes the copied objects from the game. An author's own
        // work is not a copy and must not be swept up with them.
        var vm = Open();
        if (vm == null) return;

        vm.Nodes[0].AddChild("Mine");
        vm.Seed();

        var root = Assert.Single(vm.Nodes);
        Assert.Contains(root.Children, c => c.IsMine && c.Name == "Mine");
    }

    [Fact]
    public void Switching_to_another_screen_does_not_carry_the_old_tree_across()
    {
        // The edits belong to the screen they were made on. Rebasing them onto
        // a different one would apply them to objects that merely share a name.
        var vm = Open();
        if (vm == null) return;

        var other = VanillaUiCatalog.UsableBases.FirstOrDefault(
            b => b.Token != Quit && b.Objects > 3 && VanillaUiLibrary.Node(b) != null);
        if (other == null) { _out.WriteLine("no second screen - skipping"); return; }

        string firstName = vm.Nodes[0].Name;
        vm.Source = other.Token;

        Assert.Equal(other.Name, vm.Nodes[0].Name);
        Assert.NotEqual(firstName, vm.Nodes[0].Name);
        _out.WriteLine($"{firstName} -> {vm.Nodes[0].Name}");
    }

    [Fact]
    public void An_extension_read_back_as_a_delta_opens_as_the_whole_screen()
    {
        // What a pack on disk actually holds is a few nodes. Opening it must
        // show the screen, or an author would see their own edit floating in
        // nothing and have no idea where it sits.
        var vm = Open();
        if (vm == null) return;

        var label = vm.Nodes[0].Children.FirstOrDefault(c => c.HasText);
        if (label == null) return;
        label.Text = "Changed";

        var pack = new ModPack { PackId = "test" };
        pack.Uis.Add(vm.Model);

        // Save, take the pruned form, and reopen from it as loading would.
        var restore = VanillaUiDelta.PrepareForSave(pack);
        var onDisk = Newtonsoft.Json.JsonConvert.SerializeObject(vm.Model);
        restore();

        var reloaded = Newtonsoft.Json.JsonConvert
            .DeserializeObject<UiDef>(onDisk)!;
        int stored = reloaded.Nodes.Sum(Count);

        var reopened = new UiViewModel(reloaded);
        int shown = reopened.Model.Nodes.Sum(Count);

        _out.WriteLine($"{stored} on disk -> {shown} on screen");
        Assert.True(shown > stored, "reopening has to fill the screen back in");
        Assert.Equal("Quitagme", reopened.Nodes[0].Name);

        // And the edit is still there. Filling the screen back in by SEEDING
        // would have satisfied everything above while quietly discarding the
        // one thing the author actually did - which is what the first version
        // of this did.
        var again = reopened.Nodes[0].Children.FirstOrDefault(c => c.Text == "Changed");
        Assert.True(again != null, "the edit did not survive being saved and reopened");
        Assert.Equal(label.Name, again!.Name);
        Assert.True(again.IsVanilla, "and it is still bound to the vanilla object");

        // Saving again stores the same small delta rather than growing.
        var second = new ModPack { PackId = "test" };
        second.Uis.Add(reopened.Model);
        var restoreAgain = VanillaUiDelta.PrepareForSave(second);
        try
        {
            int stored2 = reopened.Model.Nodes.Sum(Count);
            _out.WriteLine($"saved again: {stored2}");
            Assert.Equal(stored, stored2);
        }
        finally { restoreAgain(); }
    }

    [Fact]
    public void A_screen_the_editor_has_never_heard_of_is_held_rather_than_dropped()
    {
        // A pack written against a newer game. Refusing to open it, or quietly
        // clearing the source, would lose the author's work.
        var vm = new UiViewModel(
            new UiDef { Source = "vanillaui:Future/Screen" });

        Assert.False(vm.IsKnown);
        Assert.False(vm.CanSeed);
        Assert.Equal("vanillaui:Future/Screen", vm.Source);
        Assert.Empty(vm.Nodes);
        Assert.Equal("", vm.Summary);
    }

    // ── Telling one row from another ─────────────────────────────────

    [Fact]
    public void An_untouched_vanilla_object_carries_no_marker()
    {
        // The default state of almost every row. A screen can hold 1434 of
        // them, so anything that marked them all would mark nothing.
        var vm = Open();
        if (vm == null) return;

        var root = vm.Nodes[0];
        Assert.Equal("", root.Status);
        Assert.True(root.IsUntouched);
        Assert.All(root.Children.Where(c => c.IsVanilla),
                   c => Assert.Equal("", c.Status));
    }

    [Fact]
    public void Changing_a_vanilla_object_marks_it_changed()
    {
        var vm = Open();
        if (vm == null) return;

        var label = vm.Nodes[0].Children.FirstOrDefault(c => c.HasText);
        if (label == null) { _out.WriteLine("no text here - skipping"); return; }

        Assert.Equal("", label.Status);
        label.Text = "Something Else";
        Assert.Equal("changed", label.Status);
        Assert.True(label.IsChanged);
        _out.WriteLine($"{label.Name}: {label.Status}");
    }

    [Fact]
    public void Putting_a_changed_value_back_clears_the_marker()
    {
        // The marker follows the comparison rather than a "touched" flag, so
        // undoing an edit by hand un-marks the row - and matches what would
        // actually be saved, which is nothing.
        var vm = Open();
        if (vm == null) return;

        var label = vm.Nodes[0].Children.FirstOrDefault(c => c.HasText);
        if (label == null) return;

        string was = label.Text;
        label.Text = "Something Else";
        Assert.Equal("changed", label.Status);

        label.Text = was;
        Assert.Equal("", label.Status);
    }

    [Fact]
    public void An_object_the_pack_added_is_marked_new()
    {
        var vm = Open();
        if (vm == null) return;
        var added = vm.Nodes[0].AddChild("Mine");
        Assert.Equal("new", added.Status);
        Assert.False(added.IsUntouched);
    }

    [Fact]
    public void The_marker_agrees_with_what_would_be_saved()
    {
        // Two implementations of "has this been touched" would drift, and the
        // one on screen drifting from the one on disk is the bad version: an
        // author would see no marker and still ship a change. So the marked
        // rows and the stored rows are compared directly.
        var vm = Open();
        if (vm == null) return;

        var label = vm.Nodes[0].Children.FirstOrDefault(c => c.HasText);
        if (label == null) return;
        label.Text = "Marked";
        vm.Nodes[0].AddChild("Added");

        var marked = new System.Collections.Generic.List<string>();
        void Walk(UiNodeViewModel n)
        {
            if (!n.IsUntouched) marked.Add(n.Name);
            foreach (var c in n.Children) Walk(c);
        }
        Walk(vm.Nodes[0]);

        var pack = new ModPack { PackId = "test" };
        pack.Uis.Add(vm.Model);
        var restore = VanillaUiDelta.PrepareForSave(pack);
        try
        {
            var stored = new System.Collections.Generic.List<string>();
            void Collect(UiNodeDef n)
            {
                stored.Add(n.Name);
                foreach (var c in n.Children) Collect(c);
            }
            foreach (var n in vm.Model.Nodes) Collect(n);

            _out.WriteLine("marked: " + string.Join(", ", marked));
            _out.WriteLine("stored: " + string.Join(", ", stored));

            // Every marked row survives the prune. The reverse does not hold:
            // an unmarked parent is kept as the path down to a marked child.
            foreach (string name in marked) Assert.Contains(name, stored);
        }
        finally { restore(); }
    }

    [Fact]
    public void Editing_anything_tells_whoever_is_watching()
    {
        // The preview redraws off this. Without it an author edits a number and
        // the picture stays as it was, which reads as the edit not working.
        var vm = Open();
        if (vm == null) return;

        int fired = 0;
        vm.Changed += () => fired++;

        vm.Nodes[0].Children[0].PositionX += 5;
        Assert.True(fired > 0, "moving an object has to reach the preview");

        int after = fired;
        vm.Nodes[0].AddChild("Another");
        Assert.True(fired > after, "adding an object has to reach the preview too");
    }
}
