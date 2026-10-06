using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Transitions action (the author, 1.7.0): a transition picked by style,
/// on screen for as long as asked - where it used to take one signal to go in
/// and another, timed by hand, to come out - and a black screen with any words
/// on it, like the game's "A few minutes later...".
/// </summary>
public sealed class TransitionsActionTests
{
    private readonly ITestOutputHelper _out;
    public TransitionsActionTests(ITestOutputHelper o) => _out = o;

    // ── What it offers ───────────────────────────────────────────────────

    [Fact]
    public void ItIsOfferedAndItsStylesAreOnlyRealTransitions()
    {
        Assert.Contains(NodeActionTypes.Transitions, NodeActionTypes.All);
        Assert.Equal("Transitions", NodeActionTypes.Transitions);

        // The signals it plays are the game's transitions...
        var signals = Transitions.Styles.Select(Transitions.InSignal).Concat(Transitions.Styles.Select(Transitions.OutSignal))
                                 .Where(s => s != null).ToList();
        _out.WriteLine(string.Join(", ", signals));
        Assert.Equal(new[] { "Blink", "FadeIn2025", "FadeInBlack", "FadeOut2025", "FadeOutBlack", "whiteflashnosound" },
                     signals.OrderBy(x => x, StringComparer.Ordinal));
        // ...and none of the moments that are not one.
        foreach (string notOne in new[] { "kiss", "drink", "flash", "FadeUI", "ForceEnableUI" })
            Assert.DoesNotContain(notOne, signals);
    }

    [Fact]
    public void EmitSignalNoLongerOffersTheTransitionsOrTheDialoguesOwnSignals()
    {
        foreach (string gone in new[] { "DialogueStart", "DialogueEnd", "FadeInBlack", "FadeOutBlack",
                                        "FadeIn2025", "FadeOut2025", "Blink", "whiteflashnosound" })
            Assert.DoesNotContain(gone, VanillaSignals.All);
        foreach (string kept in new[] { "kiss", "drink", "flash", "FadeUI", "ForceEnableUI" })
            Assert.Contains(kept, VanillaSignals.All);
    }

    // ── The row ──────────────────────────────────────────────────────────

    private static (NodeActionDef Def, NodeActionViewModel Row) NewRow()
    {
        var def = new NodeActionDef { Type = NodeActionTypes.Transitions };
        return (def, new NodeActionViewModel(def));
    }

    private static ParamRowViewModel Field(NodeActionViewModel row, string key)
        => row.ParamRows.Single(r => r.Schema.Key == key);

    [Fact]
    public void TheTextFieldIsThereOnlyForTheScreenWithWords_AndTheTimeOnlyWhereItCanBeSet()
    {
        var (_, row) = NewRow();
        var style = Field(row, Transitions.StyleParam);
        var seconds = Field(row, Transitions.SecondsParam);
        var text = Field(row, Transitions.TextParam);

        // Untouched: a fade to black, which takes a time and has no words.
        Assert.True(seconds.IsShown);
        Assert.False(text.IsShown);

        style.Value = Transitions.TextScreen;
        Assert.True(text.IsShown);
        Assert.True(seconds.IsShown);

        style.Value = Transitions.Blink;
        Assert.False(text.IsShown);
        Assert.False(seconds.IsShown);   // the game's blink runs its own course

        style.Value = Transitions.WhiteFlash;
        Assert.False(seconds.IsShown);
    }

    [Fact]
    public void PickingTheScreenWithWordsWritesTheGamesWordsDown_SoTheyCanBeTranslated()
    {
        var (def, row) = NewRow();
        Assert.False(def.Params.ContainsKey(Transitions.TextParam));
        Field(row, Transitions.StyleParam).Value = Transitions.TextScreen;
        Assert.Equal("A few minutes later...", def.Params[Transitions.TextParam]);

        // Typed over, it stays typed over.
        Field(row, Transitions.TextParam).Value = "That night...";
        Field(row, Transitions.StyleParam).Value = Transitions.FadeToBlack;
        Field(row, Transitions.StyleParam).Value = Transitions.TextScreen;
        Assert.Equal("That night...", def.Params[Transitions.TextParam]);
    }

    // ── Its words in the pack's translations ─────────────────────────────

