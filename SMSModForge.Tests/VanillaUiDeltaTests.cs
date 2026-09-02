using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What a vanilla UI extension stores: the change, and nothing it merely
/// copied.
/// <para/>
/// Built against a hand-made baseline rather than the extraction, so the rules
/// are checked in isolation and the tests run on a clone that has never opened
/// Unity.
/// </summary>
public class VanillaUiDeltaTests
{
    private readonly ITestOutputHelper _out;
    public VanillaUiDeltaTests(ITestOutputHelper o) => _out = o;

    // ── A small pretend vanilla screen ───────────────────────────────

    private static VanillaUiSurface.Node Vanilla() => new()
    {
        Name = "Panel",
        ActiveSelf = true,
        Rect = new VanillaUiSurface.Rect
        {
            AnchorMin = new[] { 0.5f, 0.5f },
            AnchorMax = new[] { 0.5f, 0.5f },
            Pivot = new[] { 0.5f, 0.5f },
            AnchoredPosition = new[] { 0f, 0f },
            SizeDelta = new[] { 400f, 300f },
        },
        Image = new VanillaUiSurface.Image
        {
            Sprite = "Semi Rounded", Type = "Sliced", Color = "#FFFFFFFF",
        },
        Children =
        {
            new VanillaUiSurface.Node
            {
                Name = "Title",
                ActiveSelf = true,
                Rect = new VanillaUiSurface.Rect
                {
                    AnchorMin = new[] { 0.5f, 1f },
                    AnchorMax = new[] { 0.5f, 1f },
                    Pivot = new[] { 0.5f, 1f },
                    AnchoredPosition = new[] { 0f, -20f },
                    SizeDelta = new[] { 300f, 50f },
                },
                Text = new VanillaUiSurface.Text
                {
                    Kind = "TextMeshProUGUI", Value = "Shop", Font = "Curse Casual SDF",
                    FontSize = "36", Color = "#000000FF", Alignment = "Center",
                },
            },
        },
    };

    /// <summary>A lookup that answers from the pretend screen above.</summary>
    private static VanillaUiDelta.BaselineLookup Lookup(VanillaUiSurface.Node? root = null)
    {
        var tree = root ?? Vanilla();
        return (_, path) => VanillaUiDelta.NodeAt(tree, path);
    }

    /// <summary>The authored tree as it looks straight after seeding: bound to
    /// every vanilla node, carrying the vanilla values, asserting nothing.</summary>
    private static UiNodeDef Seeded()
    {
        var panel = new UiNodeDef
        {
            Name = "Panel",
            Bind = ".",                     // the base itself
            StartActive = true,
            Rect = new UiRectDef
            {
                AnchorMin = new[] { 0.5f, 0.5f }, AnchorMax = new[] { 0.5f, 0.5f },
                Pivot = new[] { 0.5f, 0.5f }, Position = new[] { 0f, 0f },
                Size = new[] { 400f, 300f },
            },
            Image = new UiImageDef { Sprite = "Semi Rounded", Type = "Sliced", Tint = "#FFFFFFFF" },
        };
        panel.Children.Add(new UiNodeDef
        {
            Name = "Title",
            Bind = "Title",
            StartActive = true,
            Rect = new UiRectDef
            {
                AnchorMin = new[] { 0.5f, 1f }, AnchorMax = new[] { 0.5f, 1f },
                Pivot = new[] { 0.5f, 1f }, Position = new[] { 0f, -20f },
                Size = new[] { 300f, 50f },
            },
            Text = new UiTextDef
            {
                Value = "Shop", Font = "Curse Casual SDF", Size = 36,
                Color = "#000000FF", Alignment = "Center",
            },
        });
        return panel;
    }

    private static ModPack PackWith(params UiNodeDef[] nodes)
    {
        var pack = new ModPack { PackId = "test" };
        pack.VanillaUiExtensions.Add(new VanillaUiExtensionDef
        {
            Source = "vanillaui:9_MainCanvas/Shop",
            Nodes = nodes.ToList(),
        });
        return pack;
    }

    // ── Paths ────────────────────────────────────────────────────────

    [Fact]
    public void A_bind_path_walks_down_from_the_base()
    {
        var tree = Vanilla();
        Assert.Same(tree, VanillaUiDelta.NodeAt(tree, "."));
        Assert.Same(tree, VanillaUiDelta.NodeAt(tree, ""));
        Assert.Equal("Title", VanillaUiDelta.NodeAt(tree, "Title")!.Name);
        Assert.Null(VanillaUiDelta.NodeAt(tree, "NotThere"));
        Assert.Null(VanillaUiDelta.NodeAt(tree, "Title/Deeper"));
    }

