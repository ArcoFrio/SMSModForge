using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The UI model, and the catalog of vanilla UI it is authored against.
/// </summary>
public class UiModelTests
{
    private readonly ITestOutputHelper _out;
    public UiModelTests(ITestOutputHelper o) => _out = o;

    /// <summary>The catalog ships beside the exe. Tests run from their own
    /// output folder, so it is copied there by the project reference — if it is
    /// missing, that is a build wiring problem and worth failing over rather
    /// than skipping past.</summary>
    private static void RequireCatalog()
    {
        string path = Path.Combine(AppContext.BaseDirectory,
                                   "VanillaUi", "vanilla_ui_bases.json");
        Assert.True(File.Exists(path),
            "vanilla_ui_bases.json was not copied next to the tests. Expected " + path);
    }

    // ── The catalog ──────────────────────────────────────────────────

    [Fact]
    public void The_catalog_loads_and_knows_the_gameplay_canvas()
    {
        RequireCatalog();
        Assert.True(VanillaUiCatalog.IsAvailable);
        Assert.Equal("9_MainCanvas", VanillaUiCatalog.GameplayCanvas);
        _out.WriteLine($"{VanillaUiCatalog.Surfaces.Count} surfaces, " +
                       $"{VanillaUiCatalog.AllBases.Count()} bases");
    }

    [Fact]
    public void The_main_canvas_is_split_into_the_screens_it_actually_holds()
    {
        RequireCatalog();
        // The whole reason a base is a child rather than a surface: this canvas
        // is 4087 objects, and nobody should open all of them to edit Payout.
        var main = VanillaUiCatalog.Surfaces.Single(s => s.Id == "9_MainCanvas");
        var names = main.Bases.Select(b => b.Name).ToList();

        Assert.Contains("Payout", names);
        Assert.Contains("Starmaker", names);
        Assert.Contains("AfterSleepEvents", names);
        Assert.Contains("Navigator", names);
        Assert.True(main.Bases.Count >= 20, "the main canvas holds a screen per child");

        // And each is a fraction of the whole.
        var payout = main.Bases.Single(b => b.Name == "Payout");
        Assert.True(payout.Objects < 200, $"Payout should be small, was {payout.Objects}");
        _out.WriteLine(payout.Token + "  " + payout.Summary);
    }

    [Fact]
    public void A_base_is_addressed_by_token_or_by_path()
    {
        RequireCatalog();
        var byToken = VanillaUiCatalog.Find("vanillaui:9_MainCanvas/Payout");
        var byPath = VanillaUiCatalog.Find("9_MainCanvas/Payout");

        Assert.NotNull(byToken);
        Assert.Same(byToken, byPath);
        Assert.Equal("vanillaui:9_MainCanvas/Payout", byToken!.Token);
    }

