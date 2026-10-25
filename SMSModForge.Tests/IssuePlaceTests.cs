using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Model;
using SMSModForge.Validation;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// An issue says where it is, and double-clicking it goes there (the author,
/// 2026-10-21: "'Hmph!' is not a scene in this pack" - double-clicking only
/// opened the dialogue, and nothing said which line or which action).
/// <para/>
/// Three things were wrong at once. Six checks wrote a node as
/// <c>nodes[26]</c> and two wrote a dialogue as <c>dialogues.Key</c>, where
/// going to an issue only understands <c>dialogues[Key].nodes[id=26]</c> -
/// so it stopped at the dialogue, or went nowhere. Six kinds of place had no
/// way to be gone to at all. And the check itself was wrong: changing an
/// action's type keeps the old type's values, and a CharacterFocus that had
/// been a Set-Active was warned about a scene it never reads.
/// </summary>
public sealed class IssuePlaceTests
{
    private readonly ITestOutputHelper _out;
    public IssuePlaceTests(ITestOutputHelper o) => _out = o;

    private static ModPack PackWith(params NodeActionDef[] onStart)
    {
        var pack = PackRepository.CreateEmpty("issues.pack");
        pack.Scenes.Add(new SceneDef { Key = "beach_kiss", DisplayName = "Beach kiss" });
        var d = new DialogueDef { Key = "naga_beach", DisplayName = "Naga at the beach" };
        var node = new DialogueNodeDef { Id = 26, Text = "Huff! One more <b>time</b>, then.", Actor = "player" };
        node.ActionsOnStart.AddRange(onStart);
        d.Nodes.Add(node);
        d.RootNodeIds.Add(26);
        pack.Dialogues.Add(d);
        return pack;
    }

    private static NodeActionDef Targeting(string type, string scene) => new()
    {
        Type = type,
        Params = { ["kind"] = "Scene", ["target"] = scene, ["active"] = "true", ["focused"] = "false" },
    };

    private static ValidationIssue[] SceneIssues(ModPack pack)
        => PackValidator.Validate(pack, Path.GetTempPath()).Where(i => i.Code == "action.unknownScene").ToArray();

    [Fact]
    public void AnActionIsOnlyCheckedForWhatItsTypeReads()
    {
        // The author's case, exactly: a CharacterFocus that used to be a
        // Set-Active, still carrying the old target. And the control: the
        // same values on a Set-Active must still be reported.
        Assert.Empty(SceneIssues(PackWith(Targeting(NodeActionTypes.CharacterFocus, "Hmph!"))));
        var real = Assert.Single(SceneIssues(PackWith(Targeting(NodeActionTypes.SetGameObjectActive, "Hmph!"))));
        _out.WriteLine(real.Where + " | " + real.Message);
        Assert.Equal("dialogues[naga_beach].nodes[id=26].actionsOnStart[0].SetGameObjectActive", real.Where);
        Assert.DoesNotContain("renaming one leaves references behind", real.Message);
    }

    [Fact]
    public void ThePlaceIsInTheAuthorsOwnWords()
    {
        var pack = PackWith(Targeting(NodeActionTypes.SetGameObjectActive, "Hmph!"));
        var issue = SceneIssues(pack).Single();
        string place = IssuePlace.Describe(issue.Where, pack);
        _out.WriteLine(place);
        Assert.Equal("Naga at the beach › “Huff! One more time, then.” › "
                     + SMSModForge.Localization.Loc.T("issue.place.onStart") + ": SetGameObjectActive", place);
    }

    [Fact]
    public void AnIssueIgnoredBeforeTheNamesChangedStaysIgnored()
    {
        // Ignore entries are written into packs. Before 1.8.0 this issue's
        // entry dropped the node, since its position was written [26].
        var pack = PackWith(Targeting(NodeActionTypes.SetGameObjectActive, "Hmph!"));
        Assert.Single(SceneIssues(pack));
        pack.IgnoredIssues.Add("action.unknownScene@dialogues[naga_beach].nodes.actionsOnStart.SetGameObjectActive");
        Assert.Empty(SceneIssues(pack));

        // And an entry in the new form names its node: another node's issue
        // of the same kind is still reported.
        var other = PackWith(Targeting(NodeActionTypes.SetGameObjectActive, "Hmph!"));
        other.IgnoredIssues.Add("action.unknownScene@dialogues[naga_beach].nodes[id=99].actionsOnStart.SetGameObjectActive");
        Assert.Single(SceneIssues(other));
    }

    [Fact]
    public void NoCheckWritesAPlaceTheJumpCannotRead()
    {
        // Every place a check writes starts group[...] - never group.{...}.
        foreach (var (file, group) in WrittenPlaces(dotForm: true))
            Assert.Fail($"{file} writes '{group}.' - write '{group}[...]', the form going to an issue reads");
    }

