using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The UI tab's "where it sits" and "how it looks" controls, read back from
/// where the object really lands and from what the save holds - not from the
/// numbers the code just wrote.
/// </summary>
public sealed class UiPlacementTests
{
    private readonly ITestOutputHelper _out;
    public UiPlacementTests(ITestOutputHelper output) => _out = output;

    private const double CanvasW = 1920, CanvasH = 1080;

    // A parent 800x600 in the middle of the canvas: 560..1360 across, 240..840 down.
    private const double ParentLeft = 560, ParentRight = 1360, ParentTop = 240, ParentBottom = 840;

    private static (UiNodeDef Root, UiNodeViewModel Node) Make()
    {
        var root = new UiNodeDef { Name = "Parent", Rect = new UiRectDef { Size = new[] { 800f, 600f } } };
        root.Children.Add(new UiNodeDef { Name = "Child", Rect = new UiRectDef { Size = new[] { 200f, 100f } } });
        var vm = new UiNodeViewModel(root);
        return (root, vm.Children[0]);
    }

    private static UiRect Drawn(UiNodeDef root, UiNodeViewModel node)
        => UiGeometry.ScreenRectOf(root, node.Model, CanvasW, CanvasH)!.Value;

    [Theory]
    [InlineData(0f, 1f)] [InlineData(0.5f, 1f)] [InlineData(1f, 1f)]
    [InlineData(0f, 0.5f)] [InlineData(0.5f, 0.5f)] [InlineData(1f, 0.5f)]
    [InlineData(0f, 0f)] [InlineData(0.5f, 0f)] [InlineData(1f, 0f)]
    public void Sticking_puts_it_flush_against_that_place_at_its_own_size(float x, float y)
    {
        var (root, node) = Make();
        node.PositionX = 77;      // somewhere else first: sticking has to bring it back
        node.PositionY = -33;

        node.StickTo(x, y);
        var r = Drawn(root, node);
        _out.WriteLine($"{x},{y}: {r.X},{r.Y} {r.Width}x{r.Height}");

        Assert.Equal(200, r.Width, 3);
        Assert.Equal(100, r.Height, 3);
        double left = x == 0 ? ParentLeft : x == 1 ? ParentRight - 200 : (ParentLeft + ParentRight) / 2 - 100;
        double top = y == 1 ? ParentTop : y == 0 ? ParentBottom - 100 : (ParentTop + ParentBottom) / 2 - 50;
        Assert.Equal(left, r.X, 3);
        Assert.Equal(top, r.Y, 3);

        // Only that place is lit - the fourth of nine reading left to right, top down.
        var lit = node.StickCells.Select((c, i) => (c, i)).Where(p => p.c.IsOn).Select(p => p.i).ToList();
        int expected = (y == 1 ? 0 : y == 0.5f ? 3 : 6) + (x == 0 ? 0 : x == 0.5f ? 1 : 2);
        Assert.Equal(new[] { expected }, lit);
    }

    [Fact]
    public void Filling_the_width_spans_the_parent_and_letting_go_brings_its_size_back()
    {
        var (root, node) = Make();
        Assert.Equal(200, Drawn(root, node).Width, 3);   // the control

        node.FillsWidth = true;
        var r = Drawn(root, node);
        Assert.Equal(ParentLeft, r.X, 3);
        Assert.Equal(ParentRight, r.Right, 3);
        Assert.Equal(100, r.Height, 3);

        // The whole middle row is lit: it runs along the middle of its parent.
        var lit = node.StickCells.Select((c, i) => (c, i)).Where(p => p.c.IsOn).Select(p => p.i).ToArray();
        Assert.Equal(new[] { 3, 4, 5 }, lit);

        node.FillsWidth = false;
        r = Drawn(root, node);
        Assert.Equal(200, r.Width, 3);
        Assert.Equal((ParentLeft + ParentRight) / 2 - 100, r.X, 3);
    }