    [Fact]
    public void Siblings_that_share_a_name_still_address_separately()
    {
        RequireCatalog();
        // Real in this scene: two children called ChangeBetButton under one
        // canvas, two called Image under another. A path alone would silently
        // pick whichever was found first, so those - and only those - carry a
        // suffix.
        var ambiguous = VanillaUiCatalog.AllBases.Where(b => b.AmbiguousName).ToList();
        Assert.NotEmpty(ambiguous);

        foreach (var b in ambiguous)
        {
            Assert.Contains("#", b.Id);
            Assert.Same(b, VanillaUiCatalog.Find(b.Token));
            _out.WriteLine(b.Token);
        }

        // Every id is unique, suffixed or not - otherwise the lookup is a lie.
        var ids = VanillaUiCatalog.AllBases.Select(b => b.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Unsuffixed ids must NOT contain a '#', or the suffix stops meaning
        // anything.
        Assert.All(VanillaUiCatalog.AllBases.Where(b => !b.AmbiguousName),
                   b => Assert.DoesNotContain("#", b.Id));

        // The collision that made this necessary: one level holds two canvases
        // with the same name, each with a child called Button. Suffixing only
        // the base name would not have separated them - the surface had to be
        // suffixed too.
        var twins = VanillaUiCatalog.Surfaces.Where(s => s.AmbiguousPath).ToList();
        Assert.NotEmpty(twins);
        Assert.All(twins, s => Assert.Contains("#", s.Id));
        Assert.All(twins, s => Assert.DoesNotContain("#", s.Path));
    }

    [Fact]
    public void Only_the_gameplay_canvas_dims_with_the_interface()
    {
        RequireCatalog();
        Assert.True(VanillaUiCatalog.DimsWithGameplayUi("vanillaui:9_MainCanvas/Payout"));

        // A canvas of its own does not inherit that group, which is the whole
        // distinction the author is being asked about.
        var elsewhere = VanillaUiCatalog.AllBases
            .First(b => b.Surface.Id == "15_EventUI");
        Assert.False(VanillaUiCatalog.DimsWithGameplayUi(elsewhere.Token));

        Assert.Single(VanillaUiCatalog.Surfaces, s => s.DimsWithGameplayUi);
    }

    [Fact]
    public void A_base_the_catalog_has_never_heard_of_is_null_not_a_crash()
    {
        RequireCatalog();
        // A pack written against a newer game reaches names this build lacks.
        Assert.Null(VanillaUiCatalog.Find("vanillaui:9_MainCanvas/NotAThing"));
        Assert.Null(VanillaUiCatalog.Find(""));
        Assert.Null(VanillaUiCatalog.Find(null));
        Assert.False(VanillaUiCatalog.DimsWithGameplayUi("vanillaui:Nope"));
    }

    [Fact]
    public void Surfaces_that_cannot_be_drawn_are_kept_out_of_what_is_offered()
    {
        RequireCatalog();
        // Six canvases have their Canvas component disabled, so Unity never
        // sized them and everything under them measured as a point. Offering
        // those would promise a preview the editor cannot produce.
        var unusable = VanillaUiCatalog.Surfaces.Where(s => !s.Usable).ToList();
        Assert.NotEmpty(unusable);
        _out.WriteLine("unusable: " + string.Join(", ", unusable.Select(s => s.Path)));

        Assert.All(VanillaUiCatalog.UsableBases, b => Assert.True(b.Surface.Usable));
        Assert.True(VanillaUiCatalog.UsableBases.Count() < VanillaUiCatalog.AllBases.Count());
    }

    // ── The authored shape ───────────────────────────────────────────

    [Fact]
    public void A_new_ui_defaults_to_hiding_with_the_rest_of_the_interface()
    {
        // The common case by a distance: a pack's window belongs to normal play
        // and should vanish during a cutscene like everything else.
        Assert.True(new UiDef().HidesWithGameplayUi);
        Assert.False(new UiDef().StartsOpen);
    }

    [Fact]
    public void An_untouched_ui_serialises_to_almost_nothing()
    {
        // Defaults must not be written out. A manifest that records every
        // property of every node is unreadable in a diff, and a diff is how an
        // author sees what they changed.
        string json = JsonConvert.SerializeObject(
            new UiDef { Id = "my-window", Name = "My Window" }, Formatting.None);
        _out.WriteLine(json);

        var o = JObject.Parse(json);
        Assert.Equal(new[] { "id", "name", "hidesWithGameplayUi" },
                     o.Properties().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void A_node_that_only_moves_something_stores_only_that()
    {
        // The delta scheme: a bound node names a vanilla object and carries the
        // one thing it changed. Everything else stays as the game made it, so a
        // game update carries the extension along instead of fighting it.
        var node = new UiNodeDef { Bind = "Header/Title", OverrideRect = true };
        node.Rect.Position = new[] { 12f, -4f };

        string json = JsonConvert.SerializeObject(node, Formatting.None);
        _out.WriteLine(json);

        var o = JObject.Parse(json);
        Assert.Equal("Header/Title", (string?)o["bind"]);
        Assert.True((bool?)o["overrideRect"]);
        Assert.Null(o["overrideImage"]);
        Assert.Null(o["image"]);
        Assert.Null(o["children"]);
        Assert.Null(o["components"]);
        Assert.True(node.IsBound);
        Assert.False(new UiNodeDef().IsBound);
    }

    [Fact]
    public void A_ui_round_trips_through_the_manifest()
    {
        var pack = new ModPack { PackId = "test" };
        pack.Uis.Add(new UiDef
        {
            Id = "shop",
            Name = "Shop",
            HidesWithGameplayUi = false,
            SortingOrder = 30,
            Nodes = { new UiNodeDef { Name = "Panel",
                                      Image = new UiImageDef { Sprite = "Semi Rounded",
                                                               Type = "Sliced",
                                                               Tint = "#302F46FF" } } },
        });
        pack.Uis.Add(new UiDef
        {
            Source = "vanillaui:9_MainCanvas/Payout",
            Nodes = { new UiNodeDef { Name = "Extra", Bind = "Line" } },
        });

        string json = JsonConvert.SerializeObject(pack, Formatting.Indented);
        var back = JsonConvert.DeserializeObject<ModPack>(json)!;

        // ONE list holding both kinds, told apart by whether they name a
        // vanilla screen rather than by which list they are in.
        Assert.Equal(2, back.Uis.Count);

        var ui = back.Uis.Single(u => !u.IsVanillaBased);
        Assert.Equal("shop", ui.Id);
        Assert.False(ui.HidesWithGameplayUi);
        Assert.Equal(30, ui.SortingOrder);
        Assert.Equal("Sliced", Assert.Single(ui.Nodes).Image!.Type);

        var ext = back.Uis.Single(u => u.IsVanillaBased);
        Assert.Equal("vanillaui:9_MainCanvas/Payout", ext.Source);
        Assert.NotNull(VanillaUiCatalog.Find(ext.Source));
    }

    [Fact]
    public void A_pack_with_no_ui_writes_no_ui_keys()
    {
        // Adding a feature must not rewrite every existing pack on next save.
        string json = JsonConvert.SerializeObject(new ModPack { PackId = "test" });
        var o = JObject.Parse(json);
        Assert.Null(o["uis"]);
        Assert.Null(o["vanillaUiExtensions"]);
        Assert.Null(o["uiFolders"]);
    }
}