    [Fact]
    public void Repeated_sibling_names_are_told_apart_by_the_same_suffix_the_catalog_uses()
    {
        // Real in the game: two children called Image under one canvas. An
        // author who binds to the second must keep binding to the second.
        var tree = new VanillaUiSurface.Node
        {
            Name = "Root",
            Children =
            {
                new VanillaUiSurface.Node { Name = "Image", Text = new VanillaUiSurface.Text { Value = "first" } },
                new VanillaUiSurface.Node { Name = "Image", Text = new VanillaUiSurface.Text { Value = "second" } },
            },
        };

        Assert.Equal("first", VanillaUiDelta.NodeAt(tree, "Image")!.Text!.Value);
        Assert.Equal("first", VanillaUiDelta.NodeAt(tree, "Image#1")!.Text!.Value);
        Assert.Equal("second", VanillaUiDelta.NodeAt(tree, "Image#2")!.Text!.Value);
        Assert.Null(VanillaUiDelta.NodeAt(tree, "Image#3"));
    }

    // ── Pruning ──────────────────────────────────────────────────────

    [Fact]
    public void A_seeded_tree_that_changes_nothing_stores_nothing()
    {
        // The case that matters most. Opening a vanilla screen and closing it
        // again must not write four hundred objects into the manifest.
        var pack = PackWith(Seeded());
        var restore = VanillaUiDelta.PrepareForSave(pack, Lookup());
        try
        {
            Assert.Empty(pack.VanillaUiExtensions[0].Nodes);
        }
        finally { restore(); }
    }

    [Fact]
    public void The_live_model_comes_back_exactly_as_it_went_in()
    {
        // Saving must not rewrite what is on screen. The author's tree is still
        // their tree afterwards.
        var pack = PackWith(Seeded());
        string before = JsonConvert.SerializeObject(pack.VanillaUiExtensions[0].Nodes);

        var restore = VanillaUiDelta.PrepareForSave(pack, Lookup());
        Assert.Empty(pack.VanillaUiExtensions[0].Nodes);
        restore();

        Assert.Equal(before, JsonConvert.SerializeObject(pack.VanillaUiExtensions[0].Nodes));
        Assert.Equal(2, pack.VanillaUiExtensions[0].Nodes[0].Children.Count + 1);
    }

    [Fact]
    public void Moving_one_object_stores_that_object_and_its_line_of_descent()
    {
        var tree = Seeded();
        tree.Children[0].Rect.Position = new[] { 12f, -20f };   // nudged sideways

        var pack = PackWith(tree);
        var restore = VanillaUiDelta.PrepareForSave(pack, Lookup());
        try
        {
            var stored = pack.VanillaUiExtensions[0].Nodes;
            var panel = Assert.Single(stored);

            // The panel itself changed nothing, but it is kept because it is the
            // path to something that did.
            Assert.False(panel.OverrideRect);
            Assert.False(panel.OverrideImage);

            var title = Assert.Single(panel.Children);
            Assert.True(title.OverrideRect);
            Assert.False(title.OverrideText);
            Assert.Equal(12f, title.Rect.Position[0]);

            // And the values it is NOT asserting are gone, so the manifest says
            // what the pack does rather than what it copied.
            Assert.Null(title.Text);
            _out.WriteLine(JsonConvert.SerializeObject(stored, Formatting.Indented));
        }
        finally { restore(); }
    }

    [Fact]
    public void A_retyped_label_is_stored_as_text_and_not_as_geometry()
    {
        var tree = Seeded();
        tree.Children[0].Text!.Value = "Emporium";

        var pack = PackWith(tree);
        var restore = VanillaUiDelta.PrepareForSave(pack, Lookup());
        try
        {
            var title = pack.VanillaUiExtensions[0].Nodes[0].Children[0];
            Assert.True(title.OverrideText);
            Assert.False(title.OverrideRect);
            Assert.Equal("Emporium", title.Text!.Value);
        }
        finally { restore(); }
    }

    [Fact]
    public void Override_flags_come_from_the_comparison_not_from_the_author()
    {
        // A flag ticked by hand on a node that matches vanilla is cleared; one
        // left unticked on a node that differs is set. The manifest describes
        // what is true rather than what someone remembered.
        var tree = Seeded();
        tree.OverrideImage = true;                          // ticked, but nothing changed
        tree.Children[0].OverrideText = false;              // unticked, but the text differs
        tree.Children[0].Text!.Value = "Changed";

        var pack = PackWith(tree);
        var restore = VanillaUiDelta.PrepareForSave(pack, Lookup());
        try
        {
            var panel = pack.VanillaUiExtensions[0].Nodes[0];
            Assert.False(panel.OverrideImage);
            Assert.True(panel.Children[0].OverrideText);
        }
        finally { restore(); }
    }