    [Fact]
    public void Margins_keep_each_side_that_far_in_whatever_the_pivot()
    {
        var (root, node) = Make();
        node.StickTo(0, 1);       // a pivot that is not the middle
        node.FillsWidth = true;
        node.FillsHeight = true;

        node.MarginLeft = 10;
        node.MarginRight = 30;
        node.MarginTop = 20;
        node.MarginBottom = 40;

        var r = Drawn(root, node);
        _out.WriteLine($"{r.X},{r.Y} to {r.Right},{r.Top}");
        Assert.Equal(ParentLeft + 10, r.X, 3);
        Assert.Equal(ParentRight - 30, r.Right, 3);
        Assert.Equal(ParentTop + 20, r.Y, 3);
        Assert.Equal(ParentBottom - 40, r.Top, 3);

        Assert.Equal(10, node.MarginLeft, 3);
        Assert.Equal(30, node.MarginRight, 3);
        Assert.Equal(20, node.MarginTop, 3);
        Assert.Equal(40, node.MarginBottom, 3);
    }

    [Fact]
    public void Sticking_keeps_a_filled_side_filled_and_lets_go_of_one_filling_both()
    {
        var (root, node) = Make();
        node.FillsWidth = true;
        node.StickTo(0f, 1f);
        var r = Drawn(root, node);
        Assert.Equal(ParentLeft, r.X, 3);
        Assert.Equal(ParentRight, r.Right, 3);    // still across the whole width
        Assert.Equal(ParentTop, r.Y, 3);          // now along the top

        node.FillsHeight = true;
        Assert.True(node.StickCells.All(c => c.IsOn));

        // A click on a parent it fills would otherwise do nothing at all.
        node.StickTo(1f, 0f);
        r = Drawn(root, node);
        Assert.False(node.FillsWidth);
        Assert.False(node.FillsHeight);
        Assert.Equal(ParentRight, r.Right, 3);
        Assert.Equal(ParentBottom, r.Top, 3);
        Assert.Equal(200, r.Width, 3);
    }

    [Fact]
    public void Anchors_between_the_nine_places_light_nothing_and_say_so()
    {
        var (_, node) = Make();
        Assert.False(node.StretchesPartly);        // the control

        node.AnchorMinX = 0.2f;
        node.AnchorMaxX = 0.7f;
        Assert.DoesNotContain(node.StickCells, c => c.IsOn);
        Assert.True(node.StretchesPartly);
        Assert.False(node.FillsWidth);
    }

