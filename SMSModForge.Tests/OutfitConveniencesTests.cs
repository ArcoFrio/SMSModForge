using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Three small things that save an author typing the same path four times.
/// <para/>
/// Most outfits of a character are the same bust in different clothes: the
/// mask, the blink frame and the two prefixes are usually the default outfit's,
/// and the art all sits in one folder. None of that was anywhere in the editor,
/// so every one of them was typed again by hand — which is where the typos that
/// make a bust not load come from.
/// </summary>
public sealed class OutfitConveniencesTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    public OutfitConveniencesTests(ITestOutputHelper o) => _out = o;

    /// <summary>The picker's memory is shared and lives as long as the editor
    /// does, so a test must not leave one behind for the next.</summary>
    public void Dispose() => PathPickerBox.Remember("");

    // ── Same as the default outfit ───────────────────────────────────

    /// <summary>A character with a filled-in default outfit and a bare second
    /// one.</summary>
    private static CharacterViewModel Character()
    {
        var model = new CharacterDef { Key = "anna", DefaultOutfit = "day" };
        model.Outfits.Add(new OutfitDef
        {
            Key = "day",
            BaseSprite = "Art/Anna/Base.png",
            MaskSprite = "Art/Anna/Mask.png",
            BlinkSprite = "Art/Anna/Blink.png",
        });
        model.Outfits[0].Mouth.Prefix = "Art/Anna/Mouth";
        model.Outfits[0].Expression.Prefix = "Art/Anna/Expression";
        model.Outfits.Add(new OutfitDef { Key = "night" });
        return new CharacterViewModel(model);
    }

    private static OutfitViewModel Outfit(CharacterViewModel c, string key)
        => c.Outfits.Single(o => o.Key == key);

    [Fact]
    public void TheDefaultOutfitHasNothingToCopyFromItself()
    {
        var c = Character();
        var day = Outfit(c, "day");

        _out.WriteLine($"day: is default {day.IsDefaultOutfit}, "
                       + $"can copy {day.CanCopyFromDefaultOutfit}");
        Assert.True(day.IsDefaultOutfit);
        Assert.False(day.CanCopyFromDefaultOutfit);
    }

    [Fact]
    public void EveryOtherOutfitCan()
    {
        var c = Character();
        var night = Outfit(c, "night");

        Assert.False(night.IsDefaultOutfit);
        Assert.True(night.CanCopyFromDefaultOutfit);
        Assert.Same(Outfit(c, "day"), night.DefaultOutfit);
    }

    [Fact]
    public void MovingTheDefaultMovesWhatTheButtonsPointAt()
    {
        // The buttons are hidden on whichever outfit IS the default, and it can
        // change while the panel is open.
        var c = Character();
        var day = Outfit(c, "day");
        var night = Outfit(c, "night");

        c.DefaultOutfit = "night";

        _out.WriteLine($"after the move: day can copy {day.CanCopyFromDefaultOutfit}, "
                       + $"night can copy {night.CanCopyFromDefaultOutfit}");
        Assert.True(day.CanCopyFromDefaultOutfit);
        Assert.False(night.CanCopyFromDefaultOutfit);
        Assert.Same(night, day.DefaultOutfit);
    }

    [Fact]
    public void ACharacterWithOneOutfitOffersNothing()
    {
        // The control. A button that is always there and does nothing is worse
        // than no button.
        var model = new CharacterDef { Key = "solo", DefaultOutfit = "only" };
        model.Outfits.Add(new OutfitDef { Key = "only" });
        var c = new CharacterViewModel(model);

        Assert.False(c.Outfits[0].CanCopyFromDefaultOutfit);
    }

    [Theory]
    [InlineData("blink")]
    [InlineData("mouth")]
    [InlineData("expression")]
    [InlineData("mask")]
    public void TheButtonTakesTheFieldFromTheDefaultOutfit(string which)
    {
        // Driven through the view model that owns the commands, because the
        // mask one asks first and the answer has to come from somewhere.
        var vm = new MainViewModel();
        var c = Character();
        vm.Pack.Characters.Add(c.Model);
        vm.Characters.Add(c);
        vm.SelectedCharacter = c;
        vm.SelectedOutfit = Outfit(c, "night");

        var command = which switch
        {
            "blink" => vm.UseDefaultBlinkCommand,
            "mouth" => vm.UseDefaultMouthPrefixCommand,
            "expression" => vm.UseDefaultExpressionPrefixCommand,
            _ => vm.UseDefaultMaskCommand,
        };

        Assert.True(command.CanExecute(null), "the command is not offered at all");
        command.Execute(null);

        var night = Outfit(c, "night");
        var day = Outfit(c, "day");
        string got = which switch
        {
            "blink" => night.BlinkSprite,
            "mouth" => night.MouthPrefix,
            "expression" => night.ExpressionPrefix,
            _ => night.MaskSprite,
        };
        string wanted = which switch
        {
            "blink" => day.BlinkSprite,
            "mouth" => day.MouthPrefix,
            "expression" => day.ExpressionPrefix,
            _ => day.MaskSprite,
        };

        _out.WriteLine($"{which}: '{got}' against the default's '{wanted}'");
        Assert.Equal(wanted, got);
        Assert.NotEqual("", got);
    }

    [Fact]
    public void TheCommandsAreNotOfferedOnTheDefaultOutfitItself()
    {
        var vm = new MainViewModel();
        var c = Character();
        vm.Pack.Characters.Add(c.Model);
        vm.Characters.Add(c);
        vm.SelectedCharacter = c;
        vm.SelectedOutfit = Outfit(c, "day");

        Assert.False(vm.UseDefaultMaskCommand.CanExecute(null));
        Assert.False(vm.UseDefaultBlinkCommand.CanExecute(null));
        Assert.False(vm.UseDefaultMouthPrefixCommand.CanExecute(null));
        Assert.False(vm.UseDefaultExpressionPrefixCommand.CanExecute(null));
    }

    // ── The picker opens where you were ──────────────────────────────

    [Fact]
    public void AnEmptyFieldOpensWhereTheLastFileCameFrom()
    {
        // An outfit's art sits together, so after the first pick the rest are
        // one click away rather than four levels of tree away.
        WindowHarness.Run(host =>
        {
            string pack = Path.Combine(Path.GetTempPath(), "SMSModForgeTests", Guid.NewGuid().ToString("N"));
            string art = Path.Combine(pack, "Art", "Anna");
            Directory.CreateDirectory(art);
            try
            {
                PathPickerBox.Remember(Path.Combine(art, "Base.png"));

                var box = new PathPickerBox { PackRoot = pack, PathText = "" };
                string opens = box.ResolveInitialDirectory();

                _out.WriteLine($"an empty field opens at: {opens}");
                Assert.Equal(art, opens);
            }
            finally { try { Directory.Delete(pack, true); } catch { } }
        });
    }

    [Fact]
    public void AFieldThatAlreadyPointsSomewhereOpensThere()
    {
        // The control: the memory is a fallback, not an override. A field with
        // a value in it still shows you where that value lives.
        WindowHarness.Run(host =>
        {
            string pack = Path.Combine(Path.GetTempPath(), "SMSModForgeTests", Guid.NewGuid().ToString("N"));
            string art = Path.Combine(pack, "Art", "Anna");
            string other = Path.Combine(pack, "Art", "Josef");
            Directory.CreateDirectory(art);
            Directory.CreateDirectory(other);
            try
            {
                PathPickerBox.Remember(Path.Combine(art, "Base.png"));

                var box = new PathPickerBox { PackRoot = pack, PathText = "Art/Josef/Base.png" };
                string opens = box.ResolveInitialDirectory();

                _out.WriteLine($"a filled field opens at: {opens}");
                Assert.Equal(other, opens);
            }
            finally { try { Directory.Delete(pack, true); } catch { } }
        });
    }

    [Fact]
    public void AFolderFromAnotherPackIsNotOffered()
    {
        // The memory outlives the pack that filled it, and a pack-relative
        // field cannot store anything from somewhere else anyway - so a second
        // pack would otherwise open in the first one's art folder.
        WindowHarness.Run(host =>
        {
            string root = Path.Combine(Path.GetTempPath(), "SMSModForgeTests", Guid.NewGuid().ToString("N"));
            string first = Path.Combine(root, "packOne", "Art");
            string second = Path.Combine(root, "packTwo");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            try
            {
                PathPickerBox.Remember(Path.Combine(first, "Base.png"));

                var box = new PathPickerBox { PackRoot = second, PathText = "" };
                string opens = box.ResolveInitialDirectory();

                _out.WriteLine($"with the memory in another pack, it opens at: {opens}");
                Assert.Equal(second, opens);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        });
    }
}
