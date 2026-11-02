using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Settings an action or condition keeps from a type it used to be (found
/// testing a pack, 2026-11-01; the author chose a migration): taken off on
/// load, put aside on a change of type in the editor - and never one the
/// plugin reads, which the first test holds <see cref="ParamsRead"/> to by
/// reading the plugin's own code.
/// </summary>
public sealed class ParamsReadTests
{
    // ── The plugin's reading, from its code ──────────────────────────────

    private static readonly string Repo = GameRun.RepoRoot();

    private static Dictionary<string, string> Sources()
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var folder in new[] { "SMSModForge.PackPlugin", "Shared" })
            foreach (var file in Directory.GetFiles(Path.Combine(Repo, folder), "*.cs"))
                all[Path.GetFileName(file)] = File.ReadAllText(file);
        return all;
    }

    /// <summary><c>const string Name = "value"</c> as <c>Class.Name</c>: two
    /// classes have an ActionType.</summary>
    private static Dictionary<string, string> Constants(Dictionary<string, string> sources)
    {
        var consts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var text in sources.Values)
        {
            var classes = Regex.Matches(text, @"\bclass\s+(\w+)").Select(m => (m.Index, m.Groups[1].Value)).ToList();
            foreach (Match m in Regex.Matches(text, "const\\s+string\\s+(\\w+)\\s*=\\s*\"([^\"]*)\""))
            {
                var owner = classes.LastOrDefault(c => c.Index < m.Index).Value ?? "";
                consts[owner + "." + m.Groups[1].Value] = m.Groups[2].Value;
            }
        }
        return consts;
    }

    private static string? Resolve(string token, Dictionary<string, string> consts)
    {
        token = token.Trim();
        if (token.StartsWith("\"", StringComparison.Ordinal)) return token.Trim('"');
        var parts = token.Split('.');
        if (parts.Length >= 2 && consts.TryGetValue(parts[^2] + "." + parts[^1], out var v)) return v;
        var same = consts.Where(kv => kv.Key.EndsWith("." + parts[^1], StringComparison.Ordinal)).Select(kv => kv.Value).Distinct().ToList();
        return same.Count == 1 ? same[0] : null;
    }

    private const string Key = "(\"[^\"]+\"|[A-Za-z_][\\w.]*)";
    private static readonly string[] Reads =
    {
        @"\bp\[" + Key + @"\]", @"DerefParam\(\s*p\s*,\s*" + Key, @"OptionalFloat\(\s*p\s*,\s*" + Key,
    };

    /// <summary>Methods given a <c>JObject p</c>: what a case hands its params to.</summary>
    private static Dictionary<string, string> Helpers(Dictionary<string, string> sources)
    {
        var helpers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var text in sources.Values)
            foreach (Match m in Regex.Matches(text, @"(?:static|internal|private|public)[^;{(=]*?\s(\w+)\(([^)]*\bJObject\s+p\b[^)]*)\)\s*(\{|=>)"))
            {
                int start = m.Index + m.Length;
                string body;
                if (m.Groups[3].Value == "=>") body = text.Substring(start, text.IndexOf(';', start) - start);
                else
                {
                    int depth = 1, i = start;
                    while (depth > 0 && i < text.Length) { depth += text[i] == '{' ? 1 : text[i] == '}' ? -1 : 0; i++; }
                    body = text.Substring(start, i - start);
                }
                helpers[m.Groups[1].Value] = (helpers.TryGetValue(m.Groups[1].Value, out var had) ? had : "") + body;
            }
        return helpers;
    }

    private static HashSet<string> KeysIn(string body, Dictionary<string, string> consts, Dictionary<string, string> helpers, HashSet<string>? seen = null)
    {
        seen ??= new HashSet<string>(StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pattern in Reads)
            foreach (Match m in Regex.Matches(body, pattern))
                if (Resolve(m.Groups[1].Value, consts) is string k) found.Add(k);
        if (Regex.IsMatch(body, @"DerefParam\(\s*p\s*,")) found.Add("valueSource");
        foreach (Match call in Regex.Matches(body, @"(\w+)\((?:[^()]|\([^()]*\))*?\bp\b"))
        {
            string h = call.Groups[1].Value;
            if (helpers.TryGetValue(h, out var hb) && seen.Add(h)) found.UnionWith(KeysIn(hb, consts, helpers, seen));
        }
        return found;
    }

    /// <summary>Each case of the switch on the type, with what it reads.</summary>
    private static Dictionary<string, HashSet<string>> CaseReads(string file)
    {
        var sources = Sources();
        var consts = Constants(sources);
        var helpers = Helpers(sources);
        string text = sources[file];
        string body = text.Substring(text.IndexOf("switch (type)", StringComparison.Ordinal));
        body = body.Substring(0, body.IndexOf("\n                default:", StringComparison.Ordinal));
        var labels = Regex.Matches(body, "^\\s{16}case\\s+(?:\"(\\w+)\"|([\\w.]+)):", RegexOptions.Multiline);
        var reads = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (int i = 0; i < labels.Count; i++)
        {
            var m = labels[i];
            string label = m.Groups[1].Success ? m.Groups[1].Value : Resolve(m.Groups[2].Value, consts) ?? m.Groups[2].Value;
            int end = i + 1 < labels.Count ? labels[i + 1].Index : body.Length;
            reads[label] = KeysIn(body.Substring(m.Index + m.Length, end - m.Index - m.Length), consts, helpers);
        }
        return reads;
    }

    [Theory]
    [InlineData("ActionRuntime.cs", true)]
    [InlineData("ConditionEvaluator.cs", false)]
    public void Every_param_the_plugin_reads_for_a_type_is_on_that_types_list(string file, bool actions)
    {
        var lists = actions ? ParamsRead.PluginActionKeys : ParamsRead.PluginConditionKeys;
        var reads = CaseReads(file);
        // Control: the reading is found at all.
        Assert.Contains("valueSource", reads[actions ? "SetVariable" : "VariableCompare"]);
        Assert.Contains("source", reads[actions ? "SetVariable" : "VariableCompare"]);

        var missing = new List<string>();
        foreach (var (type, keys) in reads)
        {
            if (!lists.TryGetValue(type, out var listed)) continue;   // a type the clean-up leaves whole
            foreach (var key in keys)
                if (!listed.Contains(key)) missing.Add(type + "." + key);
        }
        Assert.True(missing.Count == 0, "Read by the plugin but not on ParamsRead's list - a pack would lose them: " + string.Join(", ", missing));

        // Every type the plugin knows is on a list, so none is left whole by accident.
        var unlisted = reads.Keys.Where(t => !lists.ContainsKey(t)
                                             && t != "All" && t != "Any"           // groups hold conditions, not params
                                             && t != "Places" && t != "Bust" && t != "UI").ToList();
        Assert.True(unlisted.Count == 0, "Types the plugin reads that ParamsRead does not list: " + string.Join(", ", unlisted));
    }

    // ── On load ──────────────────────────────────────────────────────────

    private static NodeActionDef Act(string type, params (string, string)[] ps)
        => InGameSuitePacks.Act(type, ps);

    [Fact]
    public void Settings_left_over_from_another_type_are_taken_off_on_load_and_nothing_else()
    {
        var pack = PackRepository.CreateEmpty("leftover");
        var focus = Act(NodeActionTypes.CharacterFocus, ("focused", "false"), ("kind", "Bust"), ("active", "true"), ("target", "Hmph!"));
        var increment = Act(NodeActionTypes.IncrementVariable, ("name", "n"), ("delta", "1"), ("value", "SaveManager.GetInt(\"x\") + 1"));
        var shown = Act(NodeActionTypes.SetGameObjectActive, ("kind", "UI"), ("target", "panel"), ("active", "true"), ("signal", "OpenGift"));
        var vanillaSet = Act(NodeActionTypes.SetVariable, ("name", "Cash"), ("value", "5"), ("source", "vanilla"));   // control: source is SetVariable's own
        var mine = Act(NodeActionTypes.Wait, ("seconds", "1"), ("authorNote", "slow bit"));                          // control: a key nobody knows
        var dice = Act(NodeActionTypes.DiceRoll);
        dice.Branches = new List<DiceBranchDef> { new() { Chance = 100, Action = Act(NodeActionTypes.PlaySFX, ("clip", "c"), ("speed", "90")) } };
        var chance = new NodeConditionDef { Type = NodeConditionTypes.DailyChance, Params = { ["chance"] = "30", ["name"] = "x", ["value"] = "1" } };
        var rule = new UpdateRuleDef { Key = "r", Actions = { focus, increment, shown, vanillaSet, mine, dice }, Conditions = { chance } };
        pack.IntegrationRules.Add(rule);

        var report = PackMigration.Apply(pack);
        Assert.True(report.Migrated);
        Assert.Contains(report.Changes, c => c.What == Loc.T("migration.leftOverSettings") && c.Count == 5);

        Assert.Equal(new[] { "focused" }, focus.Params.Keys);
        Assert.Equal(new[] { "delta", "name" }, increment.Params.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.DoesNotContain("signal", shown.Params.Keys);
        Assert.Equal("UI", shown.Params["kind"]);
        Assert.Equal("vanilla", vanillaSet.Params["source"]);
        Assert.Equal("slow bit", mine.Params["authorNote"]);
        Assert.DoesNotContain("speed", dice.Branches[0].Action!.Params.Keys);
        Assert.Equal(new[] { "chance" }, chance.Params.Keys);

        // Once is enough: a pack already clean reports nothing.
        Assert.DoesNotContain(PackMigration.Apply(pack).Changes, c => c.What == Loc.T("migration.leftOverSettings"));
    }

    // ── A change of type in the editor ───────────────────────────────────

    [Fact]
    public void A_change_of_type_puts_the_old_types_settings_aside_and_switching_back_brings_them_back()
    {
        var def = Act(NodeActionTypes.SetVariable, ("name", "n"), ("value", "5"));
        var row = new NodeActionViewModel(def, _ => { });

        row.Type = NodeActionTypes.IncrementVariable;
        Assert.False(def.Params.ContainsKey("value"));   // not saved with an Increment
        Assert.Equal("n", def.Params["name"]);            // its own kept

        row.Type = NodeActionTypes.SetVariable;
        Assert.Equal("5", def.Params["value"]);

        var condition = new NodeConditionDef { Type = NodeConditionTypes.VariableCompare, Params = { ["name"] = "n", ["value"] = "3", ["comparison"] = "equals" } };
        var crow = new NodeConditionViewModel(condition, _ => { });
        crow.Type = NodeConditionTypes.DailyChance;
        Assert.DoesNotContain("name", condition.Params.Keys);
        crow.Type = NodeConditionTypes.VariableCompare;
        Assert.Equal("n", condition.Params["name"]);
        Assert.Equal("3", condition.Params["value"]);
    }
}
