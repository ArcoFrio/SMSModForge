using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The game's words in braces - {PC} and the rest - and a pack's [PV:name] are
/// filled in wherever a pack's words are shown, not only in dialogue lines
/// (2026-09-27): quest titles, descriptions and tasks, speakers' names, button
/// labels, the text on a pack's screens.
/// <para/>
/// The filling-in happens in the game plugin, which this suite cannot start,
/// so its half is checked by reading its source, the way the other plugin
/// checks here are. The editor's half runs: a variable renamed has to be
/// renamed in every one of those texts, or they go on naming one that is gone.
/// </summary>
public sealed class TokensEverywhereTests
{
    private readonly ITestOutputHelper _out;
    public TokensEverywhereTests(ITestOutputHelper o) => _out = o;

    // ── The editor: a rename reaches every text a player reads ──────────

    private static ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("tokens.pack");
        pack.Variables.Add(new PackVariableDef { Name = "coins", Type = PackVariableType.Int });
        pack.Characters.Add(new CharacterDef { Key = "clerk", DisplayName = "Clerk ([PV:coins] coins)" });
        pack.Quests.Add(new QuestDef
        {
            Key = "savings",
            Title = "Save [PV:coins] coins",
            Description = "You have [PV:coins].",
            Tasks = { new QuestTaskDef { Key = "count", Name = "Count to [PV:coins]" } },
        });
        pack.Uis.Add(new UiDef
        {
            Id = "wallet01", Name = "Wallet",
            Nodes = { new UiNodeDef { Name = "Label", Text = new UiTextDef { Value = "Coins: [PV:coins]" } } },
        });
        return pack;
    }

    [Fact]
    public void RenamingAVariableRenamesItInEveryTextAPlayerReads()
    {
        var pack = Pack();
        int n = VariableRenamer.RenameReferences(pack, "coins", "money");
        _out.WriteLine(n + " references rewritten");

        Assert.Equal("Clerk ([PV:money] coins)", pack.Characters.First(c => c.Key == "clerk").DisplayName);
        var quest = pack.Quests[0];
        Assert.Equal("Save [PV:money] coins", quest.Title);
        Assert.Equal("You have [PV:money].", quest.Description);
        Assert.Equal("Count to [PV:money]", quest.Tasks[0].Name);
        Assert.Equal("Coins: [PV:money]", pack.Uis[0].Nodes[0].Text!.Value);
        Assert.True(n >= 5, n + " references for five texts");
    }

    [Fact]
    public void FindingAVariablesUsesListsThoseTextsToo()
    {
        var where = VariableRenamer.FindReferences(Pack(), "coins");
        _out.WriteLine(string.Join("\n", where));

        Assert.Contains(Localization.Loc.F("walk.character", "name", "clerk"), where);
        Assert.Contains(Localization.Loc.F("walk.quest", "name", "savings"), where);
        Assert.Contains(Localization.Loc.F("walk.questTask", "name", "savings", "task", "count"), where);
        Assert.Contains(Localization.Loc.F("walk.ui", "name", "Wallet"), where);
        Assert.Equal(where.Count, where.Distinct().Count());
    }

    /// <summary>The packs to compare on: the one above, the checkout's sample
    /// pack, and a real pack when SMSMODFORGE_PERF_PACK names a copy of one.</summary>
    private static System.Collections.Generic.IEnumerable<(string Name, ModPack Pack)> Packs()
    {
        yield return ("tokens", Pack());
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SamplePack", "modpack.json"))) dir = dir.Parent;
        if (dir != null) yield return ("SamplePack", PackRepository.Load(Path.Combine(dir.FullName, "SamplePack")));
        string? real = Environment.GetEnvironmentVariable("SMSMODFORGE_PERF_PACK");
        if (!string.IsNullOrWhiteSpace(real) && File.Exists(Path.Combine(real, "modpack.json")))
            yield return ("real", PackRepository.Load(real));
    }

    [Fact]
    public void TheQuickLookFindsExactlyWhatGatheringEveryTextFound()
    {
        // 1.9.0: a variable's uses only gather the pack's other texts (which
        // writes the whole pack out) when one of them holds its token. The
        // uses, the references and a rename must come out exactly as when they
        // were always gathered - for every variable, and a name nobody uses.
        foreach (var (label, pack) in Packs())
        {
            var names = pack.Variables.Select(v => v.Name).Append("nosuchvariable").ToList();
            int shown = 0;
            foreach (string name in names)
            {
                string Uses() => string.Join("\n", VariableRenamer.Uses(pack, name).Select(u => $"{u.Kind}|{u.Where}|{u.Path}"))
                                 + "\n--\n" + string.Join("\n", VariableRenamer.FindReferences(pack, name));
                string quick = Uses();
                VariableRenamer.GatherOtherTextsAlways = true;
                string full;
                try { full = Uses(); }
                finally { VariableRenamer.GatherOtherTextsAlways = false; }
                Assert.Equal(full, quick);
                if (quick.Contains(nameof(VariableRenamer.UseKind.Shows))) shown++;
            }
            _out.WriteLine($"{label}: {names.Count} names agree, {shown} shown in some text");
        }

        // A rename too, on two copies of the small pack.
        var a = Pack();
        var b = Pack();
        int quickCount = VariableRenamer.RenameReferences(a, "coins", "money");
        VariableRenamer.GatherOtherTextsAlways = true;
        int fullCount;
        try { fullCount = VariableRenamer.RenameReferences(b, "coins", "money"); }
        finally { VariableRenamer.GatherOtherTextsAlways = false; }
        Assert.Equal(fullCount, quickCount);
        Assert.Equal(PackRepository.SerializeAsSaved(b), PackRepository.SerializeAsSaved(a));

        // The control: a token in a text the quick look reads is found by it
        // (the uses list a quest's text), so it is not skipping everything.
        Assert.Contains(VariableRenamer.Uses(Pack(), "coins"), u => u.Kind == VariableRenamer.UseKind.Shows
                                                                && u.Where == Localization.Loc.F("walk.quest", "name", "savings"));
    }

    // ── The game plugin: every text it hands the game fills them in ─────

    private static string PluginSource(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "SMSModForge.PackPlugin", name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("the plugin's source is not beside the tests, so nothing here is checked", name);
    }

    [Fact]
    public void TheGamesWordsAreFilledInFromTheGamesOwnList_DressedAsInALine()
    {
        string text = PluginSource("GameTextTokens.cs");
        // The list the game's dialogue fills them from, and the same dressing.
        Assert.Contains("TRepository<DialogueRepository>.Get.Values.Get", text);
        Assert.Contains("value.InBold", text);
        Assert.Contains("value.InItalic", text);
        Assert.Contains("value.UseColor", text);
        Assert.Contains("<Compile Include=\"GameTextTokens.cs\" />", PluginSource("SMSModForge.PackPlugin.csproj"));
    }

    [Fact]
    public void QuestTextsAndSpeakersNamesAreHandedOverThroughTheFillingGetter()
    {
        string quests = PluginSource("QuestRegistry.cs");
        Assert.Contains("Set(quest, \"m_Title\", GetStringPackText.For(spec.Title, spec.PackId));", quests);
        Assert.Contains("Set(quest, \"m_Description\", GetStringPackText.For(spec.Description, spec.PackId));", quests);
        Assert.Contains("Set(task, \"m_Name\", GetStringPackText.For(spec.Name, spec.PackId));", quests);
        Assert.DoesNotContain("new PropertyGetString(spec.", quests);
        Assert.DoesNotContain("new PropertyGetString(text", quests);

        Assert.Contains("GetStringPackText.For(displayName, _packId)", PluginSource("RuntimeActorFactory.cs"));
    }

    [Theory]
    [InlineData("NavigatorRuntime.cs")]
    [InlineData("RadialButtonRuntime.cs")]
    [InlineData("UiFactory.cs")]
    [InlineData("UiLiveText.cs")]
    public void LabelsAndScreensFillInBothKinds(string file)
    {
        string text = PluginSource(file);
        Assert.Contains("TextPlaceholders.ResolveAll(", text);
        Assert.DoesNotContain("TextPlaceholders.Resolve(", text);
        Assert.DoesNotContain("TextPlaceholders.HasAny(", text);
    }

    [Fact]
    public void LinesAddedToTheGamesConversationsHaveTheirPackVariablesFilledIn()
    {
        string text = PluginSource("VanillaDialogueInjector.cs");
        Assert.Contains("TextPlaceholders.Resolve((string)node[\"text\"] ?? \"\", vars)", text);
        Assert.DoesNotContain("SetText(target, (string)node[\"text\"]", text);
    }
}
