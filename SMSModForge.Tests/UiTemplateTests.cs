using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The starting shapes offered for a UI of the pack's own.
/// <para/>
/// The point of a template is that it looks like the game. That makes a
/// mistyped sprite or font name the failure that matters here: it produces a
/// window with a hole where its background should be, and nothing says so
/// except the picture. So the first test draws every template against the real
/// extraction and insists the renderer found everything it asked for.
/// </summary>
public sealed class UiTemplateTests
{
    private readonly ITestOutputHelper _out;
    public UiTemplateTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Every_template_asks_only_for_art_the_game_has()
    {
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var complaints = new System.Collections.Generic.List<string>();

        foreach (var template in UiTemplate.All)
        {
            var report = new UiRenderReport();
            var pixels = UiAuthoredRenderer.Render(template.Build(), 1920, 1080,
                                                   VanillaUiLibrary.Assets, report);

            if (pixels.Length == 0) complaints.Add($"{template.Key}: drew nothing");
            foreach (var sprite in report.MissingSprites)
                complaints.Add($"{template.Key}: no sprite '{sprite}'");
            foreach (var font in report.MissingFonts)
                complaints.Add($"{template.Key}: no font '{font}'");
            foreach (var glyph in report.MissingGlyphs)
                complaints.Add($"{template.Key}: unbaked glyph {glyph}");

            _out.WriteLine($"{template.Key,-9} {(complaints.Count == 0 ? "ok" : "")}");
        }

        Assert.Empty(complaints);
    }

    [Fact]
    public void Every_template_draws_something_you_can_see()
    {
        // A tree of correctly-named art that all lands off screen, or at zero
        // size, passes the test above and is still useless.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        foreach (var template in UiTemplate.All)
        {
            var built = template.Build();
            var pixels = UiAuthoredRenderer.Render(built, 1920, 1080, VanillaUiLibrary.Assets);

            // Any pixel with alpha at all. The buffer is premultiplied BGRA.
            int painted = 0;
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] != 0) painted++;

            _out.WriteLine($"{template.Key,-9} {painted} pixels");
            Assert.True(painted > 500, $"{template.Key} drew {painted} visible pixels");
        }
    }

    [Fact]
    public void Two_uis_from_one_template_are_two_separate_things()
    {
        // A shared tree would make editing one edit the other, and the second
        // author to notice would be the one filing the bug.
        var a = UiTemplate.Find("window")!.Build();
        var b = UiTemplate.Find("window")!.Build();

        Assert.False(ReferenceEquals(a, b));
        a.Name = "Changed";
        a.Children[0].Rect.Position[0] = 999;

        Assert.Equal("Window", b.Name);
        Assert.Equal(0, b.Children[0].Rect.Position[0]);
    }

    [Fact]
    public void Starting_a_ui_from_a_template_records_which_one()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var dialog = UiTemplate.Find("dialog")!;

            vm.AddOwnUiCommand.Execute(dialog);

            var made = vm.Uis.Last();
            Assert.Equal("dialog", made.Model.Template);
            Assert.Equal("Dialog", made.TemplateName);
            Assert.Single(made.Model.Nodes);
            Assert.Equal("Dialog", made.Model.Nodes[0].Name);

            // The shape actually arrived: a title, a close, a message and two
            // buttons.
            var kids = made.Model.Nodes[0].Children.Select(c => c.Name).ToList();
            _out.WriteLine(string.Join(", ", kids));
            Assert.Contains("Title", kids);
            Assert.Contains("Close", kids);
            Assert.Contains("Confirm", kids);
            Assert.Contains("Cancel", kids);
        });
    }

    [Fact]
    public void Starting_one_with_no_template_still_gives_something_to_look_at()
    {
        // The command is on a menu header as well as its items, and a header
        // click passes nothing.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(null);

            var made = vm.Uis.Last();
            Assert.Single(made.Model.Nodes);
            Assert.NotNull(made.Model.Nodes[0].Image);
        });
    }

    [Fact]
    public void A_piece_lands_inside_whatever_is_selected()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("window"));

            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            ui.SelectedNode = ui.Nodes[0];

            ui.AddChildTemplateCommand.Execute(UiTemplate.Find("button"));

            var added = ui.Nodes[0].Children.Last();
            Assert.Equal("Button", added.Model.Name);
            Assert.Single(added.Children);              // its label
            Assert.Equal("Button", added.Children[0].Model.Text!.Value);
            Assert.Equal("Button", ui.SelectedNode!.Model.Name);
        });
    }

    [Fact]
    public void A_piece_added_twice_does_not_share_a_name_with_itself()
    {
        // Two siblings called Button make a bind path ambiguous, and the tree
        // is where that is cheapest to stop.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("window"));

            var ui = vm.Uis.Last();
            vm.SelectedUi = ui;
            ui.SelectedNode = ui.Nodes[0];
            ui.AddChildTemplateCommand.Execute(UiTemplate.Find("button"));

            ui.SelectedNode = ui.Nodes[0];
            ui.AddChildTemplateCommand.Execute(UiTemplate.Find("button"));

            var names = ui.Nodes[0].Children.Select(c => c.Model.Name).ToList();
            _out.WriteLine(string.Join(", ", names));
            Assert.Equal(names.Count, names.Distinct().Count());
        });
    }

    [Fact]
    public void A_templated_ui_survives_being_saved_and_reopened()
    {
        var pack = new ModPack();
        var template = UiTemplate.Find("list")!;
        var def = new UiDef { Name = "Stats", Id = "stats", Template = template.Key };
        def.Nodes.Add(template.Build());
        pack.Uis.Add(def);

        string json = PackRepository.SerializeAsSaved(pack);
        var reopened = PackRepository.Deserialize(json);

        var back = reopened.Uis.Single();
        Assert.Equal("list", back.Template);
        Assert.Equal("List", back.Nodes[0].Name);
        Assert.Equal(7, back.Nodes[0].Children.Count);   // a title and six rows
        Assert.Equal("#C5C5C5FF", back.Nodes[0].Children[1].Image!.Tint);
    }

    [Fact]
    public void A_sorting_order_is_offered_only_where_it_does_something()
    {
        // Inside the gameplay canvas the UI is one object among the game's, and
        // order there is sibling order - a number would do nothing at all.
        var def = new UiDef { Name = "Overlay" };
        var vm = new UiViewModel(def);

        vm.HidesWithGameplayUi = true;
        Assert.False(vm.ShowsSortingOrder);

        vm.HidesWithGameplayUi = false;
        Assert.True(vm.ShowsSortingOrder);

        // And never for a change to a screen the game owns, which inherits its
        // place from that screen.
        vm.WantsVanilla = true;
        Assert.False(vm.ShowsSortingOrder);
    }
}
