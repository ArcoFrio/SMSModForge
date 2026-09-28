using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Editing a pack in one of its translations.
/// <para/>
/// Switching the editor to a language puts that language's words into every
/// field a player reads, and editing one edits the translation. The danger is
/// all in what ELSE could change: the game finds characters, lines, quests,
/// buttons and screens by keys, ids and targets, and a switch that touched one
/// of those would not look wrong - it would stop something being found, in one
/// language, in a way nobody sees until a player reports it.
/// <para/>
/// So most of these compare the whole saved manifest before and after, leaf by
/// leaf, rather than checking the fields someone thought to check.
/// </summary>
public sealed class LanguageSessionTests
{
    private readonly ITestOutputHelper _out;
    public LanguageSessionTests(ITestOutputHelper o) => _out = o;

    /// <summary>One of every kind of text a player reads.</summary>
    private static ModPack Pack()
    {
        var pack = new ModPack { PackId = "language.test" };
        pack.Characters.Add(new CharacterDef { Key = "kiki", DisplayName = "Kiki" });
        pack.Actors.Add(new ActorDef { Key = "narrator", DisplayName = "Narrator" });

        var dialogue = new DialogueDef { Key = "beach", DisplayName = "At the beach" };
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "kiki", Text = "Hello there!" });
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 2, Actor = "kiki", Text = "You have <b>[PV:shells]</b> shells, {name}." });
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 3, Kind = DialogueNodeKind.Random, Text = "" });
        pack.Dialogues.Add(dialogue);

        var quest = new QuestDef { Key = "find", Title = "Find the shell", Description = "Somewhere on the sand." };
        var task = new QuestTaskDef { Key = "look", Name = "Look around", QuestDescription = "You looked." };
        task.Subtasks.Add(new QuestTaskDef { Key = "dig", Name = "Dig", CountTo = 3 });
        quest.Tasks.Add(task);
        pack.Quests.Add(quest);

        var place = new PlaceDef { Key = "cove", DisplayName = "Cove" };
        place.NavigatorButtons.Add(new NavigatorButtonDef { Target = "vanilla:Beach", Label = "Back to the beach" });
        place.NavigatorButtons.Add(new NavigatorButtonDef { Target = "vanilla:Beach", Label = "The other way to the beach" });
        pack.Places.Add(place);
        pack.MapButtons.Add(new MapButtonDef { District = "Seaside", Target = "pack:language.test.cove", Label = "Cove" });

        var ui = new UiDef { Id = "hud", Name = "Shell counter" };
        var panel = new UiNodeDef { Name = "Panel" };
        panel.Children.Add(new UiNodeDef { Name = "Label", Text = new UiTextDef { Value = "Shells: [PV:shells]" } });
        ui.Nodes.Add(panel);
        pack.Uis.Add(ui);
        return pack;
    }

    private static JObject Saved(ModPack pack) => JObject.Parse(PackRepository.SerializeAsSaved(pack));

    /// <summary>A Spanish translation of every text the pack has, marked so
    /// it can be told from the pack's own words at a glance.</summary>
    private static TextFile Spanish(ModPack pack)
    {
        var file = new TextFile();
        foreach (var site in PackTexts.Of(Saved(pack)))
            file.Add(new TextFile.Entry { Key = site.Key, Text = "ES " + site.Text, English = site.Text });
        return file;
    }

    /// <summary>Every leaf path at which the two differ.</summary>
    private static List<string> Differences(JToken a, JToken b)
    {
        var found = new List<string>();
        void Walk(JToken? x, JToken? y, string path)
        {
            if (x is JObject ox && y is JObject oy)
            {
                foreach (var name in ox.Properties().Select(p => p.Name).Union(oy.Properties().Select(p => p.Name)))
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

    // ── Finding the texts ────────────────────────────────────────────

    [Fact]
    public void EveryTextThePackHasIsFoundOnTheModel()
    {
        // The finding is checked against the saved text as it goes, so a
        // mistake cannot put words in the wrong place - but it CAN leave a text
        // unshown, silently, as a field that simply stays in English. This
        // counts them.
        var pack = Pack();
        var slots = LanguageSession.Slots(pack, out int unmatched);
        foreach (var s in slots) _out.WriteLine($"{s.Key}  ->  {s.Holder.GetType().Name}.{s.Field}");

        Assert.Equal(0, unmatched);
        // Empty fields too: a line typed only in a translation has nothing in
        // the pack, and must still be found to be shown and written.
        Assert.Equal(PackTexts.Of(Saved(pack), withEmpty: true).Count, slots.Count);
        Assert.True(PackTexts.Of(Saved(pack)).Count < slots.Count);   // the control: there are empty ones
    }

    [Fact]
    public void TwoButtonsToTheSamePlaceAreTwoTexts()
    {
        // Numbered in the order they are met, on both sides. If the two walks
        // disagreed about the order, the second button would get the first
        // one's words and the check would catch it as a mismatch - but they
        // must not disagree in the first place.
        var pack = Pack();
        var slots = LanguageSession.Slots(pack, out _)
                                   .Where(s => s.Kind == PackTexts.Kind.NavigatorLabel).ToList();

        Assert.Equal(2, slots.Count);
        Assert.Equal("Back to the beach", slots[0].Get());
        Assert.Equal("The other way to the beach", slots[1].Get());
        Assert.NotEqual(slots[0].Key, slots[1].Key);
    }

    // ── What entering a language changes ─────────────────────────────

    [Fact]
    public void EnteringALanguageChangesItsWordsAndNothingElse()
    {
        // The test the feature exists to pass. Every difference between the
        // pack in its own words and the pack shown in Spanish has to be one of
        // the texts a player reads - not a key, not an id, not a target, not
        // the name of an object the game looks for.
        var pack = Pack();
        var before = Saved(pack);

        var session = LanguageSession.Enter(pack, "es", Spanish(pack));
        var during = Saved(pack);

        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var site in PackTexts.Of(during))
            texts.Add((site.Holder.Path.Length == 0 ? "" : site.Holder.Path + ".") + site.Field);

        var differences = Differences(before, during);
        foreach (var d in differences) _out.WriteLine((texts.Contains(d) ? "  text   " : "  OTHER  ") + d);

        Assert.Equal(0, session.Unmatched);
        Assert.Equal(texts.Count, differences.Count);          // the control: every text did change
        Assert.All(differences, d => Assert.True(texts.Contains(d), d + " changed and is not a text"));
    }

    [Fact]
    public void EnteringAndLeavingWithoutEditingChangesNothing()
    {
        // Looking at a pack in Spanish must not be an edit. If this left a
        // single byte different, every switch of language would mark the pack
        // unsaved and put a diff in somebody's history they did not make.
        var pack = Pack();
        string before = PackRepository.SerializeAsSaved(pack);

        var session = LanguageSession.Enter(pack, "es", Spanish(pack));
        session.Leave(pack, Spanish(pack));

        Assert.Equal(before, PackRepository.SerializeAsSaved(pack));
    }

    // ── What editing in a language does ──────────────────────────────

    [Fact]
    public void EditingALineInALanguageEditsTheTranslationAndNotThePack()
    {
        var pack = Pack();
        var session = LanguageSession.Enter(pack, "es", Spanish(pack));

        var line = pack.Dialogues[0].Nodes[0];
        Assert.Equal("ES Hello there!", line.Text);
        line.Text = "¡Hola, qué tal!";

        var file = session.Leave(pack, Spanish(pack));

        Assert.Equal("Hello there!", line.Text);                         // the pack keeps its own words
        Assert.Equal("¡Hola, qué tal!", file.Translated("dialogue.beach.1"));
        Assert.Equal("Hello there!", file.Find("dialogue.beach.1")!.English);   // and remembers what from
    }

    [Fact]
    public void ChangingSomethingThatIsNotAWordChangesItForEveryLanguage()
    {
        // Who says a line is not a translation - it is the pack. Changed while
        // looking at the Spanish, it is changed, full stop.
        var pack = Pack();
        pack.Characters.Add(new CharacterDef { Key = "mira", DisplayName = "Mira" });
        var session = LanguageSession.Enter(pack, "es", Spanish(pack));

        pack.Dialogues[0].Nodes[0].Actor = "mira";
        session.Leave(pack, Spanish(pack));

        Assert.Equal("mira", pack.Dialogues[0].Nodes[0].Actor);
        Assert.Equal("Hello there!", pack.Dialogues[0].Nodes[0].Text);
    }

    [Fact]
    public void RenamingADialogueInALanguageKeepsThePacksWordsAndMovesTheTranslation()
    {
        // Why the pack's own words are remembered by object rather than by key.
        // Keyed by key, a rename would lose track of every line under it, and
        // the Spanish would be left as the pack's words - in English packs,
        // for English players.
        var pack = Pack();
        var session = LanguageSession.Enter(pack, "es", Spanish(pack));

        pack.Dialogues[0].Key = "shore";
        var file = session.Leave(pack, Spanish(pack));

        foreach (var n in pack.Dialogues[0].Nodes.Where(n => n.Text.Length > 0))
            _out.WriteLine($"{n.Id}: '{n.Text}'");
        Assert.Equal("Hello there!", pack.Dialogues[0].Nodes[0].Text);
        Assert.Equal("ES Hello there!", file.Translated("dialogue.shore.1"));
    }

    [Fact]
    public void ALineAddedInALanguageBelongsToThatLanguageOnly()
    {
        // Decided by the author (2026-09-24). Structure is shared - the line
        // exists in the pack, for every language - but its words are the
        // Spanish that was typed, and they are Spanish's alone. Writing them
        // into the pack would put Spanish in front of every English player.
        var pack = Pack();
        var session = LanguageSession.Enter(pack, "es", Spanish(pack));

        pack.Dialogues[0].Nodes.Add(new DialogueNodeDef { Id = 9, Actor = "kiki", Text = "Una línea nueva." });
        var file = session.Leave(pack, Spanish(pack));

        var line = pack.Dialogues[0].Nodes.SingleOrDefault(n => n.Id == 9);
        Assert.NotNull(line);                                        // it is in the pack
        Assert.Equal("", line!.Text);                                // with no words of the pack's own
        Assert.Equal("Una línea nueva.", file.Translated("dialogue.beach.9"));
        Assert.Equal("", file.Find("dialogue.beach.9")!.English);   // translated from nothing

        // Nothing for a machine to translate FROM.
        var missing = PackTranslationJob.Missing(PackTranslations.Source(pack, file), file);
        Assert.DoesNotContain(missing, m => m.Key == "dialogue.beach.9");
    }

    [Fact]
    public void AFieldThatWasEmptyGetsItsWordsInTheLanguageOnly()
    {
        // Not only new objects: the subtask "dig" has no journal paragraph,
        // and one typed while looking at the Spanish is the Spanish's.
        var pack = Pack();
        var dig = pack.Quests[0].Tasks[0].Subtasks[0];
        Assert.True(string.IsNullOrEmpty(dig.QuestDescription));
        var session = LanguageSession.Enter(pack, "es", Spanish(pack));

        dig.QuestDescription = "Cavaste.";
        var file = session.Leave(pack, Spanish(pack));

        Assert.True(string.IsNullOrEmpty(dig.QuestDescription));
        Assert.Equal("Cavaste.", file.Translated("quest.find.task.dig.questDescription"));
    }

    [Fact]
    public void ThePackInItsOwnWordsHasTheNewLineEmpty()
    {
        // What saving, the unsaved check and undo all read. The control is the
        // same line outside the scope, still showing what was typed.
        var pack = Pack();
        var session = LanguageSession.Enter(pack, "es", Spanish(pack));
        var added = new DialogueNodeDef { Id = 9, Actor = "kiki", Text = "Una línea nueva." };
        pack.Dialogues[0].Nodes.Add(added);

        using (session.OwnWords(pack)) Assert.Equal("", added.Text);
        Assert.Equal("Una línea nueva.", added.Text);
        Assert.True(session.Unsaved);
    }

    [Fact]
    public void ANewTextNeverTakesTheKeyOfOneThatHasWords()
    {
        // Two buttons to one level are told apart by order. A third, added
        // FIRST in the list while looking at the Spanish, has no words of the
        // pack's own - so it is keyed after the other two, as the game keys it.
        // Keyed by what the language showed, it would have taken the first
        // button's key and filed every translation one button along.
        var pack = Pack();
        var session = LanguageSession.Enter(pack, "es", Spanish(pack));
        pack.Places[0].NavigatorButtons.Insert(0,
            new NavigatorButtonDef { Target = "vanilla:Beach", Label = "Otra vez a la playa" });

        var file = session.Leave(pack, Spanish(pack));
        var keys = PackTexts.Of(Saved(pack), withEmpty: true)
                            .Where(s => s.Kind == PackTexts.Kind.NavigatorLabel).Select(s => s.Key).ToList();
        foreach (var k in keys) _out.WriteLine(k + " = " + file.Translated(k));

        Assert.Equal("ES Back to the beach", file.Translated("navigator.cove.vanilla_Beach.label"));
        Assert.Equal("ES The other way to the beach", file.Translated("navigator.cove.vanilla_Beach-2.label"));
        Assert.Equal("Otra vez a la playa", file.Translated("navigator.cove.vanilla_Beach-3.label"));
    }

    [Fact]
    public void AnUndoStepKeepsALineTypedOnlyInTheLanguage()
    {
        var pack = Pack();
        var session = LanguageSession.Enter(pack, "es", Spanish(pack));
        pack.Dialogues[0].Nodes.Add(new DialogueNodeDef { Id = 9, Actor = "kiki", Text = "Una línea nueva." });

        string own;
        using (session.OwnWords(pack)) own = PackRepository.Serialize(pack);
        string step = LanguageSession.Wrap("es", session.Shown(pack), own);

        Assert.True(LanguageSession.Unwrap(step, out var code, out var shown, out var ownBack));
        var restored = PackRepository.Deserialize(ownBack)!;
        Assert.Equal("", restored.Dialogues[0].Nodes.Single(n => n.Id == 9).Text);

        LanguageSession.Enter(restored, code, shown);
        Assert.Equal("Una línea nueva.", restored.Dialogues[0].Nodes.Single(n => n.Id == 9).Text);
    }

    // ── What a language shows ────────────────────────────────────────

    [Fact]
    public void ATranslationOfWordsThePackNoLongerSaysShowsThePacksWords()
    {
        // The runtime's rule, and the right one for editing too: the author
        // sees what the line says NOW, not a translation of something it used
        // to say, and can translate that.
        var pack = Pack();
        var spanish = Spanish(pack);
        pack.Dialogues[0].Nodes[0].Text = "Hi, it's been a while.";

        LanguageSession.Enter(pack, "es", spanish);
        Assert.Equal("Hi, it's been a while.", pack.Dialogues[0].Nodes[0].Text);
        Assert.Equal("ES Shells: [PV:shells]", pack.Uis[0].Nodes[0].Children[0].Text!.Value);
    }

    [Fact]
    public void ALanguageWithNoFileShowsThePacksOwnWords()
    {
        var pack = Pack();
        string before = PackRepository.SerializeAsSaved(pack);

        var session = LanguageSession.Enter(pack, "ja", null);
        Assert.Equal(before, PackRepository.SerializeAsSaved(pack));

        var file = session.Leave(pack, null);
        Assert.Equal(before, PackRepository.SerializeAsSaved(pack));

        // And the file it hands back holds every text, untranslated - the same
        // as one started by hand, so nothing downstream treats it differently.
        Assert.Equal(PackTexts.Of(Saved(pack)).Count, file.Entries.Count);
    }
}
