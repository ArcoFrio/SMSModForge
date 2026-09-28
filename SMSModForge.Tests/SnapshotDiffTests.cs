using System.Linq;
using Newtonsoft.Json;
using SMSModForge.Model;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Finding the one entry two undo snapshots differ in, from the text alone -
/// and saying nothing whenever the difference is anywhere else as well, since
/// then only a full restore is right.
/// </summary>
public sealed class SnapshotDiffTests
{
    private static ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("diff.pack");
        foreach (string key in new[] { "beach", "park", "mall" })
            pack.Places.Add(new PlaceDef
            {
                Key = key, InternalName = key, DisplayName = key,
                GameObjects = { new GameObjectDef { Name = "Sign", Children = { new GameObjectDef { Name = "Text" } } } },
            });
        pack.Variables.Add(new PackVariableDef { Name = "met", Type = PackVariableType.Bool });
        return pack;
    }

    private static string Text(ModPack pack) => PackRepository.Serialize(pack);

    [Fact]
    public void OnePlaceChanged_IsFound_AndReadsBackAsThatPlace()
    {
        var a = Pack();
        var b = Pack();
        b.Places[1].GameObjects[0].Children[0].X = 12.5f;

        var entry = SnapshotDiff.OneEntry(Text(a), Text(b));
        Assert.NotNull(entry);
        Assert.Equal("places", entry!.Section);
        Assert.Equal(1, entry.Index);

        var place = JsonConvert.DeserializeObject<PlaceDef>(entry.To, PackRepository.ManifestSettings)!;
        Assert.Equal("park", place.Key);
        Assert.Equal(12.5f, place.GameObjects[0].Children[0].X);
        Assert.Equal(0f, JsonConvert.DeserializeObject<PlaceDef>(entry.From, PackRepository.ManifestSettings)!
                             .GameObjects[0].Children[0].X);
    }

    [Fact]
    public void TheLastEntry_WhichHasNoCommaAfterIt_IsFoundToo()
    {
        var a = Pack();
        var b = Pack();
        b.Places[2].GameObjects[0].Y = 3;
        var entry = SnapshotDiff.OneEntry(Text(a), Text(b));
        Assert.Equal(2, entry!.Index);
        Assert.EndsWith("}", entry.To);
    }

    [Fact]
    public void AnythingMoreThanOneEntry_IsNotOne()
    {
        var a = Pack();

        var twoPlaces = Pack();
        twoPlaces.Places[0].GameObjects[0].X = 1;
        twoPlaces.Places[2].GameObjects[0].X = 1;
        Assert.Null(SnapshotDiff.OneEntry(Text(a), Text(twoPlaces)));

        var placeAndVariable = Pack();
        placeAndVariable.Places[0].GameObjects[0].X = 1;
        placeAndVariable.Variables[0].Name = "metRiver";
        Assert.Null(SnapshotDiff.OneEntry(Text(a), Text(placeAndVariable)));

        var outsideTheLists = Pack();
        outsideTheLists.Version = "9.9.9";
        Assert.Null(SnapshotDiff.OneEntry(Text(a), Text(outsideTheLists)));

        // ...and with a place as well: the place alone would put back a
        // different pack from the one the step left.
        var placeAndOutside = Pack();
        placeAndOutside.Places[0].GameObjects[0].X = 1;
        placeAndOutside.Version = "9.9.9";
        Assert.Null(SnapshotDiff.OneEntry(Text(a), Text(placeAndOutside)));

        var oneMore = Pack();
        oneMore.Places.Add(new PlaceDef { Key = "zoo", InternalName = "zoo" });
        Assert.Null(SnapshotDiff.OneEntry(Text(a), Text(oneMore)));

        var oneFewer = Pack();
        oneFewer.Places.RemoveAt(1);
        Assert.Null(SnapshotDiff.OneEntry(Text(a), Text(oneFewer)));

        Assert.Null(SnapshotDiff.OneEntry(Text(a), Text(Pack())));   // no difference at all
    }

    [Fact]
    public void AChangeToAVariable_IsOneEntryOfItsList()
    {
        // Found - and it is for the caller to decide it has no quick way back
        // for that list; the places are the ones it has.
        var a = Pack();
        var b = Pack();
        b.Variables[0].Name = "metRiver";
        var entry = SnapshotDiff.OneEntry(Text(a), Text(b));
        Assert.Equal("variables", entry!.Section);
    }
}
