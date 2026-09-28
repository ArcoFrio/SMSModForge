using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A pack's translation, from the file the editor writes to the words a
/// player reads: written, translated, exported, and laid over the manifest by
/// the same code the plugin runs.
/// </summary>
public class PackTranslationTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SMSModForgePackText", Guid.NewGuid().ToString("N"));
    private string PackRoot => Path.Combine(_root, "pack");

    public PackTranslationTests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(PackRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>One of every text a player reads.</summary>
    private static ModPack Pack()
    {
        var pack = new ModPack { PackId = "translate.test" };
        pack.Characters.Add(new CharacterDef { Key = "kiki", DisplayName = "Kiki" });

        var dialogue = new DialogueDef { Key = "beach", DisplayName = "At the beach" };
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "kiki", Text = "Hello there!\nNice day." });
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 2, Kind = DialogueNodeKind.Choice, Text = "Say hi, <b>{mood}</b> [PV:mood]" });
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 3, Kind = DialogueNodeKind.Random, Text = "" });
        pack.Dialogues.Add(dialogue);

        var quest = new QuestDef { Key = "find", Title = "Find the shell", Description = "Somewhere on the sand." };
        var task = new QuestTaskDef { Key = "look", Name = "Look around", QuestDescription = "You looked." };
        task.Subtasks.Add(new QuestTaskDef { Key = "dig", Name = "Dig" });
        quest.Tasks.Add(task);
        pack.Quests.Add(quest);

        var place = new PlaceDef { Key = "cove", DisplayName = "Cove" };
        place.NavigatorButtons.Add(new NavigatorButtonDef { Target = "vanilla:Beach", Label = "Back to the beach" });
        pack.Places.Add(place);
        pack.MapButtons.Add(new MapButtonDef { District = "Seaside", Target = "pack:translate.test.cove", Label = "Cove" });

        var ui = new UiDef { Id = "hud", Name = "Shell counter" };
        var panel = new UiNodeDef { Name = "Panel" };
        panel.Children.Add(new UiNodeDef { Name = "Label", Text = new UiTextDef { Value = "Shells: [PV:shells]" } });
        ui.Nodes.Add(panel);
        pack.Uis.Add(ui);
        return pack;
    }

    private static JObject Saved(ModPack pack) => JObject.Parse(PackRepository.SerializeAsSaved(pack));

    private static readonly string[] AllKeys =
    {
        "character.kiki.name",
        "dialogue.beach.1",
        "dialogue.beach.2",
        "quest.find.title",
        "quest.find.description",
        "quest.find.task.look.name",
        "quest.find.task.look.questDescription",
        "quest.find.task.dig.name",
        "navigator.cove.vanilla_Beach.label",
        "mapButton.Seaside.pack_translate_test_cove.label",
        "ui.hud.Panel.Label.text",
    };

    // ── A translation changes words and nothing else ─────────────────

    /// <summary>Every leaf of <paramref name="a"/> and <paramref name="b"/>
    /// that differs, by its JSON path - present in one and not the other
    /// counts as differing.</summary>
    private static List<string> Differences(JToken a, JToken b)
    {
        var found = new List<string>();
        void Walk(JToken? x, JToken? y, string path)
        {
            if (x is JObject ox && y is JObject oy)
            {
                foreach (var name in ox.Properties().Select(p => p.Name)
                                       .Union(oy.Properties().Select(p => p.Name)))
                    Walk(ox[name], oy[name], path.Length == 0 ? name : path + "." + name);
                return;
            }
            if (x is JArray ax && y is JArray ay)
            {
                for (int i = 0; i < Math.Max(ax.Count, ay.Count); i++)
                    Walk(i < ax.Count ? ax[i] : null, i < ay.Count ? ay[i] : null, path + "[" + i + "]");
                return;
            }
            if (!JToken.DeepEquals(x, y)) found.Add(path);
        }
        Walk(a, b, "");
        return found;
    }

    [Fact]
    public void TranslatingAPackChangesItsWordsAndNothingElse()
    {
        // The rule the whole feature rests on, and the one it is easiest to
        // break without noticing: the game finds characters, lines, quests,
        // buttons and screens by keys, ids and targets, and a translation that
        // changed one of those would not look wrong - it would quietly stop the
        // thing it touched from being found, in one language only.
        //
        // So this lays a translation over every text there is and then compares
        // the whole manifest, leaf by leaf. Every difference has to be one of
        // the texts, or the note a line keeps of its own words.
        var original = Saved(Pack());
        var sites = PackTexts.Of(original);

        var translation = new TextFile();
        foreach (var site in sites)
            translation.Add(new TextFile.Entry { Key = site.Key, Text = "«" + site.Text + "»" });

        var translated = (JObject)original.DeepClone();
        var applied = PackTexts.Apply(translated, translation);

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var site in PackTexts.Of(translated))
        {
            string at = site.Holder.Path;
            allowed.Add((at.Length == 0 ? "" : at + ".") + site.Field);
            allowed.Add((at.Length == 0 ? "" : at + ".") + PackTexts.OriginalTextKey);
        }

        var differences = Differences(original, translated);
        foreach (var d in differences) _out.WriteLine((allowed.Contains(d) ? "  text   " : "  OTHER  ") + d);

        // The control first: a translation that changed nothing would pass
        // the next check by doing nothing at all.
        Assert.Equal(sites.Count, applied.Translated);
        Assert.Equal(sites.Count, differences.Count(allowed.Contains) -
                                  differences.Count(d => d.EndsWith(PackTexts.OriginalTextKey)));

        Assert.All(differences, d => Assert.True(allowed.Contains(d),
            "a translation changed " + d + ", which is not a text a player reads"));
    }

    /// <summary>
    /// What the game finds things BY. A field on this list must never be a
    /// text that gets translated, because the lookup that uses it would then
    /// succeed in one language and fail in the others.
    /// <para/>
    /// <c>name</c> is deliberately NOT here, because it means two things: on a
    /// quest task it is the words the journal shows, and on a UI node it is
    /// the object the game finds. A list of field names cannot tell those
    /// apart, so this test cannot either - the one above can, because it works
    /// by path, and it is what proves a UI node's name never changes (its text
    /// lives at <c>text.value</c>).
    /// </summary>
    private static readonly string[] Identity =
    {
        "key", "id", "packId", "target", "source", "actor", "district",
        "gameObjectName", "task", "under", "scene", "variable",
    };

    [Fact]
    public void NoTranslatedFieldIsOneTheGameFindsThingsBy()
    {
        // The other half of the test above. That one proves a translation
        // changes only its own texts - but if a text were ALSO an identity,
        // that test would pass while the game broke. This pins the set of
        // fields a translation writes, so adding a new kind of translatable
        // text means deciding, here, that it is not something anything looks
        // things up by.
        //
        // Checked against the runtime rather than assumed: navigator and map
        // labels are what a button shows, and the button is found by its
        // target; quest titles and task names are what the journal shows, and
        // quests are found by key; a character's name is what is drawn above
        // a line - and what its colour is matched on, which is consistent
        // because both sides use the translated name.
        var fields = PackTexts.Of(Saved(Pack())).Select(s => s.Field).Distinct().OrderBy(f => f).ToList();
        _out.WriteLine("translated fields: " + string.Join(", ", fields));

        Assert.Empty(fields.Intersect(Identity, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(new[] { "description", "displayName", "label", "name", "questDescription", "text", "title", "value" },
                     fields);
    }

    [Fact]
    public void EveryTextAPlayerReadsHasAKey_AndAnEmptyLineHasNone()
    {
        var keys = PackTexts.Of(Saved(Pack())).Select(s => s.Key).ToList();
        _out.WriteLine(string.Join("\n", keys));
        Assert.Equal(AllKeys, keys);
    }

    [Fact]
    public void KeysAreMadeOfIds_SoMovingALineOrATaskKeepsItsKey()
    {
        var pack = Pack();
        pack.Dialogues[0].Nodes.Reverse();
        var moved = PackTexts.Of(Saved(pack)).ToDictionary(s => s.Key, s => s.Text);
        Assert.Equal("Hello there!\nNice day.", moved["dialogue.beach.1"]);
        Assert.Equal(AllKeys.OrderBy(k => k), moved.Keys.OrderBy(k => k));
    }

    [Fact]
    public void TwoThingsWithTheSameNameInOnePlace_GetKeysOfTheirOwn()
    {
        var pack = Pack();
        pack.Places[0].NavigatorButtons.Add(new NavigatorButtonDef { Target = "vanilla:Beach", Label = "The beach again" });
        pack.Uis[0].Nodes[0].Children.Add(new UiNodeDef { Name = "Label", Text = new UiTextDef { Value = "Second" } });
        var keys = PackTexts.Of(Saved(pack)).Select(s => s.Key).ToList();
        Assert.Contains("navigator.cove.vanilla_Beach-2.label", keys);
        Assert.Contains("ui.hud.Panel.Label-2.text", keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void OneOfTheGamesQuests_OffersOnlyWhatThePackSays()
    {
        var pack = new ModPack { PackId = "p" };
        var quest = new QuestDef { Key = "drink", Source = "Drink Quest", Description = "A new paragraph." };
        quest.VanillaTasks.Add(new VanillaTaskHookDef { Task = "3", QuestDescription = "Once the third is done." });
        quest.AddedTasks.Add(new AddedTaskDef { Key = "extra", Name = "An extra step" });
        pack.Quests.Add(quest);

        var keys = PackTexts.Of(Saved(pack)).Select(s => s.Key).ToList();
        _out.WriteLine(string.Join("\n", keys));
        // Its title is the game's, and stays so.
        Assert.DoesNotContain("quest.drink.title", keys);
        Assert.Contains("quest.drink.description", keys);
        Assert.Contains("quest.drink.gameTask.3.questDescription", keys);
        Assert.Contains("quest.drink.task.extra.name", keys);
    }

    [Fact]
    public void AFileWrittenTranslatedAndExported_IsWhatThePluginPutsInPlace()
    {
        var pack = Pack();
        PackRepository.Save(pack, PackRoot);

        var (path, existed) = PackTranslations.CreateOrUpdate(pack, PackRoot, "es");
        Assert.False(existed);
        Assert.Equal(Path.Combine(PackRoot, "translations", "es.txt"), path);
        string written = File.ReadAllText(path, Encoding.UTF8);
        _out.WriteLine(written);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(path).Take(3).ToArray());
        Assert.Contains("# en: Hello there!\\nNice day.", written);
        Assert.Contains("# Line 1, said by Kiki", written);
        Assert.Contains("[Dialogue: At the beach]", written);

        // A translator's work, as it would come back from Notepad++ or an AI tool.
        string translated = written
            .Replace("dialogue.beach.1 = Hello there!\\nNice day.", "dialogue.beach.1 = ¡Hola!\\nBuen día.")
            .Replace("quest.find.title = Find the shell", "quest.find.title = Encuentra la concha")
            .Replace("ui.hud.Panel.Label.text = Shells: [PV:shells]", "ui.hud.Panel.Label.text = Conchas: [PV:shells]");
        Assert.NotEqual(written, translated);
        File.WriteAllText(path, translated, new UTF8Encoding(true));
        // The kept copy of a repair never ships.
        File.WriteAllText(path + ".bak", "x");

        var check = TextCheck.Run(PackTranslations.Source(pack), Loc.Read(path)!, "es");
        Assert.DoesNotContain(check.Findings, f => f.IsError);

        string smspack = Path.Combine(_root, "out.smspack");
        PackExporter.Export(PackRoot, smspack);
        using var zip = ZipFile.OpenRead(smspack);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.Contains("translations/es.txt", names);
        Assert.DoesNotContain("translations/es.txt.bak", names);

        string Read(string name)
        {
            using var reader = new StreamReader(zip.Entries.First(e => e.FullName.Replace('\\', '/') == name).Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }

        // What the plugin does with the archive: pick the player's file, lay it over the manifest.
        var manifest = JObject.Parse(Read("modpack.json"));
        var files = PackTexts.Files(names);
        Assert.Equal("es", LanguageMatch.Best("es-MX", files.Keys));
        var applied = PackTexts.Apply(manifest, TextFile.Parse(Read(files["es"])));
        _out.WriteLine($"{applied.Translated} translated, {applied.Untranslated} untranslated, {applied.Outdated.Count} outdated");

        Assert.Equal(3, applied.Translated);
        Assert.Equal(AllKeys.Length - 3, applied.Untranslated);
        Assert.Empty(applied.Outdated);
        var line = (JObject)manifest["dialogues"]![0]!["nodes"]!.First(n => (int)n["id"]! == 1);
        Assert.Equal("¡Hola!\nBuen día.", (string?)line["text"]);
        // Sound cues still hear the pack's own words.
        Assert.Equal("Hello there!\nNice day.", (string?)line[PackTexts.OriginalTextKey]);
        Assert.Equal("Encuentra la concha", (string?)manifest["quests"]![0]!["title"]);
        Assert.Equal("Conchas: [PV:shells]", (string?)manifest.SelectToken("uis[0].nodes[0].children[0].text.value"));
        // Untouched: still the pack's.
        Assert.Equal("Cove", (string?)manifest["mapButtons"]![0]!["label"]);
    }

    [Fact]
    public void ATextThePackChangedAfterItWasTranslated_ShowsThePacksWordsUntilUpdated()
    {
        var pack = Pack();
        var translation = TextFile.Parse(
            "# en: Hello there!\\nNice day.\n" +
            "dialogue.beach.1 = ¡Hola!\\nBuen día.\n\n" +
            "# en: Find the old shell\n" +
            "quest.find.title = Encuentra la concha vieja\n\n" +
            "character.kiki.name = キキ\n");
        var manifest = Saved(pack);
        var applied = PackTexts.Apply(manifest, translation);

        Assert.Equal(new[] { "quest.find.title" }, applied.Outdated);
        Assert.Equal("Find the shell", (string?)manifest["quests"]![0]!["title"]);
        Assert.Equal("¡Hola!\nBuen día.", (string?)manifest["dialogues"]![0]!["nodes"]![0]!["text"]);
        // A line written with no note is taken at its word.
        Assert.Equal("キキ", (string?)manifest["characters"]![0]!["displayName"]);
    }

    [Fact]
    public void UpdatingAfterThePackChanged_KeepsTheWork_MarksTheChange_AndSetsAsideWhatIsGone()
    {
        var pack = Pack();
        var (path, _) = PackTranslations.CreateOrUpdate(pack, PackRoot, "es");
        File.WriteAllText(path, File.ReadAllText(path)
            .Replace("dialogue.beach.1 = Hello there!\\nNice day.", "dialogue.beach.1 = ¡Hola!\\nBuen día.")
            .Replace("quest.find.task.dig.name = Dig", "quest.find.task.dig.name = Cava"));

        // The line is rewritten and the dig task goes.
        pack.Dialogues[0].Nodes[0].Text = "Hello, friend!";
        pack.Quests[0].Tasks[0].Subtasks.Clear();
        var (again, existed) = PackTranslations.CreateOrUpdate(pack, PackRoot, "es");
        Assert.True(existed);
        string text = File.ReadAllText(again);
        _out.WriteLine(text);

        var file = Loc.Read(again)!;
        Assert.Equal("¡Hola!\nBuen día.", file.Get("dialogue.beach.1"));
        Assert.Equal("Hello there!\nNice day.", file.Find("dialogue.beach.1")!.ChangedFrom);
        Assert.Equal("Hello, friend!", file.Find("dialogue.beach.1")!.English);
        // Set aside, not thrown away - and not a mistake to report.
        Assert.Equal("Cava", file.Get("quest.find.task.dig.name"));
        Assert.Equal(TextFileWriter.UnusedHeading, file.Find("quest.find.task.dig.name")!.Heading);

        var check = PackTranslations.CheckAll(pack, PackRoot).Single();
        Assert.Equal("es", check.Code);
        Assert.DoesNotContain(check.Result.Findings, f => f.IsError);
        Assert.Contains(check.Result.Findings, f => f.Kind == TextCheck.Kind.NeedsReview && f.Key == "dialogue.beach.1");

        string report = TranslationFiles.Report(check.Result, pack: true);
        _out.WriteLine(report);
        Assert.Contains("what the pack says now", report);
        Assert.DoesNotContain("English", report);
    }

    [Fact]
    public void ADamagedKeyInAPackFileIsFoundAndPutBack()
    {
        var pack = Pack();
        var (path, _) = PackTranslations.CreateOrUpdate(pack, PackRoot, "es");
        File.WriteAllText(path, File.ReadAllText(path).Replace("quest.find.", "mision.find."));

        var check = PackTranslations.CheckAll(pack, PackRoot).Single();
        int unknown = check.Result.Of(TextCheck.Kind.Unknown);
        Assert.Equal(5, unknown);
        Assert.Equal(unknown, TranslationFiles.PutKeysBack(path, check.Result));
        Assert.DoesNotContain(PackTranslations.CheckAll(pack, PackRoot).Single().Result.Findings, f => f.IsError);
    }

    [Fact]
    public void ThePlayersLanguagePicksTheFile()
    {
        Assert.Equal("es", LanguageMatch.Best("es-MX", new[] { "es", "pt-BR" }));
        Assert.Equal("pt-BR", LanguageMatch.Best("pt-PT", new[] { "es", "pt-BR" }));
        Assert.Equal("pt-BR", LanguageMatch.Best(LanguageMatch.CodeOfSystemLanguage("Portuguese"), new[] { "pt-BR" }));
        Assert.Equal("zh-Hans", LanguageMatch.Best("zh-CN", new[] { "zh-Hant", "zh-Hans" }));
        Assert.Equal("zh-Hant", LanguageMatch.Best("zh-TW", new[] { "zh-Hant", "zh-Hans" }));
        Assert.Equal("zh-Hant", LanguageMatch.Best(LanguageMatch.CodeOfSystemLanguage("ChineseTraditional"), new[] { "zh-Hant", "zh-Hans" }));
        // A reader of one Chinese script is never handed the other.
        Assert.Null(LanguageMatch.Best("zh-TW", new[] { "zh-Hans" }));
        Assert.Null(LanguageMatch.Best("ja", new[] { "es", "ko" }));
        Assert.Equal("en", LanguageMatch.Best("en-GB", new[] { "en", "es" }));
        Assert.Null(LanguageMatch.CodeOfSystemLanguage("Unknown"));

        var files = PackTexts.Files(new[]
        {
            "modpack.json", "translations/es.txt", "translations/pt-BR.txt", "translations/es.txt.bak",
            "translations/notes/fr.txt", "translations/read me.txt", "art/translations/de.txt",
        });
        Assert.Equal(new[] { "es", "pt-BR" }, files.Keys.OrderBy(k => k));
    }
}
