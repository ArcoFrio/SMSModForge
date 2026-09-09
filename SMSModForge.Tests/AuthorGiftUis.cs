using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A one-off authoring pass: builds a pack's gift shop and gifting screen as
/// UI data, from copies of the game's own screens.
/// <para/>
/// Written as a test rather than a tool because it needs the editor's model and
/// the shipped extraction, and both are already wired up here. It is SKIPPED
/// unless the pack it targets is passed in, so it never runs in an ordinary
/// test pass and never touches anyone's pack by accident.
/// <para/>
/// Nothing about it is specific to the editor: the same two screens could be
/// assembled by hand through the UI tab, from the same menu items. This exists
/// because doing 150 objects by hand is tedious, not because it cannot be done.
/// </summary>
public sealed class AuthorGiftUis
{
    private readonly ITestOutputHelper _out;
    public AuthorGiftUis(ITestOutputHelper output) => _out = output;

    /// <summary>Set to a modpack.json to author into it. Unset, this does
    /// nothing.</summary>
    private static string? Target =>
        Environment.GetEnvironmentVariable("SMSMODFORGE_AUTHOR_PACK");

    /// <summary>The shop's stock, exactly as the C# builds it today.</summary>
    private static readonly (string Name, int Price, string Image)[] Stock =
    {
        ("Sunscreen", 600, "Items/Sunscreen.png"),
        ("Ring", 700, "Items/Ring.png"),
        ("Bikini", 900, "Items/Bikini.png"),
        ("Action Figure", 950, "Items/Figure.png"),
        ("Tropical Flower Bouquet", 1100, "Items/Bouquet.png"),
        ("Sunglasses", 1200, "Items/Sunglasses.png"),
        ("Parasol", 1600, "Items/Parasol.png"),
        ("Shark Tooth Necklace", 1850, "Items/Necklace.png"),
        ("Bonsai Tree", 3500, "Items/Bonsai.png"),
    };

    /// <summary>
    /// Everything giftable, as the C# lists it: the nine bought in the shop and
    /// the ten the game already has. Each carries the variable that says whether
    /// it is owned, and whether that variable is the game's or the pack's -
    /// which is what a visibility rule needs to ask the right store.
    /// </summary>
    private static readonly (string Variable, string Label, bool Vanilla, string Image)[] Gifts =
    {
        ("Gift_Action-Figure", "Action Figure", false, "Items/Figure.png"),
        ("Beer", "Beer", true, "Items/Beer.png"),
        ("Gift_Bikini", "Bikini", false, "Items/Bikini.png"),
        ("Body-Oil", "Body Oil", true, "Items/Body Oil.png"),
        ("Gift_Bonsai-Tree", "Bonsai Tree", false, "Items/Bonsai.png"),
        ("Chocolate", "Chocolate", true, "Items/Chocolate.png"),
        ("Inv-energydrink", "Energy Drink", true, "Items/Energy Drink.png"),
        ("Flowers", "Flowers", true, "Items/Flowers.png"),
        ("inv-lovegum", "Love Gum", true, "Items/Love Gum.png"),
        ("Gift_Parasol", "Parasol", false, "Items/Parasol.png"),
        ("red-meat", "Red Meat", true, "Items/Red Meat.png"),
        ("Gift_Ring", "Ring", false, "Items/Ring.png"),
        ("Gift_Shark-Tooth-Necklace", "Shark Tooth Necklace", false, "Items/Necklace.png"),
        ("Gift_Sunglasses", "Sunglasses", false, "Items/Sunglasses.png"),
        ("Gift_Sunscreen", "Sunscreen", false, "Items/Sunscreen.png"),
        ("Gift_Tropical-Flower-Bouquet", "Tropical Flower Bouquet", false, "Items/Bouquet.png"),
        ("inv-vape", "Vape", true, "Items/Vape.png"),
        ("Whiskey", "Whiskey", true, "Items/Whiskey.png"),
        ("Wine", "Wine", true, "Items/Wine.png"),
    };