    [Fact]
    public void A_colour_spelled_differently_is_not_a_change()
    {
        var tree = Seeded();
        tree.Image!.Tint = "#ffffff";        // same white, fewer digits, lower case
        tree.Children[0].Text!.Color = "#000000";

        var pack = PackWith(tree);
        var restore = VanillaUiDelta.PrepareForSave(pack, Lookup());
        try
        {
            Assert.Empty(pack.VanillaUiExtensions[0].Nodes);
        }
        finally { restore(); }
    }

    [Fact]
    public void Rounding_from_the_extractor_is_not_a_change()
    {
        // Every number here went through a five-decimal text form on the way
        // out of Unity, so an exact compare would invent differences.
        var tree = Seeded();
        tree.Rect.Size = new[] { 400.00001f, 299.99999f };

        var pack = PackWith(tree);
        var restore = VanillaUiDelta.PrepareForSave(pack, Lookup());
        try { Assert.Empty(pack.VanillaUiExtensions[0].Nodes); }
        finally { restore(); }
    }

    [Fact]
    public void Switching_something_off_is_a_change_worth_storing()
    {
        var tree = Seeded();
        tree.Children[0].StartActive = false;

        var pack = PackWith(tree);
        var restore = VanillaUiDelta.PrepareForSave(pack, Lookup());
        try
        {
            var title = pack.VanillaUiExtensions[0].Nodes[0].Children[0];
            Assert.True(title.OverrideActive);
            Assert.False(title.StartActive);
        }
        finally { restore(); }
    }

    [Fact]
    public void An_object_the_pack_added_is_always_kept()
    {
        var tree = Seeded();
        tree.Children.Add(new UiNodeDef { Name = "My Button" });   // no Bind

        var pack = PackWith(tree);
        var restore = VanillaUiDelta.PrepareForSave(pack, Lookup());
        try
        {
            var panel = Assert.Single(pack.VanillaUiExtensions[0].Nodes);
            var added = Assert.Single(panel.Children);
            Assert.Equal("My Button", added.Name);
            Assert.False(added.IsBound);
        }
        finally { restore(); }
    }

    [Fact]
    public void Behaviour_attached_to_a_vanilla_object_keeps_it()
    {
        // Behaviour on an object the game already has: nothing about its
        // appearance changed, so a comparison alone would drop it - but the
        // component is the whole point of the extension.
        var tree = Seeded();
        tree.Children[0].Components.Add(new ComponentDef { Type = "FadeInSprite" });

        var pack = PackWith(tree);
        var restore = VanillaUiDelta.PrepareForSave(pack, Lookup());
        try
        {
            var title = Assert.Single(pack.VanillaUiExtensions[0].Nodes[0].Children);
            Assert.Equal("Title", title.Name);
            Assert.Single(title.Components);
        }
        finally { restore(); }
    }

    [Fact]
    public void Without_a_baseline_the_authors_own_flags_are_honoured()
    {
        // An editor that has never run the extraction. Guessing would be worse
        // than doing as told.
        var tree = Seeded();
        tree.OverrideRect = true;

        var pack = PackWith(tree);
        var restore = VanillaUiDelta.PrepareForSave(pack, (_, _) => null);
        try
        {
            var panel = Assert.Single(pack.VanillaUiExtensions[0].Nodes);
            Assert.True(panel.OverrideRect);
            Assert.Single(panel.Children);
        }
        finally { restore(); }
    }

    [Fact]
    public void Measure_says_how_much_smaller_the_stored_form_is()
    {
        var tree = Seeded();
        tree.Children[0].Rect.Position = new[] { 12f, -20f };

        var extension = new VanillaUiExtensionDef
        {
            Source = "vanillaui:9_MainCanvas/Shop",
            Nodes = { tree },
        };
        var (before, after) = VanillaUiDelta.Measure(extension, Lookup());

        Assert.Equal(2, before);
        Assert.Equal(2, after);       // the path to the change is kept

        var untouched = new VanillaUiExtensionDef
        {
            Source = "vanillaui:9_MainCanvas/Shop",
            Nodes = { Seeded() },
        };
        var (was, now) = VanillaUiDelta.Measure(untouched, Lookup());
        Assert.Equal(2, was);
        Assert.Equal(0, now);
        _out.WriteLine($"untouched: {was} nodes -> {now}");
    }

    [Fact]
    public void A_pack_with_no_ui_extensions_is_left_alone()
    {
        var pack = new ModPack { PackId = "test" };
        var restore = VanillaUiDelta.PrepareForSave(pack, Lookup());
        Assert.Empty(pack.VanillaUiExtensions);
        restore();
    }
}
