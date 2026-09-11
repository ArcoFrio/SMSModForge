using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The jiggle mask on one of the GAME's busts is authored with the same painter
/// a pack's own outfit uses.
/// <para/>
/// Every other row under "Replace textures" is a PNG somebody drew somewhere
/// else and pointed at. The mask is not: it is three intensity planes packed
/// into R/G/B, and nothing outside this editor knows what they mean — so the
/// one texture on the row that CANNOT be hand-made was the one texture with no
/// way to make it.
/// <para/>
/// Driven through the real window, because the whole change is a button in a
/// DataTemplate and a host contract the painter reaches through. Both of those
/// fail silently: a mistyped binding hides the button, and a host the painter
/// cannot write to loses the mask on save.
/// </summary>
public sealed class VanillaMaskPainterTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public VanillaMaskPainterTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmf-maskrow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* the test is not about tidying up */ }
    }

    private static void ShowCharacters(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Characters")
            { tabs.SelectedIndex = i; WindowHarness.Pump(); return; }

        throw new Xunit.Sdk.XunitException("no Characters tab");
    }

    /// <summary>One of the game's characters, with the Replace-textures panel
    /// open on one of the busts the game gave them.</summary>
    private static (CharacterViewModel Character, OutfitViewModel Outfit) TheirsShowing(
        MainViewModel vm)
    {
        var character = vm.Characters.First(c => c.IsVanillaBust && c.Outfits.Count > 0);
        var outfit = character.Outfits[0];
        vm.SelectedCharacter = character;
        vm.SelectedOutfit = outfit;
        WindowHarness.Pump();
        outfit.OverridesShown = true;
        WindowHarness.Pump();
        return (character, outfit);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) yield return hit;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    /// <summary>The painter button drawn on one override row, or null.</summary>
    private static Button? PainterOn(MainWindow window, SpriteOverrideViewModel row)
    {
        foreach (var button in Descendants<Button>(window))
        {
            if (!ReferenceEquals(button.DataContext, row)) continue;
            if (button.Content as string != "Edit Mask…") continue;
            return button;
        }
        return null;
    }

    [Fact]
    public void TheMaskRowIsTheOnlyOneThatOffersThePainter()
    {
        // Measured off the rows that are actually drawn, and the other rows are
        // the control: a button placed on every row would pass an "is it there"
        // assertion just as well as a button placed on the right one.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (_, outfit) = TheirsShowing(vm);

            Assert.True(outfit.ShowOverridesPanel, "the Replace textures panel never opened");
            Assert.NotEmpty(outfit.Overrides);

            var offered = new List<string>();
            var withheld = new List<string>();
            foreach (var row in outfit.Overrides)
            {
                var painter = PainterOn(window, row);
                bool shown = painter != null && painter.IsVisible;
                (shown ? offered : withheld).Add(row.Slot);
            }

            _out.WriteLine("painter on: " + string.Join(", ", offered));
            _out.WriteLine("no painter: " + string.Join(", ", withheld));

            Assert.Equal(new[] { SpriteSlotNames.Mask }, offered);
            Assert.Contains(SpriteSlotNames.Base, withheld);
            Assert.True(withheld.Count >= 3,
                        "every row but the mask should be a plain path box");
        });
    }

    [Fact]
    public void ItIsDeadUntilThereIsSomewhereToPutTheMask()
    {
        // Unticked, the row carries no entry, so there is nowhere for a saved
        // mask to go — the path box is disabled for the same reason and the
        // button follows it rather than opening a painter over nothing.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (_, outfit) = TheirsShowing(vm);

            var mask = outfit.Overrides.Single(o => o.Slot == SpriteSlotNames.Mask);
            Assert.False(mask.Replaced);

            var painter = PainterOn(window, mask);
            Assert.NotNull(painter);
            Assert.False(painter!.IsEnabled);

            mask.Replaced = true;
            WindowHarness.Pump();
            Assert.True(painter.IsEnabled);
        });
    }

    [Fact]
    public void ClickingItOpensThePainterOnThatRow()
    {
        // End to end, because every link in it is one that fails quietly: the
        // Click name resolving to a handler, the handler finding the row on the
        // button's DataContext, and the row satisfying the painter's host
        // contract. None of the three raises anything when it is wrong.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.PackRoot = _dir;                       // or it asks you to save first
            ShowCharacters(window);
            var (_, outfit) = TheirsShowing(vm);

            var mask = outfit.Overrides.Single(o => o.Slot == SpriteSlotNames.Mask);
            mask.Replaced = true;
            WindowHarness.Pump();

            var painter = PainterOn(window, mask);
            Assert.NotNull(painter);

            try
            {
                painter!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives
                                                        .ButtonBase.ClickEvent));
                WindowHarness.Pump();

                var opened = Application.Current.Windows.OfType<MaskEditorWindow>().ToList();
                _out.WriteLine($"painter windows open: {opened.Count}");
                Assert.Single(opened);

                // ...and a second click raises the one already up rather than
                // starting a second painter on the same mask.
                painter.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives
                                                       .ButtonBase.ClickEvent));
                WindowHarness.Pump();
                Assert.Single(Application.Current.Windows.OfType<MaskEditorWindow>());
            }
            finally
            {
                foreach (var w in Application.Current.Windows.OfType<MaskEditorWindow>().ToList())
                    try { w.Close(); } catch { /* closing is not the test */ }
                WindowHarness.Pump();
            }
        });
    }

    [Fact]
    public void WhatThePainterSavesLandsInTheOverride()
    {
        // The painter writes the path it saved to back through MaskPath. On a
        // pack outfit that is the outfit's own field; here the only place a
        // path lives is the override entry, and a host that dropped it would
        // leave the author with a mask on disk that the manifest never names.
        var outfit = new OutfitDef { Key = "Kate_bust", GameObjectName = "Kate_bust" };
        var row = new SpriteOverrideViewModel(outfit, SpriteSlotNames.Mask);
        var host = (IMaskEditorHost)row;

        row.Replaced = true;
        host.MaskPath = "art/kate-jiggle.png";

        Assert.Equal("art/kate-jiggle.png", row.Path);
        Assert.Equal("art/kate-jiggle.png", outfit.OverrideFor(SpriteSlotNames.Mask));
        Assert.Equal("art/kate-jiggle.png", host.MaskPath);
    }

    [Fact]
    public void AnUntickedRowKeepsNothing()
    {
        // The control for the one above. Unticking throws the path away — that
        // is what the tick means — so a write that arrived afterwards must not
        // put a slot back into the manifest the author has said they do not
        // want replaced.
        var outfit = new OutfitDef { Key = "Kate_bust", GameObjectName = "Kate_bust" };
        var row = new SpriteOverrideViewModel(outfit, SpriteSlotNames.Mask);

        ((IMaskEditorHost)row).MaskPath = "art/kate-jiggle.png";

        Assert.Equal("", row.Path);
        Assert.Empty(outfit.SpriteOverrides);
    }

    [Fact]
    public void ThePainterDrawsOverThePacksOwnBaseWhenThereIsOne()
    {
        // A mask means nothing on its own — it is painted against the art it
        // deforms. The pack's replacement base is the only art here the editor
        // can honestly show, so it is the underlay when the pack has one, and
        // there is no underlay when it does not: the bust below belongs to the
        // game and its texture is not in this pack.
        var outfit = new OutfitDef { Key = "Kate_bust", GameObjectName = "Kate_bust" };
        var mask = new SpriteOverrideViewModel(outfit, SpriteSlotNames.Mask);

        Assert.Equal("", mask.PoseSpritePath);

        var baseRow = new SpriteOverrideViewModel(outfit, SpriteSlotNames.Base);
        baseRow.Replaced = true;
        baseRow.Path = "art/kate-base.png";

        Assert.Equal("art/kate-base.png", mask.PoseSpritePath);
    }
}