    [Fact]
    public void A_placement_comes_back_from_the_real_save()
    {
        var (root, node) = Make();
        node.StickTo(1f, 1f);
        node.FillsHeight = true;
        node.MarginTop = 12;
        node.RotationZ = 15;
        node.ScaleX = 0.5f;

        var pack = PackRepository.CreateEmpty("placement.check");
        pack.Uis.Add(new UiDef { Id = "placed", Name = "Placed", Nodes = { root } });
        string folder = Path.Combine(Path.GetTempPath(), "smsmf-placed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var before = Drawn(root, node);
            PackRepository.Save(pack, folder);
            var back = PackRepository.Deserialize(File.ReadAllText(Path.Combine(folder, "modpack.json"))).Uis.Single().Nodes.Single();
            var again = new UiNodeViewModel(back).Children[0];

            var after = Drawn(back, again);
            Assert.Equal(before.X, after.X, 3);
            Assert.Equal(before.Top, after.Top, 3);
            Assert.True(again.FillsHeight);
            Assert.Equal(15, again.RotationZ, 3);
            Assert.Equal(12, again.MarginTop, 3);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    // ── Taking something off one of the game's objects ───────────────────

    private static UiViewModel? Quit()
    {
        if (!VanillaUiLibrary.IsAvailable) return null;
        var ui = new UiViewModel(new UiDef { Source = "vanillaui:9_MainCanvas/Quitagme" });
        return ui.Nodes.Count > 0 ? ui : null;
    }

    private static UiNodeViewModel First(UiViewModel ui, Func<UiNodeViewModel, bool> where)
    {
        UiNodeViewModel? Walk(UiNodeViewModel n)
            => where(n) ? n : n.Children.Select(Walk).FirstOrDefault(f => f != null);
        return ui.Nodes.Select(Walk).First(f => f != null)!;
    }

    private static UiViewModel Reopened(UiViewModel ui)
    {
        var pack = PackRepository.CreateEmpty("takeoff.check");
        pack.Uis.Add(ui.Model);
        string folder = Path.Combine(Path.GetTempPath(), "smsmf-takeoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            PackRepository.Save(pack, folder);
            return new UiViewModel(PackRepository.Deserialize(File.ReadAllText(Path.Combine(folder, "modpack.json"))).Uis.Single());
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void A_picture_taken_off_a_games_object_stays_off_and_is_said_to_the_game()
    {
        var ui = Quit();
        if (ui == null) return;
        var node = First(ui, n => n.IsVanilla && n.HasImage && n.Parent != null);
        string bind = node.Model.Bind;

        node.Sprite = "";
        Assert.False(node.HasImage);
        Assert.True(ui.HasChanges(node.Model));
        // The form the plugin reads as "remove it": an image with no sprite. Left
        // out altogether, the game would keep its picture.
        Assert.Equal("", node.Model.Image!.Sprite);

        var back = First(Reopened(ui), n => n.Model.Bind == bind);
        Assert.False(back.HasImage);
    }

    [Fact]
    public void Words_taken_off_a_games_object_stay_off()
    {
        var ui = Quit();
        if (ui == null) return;
        var node = First(ui, n => n.IsVanilla && n.HasText);
        string bind = node.Model.Bind;

        node.Text = "";
        Assert.False(node.HasText);
        Assert.True(ui.HasChanges(node.Model));

        var back = First(Reopened(ui), n => n.Model.Bind == bind);
        Assert.False(back.HasText);
    }

    [Fact]
    public void A_picture_taken_off_the_packs_own_object_is_simply_gone()
    {
        var (_, node) = Make();
        node.Sprite = "Rounded";
        Assert.NotNull(node.Model.Image);
        node.Sprite = "";
        Assert.Null(node.Model.Image);     // nothing to tell the game: it was never there
    }

    [Fact]
    public void A_games_shadow_switched_off_is_made_clear_and_one_added_goes_away_entirely()
    {
        var ui = Quit();
        if (ui == null) return;

        // Has an outline in the game: having none would leave it standing.
        var outlined = First(ui, n => n.IsVanilla && n.Model.Outline != null);
        string bind = outlined.Model.Bind;
        Assert.True(outlined.HasOutline);
        outlined.HasOutline = false;
        Assert.False(outlined.HasOutline);
        Assert.NotNull(outlined.Model.Outline);
        Assert.Equal(0, UiColor.Parse(outlined.Model.Outline!.Color).A);
        Assert.True(ui.HasChanges(outlined.Model));
        Assert.False(First(Reopened(ui), n => n.Model.Bind == bind).HasOutline);

        // Has none in the game: on and off again leaves nothing.
        var plain = First(new UiViewModel(new UiDef { Source = "vanillaui:9_MainCanvas/Quitagme" }),
                          n => n.IsVanilla && n.Model.Shadow == null && n.Model.Outline == null && n.Parent != null);
        plain.HasShadow = true;
        Assert.NotNull(plain.Model.Shadow);
        plain.HasShadow = false;
        Assert.Null(plain.Model.Shadow);
    }

    // ── Read at a glance ─────────────────────────────────────────────────

    [Fact]
    public void A_closed_section_says_what_is_in_it()
    {
        var (_, node) = Make();
        string nothing = node.ClicksSummary;
        string always = node.ShowsSummary;
        Assert.NotEqual(nothing, always);

        node.AddActionCommand.Execute(null);
        Assert.Contains("1", node.ClicksSummary);
        node.AddActionCommand.Execute(null);
        Assert.Contains("2", node.ClicksSummary);
        Assert.NotEqual(nothing, node.ClicksSummary);

        node.StartActive = false;
        Assert.NotEqual(always, node.ShowsSummary);
        node.AddActiveConditionCommand.Execute(null);
        Assert.Contains("1", node.ShowsSummary);

        Assert.Equal(nothing, node.MovesSummary);
        node.PulsesFade = true;
        Assert.NotEqual(nothing, node.MovesSummary);

        Assert.Equal(nothing, node.InsideSummary);
        node.ClipsChildren = true;
        Assert.NotEqual(nothing, node.InsideSummary);

        Assert.Equal(nothing, node.ComponentsSummary);
        node.AddComponentCommand.Execute(null);
        Assert.NotEqual(nothing, node.ComponentsSummary);
        Assert.Single(node.Model.Components);       // and it is really there

        Assert.Equal("200 × 100", node.PlaceSummary);
        node.FillsWidth = true;
        Assert.NotEqual("200 × 100", node.PlaceSummary);
    }

    [Fact]
    public void What_an_object_is_comes_from_what_it_holds()
    {
        var (_, node) = Make();
        string empty = node.KindLabel;

        node.Text = "Words";
        string words = node.KindLabel;
        Assert.NotEqual(empty, words);

        node.Sprite = "Rounded";
        string picture = node.KindLabel;
        Assert.NotEqual(words, picture);

        node.AddActionCommand.Execute(null);
        Assert.NotEqual(picture, node.KindLabel);   // whatever it looks like, it is a button now
    }

    // ── Everything has a control ─────────────────────────────────────────

    /// <summary>Every saved setting of a UI object, and what on the tab edits it.
    /// A new saved setting fails until someone says where it can be reached:
    /// the tab had about twenty that could only be set by editing the file.</summary>
    private static readonly Dictionary<string, string> Controls = new()
    {
        ["UiNodeDef.name"] = "Name",
        ["UiNodeDef.alpha"] = "Opacity",
        ["UiNodeDef.shadow"] = "HasShadow",
        ["UiNodeDef.outline"] = "HasOutline",
        ["UiNodeDef.clipChildren"] = "ClipsChildren",
        ["UiNodeDef.startActive"] = "StartActive",
        ["UiNodeDef.activeConditions"] = "ActiveConditions",
        ["UiNodeDef.clickConditions"] = "ClickConditions",
        ["UiNodeDef.components"] = "Components",
        ["UiNodeDef.onClick"] = "Actions",
        ["UiNodeDef.hoverTint"] = "HoverTint",
        ["UiNodeDef.clickSound"] = "ClickSound",
        ["UiNodeDef.open"] = "Open",
        ["UiNodeDef.close"] = "Close",
        ["UiNodeDef.layout"] = "Arrange",
        ["UiNodeDef.children"] = "Children",
        ["UiNodeDef.upright"] = "Upright",
        ["UiNodeDef.pressScale"] = "PressScale",
        ["UiNodeDef.loop"] = "PulsesFade",
        ["UiRectDef.anchorMin"] = "AnchorMinX",
        ["UiRectDef.anchorMax"] = "AnchorMaxX",
        ["UiRectDef.pivot"] = "PivotX",
        ["UiRectDef.position"] = "PositionX",
        ["UiRectDef.size"] = "Width",
        ["UiRectDef.scale"] = "ScaleX",
        ["UiRectDef.rotationZ"] = "RotationZ",
        ["UiImageDef.sprite"] = "Sprite",
        ["UiImageDef.type"] = "ImageType",
        ["UiImageDef.tint"] = "Tint",
        ["UiImageDef.fillCenter"] = "FillsCenter",
        ["UiImageDef.preserveAspect"] = "KeepsAspect",
        ["UiImageDef.pixelsPerUnitMultiplier"] = "BorderScale",
        ["UiImageDef.raycastTarget"] = "CatchesMouse",
        ["UiTextDef.value"] = "Text",
        ["UiTextDef.font"] = "Font",
        ["UiTextDef.size"] = "FontSize",
        ["UiTextDef.color"] = "TextColor",
        ["UiTextDef.alignment"] = "TextAlignment",
        ["UiTextDef.wrap"] = "WrapsText",
        ["UiTextDef.lineSpacing"] = "LineSpacing",
        ["UiTextDef.characterSpacing"] = "LetterSpacing",
        ["UiTextDef.autoSize"] = "ShrinksToFit",
        ["UiTextDef.autoSizeMin"] = "ShrinkMin",
        ["UiTextDef.autoSizeMax"] = "ShrinkMax",
        ["UiEffectDef.color"] = "ShadowColor",
        ["UiEffectDef.distance"] = "ShadowX",
    };

    /// <summary>What the save works out for itself, or holds as parts above.</summary>
    private static readonly string[] NotEdited =
    {
        "UiNodeDef.rect", "UiNodeDef.image", "UiNodeDef.text",
        "UiNodeDef.bind", "UiNodeDef.overrideRect", "UiNodeDef.overrideImage", "UiNodeDef.overrideText",
        "UiNodeDef.overrideActive", "UiNodeDef.siblingIndex",
    };

    [Fact]
    public void Every_saved_setting_of_a_ui_object_has_a_control_on_the_tab()
    {
        string xaml = File.ReadAllText(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "MainWindow.xaml"));
        int from = xaml.IndexOf("Header=\"{l:T ui.ui}\"", StringComparison.Ordinal);
        int to = xaml.IndexOf("Header=\"{l:T quests.quests}\"", StringComparison.Ordinal);
        Assert.True(from > 0 && to > from);
        string tab = xaml.Substring(from, to - from);

        var missing = new List<string>();
        foreach (var type in new[] { typeof(UiNodeDef), typeof(UiRectDef), typeof(UiImageDef), typeof(UiTextDef), typeof(UiEffectDef) })
            foreach (var property in type.GetProperties())
                foreach (JsonPropertyAttribute a in property.GetCustomAttributes(typeof(JsonPropertyAttribute), false))
                {
                    string setting = type.Name + "." + a.PropertyName;
                    if (NotEdited.Contains(setting)) continue;
                    if (!Controls.TryGetValue(setting, out string? control))
                        missing.Add(setting + ": new, and nothing says where it can be reached");
                    else if (!Regex.IsMatch(tab, @"\{Binding\s+(Path=)?" + control + @"\b"))
                        missing.Add(setting + ": nothing on the tab binds " + control);
                }

        Assert.True(missing.Count == 0, string.Join("\n", missing));

        // The control: a binding that is not there is not found.
        Assert.DoesNotMatch(@"\{Binding\s+(Path=)?NoSuchSettingAnywhere\b", tab);
    }

    // ── The world map ────────────────────────────────────────────────────

    [Fact]
    public void Something_hung_on_the_packs_own_map_button_is_not_called_missing()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        const string token = MapButtonDef.WorldMapScreen;
        if (VanillaUiCatalog.Find(token) == null) return;

        var button = new MapButtonDef { Target = "self:SecretPlace", District = "Seaside" };
        Assert.Equal("Radial_Buttons/Seaside/SecretPlace", button.ScreenPath);

        UiDef Stored() => new()
        {
            Source = token,
            Nodes =
            {
                new UiNodeDef
                {
                    Name = "SecretPlace", Bind = button.ScreenPath,
                    Children = { new UiNodeDef { Name = "Mark", Image = new UiImageDef { Sprite = "Rounded" } } },
                },
            },
        };

        var mine = new UiViewModel(Stored(), _ => new HashSet<string> { button.ScreenPath });
        Assert.Equal(0, mine.Stranded);
        Assert.Equal("", mine.Warning);

        // Under its district, as it will be when the game runs, with what hangs on it.
        var placed = First(mine, n => n.Model.Bind == button.ScreenPath);
        Assert.Equal("Seaside", placed.Parent!.Name);
        Assert.Contains(placed.Children, c => c.Name == "Mark");

        // The control: told nothing, it is what the warning is for.
        var unknown = new UiViewModel(Stored());
        Assert.Equal(1, unknown.Stranded);
        Assert.NotEqual("", unknown.Warning);
    }
}
