using System.Linq;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Whether a line fits the game's dialogue box, measured with the game's own
/// font against the box read off a running game (1010 x 125, 38 pt shrinking
/// to 28, and what does not fit at 28 runs out of the box).
/// </summary>
public sealed class DialogueFitTests
{
    private readonly ITestOutputHelper _out;
    public DialogueFitTests(ITestOutputHelper o) => _out = o;

    private static TmpFont Font()
    {
        Assert.True(VanillaUiLibrary.IsAvailable, "the game's extracted fonts should be beside the tests");
        var set = VanillaUiLibrary.Assets.Font(DialogueLook.FontName);
        Assert.NotNull(set?.Font);
        return set!.Font!;
    }

    private DialogueFit.Result Fit(string line)
    {
        var r = DialogueFit.Of(Font(), line);
        _out.WriteLine($"{r.Verdict} at {r.PointSize} pt, {r.Lines} line(s): {line.Length} characters");
        return r;
    }

    private const string Sentence = "I went down to the beach this morning and the water was warmer than I expected. ";

    [Fact]
    public void AShortLineFitsAtTheFullSize()
    {
        var r = Fit("Hello there!");
        Assert.Equal(DialogueFit.Verdict.Fits, r.Verdict);
        Assert.Equal(DialogueLook.BodyPointSize, r.PointSize);
    }

    [Fact]
    public void ALongerLineIsShrunkAndAVeryLongOneRunsOut()
    {
        // Growing the same text a sentence at a time, the verdicts must come in
        // order - fits, then shrinks (never growing again), then spills - and
        // all three must be reached, or the test proves nothing.
        var seen = new System.Collections.Generic.List<DialogueFit.Result>();
        for (int n = 1; n <= 8; n++) seen.Add(Fit(string.Concat(Enumerable.Repeat(Sentence, n)).Trim()));

        Assert.Contains(seen, r => r.Verdict == DialogueFit.Verdict.Fits);
        Assert.Contains(seen, r => r.Verdict == DialogueFit.Verdict.Shrinks);
        Assert.Contains(seen, r => r.Verdict == DialogueFit.Verdict.Spills);
        for (int i = 1; i < seen.Count; i++)
        {
            Assert.True(seen[i].Verdict >= seen[i - 1].Verdict, "a longer line should never fit better");
            Assert.True(seen[i].PointSize <= seen[i - 1].PointSize, "a longer line should never be typed bigger");
        }
        Assert.All(seen.Where(r => r.Verdict == DialogueFit.Verdict.Shrinks),
                   r => Assert.InRange(r.PointSize, DialogueLook.BodyMinPointSize, DialogueLook.BodyPointSize));
    }

    [Fact]
    public void MarkupIsNotMeasuredAsWords()
    {
        // A tag the player never sees must not make a line look longer.
        string plain = string.Concat(Enumerable.Repeat(Sentence, 2)).Trim();
        string tagged = "<color=#FF6666>" + plain + "</color>";
        Assert.Equal(Fit(plain), Fit(tagged));
    }

    [Fact]
    public void LettersTheGamesFontLacksAreNotGuessedAt()
    {
        Assert.Equal(DialogueFit.Verdict.CannotTell, Fit("这是一句很长的中文台词。").Verdict);
    }

    // ── Where the author hears about it ───────────────────────────────

    private static string Long => string.Concat(Enumerable.Repeat(Sentence, 6)).Trim();

    [Fact]
    public void ThePacksCheckWarnsAboutALineThatRunsOutOfTheBox()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "smsmodforge-fit-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var pack = Model.PackRepository.CreateEmpty("fit.pack");
            var d = new Model.DialogueDef { Key = "beach" };
            d.Nodes.Add(new Model.DialogueNodeDef { Id = 1, Text = Long });
            d.Nodes.Add(new Model.DialogueNodeDef { Id = 2, Text = "Short." });
            // The control: a choice's options are drawn in a box of their own.
            d.Nodes.Add(new Model.DialogueNodeDef { Id = 3, Kind = Model.DialogueNodeKind.Choice, Text = Long });
            pack.Dialogues.Add(d);
            Model.PackRepository.Save(pack, dir);

            // A Spanish line that runs long where the English did not.
            var es = new Shared.TextFile();
            es.Add(new Shared.TextFile.Entry { Key = "dialogue.beach.2", Text = Long, English = "Short." });
            Localization.PackTranslations.Write(pack, dir, "es", Localization.PackTranslations.Source(pack, es), es);

            var issues = Validation.PackValidator.Validate(pack, dir)
                .Where(i => i.Code == Validation.TranslationValidation.LineTooLong).ToList();
            foreach (var i in issues) _out.WriteLine($"{i.Where}: {i.Message}");

            Assert.Equal(2, issues.Count);
            Assert.Contains(issues, i => i.Where == "dialogues[beach].nodes[id=1].text"
                                         && i.Message.Contains(Localization.TranslationFiles.NativeName("en")!));
            Assert.Contains(issues, i => i.Where == "dialogues[beach].nodes[id=2].text"
                                         && i.Message.Contains(Localization.TranslationFiles.NativeName("es")!));
            Assert.DoesNotContain(issues, i => i.Where.Contains("id=3"));
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { }
        }
    }

    [Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
    [Fact]
    public void TheNoteUnderTheTextBoxFollowsTheLine()
    {
        WindowHarness.Run(window =>
        {
            var vm = (ViewModel.MainViewModel)window.DataContext;
            vm.AddDialogueCommand.Execute(null);
            vm.AddDialogueRootNodeCommand.Execute(null);
            WindowHarness.Pump();
            var node = vm.SelectedNode!;

            node.Text = "Hello there!";
            Assert.Equal("", vm.SelectedNodeFitNote);

            node.Text = string.Concat(Enumerable.Repeat(Sentence, 2)).Trim();
            _out.WriteLine(vm.SelectedNodeFitNote);
            Assert.NotEqual("", vm.SelectedNodeFitNote);
            Assert.False(vm.SelectedNodeFitSpills);

            node.Text = Long;
            _out.WriteLine(vm.SelectedNodeFitNote);
            Assert.True(vm.SelectedNodeFitSpills);
        });
    }

    [Fact]
    public void TheBoxIsTheOneMeasuredInTheGame()
    {
        Assert.Equal(1010, DialogueFit.BoxWidth);
        Assert.Equal(125, DialogueFit.BoxHeight);
        Assert.Equal(DialogueFit.BoxWidth, DialogueLook.WrapWidth);
    }
}
