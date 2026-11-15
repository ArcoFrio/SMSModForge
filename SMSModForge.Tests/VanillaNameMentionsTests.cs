using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The table of where the game's own words name its characters (1.9.0), against the
/// editor's own copy of those words. The plugin renames a place only when the text it
/// finds hashes as the table says, so a hash computed differently here and in the
/// generator (C# against Python) would make every check fail, silently: no line would
/// ever be renamed. Recomputed here from the texts, not read back from the table.
/// </summary>
public sealed class VanillaNameMentionsTests
{
    private readonly ITestOutputHelper _out;
    public VanillaNameMentionsTests(ITestOutputHelper o) => _out = o;

    private sealed record Row(string Source, string Where, uint Hash, (int Start, int Length, string Key)[] Spans);

    private static List<Row> Rows() => VanillaNameMentions.Rows.Select(r =>
    {
        var c = r.Split('\t');
        var spans = c[3].Split(';').Select(s => s.Split(':')).Select(p => (int.Parse(p[0]), int.Parse(p[1]), p[2])).ToArray();
        return new Row(c[0], c[1], uint.Parse(c[2]), spans);
    }).ToList();

    /// <summary>Every name a character goes by in the game's words: the cast's, and the
    /// one their Actor speaks.</summary>
    private static Dictionary<string, HashSet<string>> NamesByKey()
    {
        var names = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, string name)
        {
            if (!names.TryGetValue(key, out var set)) names[key] = set = new(StringComparer.Ordinal);
            set.Add(name);
        }
        foreach (var b in VanillaCastData.Busts)
        {
            string name = VanillaCastData.SpokenName(b.Character);
            Add(VanillaCastData.KeyFor(name), name);
        }
        foreach (var s in VanillaSpeech.All) Add(s.Key, s.Name);
        Add("judgebenjamin", "Benjamin");
        Add("fanjeff", "Jeff");
        return names;
    }

    [Fact]
    public void EveryLineAndQuestTextHashesAsTheTableSays_AndEachSpanIsThatCharactersName()
    {
        var names = NamesByKey();
        int lines = 0, quests = 0;
        var wrong = new List<string>();
        foreach (var row in Rows())
        {
            string? text = row.Source switch
            {
                "line" => LineText(row.Where),
                "quest" => QuestText(row.Where),
                _ => null,
            };
            if (row.Source == "ui") continue;   // the running game's screens: checked in game (K10)
            if (text == null) { wrong.Add("no text for " + row.Source + " " + row.Where); continue; }
            if (row.Source == "line") lines++; else quests++;

            if (VanillaNameMentions.Hash(text) != row.Hash) wrong.Add("hash " + row.Where);
            foreach (var (start, length, key) in row.Spans)
            {
                string said = start + length <= text.Length ? text.Substring(start, length) : "(past the end)";
                if (!names.TryGetValue(key, out var hers) || !hers.Contains(said))
                    wrong.Add($"{row.Where} @{start}: '{said}' is not a name of {key}");
            }
        }
        _out.WriteLine($"{lines} lines and {quests} quest texts checked");
        foreach (var w in wrong.Take(10)) _out.WriteLine(w);
        Assert.True(lines > 1500 && quests > 150, "the table is smaller than it was made: " + lines + " / " + quests);
        Assert.Empty(wrong);
    }

    [Fact]
    public void ATextThatSaysAnythingElseHashesDifferently()
    {
        // The control: one letter changed is a different text, which the plugin leaves alone.
        var row = Rows().First(r => r.Source == "line");
        string text = LineText(row.Where)!;
        Assert.Equal(row.Hash, VanillaNameMentions.Hash(text));
        Assert.NotEqual(row.Hash, VanillaNameMentions.Hash(text + " "));
        Assert.NotEqual(row.Hash, VanillaNameMentions.Hash(text.Replace(text[0], char.IsUpper(text[0]) ? char.ToLowerInvariant(text[0]) : char.ToUpperInvariant(text[0]))));
    }

    [Fact]
    public void NoNameThatIsAlsoAWordIsTiedUnread_AndDescriptionsNeverAre()
    {
        // The reviewed ones are in reviewed.tsv; a Ghost that is an inventory item is
        // not tied, and "Cool Guy" in a sentence is never touched.
        Assert.DoesNotContain(Rows(), r => r.Source == "ui" && r.Where.Contains("Inventory/Items/Core/Item1 (51)"));
        Assert.DoesNotContain(Rows(), r => r.Spans.Any(s => s.Key is "coolguy" or "cop" or "civilian"));
    }

    /// <summary>A text renamed as the plugin renames one (GameNameMentions.Renamed): each
    /// span right to left, its stutters carried first.</summary>
    private static string Rename(string text, int start, int length, string newName)
    {
        text = NameStutters.Carry(text, start, text.Substring(start, length), newName);
        return text[..start] + newName + text[(start + length)..];
    }

    [Fact]
    public void AStutteredNameKeepsItsStutter_InTheNewNamesFirstLetter()
    {
        Assert.Equal("B-Bella, wait.", Rename("A-Anna, wait.", 2, 4, "Bella"));
        Assert.Equal("B-b-Bella!", Rename("A-a-Anna!", 4, 4, "Bella"));
        Assert.Equal("\"B-Bella...\"", Rename("\"A-Anna...\"", 3, 4, "Bella"));
        // Controls: a hyphenated word is not a stutter, nor another letter, and a name
        // that starts with the same letter, or with none, leaves the stutter as it is.
        Assert.Equal("Ma-Bella", Rename("Ma-Anna", 3, 4, "Bella"));
        Assert.Equal("X-Bella", Rename("X-Anna", 2, 4, "Bella"));
        Assert.Equal("A-Annie", Rename("A-Anna", 2, 4, "Annie"));
        Assert.Equal("A-#1", Rename("A-Anna", 2, 4, "#1"));
        Assert.False(NameStutters.Stuttered("Ma-Anna", 3, "Anna"));
        Assert.True(NameStutters.Stuttered("A-Anna", 2, "Anna"));
    }

    [Fact]
    public void TheGamesStutteredNames_AllReadWithTheNewName()
    {
        // The game's own: every stuttered span in the table, renamed, starts its stutter
        // with the new name's letter. The reviewer counted 31.
        int stuttered = 0;
        foreach (var row in Rows().Where(r => r.Source == "line"))
        {
            string? text = LineText(row.Where);
            if (text == null) continue;
            foreach (var (start, length, key) in row.Spans)
            {
                string name = text.Substring(start, length);
                if (!NameStutters.Stuttered(text, start, name)) continue;
                stuttered++;
                string renamed = Rename(text, start, length, "Quill");
                Assert.Equal("Q-Quill", renamed.Substring(start - 2, 7).Replace("q-", "Q-"));
            }
        }
        _out.WriteLine(stuttered + " stuttered names in the game's lines");
        Assert.True(stuttered >= 20, "only " + stuttered + " stuttered names found");
    }

    private static string? LineText(string where)
    {
        int hash = where.LastIndexOf('#');
        var d = VanillaDialogueCatalog.Open(where[..hash]);
        return d != null && d.Nodes.TryGetValue(where[(hash + 1)..], out var n) ? n.PlainText : null;
    }

    private static string? QuestText(string where)
    {
        int hash = where.IndexOf('#');
        var q = VanillaQuests.Find(where[..hash]);
        if (q == null) return null;
        string part = where[(hash + 1)..];
        if (part == "title") return q.Title;
        if (part == "description") return q.Description;
        if (part.StartsWith("task ", StringComparison.Ordinal) && int.TryParse(part[5..], out int id))
            return q.Tasks.FirstOrDefault(t => t.Id == id)?.Name;
        return null;
    }
}
