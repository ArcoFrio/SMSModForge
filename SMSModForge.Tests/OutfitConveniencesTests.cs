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
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class OutfitConveniencesTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    public OutfitConveniencesTests(ITestOutputHelper o) => _out = o;

    /// <summary>The picker's memory is shared and lives as long as the editor
    /// does, so a test must not leave one behind for the next.</summary>
    public void Dispose() => PathPickerBox.Remember("");

    // ── Same as the default outfit ───────────────────────────────────

    /// <summary>
    /// A character with a filled-in default outfit and a bare second one.
    /// <para/>
    /// In the shape a manifest really has: defaultOutfit holds the outfit's
    /// GameObject NAME, and the outfit's key is a different string. These tests
    /// used to give both the same value, and the buttons, which matched the
    /// key, passed here while never appearing on any character of a real pack
    /// whose keys were not their names.
    /// </summary>
    private static CharacterViewModel Character()
    {
        var model = new CharacterDef { Key = "anna", DefaultOutfit = "AnnaDay" };
        model.Outfits.Add(new OutfitDef
        {
            Key = "day",
            GameObjectName = "AnnaDay",
            BaseSprite = "Art/Anna/Base.png",
            MaskSprite = "Art/Anna/Mask.png",
            BlinkSprite = "Art/Anna/Blink.png",
        });
        model.Outfits[0].Mouth.Prefix = "Art/Anna/Mouth";
        model.Outfits[0].Expression.Prefix = "Art/Anna/Expression";
        model.Outfits.Add(new OutfitDef { Key = "night", GameObjectName = "AnnaNight" });
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

        // The whole reason this fixture changed: the default is named by
        // something that is not the key.
        Assert.NotEqual(Outfit(c, "day").Key, c.DefaultOutfit);

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

        c.DefaultOutfit = "AnnaNight";

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

    [Fact]
    public void ABlankDefaultIsTheFirstOutfit()
    {
        // As it is in game: a character that names no default enters in the
        // first outfit, so that is the one there is something to copy from.
        var c = Character();
        c.Model.DefaultOutfit = "";
        c.RefreshOutfitDefaults();

        Assert.True(Outfit(c, "day").IsDefaultOutfit);
        Assert.True(Outfit(c, "night").CanCopyFromDefaultOutfit);
    }

    [Fact]
    public void TheNameIsMatchedWhateverItsCase()
    {
        // The game finds the bust ignoring case, so a default written in the
        // wrong case still dresses the character - and still has buttons.
        var c = Character();
        c.DefaultOutfit = "annaday";

        Assert.Same(Outfit(c, "day"), Outfit(c, "night").DefaultOutfit);
        Assert.True(Outfit(c, "night").CanCopyFromDefaultOutfit);
    }

    [Fact]
    public void ADefaultThatIsNoOutfitOffersNothing()
    {
        // The control for the two above: a name that matches nothing is not
        // quietly taken to mean some other outfit to copy from.
        var c = Character();
        c.DefaultOutfit = "Nobody";

        Assert.Null(Outfit(c, "night").DefaultOutfit);
        Assert.False(Outfit(c, "night").CanCopyFromDefaultOutfit);
        Assert.False(Outfit(c, "day").CanCopyFromDefaultOutfit);
    }

    [Fact]
    public void RenamingTheDefaultOutfitKeepsTheButtonsOnTheOthers()
    {
        // A rename rewrites the character's defaultOutfit behind the view
        // model. Unless the outfits are told, the buttons go on looking for the
        // old name - and the renamed outfit is still the default.
        var vm = new MainViewModel();
        var c = Character();
        vm.Pack.Characters.Add(c.Model);
        vm.Characters.Add(c);
        vm.SelectedCharacter = c;
        var day = Outfit(c, "day");
        var night = Outfit(c, "night");
        vm.SelectedOutfit = day;

        day.GameObjectName = "AnnaMorning";

        int told = 0;
        night.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OutfitViewModel.CanCopyFromDefaultOutfit)) told++;
        };
        vm.SelectedOutfit = night;      // leaving the outfit is what commits a rename

        _out.WriteLine($"default is now '{c.DefaultOutfit}', night can copy "
                       + $"{night.CanCopyFromDefaultOutfit}, told {told} time(s)");
        Assert.Equal("AnnaMorning", c.DefaultOutfit);
        Assert.Same(day, night.DefaultOutfit);
        Assert.True(night.CanCopyFromDefaultOutfit);
        Assert.True(told > 0, "the outfit was never told, so a button already on screen would not come back");
    }

    [Fact]
    public void ABustAddedToOneOfTheGamesCharactersOffersNothing()
    {
        // Its default is the character's own bust in the game, which has no
        // paths. Copying from it would empty the field it was pressed on.
        var vm = new MainViewModel();
        var theirs = vm.Characters.First(c => c.IsVanillaBust && c.Outfits.Count > 0);
        var added = theirs.AddOutfit();

        _out.WriteLine($"{theirs.Key}: default '{theirs.DefaultOutfit}', added '{added.GameObjectName}'");
        Assert.True(added.ShowsPackArt, "the added bust is not one the pack draws, so this proves nothing");
        Assert.NotNull(added.DefaultOutfit);
        Assert.False(added.DefaultOutfit!.ShowsPackArt);
        Assert.False(added.CanCopyFromDefaultOutfit);
    }

    [Fact]
    public void TheButtonsAreOnScreen()
    {
        // Measured on the real window, not asked of the view model: the report
        // was "I don't see them", and a property that says true behind a
        // binding that never reaches the button reads exactly the same.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (System.Windows.Controls.TabControl)window.FindName("MainTabs");
            for (int i = 0; i < tabs.Items.Count; i++)
                if (tabs.Items[i] is System.Windows.Controls.TabItem t && (t.Header as string) == "Characters")
                { tabs.SelectedIndex = i; break; }
            WindowHarness.Pump();

            var c = Character();
            vm.Pack.Characters.Add(c.Model);
            vm.Characters.Add(c);
            vm.SelectedCharacter = c;
            vm.SelectedOutfit = Outfit(c, "night");
            WindowHarness.Pump();

            string[] names =
            {
                "MaskSameAsDefaultButton", "BlinkSameAsDefaultButton",
                "MouthSameAsDefaultButton", "ExpressionSameAsDefaultButton",
            };
            foreach (string name in names)
            {
                var button = Named(window, name);
                _out.WriteLine($"night: {name} visible={button?.IsVisible}");
                Assert.True(button != null, $"{name} is not in the window at all");
                Assert.True(button!.IsVisible, $"{name} is not on screen for an outfit that is not the default");
            }

            // ...and gone again on the default outfit itself.
            vm.SelectedOutfit = Outfit(c, "day");
            WindowHarness.Pump();
            foreach (string name in names)
            {
                var button = Named(window, name);
                _out.WriteLine($"day: {name} visible={button?.IsVisible}");
                Assert.False(button?.IsVisible == true, $"{name} is on screen for the default outfit");
            }
        });
    }

    /// <summary>A control by x:Name, walked out of the visual tree so a name
    /// inside a template is found as well as one in the window's own.</summary>
    private static System.Windows.FrameworkElement? Named(System.Windows.DependencyObject from, string name)
    {
        if (from is System.Windows.FrameworkElement fe && fe.Name == name) return fe;
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(from);
        for (int i = 0; i < count; i++)
        {
            var found = Named(System.Windows.Media.VisualTreeHelper.GetChild(from, i), name);
            if (found != null) return found;
        }
        return null;
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