    [Fact]
    public void Author()
    {
        string? path = Target;
        if (string.IsNullOrEmpty(path))
        {
            _out.WriteLine("SMSMODFORGE_AUTHOR_PACK not set - nothing to author.");
            return;
        }
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction"); return; }

        var pack = PackRepository.Deserialize(File.ReadAllText(path));
        Assert.NotNull(pack);

        pack!.Uis.RemoveAll(u => u.Id == "giftshop" || u.Id == "giftui");
        pack.Uis.Add(BuildShop());
        pack.Uis.Add(BuildGiftWindow());

        // No rules at all now. Every card asks its own question and hides
        // itself, so the nine purchase rules and the one visibility rule - and
        // the two variables that existed only to carry a request between a
        // click and a rule - are gone.
        pack.IntegrationRules.RemoveAll(r => r.Key.StartsWith("GiftShop", StringComparison.Ordinal));
        pack.Variables.RemoveAll(v => v.Name == "GiftShop_Buy" || v.Name == "GiftShop_Items");

        File.WriteAllText(path, PackRepository.SerializeAsSaved(pack));

        // Checked here rather than left to be found in the editor: a rule that
        // reads an undeclared variable silently reads a default, which is a
        // screen that quietly does nothing.
        var mine = SMSModForge.Validation.PackValidator
            .Validate(pack, Path.GetDirectoryName(path) ?? "")
            .Where(i => i.Where.Contains("Gift", StringComparison.OrdinalIgnoreCase))
            .ToList();
        _out.WriteLine($"VALIDATOR {mine.Count} issue(s) about these screens");
        foreach (var i in mine.Take(10)) _out.WriteLine($"   {i.Severity} {i.Where}: {i.Message}");
        _out.WriteLine($"AUTHORED {pack.Uis.Count} UI(s) into {path}");
        foreach (var ui in pack.Uis)
            _out.WriteLine($"  {ui.Id}: {Count(ui.Nodes[0])} objects");
    }

    /// <summary>Make sure a variable exists, without disturbing one the pack
    /// already declares - its default may have been deliberately changed.</summary>
    private static void Declare(ModPack pack, string name, PackVariableType type,
                                string fallback, string description)
    {
        var existing = pack.Variables.FirstOrDefault(v =>
            string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            pack.Variables.Add(new PackVariableDef
            {
                Name = name,
                Type = type,
                DefaultValue = fallback,
                Description = description,
            });
            return;
        }

        existing.Type = type;
        if (string.IsNullOrEmpty(existing.DefaultValue)) existing.DefaultValue = fallback;
    }

    // ── What the shop does ───────────────────────────────────────────
    //
    // The click cannot do the buying: it has to check the money first, and a
    // click has no way to ask a question. So a card writes down what was asked
    // for and a rule decides.

    private static NodeConditionDef Condition(string type, params (string, string)[] ps)
    {
        var c = new NodeConditionDef { Type = type };
        foreach (var (k, v) in ps) c.Params[k] = v;
        return c;
    }

    private static NodeActionDef Action(string type, params (string, string)[] ps)
    {
        var a = new NodeActionDef { Type = type };
        foreach (var (k, v) in ps) a.Params[k] = v;
        return a;
    }

    // ── The shop ─────────────────────────────────────────────────────

    /// <summary>
    /// The chrome and the store body, copied from ShopCore exactly as
    /// InitializeModShops copies them, then stocked.
    /// </summary>
    private static UiDef BuildShop()
    {
        var ui = new UiDef
        {
            Id = "giftshop",
            Name = "Gift shop",
            Template = "copy",
            HidesWithGameplayUi = true,
            StartsOpen = false,
        };

        var root = new UiNodeDef { Name = "GiftShop" };
        root.Rect.AnchorMin = new[] { 0f, 0f };
        root.Rect.AnchorMax = new[] { 1f, 1f };
        root.Rect.Size = new[] { 0f, 0f };

        // The same five pieces of chrome, in the same order - the order is the
        // draw order, and Image (3) is the backdrop everything else sits on.
        foreach (string piece in new[] { "Image (3)", "Image", "Image (2)", "shopname", "CloseStore" })
        {
            var copied = CopyFromShop(piece);
            if (copied != null) root.Children.Add(copied);
        }

        var closeShop = root.Children.FirstOrDefault(c => c.Name == "CloseStore");
        if (closeShop != null) closeShop.OnClick.Add(SetActive("giftshop", "false"));

        var store = CopyFromShop("GeneralStore");
        if (store != null)
        {
            store.Name = "GiftStore";

            // The game keeps its stores switched off and shows one at a time,
            // so a straight copy arrives invisible - and a hidden parent hides
            // everything under it. This screen IS the store, so it is on.
            Show(store);
            FillShelves(store);
            root.Children.Add(store);
        }

        ui.Nodes.Add(root);
        return ui;
    }

