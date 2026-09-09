using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Category picker on the Set-Active and Set-Sprite rows, and the Target
/// list that is supposed to follow it.
/// <para/>
/// Reported: choosing UI offered busts. The category was in the list and the
/// runtime understood it, but nothing ever told the Target dropdown what to
/// show for it, so it fell through to the default - every GameObject the pack
/// names. A category with no list wired is invisible until someone picks it,
/// which is why the last test here checks all of them at once rather than only
/// the one that was reported.
/// </summary>
public sealed class ActionCategoryTests
{
    private readonly ITestOutputHelper _out;
    public ActionCategoryTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void The_UI_category_offers_the_packs_own_screens()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var made = vm.Uis.Last();
            made.Name = "Gift shop";
            WindowHarness.Pump();

            var tokens = vm.UiIdOptions.Select(o => o.Token).ToList();
            _out.WriteLine("offered: " + string.Join(", ",
                vm.UiIdOptions.Select(o => $"{o.DisplayLabel} -> {o.Token}")));

            Assert.Contains(made.Model.Id, tokens);

            // The token is the id, because that is what the runtime looks up.
            // The label carries the name, because an id on its own says nothing.
            var entry = vm.UiIdOptions.First(o => o.Token == made.Model.Id);
            Assert.Contains("Gift shop", entry.DisplayLabel);
            Assert.Equal(made.Model.Id, entry.ToString());   // what lands in the box
        });
    }

    [Fact]
    public void It_is_bound_to_the_screens_and_not_to_the_fall_through()
    {
        // The control for the actual report. With no trigger of its own the UI
        // category landed on the base setter's list, GameObjectNameOptions -
        // which holds every bust GameObject name as well as every place's, and
        // in a pack with more characters than scenery reads as nothing but
        // busts. Asserting the binding directly is what pins the fix: a list
        // that merely happens to look right today would not.
        string? xaml = FindWindowXaml();
        if (xaml == null) { _out.WriteLine("MainWindow.xaml not found - skipping"); return; }

        string bound = TargetSourceFor(xaml, "UI");
        _out.WriteLine("UI target list: " + bound);

        Assert.Contains("UiIdOptions", bound);
        Assert.DoesNotContain("GameObjectNameOptions", bound);
        Assert.DoesNotContain("BustName", bound);
    }

    /// <summary>What the Target combo is told to show for one category.</summary>
    private static string TargetSourceFor(string xaml, string category)
    {
        var doc = XDocument.Load(xaml);
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var trigger = doc.Descendants(p + "DataTrigger").FirstOrDefault(t =>
            (string?)t.Attribute("Binding") == "{Binding Category}" &&
            (string?)t.Attribute("Value") == category);

        if (trigger == null) return "";

        var setter = trigger.Elements(p + "Setter")
            .FirstOrDefault(x => (string?)x.Attribute("Property") == "ItemsSource");

        return (string?)setter?.Attribute("Value") ?? "";
    }

    [Fact]
    public void A_screen_that_goes_away_stops_being_offered()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var made = vm.Uis.Last();
            string id = made.Model.Id;
            vm.SelectedUi = made;
            WindowHarness.Pump();
            Assert.Contains(id, vm.UiIdOptions.Select(o => o.Token));

            vm.RemoveUiCommand.Execute(null);
            WindowHarness.Pump();

            _out.WriteLine("after removing: " + string.Join(", ", vm.UiIdOptions.Select(o => o.Token)));
            Assert.DoesNotContain(id, vm.UiIdOptions.Select(o => o.Token));
        });
    }

    [Fact]
    public void The_games_own_screens_are_not_offered()
    {
        // A change to a screen the game ships is not a screen the pack can
        // switch on and off; it exists whether the pack says so or not.
        WindowHarness.Run(window =>
        {
            if (!SMSModForge.Rendering.VanillaUiLibrary.IsAvailable)
            { _out.WriteLine("no extraction - skipping"); return; }

            var vm = (MainViewModel)window.DataContext;
            vm.AddVanillaUiCommand.Execute(null);
            vm.Uis[^1].Source = "vanillaui:9_MainCanvas/Quitagme";
            WindowHarness.Pump();

            var vanilla = vm.Uis.Where(u => u.IsVanillaBased).ToList();
            _out.WriteLine($"{vanilla.Count} vanilla-based, {vm.UiIdOptions.Count} offered");

            Assert.NotEmpty(vanilla);        // or this proves nothing
            foreach (var v in vanilla)
                Assert.DoesNotContain(v.Model.Id, vm.UiIdOptions.Select(o => o.Token));
        });
    }

    /// <summary>
    /// Every category the row offers has to have somewhere for its Target list
    /// to come from.
    /// <para/>
    /// Read out of the XAML rather than exercised through the window, because
    /// that is where the omission was: the category existed, the runtime handled
    /// it, and the only thing missing was one trigger. A test that clicked
    /// through the control would have needed a pack with every kind of target in
    /// it to notice, whereas the fact being asserted is a plain one - the list
    /// of categories and the list of triggers must agree.
    /// </summary>
    [Fact]
    public void Every_category_has_a_target_list_behind_it()
    {
        string? xaml = FindWindowXaml();
        if (xaml == null) { _out.WriteLine("MainWindow.xaml not found beside the tests - skipping"); return; }

        var doc = XDocument.Load(xaml);
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        // Every DataTrigger anywhere in the window that switches on Category.
        var wired = doc.Descendants(p + "DataTrigger")
            .Where(t => (string?)t.Attribute("Binding") == "{Binding Category}")
            .Select(t => (string?)t.Attribute("Value"))
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!)
            .ToHashSet(StringComparer.Ordinal);

        _out.WriteLine("triggers on Category: " + string.Join(", ", wired.OrderBy(x => x)));

        var offered = NodeActionViewModel.SetActiveCategories
            .Concat(NodeActionViewModel.SetSpriteCategories)
            .Distinct()
            .ToList();

        // Direct Path is the fall-through the base setter provides, so it is the
        // one category that is meant to have no trigger of its own.
        var missing = offered
            .Where(c => c != NodeActionViewModel.CatPath && !wired.Contains(c))
            .ToList();

        Assert.True(missing.Count == 0,
            "these categories offer no Target list: " + string.Join(", ", missing));
    }

    private static string? FindWindowXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "SMSModForge", "MainWindow.xaml");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Objects_inside_a_screen_are_offered_too()
    {
        // So a button can switch one panel of a screen rather than the whole
        // screen - showing one tab and hiding another is this action twice.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;

            var root = ui.Nodes[0];
            root.Model.Name = "Panel";
            var inner = root.AddChild(new UiNodeDef { Name = "Close" });
            inner.AddChild(new UiNodeDef { Name = "Icon" });
            vm.RebuildUiOptions();
            WindowHarness.Pump();

            var tokens = vm.UiIdOptions.Select(o => o.Token).ToList();
            _out.WriteLine(string.Join(", ", tokens));

            string id = ui.Model.Id;
            Assert.Contains(id, tokens);                        // the screen itself
            Assert.Contains(id + "/Panel/Close", tokens);       // an object in it
            Assert.Contains(id + "/Panel/Close/Icon", tokens);  // and one inside that
        });
    }

    [Fact]
    public void A_child_is_addressed_by_its_whole_path_not_its_bare_name()
    {
        // Names repeat - a screen copied from the game's own carries several
        // objects called "Image" - so a bare name would resolve to whichever
        // the runtime's walk happened to meet first.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;

            var root = ui.Nodes[0];
            root.Model.Name = "Panel";
            var left = root.AddChild(new UiNodeDef { Name = "Left" });
            var right = root.AddChild(new UiNodeDef { Name = "Right" });
            left.AddChild(new UiNodeDef { Name = "Image" });
            right.AddChild(new UiNodeDef { Name = "Image" });
            vm.RebuildUiOptions();
            WindowHarness.Pump();

            var tokens = vm.UiIdOptions.Select(o => o.Token).ToList();
            string id = ui.Model.Id;

            _out.WriteLine(string.Join(", ", tokens.Where(t => t.EndsWith("Image"))));
            Assert.Contains(id + "/Panel/Left/Image", tokens);
            Assert.Contains(id + "/Panel/Right/Image", tokens);
            Assert.Equal(tokens.Count, tokens.Distinct().Count());   // no two entries collide
        });
    }

    [Fact]
    public void Two_children_sharing_a_name_do_not_bring_the_editor_down()
    {
        // Reported as "Index must be within the bounds of the List" while
        // opening a pack. Every card copied from the game's own shop carries
        // two children called "Image", so both had the same address; the
        // in-place options sync searched from the start each time, never placed
        // the second, and walked off the end of a collection that had stopped
        // growing.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;

            var root = ui.Nodes[0];
            root.Model.Name = "Panel";
            // Straight onto the model, because adding through the tree gives
            // the second one a unique name - which is right for the editor and
            // wrong for this test. A pack arrives deserialized, and the game's
            // own screens really do have two same-named children.
            var card = new UiNodeDef { Name = "Card" };
            card.Children.Add(new UiNodeDef { Name = "Image" });
            card.Children.Add(new UiNodeDef { Name = "Image" });   // the shape of the crash
            root.Model.Children.Add(card);

            vm.RebuildUiOptions();          // threw here
            vm.RebuildUiOptions();          // and again on the second pass
            WindowHarness.Pump();

            var tokens = vm.UiIdOptions.Select(o => o.Token).ToList();
            _out.WriteLine(string.Join(", ", tokens));

            string id = ui.Model.Id;
            Assert.Contains(id + "/Panel/Card/Image", tokens);

            // Once, not twice: the runtime walks direct children by name and
            // stops at the first, so only one of the two is reachable and
            // offering both would promise something that cannot be done.
            Assert.Equal(1, tokens.Count(t => t == id + "/Panel/Card/Image"));
            Assert.Equal(tokens.Count, tokens.Distinct().Count());
        });
    }

    [Fact]
    public void An_options_list_that_wants_a_value_twice_keeps_both()
    {
        // The helper underneath, reached through the one caller that can hold
        // repeats. Names that repeat across DIFFERENT screens are legitimate -
        // two packs' screens can each have a "Close" - and both entries have to
        // survive, so the fix cannot simply drop duplicates on the floor.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var first = vm.Uis.Last();
            first.Name = "Shop";
            first.Nodes[0].Model.Name = "Panel";
            first.Nodes[0].AddChild(new UiNodeDef { Name = "Close" });

            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            var second = vm.Uis.Last();
            second.Name = "Gifts";
            second.Nodes[0].Model.Name = "Panel";
            second.Nodes[0].AddChild(new UiNodeDef { Name = "Close" });

            vm.RebuildUiOptions();
            WindowHarness.Pump();

            var tokens = vm.UiIdOptions.Select(o => o.Token).ToList();
            _out.WriteLine(string.Join(", ", tokens.Where(t => t.EndsWith("Close"))));

            Assert.Contains(first.Model.Id + "/Panel/Close", tokens);
            Assert.Contains(second.Model.Id + "/Panel/Close", tokens);
        });
    }
}
