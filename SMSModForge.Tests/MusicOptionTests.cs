using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.View.Converters;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The music pickers list the pack's tracks and the game's together, under
/// headings that say which is which.
/// <para/>
/// The grouping is the part worth testing. It is done with a
/// PropertyGroupDescription holding a null property name — the form that hands
/// the converter the item itself rather than a property of it — and if that
/// were wrong the list would still render, just ungrouped or under one heading.
/// A test that only counted the items would pass either way, so these walk the
/// view's groups and check both sides land where they belong.
/// </summary>
public class MusicOptionTests
{
    private static MainViewModel WithTracks(params string[] keys)
    {
        var vm = new MainViewModel();
        foreach (var key in keys)
        {
            vm.AddMusicCommand.Execute(null);
            vm.SelectedMusic!.Key = key;
        }
        vm.RebuildMusicKeyOptions();
        return vm;
    }

    /// <summary>group name -> items, in the order the view presents them.</summary>
    private static List<KeyValuePair<string, List<string>>> Groups(
        System.ComponentModel.ICollectionView view)
        => view.Groups!
               .Cast<System.Windows.Data.CollectionViewGroup>()
               .Select(g => new KeyValuePair<string, List<string>>(
                   (string)g.Name, g.Items.Cast<string>().ToList()))
               .ToList();

    [Fact]
    public void Catalog_holds_the_audio_player_children()
    {
        // Both ends of the dump, so a truncated read would show up.
        Assert.Equal("Music", VanillaMusic.Default);
        Assert.Equal(VanillaMusic.Default, VanillaMusic.All[0]);
        Assert.Equal("CasinoMusic", VanillaMusic.All[^1]);
        Assert.Equal(38, VanillaMusic.All.Count);
        Assert.Equal(VanillaMusic.All.Count, VanillaMusic.All.Distinct().Count());

        Assert.True(VanillaMusic.Contains("Beach"));
        // Case-insensitive: the game's own names are not consistently cased,
        // so a name retyped with the wrong case still has to be recognised as
        // the game's rather than read as something the author wrote.
        Assert.True(VanillaMusic.Contains("beach"));
        // The control: a name the game does not have must not match.
        Assert.False(VanillaMusic.Contains("Neon Rain"));
        Assert.False(VanillaMusic.Contains(""));
    }

    [Fact]
    public void Action_picker_lists_the_packs_tracks_before_the_games()
    {
        var vm = WithTracks("neonRain");

        Assert.Equal("neonRain", vm.MusicKeyOptions[0]);
        Assert.Equal(1 + VanillaMusic.All.Count, vm.MusicKeyOptions.Count);
        Assert.Contains("CasinoMusic", vm.MusicKeyOptions);
    }

    [Fact]
    public void Action_picker_groups_by_where_the_track_came_from()
    {
        var vm = WithTracks("neonRain");
        var groups = Groups(vm.MusicKeyOptionsGrouped);

        Assert.Equal(2, groups.Count);
        Assert.Equal(MusicOriginConverter.PackHeading, groups[0].Key);
        Assert.Equal(new[] { "neonRain" }, groups[0].Value);

        Assert.Equal(MusicOriginConverter.GameHeading, groups[1].Key);
        Assert.Equal(VanillaMusic.All, groups[1].Value);
    }

    [Fact]
    public void Button_pickers_offer_the_same_list_as_the_action()
    {
        // One list, no extra row on the front of it. The button fields used to
        // carry a "(leave unchanged)" sentinel for the empty state; they now
        // start on a real track instead.
        var vm = WithTracks("neonRain");
        var groups = Groups(vm.MusicKeyOptionsGrouped);

        Assert.Equal(2, groups.Count);
        Assert.Equal(MusicOriginConverter.PackHeading, groups[0].Key);
        Assert.Equal(MusicOriginConverter.GameHeading, groups[1].Key);
        Assert.DoesNotContain(vm.MusicKeyOptions, o => o.StartsWith("("));
    }

    [Fact]
    public void The_two_kinds_of_button_default_opposite_ways()
    {
        var vm = new MainViewModel();

        // A map button crosses into somewhere new and should say what plays
        // there.
        vm.AddMapButtonCommand.Execute(null);
        Assert.Equal(VanillaMusic.Default, vm.MapButtons.Single().Music);

        // A navigator button is a door within an area, and the area’s music
        // should carry across it. This is the control on the line above: if the
        // default were being applied by the model or the serializer rather than
        // by the two Add paths, both of these would read the same.
        vm.AddPlaceCommand.Execute(null);
        var inPlace = vm.Places.Single().AddNavigatorButton();
        Assert.Equal("", inPlace!.Music);

        // Both Add paths, because a vanilla extension has its own.
        vm.AddVanillaExtensionCommand.Execute(null);
        var inExtension = vm.VanillaExtensions.Single().AddNavigatorButton();
        Assert.Equal("", inExtension.Music);
    }

    [Fact]
    public void A_button_saved_with_no_music_keeps_none()
    {
        // The default is what a NEW button starts on, not what an empty field
        // means. Empty tells the runtime to skip the switch outright, so a door
        // that deliberately lets the area’s track keep playing has to survive a
        // round trip through the editor. Nothing may fill it in on the way.
        Assert.Equal("", new NavigatorButtonDef().Music);
        Assert.Equal("", new MapButtonDef().Music);

        var pack = PackRepository.CreateEmpty("t");
        var place = new PlaceDef { Key = "lab" };
        place.NavigatorButtons.Add(new NavigatorButtonDef { Label = "Out" });
        pack.Places.Add(place);

        var back = PackRepository.Deserialize(PackRepository.SerializeAsSaved(pack))!;
        Assert.Equal("", back.Places.Single().NavigatorButtons.Single().Music);
    }

    [Fact]
    public void A_pack_track_named_like_a_vanilla_one_is_listed_once()
    {
        // Two objects would answer to this name at runtime. Listing it twice
        // would suggest they are separately choosable from a box that stores
        // nothing but the name.
        var vm = WithTracks("Beach");

        Assert.Equal(VanillaMusic.All.Count, vm.MusicKeyOptions.Count);
        Assert.Single(vm.MusicKeyOptions, o => o == "Beach");
    }

    [Fact]
    public void Renaming_a_track_moves_it_between_the_headings()
    {
        // The view is built once and held, so this is the case that proves it
        // keeps up with the collection underneath instead of going stale.
        var vm = WithTracks("neonRain");
        vm.SelectedMusic!.Key = "Beach";
        vm.RebuildMusicKeyOptions();

        var groups = Groups(vm.MusicKeyOptionsGrouped);
        Assert.Single(groups);
        Assert.Equal(MusicOriginConverter.GameHeading, groups[0].Key);
        Assert.DoesNotContain("neonRain", vm.MusicKeyOptions);
    }
}