    /// <summary>Replace the general store's goods with the gift catalogue,
    /// keeping one of its cards as the shape every gift card takes.</summary>
    private static void FillShelves(UiNodeDef store)
    {
        var core = store.Children.FirstOrDefault(c => c.Name == "Core");
        if (core == null) return;

        // The card the C# clones. Kept as the pattern, then removed with the
        // rest of the groceries.
        var pattern = core.Children.FirstOrDefault(c => c.Name == "Beer")
                   ?? core.Children.FirstOrDefault();
        if (pattern == null) return;

        var card = Clone(pattern);
        core.Children.Clear();

        // A row that closes ranks: an item bought is switched off, and the ones
        // after it move up rather than leaving a hole. That is the whole reason
        // the C# marks this for a layout rebuild every time the store opens.
        core.Layout = new UiLayoutDef
        {
            Kind = UiLayoutKinds.Horizontal,
            Spacing = new[] { 35f, 0f },
            Alignment = "MiddleCenter",
        };

        foreach (var (name, price, image) in Stock)
            core.Children.Add(MakeCard(card, name, price, image));
    }

    /// <summary>Switch an object and everything under it on.</summary>
    private static void Show(UiNodeDef node)
    {
        node.StartActive = true;
        foreach (var child in node.Children) Show(child);
    }

    /// <summary>The variable-safe form of an item's name: what the pack's
    /// Gift_ variables already use, and what a rule's {item} stands in for.
    /// The object is named this so one rule can name both the variable and the
    /// object from a single token; the label still reads properly.</summary>
    private static string Key(string name) => name.Replace(" ", "-");

    private static UiNodeDef MakeCard(UiNodeDef pattern, string name, int price, string image)
    {
        var card = Clone(pattern);
        card.Name = Key(name);
        Show(card);

        // Its picture, its name, and its price - the three things the C# reaches
        // in by child index. Reached by name here, which survives the card
        // gaining or losing a child.
        var picture = Find(card, n => n.Image != null && n.Name == "Image");
        if (picture?.Image != null) picture.Image.Sprite = image;

        var label = Find(card, n => n.Text != null);
        if (label?.Text != null) label.Text.Value = name;

        var priceLabel = Find(card, n => n.Text != null && !ReferenceEquals(n, label));
        if (priceLabel?.Text != null) priceLabel.Text.Value = "$" + price;

        // The card hides itself once it is owned - it states its own condition
        // rather than a rule stating it on the card's behalf. The shelf
        // arranges itself, so the survivors close the gap.
        card.ActiveConditions.Clear();
        card.ActiveConditions.Add(Condition(NodeConditionTypes.VariableEquals,
            ("name", "Gift_" + Key(name)), ("value", "false")));

        // And it does its own buying: ask whether there is enough, then take
        // it. This is the whole of what the C# does on click.
        var button = Find(card, n => n.Name == "Button") ?? card;
        button.HoverTint = "#FFBFCCFF";        // the pink the C# tints these

        button.ClickConditions.Clear();
        button.ClickConditions.Add(Condition(NodeConditionTypes.GameVariableNumberGreaterOrEqual,
            ("name", "Cash"), ("value", price.ToString())));

        button.OnClick.Clear();
        button.OnClick.Add(Action(NodeActionTypes.IncrementVariable,
            ("name", "Cash"), ("delta", (-price).ToString()), ("source", "Vanilla")));
        button.OnClick.Add(Set("Gift_" + Key(name), "true"));

        return card;
    }

    // ── The gifting window ───────────────────────────────────────────

    /// <summary>
    /// The gift window: its own canvas, the two lists and the close button,
    /// copied from the screen the C# clones.
    /// </summary>
    private static UiDef BuildGiftWindow()
    {
        var ui = new UiDef
        {
            Id = "giftui",
            Name = "Gifting",
            Template = "copy",
            // Its own canvas, above the interface - the C# achieves this by
            // reparenting the clone to no parent at all.
            HidesWithGameplayUi = false,
            SortingOrder = 100,
            StartsOpen = false,
        };

        var root = new UiNodeDef { Name = "GiftWindow" };
        root.Rect.AnchorMin = new[] { 0f, 0f };
        root.Rect.AnchorMax = new[] { 1f, 1f };
        root.Rect.Size = new[] { 0f, 0f };

        // A GRID, seven across. The C# keeps three lists and moves to the next
        // once one holds seven, which is a grid written out by hand - and this
        // way the wrapping and the closing of gaps are one mechanism.
        var board = new UiNodeDef { Name = "Gifts" };
        board.Rect.Size = new[] { 1450f, 600f };
        board.Layout = new UiLayoutDef
        {
            Kind = UiLayoutKinds.Grid,
            CellSize = new[] { 175f, 175f },       // the size the C# forces on each
            Spacing = new[] { 25f, 25f },
            Constraint = "FixedColumnCount",
            ConstraintCount = 7,
            Alignment = "MiddleCenter",
        };

        // One card per gift, from the screen's own gift as the pattern, so
        // every one of them looks like the game's.
        var pattern = Copy("UI_GiftItem_Canvas/gift_list")?.Children.FirstOrDefault();
        if (pattern != null)
            foreach (var (variable, label, vanilla, image) in Gifts)
                board.Children.Add(MakeGift(pattern, variable, label, image));

        root.Children.Add(board);

        var close = Copy("UI_GiftItem_Canvas/Close_GiftUI");
        if (close != null)
        {
            close.Name = "Close";
            Show(close);
            close.OnClick.Add(SetActive("giftui", "false"));
            root.Children.Add(close);
        }

        ui.Nodes.Add(root);
        return ui;
    }