    private static ModPack PackWithScreen(string words)
    {
        var pack = PackRepository.CreateEmpty("transitions.pack");
        var d = new DialogueDef { Key = "chat" };
        var node = new DialogueNodeDef { Id = 1, Text = "hi" };
        var transition = new NodeActionDef { Type = NodeActionTypes.Transitions };
        transition.Params[Transitions.StyleParam] = Transitions.TextScreen;
        transition.Params[Transitions.TextParam] = words;
        node.ActionsOnStart.Add(transition);
        d.Nodes.Add(node);
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);

        var rule = new UpdateRuleDef { Key = "sleep" };
        var night = new NodeActionDef { Type = NodeActionTypes.Transitions };
        night.Params[Transitions.StyleParam] = Transitions.TextScreen;
        night.Params[Transitions.TextParam] = "The next morning...";
        rule.Actions.Add(night);
        pack.IntegrationRules.Add(rule);
        return pack;
    }

    [Fact]
    public void TheWordsAreAmongThePacksTextsToTranslate()
    {
        var pack = PackWithScreen("Hours later...");
        var sites = PackTexts.Of(JObject.Parse(PackRepository.SerializeAsSaved(pack)))
                             .Where(s => s.Kind == PackTexts.Kind.TransitionText).ToList();
        foreach (var s in sites) _out.WriteLine($"{s.Key} = {s.Text}");
        Assert.Contains(sites, s => s.Key == "dialogue.chat.1.transition" && s.Text == "Hours later...");
        Assert.Contains(sites, s => s.Key == "rule.sleep.transition" && s.Text == "The next morning...");

        // And the editor finds every one of them on the pack itself, so they
        // can be edited in a language like any other words.
        var slots = LanguageSession.Slots(pack, out int unmatched);
        Assert.Equal(0, unmatched);
        Assert.Contains(slots, s => s.Key == "dialogue.chat.1.transition");
    }

    [Fact]
    public void TheGameReadsTheWordsInThePlayersLanguage()
    {
        var pack = PackWithScreen("Hours later...");
        var json = JObject.Parse(PackRepository.SerializeAsSaved(pack));
        var es = new TextFile();
        es.Add(new TextFile.Entry { Key = "dialogue.chat.1.transition", Text = "Horas después...", English = "Hours later..." });

        PackTexts.Apply(json, es);

        string after = (string)json["dialogues"]![0]!["nodes"]![0]!["actionsOnStart"]![0]!["params"]![Transitions.TextParam]!;
        _out.WriteLine(after);
        Assert.Equal("Horas después...", after);
    }

    [Fact]
    public void RenamingAVariableReachesTheWords()
    {
        var pack = PackWithScreen("[PV:days] days later...");
        int n = Services.VariableRenamer.RenameReferences(pack, "days", "daysPassed");
        Assert.Equal(1, n);
        Assert.Equal("[PV:daysPassed] days later...",
                     pack.Dialogues[0].Nodes[0].ActionsOnStart[0].Params[Transitions.TextParam]);
    }

    // ── The game's side, read from its source ────────────────────────────

    private static string Plugin(string file)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "SMSModForge.PackPlugin", file)))
            root = Path.GetDirectoryName(root)!;
        return File.ReadAllText(Path.Combine(root, "SMSModForge.PackPlugin", file));
    }

    [Fact]
    public void WaitAndTransitionsHoldTheRestOfTheList_OnTheRealClock_NotOnACoroutine()
    {
        string runtime = Plugin("ActionRuntime.cs");
        Assert.DoesNotContain("WaitCoroutine", runtime);
        Assert.Contains("ActionSequence.Resume(actions, i + 1, ctx, wait)", runtime);
        Assert.Contains("_hold = TransitionRuntime.Play(", runtime);

        string sequence = Plugin("ActionSequence.cs");
        Assert.Contains("Time.unscaledTime", sequence);
        Assert.DoesNotContain("StartCoroutine", sequence);
        Assert.Contains("ActionSequence.Tick()", Plugin("Plugin.cs"));

        string transitions = Plugin("TransitionRuntime.cs");
        Assert.Contains("Time.unscaledTime", transitions);
        Assert.DoesNotContain("StartCoroutine", transitions);
    }
}
