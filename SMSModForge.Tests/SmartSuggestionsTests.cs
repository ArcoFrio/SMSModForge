using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Smart suggestions (the author, 1.8.0): a dropdown opened empty puts on top
/// what the record being edited makes likely - a dialogue that names a
/// character anywhere and runs at a place puts that character's scenes there
/// first - from the words names are made of. These check the ranking itself,
/// over a pack written the way the editor saves one.
/// </summary>
public class SmartSuggestionsTests
{
    private readonly ITestOutputHelper _out;
    public SmartSuggestionsTests(ITestOutputHelper o) => _out = o;

    /// <summary>Two characters with scenes at two places, and enough other
    /// scenes that a word only some have tells something.</summary>
    internal static ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("suggest.pack");
        foreach (var who in new[] { "Mira", "Juno", "Kai", "Lena" })
            pack.Characters.Add(new CharacterDef { Key = who, DisplayName = who });
        pack.Places.Add(new PlaceDef { Key = "NightMarket", DisplayName = "Night Market" });
        pack.Places.Add(new PlaceDef { Key = "Harbor", DisplayName = "Harbor" });
        foreach (var key in new[]
                 {
                     "MiraNightmarketScene01", "MiraNightmarketScene02", "MiraHarborScene01",
                     "JunoNightmarketScene01", "JunoHarborScene01", "JunoHarborScene02",
                     "KaiParkScene01", "KaiParkScene02", "KaiNightmarketScene01", "LenaGymScene01", "LenaGymScene02",
                     "LenaHarborScene01",
                 })
            pack.Scenes.Add(new SceneDef { Key = key, DisplayName = key });
        pack.Dialogues.Add(Talk("MiraEvening", "Mira", "place:NightMarket"));
        pack.Dialogues.Add(Talk("JunoMorning", "Juno", "place:Harbor"));
        return pack;
    }

    internal static DialogueDef Talk(string key, string actor, string? level)
    {
        var d = new DialogueDef { Key = key, DisplayName = key };
        if (level != null)
            d.StartConditions.Add(new NodeConditionDef { Type = NodeConditionTypes.LevelActive, Params = { ["level"] = level } });
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = actor, Text = "Over here!" });
        d.Nodes.Add(new DialogueNodeDef { Id = 2, Actor = actor, Text = "Took you long enough." });
        d.Nodes[0].Children.Add(2);
        d.RootNodeIds.Add(1);
        return d;
    }

    private static List<SuggestionOption> Options(IEnumerable<string> names)
        => names.Select(n => new SuggestionOption(n, n, null, null)).ToList();

    /// <summary>What goes on top, best first, for the record given.</summary>
    private static List<string> Ranked(ModPack pack, object record, IEnumerable<string> names)
    {
        var index = SmartSuggestions.Index.Parse(PackRepository.SerializeAsSaved(pack));
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(record, PackRepository.ManifestSettings);
        var context = SmartSuggestions.Context.Of(SmartSuggestions.Record.From(Newtonsoft.Json.Linq.JToken.Parse(json))!, index);
        var scores = SmartSuggestions.Rank(Options(names), context, index);
        return scores == null ? new() : scores.OrderByDescending(s => s.Value).Select(s => (string)s.Key).ToList();
    }

    private static IEnumerable<string> SceneKeys(ModPack pack) => pack.Scenes.Select(s => s.Key);

    [Fact]
    public void NamesAreReadAsTheirWords()
    {
        var words = new List<string>();
        SuggestionWords.Each("AnisHHBedroom01Scene01", words.Add);
        Assert.Contains("anis", words);
        Assert.Contains("hh", words);
        Assert.Contains("bedroom", words);
        Assert.Contains("scene", words);
        Assert.DoesNotContain("01", words);   // a number is no word

        words.Clear();
        SuggestionWords.Each("place:NightMarket", words.Add);
        Assert.Contains("nightmarket", words);   // meets "Nightmarket" in a scene's name
        Assert.Contains("market", words);

        words.Clear();
        SuggestionWords.Each("Scene01_copy2", words.Add);
        Assert.DoesNotContain("copy", words);   // how it was made, not what it is
    }

    [Fact]
    public void TheCharactersScenesAtTheDialoguesPlaceComeFirst()
    {
        var pack = Pack();
        var ranked = Ranked(pack, pack.Dialogues[0], SceneKeys(pack));
        _out.WriteLine(string.Join(", ", ranked));
        Assert.Equal(new[] { "MiraNightmarketScene01", "MiraNightmarketScene02", "MiraHarborScene01" }, ranked.Take(3));
        Assert.DoesNotContain(ranked, k => k.StartsWith("Juno") || k.StartsWith("Lena"));

        // The control: the other character's dialogue, at the other place.
        ranked = Ranked(pack, pack.Dialogues[1], SceneKeys(pack));
        _out.WriteLine(string.Join(", ", ranked));
        Assert.Equal(new[] { "JunoHarborScene01", "JunoHarborScene02", "JunoNightmarketScene01" }, ranked.Take(3));
    }

    [Fact]
    public void ARecordThatNamesNothingInTheListSuggestsNothing()
    {
        var pack = Pack();
        var quiet = Talk("Weather", "nobody", null);
        Assert.Empty(Ranked(pack, quiet, SceneKeys(pack)));
    }

    [Fact]
    public void AWordEveryOptionSharesLiftsNone()
    {
        var pack = Pack();
        // "scene" in every option, and said over and over by the record.
        var d = Talk("Scenes", "nobody", null);
        d.Nodes[0].Text = "scene scene scene";
        d.DisplayName = "Scene Scene Scene";
        Assert.Empty(Ranked(pack, d, SceneKeys(pack)));
    }

    [Fact]
    public void TheRecordIsNotItsOwnSuggestion()
    {
        var pack = Pack();
        pack.Dialogues.Add(Talk("MiraLater", "Mira", null));
        pack.Dialogues.Add(Talk("KaiLater", "Kai", null));
        var ranked = Ranked(pack, pack.Dialogues[0], pack.Dialogues.Select(d => d.Key));
        Assert.DoesNotContain("MiraEvening", ranked);
        Assert.Equal("MiraLater", ranked.FirstOrDefault());
    }

    [Fact]
    public void AFolderSaysWhoseAnOptionIs()
    {
        // Names that say nothing, filed under who they belong to.
        var pack = PackRepository.CreateEmpty("folders.pack");
        pack.Characters.Add(new CharacterDef { Key = "Mira", DisplayName = "Mira" });
        var keys = Enumerable.Range(1, 12).Select(i => $"S{i:000}").ToList();
        foreach (var k in keys) pack.Scenes.Add(new SceneDef { Key = k, DisplayName = k });
        pack.SceneFolders.Add(new UnitFolderDef { Name = "Mira", Items = { "S003", "S007" } });
        pack.SceneFolders.Add(new UnitFolderDef { Name = "Others", Items = keys.Except(new[] { "S003", "S007" }).ToList() });
        pack.Dialogues.Add(Talk("Chat", "Mira", null));

        var ranked = Ranked(pack, pack.Dialogues[0], keys);
        Assert.Equal(new[] { "S003", "S007" }, ranked);
    }

    [Fact]
    public void AKeyTheRecordUsesBringsTheNamesBehindIt()
    {
        // The actor is "c07"; only the character says she is Mira.
        var pack = Pack();
        pack.Characters.Add(new CharacterDef { Key = "c07", DisplayName = "Mira" });
        var d = Talk("Chat", "c07", null);
        for (int n = 3; n <= 6; n++) d.Nodes.Add(new DialogueNodeDef { Id = n, Actor = "c07", Text = "Mm." });
        var ranked = Ranked(pack, d, SceneKeys(pack));
        Assert.Equal(new[] { "MiraHarborScene01", "MiraNightmarketScene01", "MiraNightmarketScene02" }, ranked.Take(3).OrderBy(k => k));
    }

    [Fact]
    public void ARecordReadInTheBackgroundFollowsItsEdits()
    {
        var pack = Pack();
        var suggestions = new SmartSuggestions();
        suggestions.Rebuild(PackRepository.SerializeAsSaved(pack));
        var dialogue = pack.Dialogues[0];
        suggestions.Follow(dialogue);
        Assert.True(suggestions.Idle.Wait(TimeSpan.FromSeconds(10)));
        var options = Options(SceneKeys(pack));
        Assert.StartsWith("Mira", (string)suggestions.Rank(options)!.OrderByDescending(s => s.Value).First().Key);

        // A commit: the lines are Juno's now.
        foreach (var node in dialogue.Nodes) node.Actor = "Juno";
        suggestions.Follow(dialogue);
        Assert.True(suggestions.Idle.Wait(TimeSpan.FromSeconds(10)));
        Assert.StartsWith("Juno", (string)suggestions.Rank(options)!.OrderByDescending(s => s.Value).First().Key);

        suggestions.Follow(null);   // nothing being edited
        Assert.True(suggestions.Idle.Wait(TimeSpan.FromSeconds(10)));
        Assert.Null(suggestions.Rank(options));
    }

    /// <summary>
    /// Nothing heavy where a person waits (the author: "without causing a
    /// stutter even on large context boxes much larger than Androids's"). A
    /// pack ten times a big one: reading it all happens off the window's
    /// thread, and what is left for the moment a dropdown opens - ranking a
    /// list of two thousand - and for a commit - reading one long dialogue
    /// again - stays inside a frame.
    /// </summary>
    [Fact]
    public void ABigPackCostsTheWindowNothingNoticeable()
    {
        var pack = PackRepository.CreateEmpty("big.pack");
        var rng = new Random(7);
        string[] places = Enumerable.Range(0, 60).Select(i => $"Place{i}Area").ToArray();
        for (int c = 0; c < 300; c++) pack.Characters.Add(new CharacterDef { Key = $"Char{c}", DisplayName = $"Char{c}" });
        for (int s = 0; s < 2000; s++)
            pack.Scenes.Add(new SceneDef { Key = $"Char{rng.Next(300)}{places[rng.Next(60)]}Scene{s:0000}", DisplayName = $"Scene {s}" });
        for (int d = 0; d < 900; d++)
        {
            var dialogue = Talk($"Talk{d}", $"Char{rng.Next(300)}", "place:" + places[rng.Next(60)]);
            for (int n = 3; n < 40; n++)
                dialogue.Nodes.Add(new DialogueNodeDef { Id = n, Actor = $"Char{rng.Next(300)}", Text = "Some words a player reads, more of them than needed." });
            pack.Dialogues.Add(dialogue);
        }
        string json = PackRepository.SerializeAsSaved(pack);
        _out.WriteLine($"pack: {json.Length / 1024} KB");

        var suggestions = new SmartSuggestions();
        var clock = Stopwatch.StartNew();
        suggestions.Rebuild(json);
        double queued = clock.Elapsed.TotalMilliseconds;
        Assert.True(suggestions.Idle.Wait(TimeSpan.FromSeconds(60)));
        _out.WriteLine($"reading the pack: {queued:0.0} ms on the caller, {clock.ElapsedMilliseconds} ms in all");
        Assert.True(queued < 50, $"reading the pack held the caller {queued:0} ms");

        // A long dialogue read again after a commit: serializing it is the
        // window's part.
        var longest = pack.Dialogues[0];
        for (int n = 40; n < 600; n++)
            longest.Nodes.Add(new DialogueNodeDef { Id = n, Actor = "Char1", Text = "A longer conversation than any real one." });
        var options = Options(pack.Scenes.Select(s => s.Key));
        suggestions.Follow(longest);   // warm up
        Assert.True(suggestions.Idle.Wait(TimeSpan.FromSeconds(10)));
        suggestions.Rank(options);

        var follows = new List<double>();
        var ranks = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            longest.Nodes[0].Text = "Edited " + i;
            clock.Restart();
            suggestions.Follow(longest);
            follows.Add(clock.Elapsed.TotalMilliseconds);
            Assert.True(suggestions.Idle.Wait(TimeSpan.FromSeconds(10)));
            clock.Restart();
            suggestions.Rank(options);
            ranks.Add(clock.Elapsed.TotalMilliseconds);
        }
        double follow = follows.OrderBy(x => x).ElementAt(2), rank = ranks.OrderBy(x => x).ElementAt(2);
        _out.WriteLine($"a commit (600 lines): {follow:0.0} ms; opening a list of {options.Count}: {rank:0.0} ms (medians)");
        Assert.True(follow < 16, $"reading the edited dialogue took {follow:0.0} ms");
        Assert.True(rank < 16, $"ranking {options.Count} options took {rank:0.0} ms");
    }
}