    /// <summary>One giftable thing, built from the screen's own gift card.
    /// Picking it names the gift and closes the window; which NPC is being
    /// given to is already the pack's Gifting_Target.</summary>
    private static UiNodeDef MakeGift(UiNodeDef pattern, string variable, string label, string image)
    {
        var card = Clone(pattern);
        card.Name = label;
        Show(card);

        // "Image (2)" is the item's own picture - Image (1) is the round
        // backdrop and Image is the gift ribbon, so setting either of those
        // leaves every card showing whichever gift was used as the pattern.
        var picture = Find(card, n => n.Image != null && n.Name == "Image (2)");
        if (picture?.Image != null) picture.Image.Sprite = image;

        var title = Find(card, n => n.Text != null && n.Name == "Text (TMP)");
        if (title?.Text != null) title.Text.Value = label;

        // The description belongs to the gift the pattern came from and is
        // wrong on every other one, so it goes - which is what the C# does to
        // its own template.
        var blurb = card.Children.FirstOrDefault(c => c.Name == "Text (TMP) (1)");
        if (blurb != null) card.Children.Remove(blurb);

        var button = Find(card, n => n.Name == "Button") ?? card;
        button.HoverTint = "#FFBFCCFF";
        button.OnClick.Clear();
        button.OnClick.Add(Set("Gifting_Gift", variable));
        button.OnClick.Add(SetActive("giftui", "false"));

        return card;
    }

    // ── Bits and pieces ──────────────────────────────────────────────

    private static UiNodeDef? Copy(string token)
    {
        var entry = VanillaUiCatalog.Find("vanillaui:" + token) ?? VanillaUiCatalog.Find(token);
        var vanilla = entry == null ? null : VanillaUiLibrary.Node(entry);
        return vanilla == null ? null : VanillaUiSeed.CopyOf(vanilla, VanillaUiLibrary.Assets.NameForKey);
    }

    /// <summary>One of ShopCore's children, as the pack's own.</summary>
    private static UiNodeDef? CopyFromShop(string child)
    {
        var whole = Copy("9_MainCanvas/ShopCore");
        return whole?.Children.FirstOrDefault(c => c.Name == child);
    }

    private static NodeActionDef Set(string name, string value)
    {
        var action = new NodeActionDef { Type = NodeActionTypes.SetVariable };
        action.Params["name"] = name;
        action.Params["value"] = value;
        return action;
    }

    private static NodeActionDef SetActive(string target, string active)
    {
        var action = new NodeActionDef { Type = NodeActionTypes.SetGameObjectActive };
        action.Params["kind"] = "UI";
        action.Params["target"] = target;
        action.Params["active"] = active;
        return action;
    }

    /// <summary>The game's object names for its gifts are not the names its
    /// inventory uses. The mapping is the one the C# already carries.</summary>
    private static string InventoryName(string objectName)
    {
        switch (objectName)
        {
            case "gift_chocolate": return "Chocolate";
            case "gift_wine": return "Wine";
            case "gift_EnergyDrink": return "Inv-energydrink";
            case "gift_Vape": return "inv-vape";
            case "gift_lovegum": return "inv-lovegum";
            default: return objectName;
        }
    }

    private static UiNodeDef Clone(UiNodeDef from)
        => PackRepository.Deserialize(
               PackRepository.SerializeAsSaved(Wrap(from)))!.Uis[0].Nodes[0];

    private static ModPack Wrap(UiNodeDef node)
    {
        var pack = new ModPack();
        var ui = new UiDef { Id = "tmp" };
        ui.Nodes.Add(node);
        pack.Uis.Add(ui);
        return pack;
    }

    private static UiNodeDef? Find(UiNodeDef node, Func<UiNodeDef, bool> want)
    {
        if (want(node)) return node;
        foreach (var child in node.Children)
        {
            var found = Find(child, want);
            if (found != null) return found;
        }
        return null;
    }

    private static int Count(UiNodeDef n) => 1 + n.Children.Sum(Count);
}