    [Fact]
    [Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
    public void EveryKindOfPlaceACheckWritesCanBeGoneTo()
    {
        var groups = WrittenPlaces(dotForm: false).Select(w => w.Group).Distinct().OrderBy(g => g).ToList();
        _out.WriteLine(string.Join(", ", groups));
        Assert.Contains("dialogues", groups);   // the scan is reading something

        WindowHarness.Run(window =>
        {
            var missing = groups.Where(g => !window.NavigateToIssue(new ValidationIssue(Severity.Warning, g + "[x]", "test")))
                                .ToList();
            WindowHarness.Pump();
            Assert.True(missing.Count == 0, "Nothing goes to: " + string.Join(", ", missing));
            // The control: a place nobody writes is not gone to.
            Assert.False(window.NavigateToIssue(new ValidationIssue(Severity.Warning, "nowhere[x]", "test")));
        });
    }

    [Fact]
    [Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
    public void DoubleClickingANodesActionSelectsThatNode()
    {
        var dir = Path.Combine(Path.GetTempPath(), "smsmodforge-issueplace-" + Guid.NewGuid().ToString("N"));
        try
        {
            WindowHarness.Run(window =>
            {
                var pack = PackWith(Targeting(NodeActionTypes.SetGameObjectActive, "Hmph!"));
                pack.Dialogues[0].Nodes.Insert(0, new DialogueNodeDef { Id = 3, Text = "First" });
                PackRepository.Save(pack, dir);
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(dir);
                WindowHarness.Pump();

                var issue = vm.Issues.Single(i => i.Code == "action.unknownScene");
                _out.WriteLine($"{issue.Place}  ({issue.Where})");
                Assert.Contains("Huff!", issue.Place);

                Assert.True(window.NavigateToIssue(issue));
                WindowHarness.Pump();
                WindowHarness.Pump();
                Assert.Equal("naga_beach", vm.SelectedDialogue?.Key);
                Assert.Equal(26, vm.SelectedNode?.Id);
            });
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    [Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
    public void AnIssueInATranslationOpensThatTranslation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "smsmodforge-issuelang-" + Guid.NewGuid().ToString("N"));
        PackRepository.Save(PackWith(), dir);
        try { WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(dir);   // a translation lives beside a pack
            WindowHarness.Pump();
            Assert.Equal("", vm.EditingLanguage);
            string code = vm.EditingLanguageOptions.Select(o => o.Code).First(c => c.Length > 0);
            window.NavigateToIssue(new ValidationIssue(Severity.Warning, "dialogues[x].nodes[id=1].text", "too long")
                                   { Language = code });
            WindowHarness.Pump();
            _out.WriteLine("now editing " + vm.EditingLanguage);
            Assert.Equal(code, vm.EditingLanguage, ignoreCase: true);
            vm.EditingLanguage = "";
        }); }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Theory]
    [InlineData("¡Hola!", true)]          // the {PC} is gone
    [InlineData("¡Hola, {PC}!", false)]   // the control: a translation that kept it
    public void ATranslationThatLostATokenIsAnIssueInThatLanguage(string spanish, bool reported)
    {
        var dir = Path.Combine(Path.GetTempPath(), "smsmodforge-lostcodes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pack = PackWith();
            pack.Dialogues[0].Nodes[0].Text = "Hi {PC}!";
            PackRepository.Save(pack, dir);
            var site = SMSModForge.Shared.PackTexts.Of(Newtonsoft.Json.Linq.JObject.Parse(PackRepository.SerializeAsSaved(pack)))
                .Single(s => s.Text == "Hi {PC}!");
            string file = SMSModForge.Localization.PackTranslations.PathOf(dir, "es");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "language.name = Español\n\n" + site.Key + " = " + spanish + "\n");

            var found = PackValidator.Validate(pack, dir).Where(i => i.Code == TranslationValidation.TranslationCodes).ToList();
            foreach (var i in found) _out.WriteLine($"{i.Language} | {i.Where} | {i.Message}");
            if (!reported) { Assert.Empty(found); return; }
            var issue = Assert.Single(found);
            Assert.Equal("es", issue.Language);
            Assert.Equal("dialogues[naga_beach].nodes[id=26].text", issue.Where);
            Assert.Contains("{PC}", issue.Message);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    /// <summary>Every place the checks write, by the group it starts with:
    /// <c>$"group[...</c>, or with <paramref name="dotForm"/> the old
    /// <c>$"group.{...</c> that going to an issue cannot read.</summary>
    private static (string File, string Group)[] WrittenPlaces(bool dotForm)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "SMSModForge", "Validation")))
            dir = dir.Parent;
        Assert.True(dir != null, "no SMSModForge/Validation above " + AppContext.BaseDirectory);
        var pattern = new Regex(dotForm ? @"\$""([a-z][A-Za-z]+)\.\{" : @"\$?""([a-z][A-Za-z]+)\[");
        return Directory.GetFiles(Path.Combine(dir!.FullName, "SMSModForge", "Validation"), "*.cs")
            .SelectMany(f => pattern.Matches(File.ReadAllText(f)).Select(m => (Path.GetFileName(f), m.Groups[1].Value)))
            .ToArray();
    }
}
