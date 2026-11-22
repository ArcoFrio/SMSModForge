using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// An undo step is the pack in pieces (Model.PackPieces, 1.10.0): an entry of
/// each of its lists a piece, taken side by side, shared between steps. Held
/// here on the packs the in-game suite is made of (every kind of record a pack
/// has) and, when one is named, on a real one
/// (<c>SMSMODFORGE_UNDO_PROBE_PACK</c>):
/// <list type="bullet">
/// <item>the pieces put together read back as the pack the whole text reads
/// back as - control: one piece changed, and they do not;</item>
/// <item>taken on one core or on all, the same pieces;</item>
/// <item>a step shares every piece that did not change with the one before,
/// and the two differ exactly where the pack was changed;</item>
/// <item>a step put into the pack (Apply) makes the pack that step, and every
/// entry it did not change is still the object it was;</item>
/// <item>a piece packed small gives back exactly its text.</item>
/// </list>
/// </summary>
public sealed class PackPiecesTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-pieces-" + Guid.NewGuid().ToString("N"));

    public PackPiecesTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string Folder(string id) { string f = Path.Combine(_dir, id); Directory.CreateDirectory(f); return f; }

    private IEnumerable<(string Name, ModPack Pack)> Packs()
    {
        yield return ("gamecheck", InGameCheckPacksTests.GameCheck(Folder("gamecheck")));
        yield return ("gameorder", InGameCheckPacksTests.GameOrder());
        yield return ("world", InGameSuitePacks.World.Make(Folder("world")));
        yield return ("story", InGameSuitePacks.Story.Make(Folder("story")));
        yield return ("quests", InGameSuitePacks.Quests.Make());
        yield return ("rules", InGameSuitePacks.Rules.Make());
        yield return ("saving", InGameSuitePacks.Saving.Make());
        yield return ("talk", InGameSuitePacks.Talk.Make(Folder("talk")));
        yield return ("looks", InGameSuitePacks.Looks.Make(Folder("looks")));
        string? real = Environment.GetEnvironmentVariable("SMSMODFORGE_UNDO_PROBE_PACK");
        if (!string.IsNullOrWhiteSpace(real) && File.Exists(Path.Combine(real, "modpack.json")))
            yield return ("the pack named", PackRepository.Load(real));
    }

    /// <summary>A pack as its text reads back and is written again: what "the same pack" is held to.</summary>
    private static string ReadBack(string json) => PackRepository.Serialize(PackRepository.Deserialize(json)!);

    [Fact]
    public void ThePackIsTakenInPieces_NotAsOneText()
        => Assert.True(PackPieces.InPieces, "the pack's top-level values are written in a way the pieces do not reproduce: every step is the whole text again");

    [Fact]
    public void ThePiecesPutTogetherReadBackAsThePack()
    {
        foreach (var (name, pack) in Packs())
        {
            string whole = PackRepository.Serialize(pack);
            var pieces = PackPieces.Take(pack);
            Assert.False(pieces.IsWhole);
            int count = pieces.Entries.Sum(e => e.Length);
            Assert.Equal(ReadBack(whole), ReadBack(pieces.ToJson()));
            // And nothing in the pack was changed by taking it.
            Assert.Equal(whole, PackRepository.Serialize(pack));
            _out.WriteLine($"{name}: {pieces.Names.Length} sections, {count} pieces, {whole.Length} characters whole");

            // Control: a piece that says something else is another pack.
            int s = Array.FindIndex(pieces.Lists, l => l);
            if (s >= 0 && pieces.Entries[s].Length > 0)
            {
                var broken = PackPieces.Take(pack);
                broken.Entries[s][0] = new PackPiece("{}");
                Assert.NotEqual(ReadBack(whole), ReadBack(broken.ToJson()));
            }
        }
    }

    [Fact]
    public void TakenOnOneCoreOrOnAll_ThePiecesAreTheSame()
    {
        foreach (var (name, pack) in Packs())
        {
            PackPieces one;
            PackPieces.OneCoreForTests = true;
            try { one = PackPieces.Take(pack); }
            finally { PackPieces.OneCoreForTests = false; }
            var all = PackPieces.Take(pack);
            Assert.True(one.SameAs(all), name + ": the pieces taken on every core are not those taken on one");
            Assert.Equal(one.ToJson(), all.ToJson());
        }
    }

    [Fact]
    public void AStepSharesWhatDidNotChange_AndTwoStepsDifferWhereThePackWasChanged()
    {
        var pack = InGameSuitePacks.Story.Make(Folder("story2"));
        var before = PackPieces.Take(pack);
        Assert.Empty(PackPieces.Diff(before, PackPieces.Take(pack, before)));

        int index = pack.Dialogues.Count - 1;
        pack.Dialogues[index].Nodes[0].Text += " (changed)";
        var after = PackPieces.Take(pack, before);
        var changes = PackPieces.Diff(before, after);
        var change = Assert.Single(changes);
        Assert.Equal("dialogues", change.Name);
        Assert.Equal(new[] { index }, change.Changed);
        // Every other piece is the very piece the step before has.
        int shared = 0, own = 0;
        for (int s = 0; s < after.Names.Length; s++)
            for (int i = 0; i < after.Entries[s].Length; i++)
                if (ReferenceEquals(after.Entries[s][i], before.Entries[s][i])) shared++; else own++;
        _out.WriteLine($"{shared} pieces shared with the step before, {own} of the step's own");
        Assert.Equal(1, own);
        Assert.False(before.SameAs(after));

        // An entry added: every entry that was there is still shared, though each has moved.
        pack.Variables.Insert(0, new PackVariableDef { Name = "addedFirst", Type = PackVariableType.Bool, DefaultValue = "false" });
        var grown = PackPieces.Take(pack, after);
        var growth = Assert.Single(PackPieces.Diff(after, grown));
        Assert.Equal("variables", growth.Name);
        Assert.Null(growth.Changed);
        int v = Array.IndexOf(grown.Names, "variables");
        for (int i = 1; i < grown.Entries[v].Length; i++)
            Assert.Same(after.Entries[Array.IndexOf(after.Names, "variables")][i - 1], grown.Entries[v][i]);
    }

    [Fact]
    public void AStepPutIntoThePackMakesItThatStep_AndWhatItDidNotChangeIsStillTheObjectItWas()
    {
        foreach (var (name, pack) in Packs())
        {
            if (pack.Dialogues.Count == 0 && pack.Variables.Count == 0) continue;
            var before = PackPieces.Take(pack);
            string wholeBefore = PackRepository.Serialize(pack);
            var objects = new List<object>();
            objects.AddRange(pack.Dialogues);
            objects.AddRange(pack.Variables);
            objects.AddRange(pack.Places);

            // Changed: an entry edited, one added, one taken away, a list turned round - and a value of the pack's own.
            DialogueDef? edited = pack.Dialogues.FirstOrDefault(d => d.Nodes.Count > 0);
            if (edited != null) edited.Nodes[0].Text += " (edited)";
            pack.Variables.Add(new PackVariableDef { Name = "added_" + name.Replace(' ', '_'), Type = PackVariableType.Int, DefaultValue = "1" });
            if (pack.Variables.Count > 2) pack.Variables.RemoveAt(0);
            if (pack.Places.Count > 1) pack.Places.Reverse();
            var after = PackPieces.Take(pack, before);
            string wholeAfter = PackRepository.Serialize(pack);
            Assert.NotEqual(wholeBefore, wholeAfter);

            // Back.
            Assert.True(PackPieces.Apply(pack, PackPieces.Diff(after, before), out _), name + ": the step could not be put back by its pieces");
            Assert.Equal(ReadBack(wholeBefore), ReadBack(PackRepository.Serialize(pack)));
            Assert.True(PackPieces.Take(pack, before).SameAs(before));
            // Every entry the change did not touch is the object it was.
            var now = new HashSet<object>(pack.Dialogues.Cast<object>().Concat(pack.Variables).Concat(pack.Places), ReferenceEqualityComparer.Instance);
            int kept = objects.Count(now.Contains), lost = objects.Count - kept;
            _out.WriteLine($"{name}: back by its pieces; {kept} entries are the objects they were, {lost} were read again");
            Assert.True(lost <= 2, $"{name}: {lost} entries were read again for a step that changed two");

            // And forward again.
            Assert.True(PackPieces.Apply(pack, PackPieces.Diff(before, after), out _));
            Assert.Equal(ReadBack(wholeAfter), ReadBack(PackRepository.Serialize(pack)));
        }
    }

    [Fact]
    public void APiecePackedSmallGivesBackExactlyItsText()
    {
        string plain = string.Concat(Enumerable.Range(0, 400).Select(i => $"  \"line{i}\": \"Olá, 你好 😀 é - the same words again and again\",\n"));
        var piece = new PackPiece(plain);
        piece.Pack();
        Assert.True(piece.IsPacked, "a long piece was not packed");
        Assert.Equal(plain, piece.Text);
        Assert.False(piece.IsPacked);

        // A text UTF-8 cannot hold (a lone half of a pair) is kept as it is.
        string lone = plain + "\"half \uD83D alone\"";
        var kept = new PackPiece(lone);
        kept.Pack();
        Assert.False(kept.IsPacked);
        Assert.Equal(lone, kept.Text);

        // And one too short to be worth it.
        var small = new PackPiece("{ \"a\": 1 }");
        small.Pack();
        Assert.False(small.IsPacked);
    }

    [Fact]
    public void ARecordGivenAnothersValuesIsStillTheObjectItWas()
    {
        var node = new DialogueNodeDef { Id = 7, Actor = "anna", Text = "Before", Timeout = 2f };
        node.ActionsOnStart.Add(new NodeActionDef { Type = "Wait" });
        var other = new DialogueNodeDef { Id = 7, Actor = "", Text = "After", Tag = "here", Duration = NodeDurationMode.Timeout };
        string wanted = ModelAssign.Written(other);
        Assert.NotEqual(wanted, ModelAssign.Written(node));
        Assert.True(ModelAssign.Into(node, other));
        Assert.Equal(wanted, ModelAssign.Written(node));
        Assert.Empty(node.ActionsOnStart);
        // Control: of another kind, nothing is given.
        Assert.False(ModelAssign.Into(node, new DialogueDef()));
    }

    /// <summary>
    /// A screen is kept object by object (SplitPiece): its parts put together are the text the screen is
    /// written as; a step shares every object that did not change with the one before - where it was, or
    /// anywhere once an object was added in front of it; two steps differ in the objects that were changed;
    /// a step holds the pack to it object by object, and is put back into it.
    /// </summary>
    [Fact]
    public void AScreenIsKeptObjectByObject_AndAStepSharesEveryObjectThatDidNotChange()
    {
        var pack = InGameSuitePacks.Story.Make(Folder("story-split"));
        var first = PackPieces.Take(pack);
        int s = Array.IndexOf(first.Names, "uis");
        Assert.True(s >= 0 && pack.Uis.Count > 0, "the pack has no screens: this proves nothing");
        int Objects(UiNodeDef n) => 1 + n.Children.Sum(Objects);
        for (int i = 0; i < pack.Uis.Count; i++)
        {
            var split = Assert.IsType<SplitPiece>(first.Entries[s][i]);
            Assert.Equal(1 + pack.Uis[i].Nodes.Sum(Objects), split.Parts.Length);
            Assert.Equal(pack.Uis[i].Nodes.Count, split.Kids[0]);
            // Its parts put together: the text the screen is written as, and a screen that reads back the same.
            Assert.Equal(ModelAssign.Written(pack.Uis[i]), split.Text);
            Assert.Equal(ModelAssign.Written(pack.Uis[i]), ModelAssign.Written(split.Build()));
        }

        // The same pack again: the very pieces.
        var again = PackPieces.Take(pack, first);
        for (int i = 0; i < pack.Uis.Count; i++) Assert.Same(first.Entries[s][i], again.Entries[s][i]);
        Assert.Empty(PackPieces.Diff(first, again));

        // One object changed: one part is another, and the two steps differ in that screen alone.
        int u = pack.Uis.FindIndex(x => x.Nodes.Any(n => n.Children.Count >= 2));
        Assert.True(u >= 0, "no screen with an object holding two others: this proves nothing");
        var holder = pack.Uis[u].Nodes.First(n => n.Children.Count >= 2);
        holder.Children[1].Rect.Position = new[] { 31f, 17f };
        var second = PackPieces.Take(pack, first);
        var a = (SplitPiece)first.Entries[s][u];
        var b = (SplitPiece)second.Entries[s][u];
        Assert.NotSame(a, b);
        Assert.True(a.SameShapeAs(b));
        Assert.False(a.SameTextAs(b));
        Assert.Equal(1, Enumerable.Range(0, a.Parts.Length).Count(k => !ReferenceEquals(a.Parts[k], b.Parts[k])));
        var diff = PackPieces.Diff(first, second);
        var only = Assert.Single(diff);
        Assert.Equal("uis", only.Name);
        Assert.Equal(new[] { u }, only.Changed);
        for (int i = 0; i < pack.Uis.Count; i++)
            if (i != u) Assert.Same(first.Entries[s][i], second.Entries[s][i]);

        // An object added in front of the others: every object after it is somewhere else, and still shared.
        holder.Children.Insert(0, new UiNodeDef { Name = "Added in front" });
        var third = PackPieces.Take(pack, second);
        var c = (SplitPiece)third.Entries[s][u];
        Assert.False(b.SameShapeAs(c));
        Assert.Equal(b.Parts.Length + 1, c.Parts.Length);
        var before = new HashSet<PackPiece>(b.Parts, ReferenceEqualityComparer.Instance);
        Assert.Equal(1, c.Parts.Count(p => !before.Contains(p)));
        Assert.Equal(ModelAssign.Written(pack.Uis[u]), c.Text);

        // The step holds the pack to it, object by object - control: an object changed, and it does not.
        Assert.True(third.Holds(pack, "uis"));
        Assert.True(third.Holds(pack, "uis", new[] { u }));
        holder.Children[0].Name = "Added, renamed";
        Assert.False(third.Holds(pack, "uis", new[] { u }));
        Assert.False(third.Holds(pack, "uis"));
        holder.Children[0].Name = "Added in front";
        Assert.True(third.Holds(pack, "uis"));

        // And put back into the pack: the first step again, every other screen the object it was.
        var others = pack.Uis.Where((_, i) => i != u).ToList();
        Assert.True(PackPieces.Apply(pack, PackPieces.Diff(third, first), out _));
        Assert.True(PackPieces.Take(pack).SameAs(first));
        Assert.True(first.Holds(pack, "uis"));
        Assert.Equal(others, pack.Uis.Where((_, i) => i != u).ToList());

        // Packed small, the parts say what they said.
        string whole = a.Text;
        a.Pack();
        Assert.Equal(whole, a.Text);
    }

    /// <summary>
    /// A place is kept object by object too - and an object of the same kind that is not one of the place's
    /// tree (a prop hung on a character standing on an object) is written whole inside the object it belongs
    /// to, with everything under it. Left out there as it is for the tree's own objects, what was under the
    /// prop was gone from every step.
    /// </summary>
    [Fact]
    public void APlaceIsKeptObjectByObject_AndWhatHangsOnACharacterStaysWholeInsideItsObject()
    {
        var place = new PlaceDef { Key = "room" };
        var stand = new GameObjectDef { Name = "Stand", X = 1 };
        var under = new GameObjectDef { Name = "Under the stand", Y = 2 };
        under.Children.Add(new GameObjectDef { Name = "Deeper" });
        stand.Children.Add(under);
        var placed = new NpcPlacementDef();
        var prop = new GameObjectDef { Name = "Prop" };
        prop.Children.Add(new GameObjectDef { Name = "Under the prop" });
        prop.Children[0].Children.Add(new GameObjectDef { Name = "Deeper under the prop" });
        placed.Children.Add(prop);
        stand.Npcs.Add(placed);
        place.GameObjects.Add(stand);
        place.GameObjects.Add(new GameObjectDef { Name = "Other" });
        var pack = new ModPack { PackId = "pieces" };
        pack.Places.Add(place);

        var step = PackPieces.Take(pack);
        var split = Assert.IsType<SplitPiece>(step.Entries[Array.IndexOf(step.Names, "places")][0]);
        // The place, and the four objects of its tree; the prop and what is under it are inside "Stand".
        Assert.Equal(5, split.Parts.Length);
        Assert.Equal(new[] { 2, 1, 1, 0, 0 }, split.Kids);
        Assert.Contains("Deeper under the prop", split.Parts[1].Text);
        Assert.DoesNotContain("Under the stand", split.Parts[1].Text);
        string written = ModelAssign.Written(place);
        Assert.Contains("Deeper under the prop", written);     // control: the place is written with it
        Assert.Equal(written, split.Text);
        Assert.Equal(written, ModelAssign.Written(split.Build()));
        Assert.Equal(ModelAssign.Written(under), ModelAssign.Written(split.BuildNode(2)));
        Assert.Equal(ReadBack(PackRepository.Serialize(pack)), ReadBack(step.ToJson()));
        Assert.True(step.Holds(pack, "places"));

        // What is under the prop changed: the object it is inside is the part that differs.
        prop.Children[0].Children[0].Name = "Renamed under the prop";
        var next = PackPieces.Take(pack, step);
        var after = (SplitPiece)next.Entries[Array.IndexOf(next.Names, "places")][0];
        Assert.Equal(new[] { 1 }, Enumerable.Range(0, 5).Where(k => !ReferenceEquals(split.Parts[k], after.Parts[k])).ToArray());
        Assert.False(step.Holds(pack, "places"));
    }
}
