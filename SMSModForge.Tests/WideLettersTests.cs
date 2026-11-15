using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The plugin's font scan of a pack (1.9.0): it reads the letters from U+0E00 up
/// straight off the manifest instead of writing the whole manifest out as text
/// first. What the scan does with them - which scripts the pack is written in,
/// and which letters to draw ahead, in order - must come out exactly the same.
/// </summary>
public sealed class WideLettersTests
{
    private readonly ITestOutputHelper _out;
    public WideLettersTests(ITestOutputHelper o) => _out = o;

    /// <summary>What PluginFonts.Prepare draws ahead from a text, in order.</summary>
    private static string Prepared(string text)
    {
        var letters = new List<char>();
        var seen = new HashSet<char>();
        foreach (char c in text)
            if (c >= 0x0E00 && !char.IsSurrogate(c) && !char.IsWhiteSpace(c) && !char.IsControl(c) && seen.Add(c))
                letters.Add(c);
        return new string(letters.ToArray());
    }

    private static IEnumerable<(string Name, JObject Json)> Manifests()
    {
        // Every kind of value, the escapes the writer makes, and the scripts.
        yield return ("made", JObject.Parse(@"{
            ""packId"": ""wide"", ""n"": 3, ""f"": 1.5, ""b"": true, ""z"": null,
            ""名前"": ""你好，世界"", ""when"": ""2026-11-15T10:00:00Z"",
            ""lines"": [ { ""text"": ""こんにちは 次の行"" }, { ""text"": ""안녕하세요 \u0085 สวัสดี"" } ],
            ""quote"": ""say \""喂\"" \\ back"", ""emoji"": ""😀 and \u0001 control""
        }"));
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SamplePack", "modpack.json"))) dir = dir.Parent;
        if (dir != null) yield return ("SamplePack", JObject.Parse(File.ReadAllText(Path.Combine(dir.FullName, "SamplePack", "modpack.json"))));
        string? real = Environment.GetEnvironmentVariable("SMSMODFORGE_PERF_PACK");
        if (!string.IsNullOrWhiteSpace(real) && File.Exists(Path.Combine(real, "modpack.json")))
            yield return ("real", JObject.Parse(File.ReadAllText(Path.Combine(real, "modpack.json"))));
    }

    [Fact]
    public void TheScanFindsTheSameScriptsAndLettersAsInTheWrittenOutText()
    {
        foreach (var (name, json) in Manifests())
        {
            string written = json.ToString();
            string wide = TextScripts.WideLettersOf(json);

            Assert.Equal(TextScripts.Of(written), TextScripts.Of(wide));
            Assert.Equal(Prepared(written), Prepared(wide));
            _out.WriteLine($"{name}: {written.Length} characters written out, {wide.Length} read; "
                           + $"scripts {string.Join(",", TextScripts.Of(wide))}, {Prepared(wide).Length} letters");
        }

        // The control: a scan that dropped the property names would miss the
        // Chinese one above, so the comparison can see a difference.
        var made = Manifests().First().Json;
        var valuesOnly = string.Concat(made.Descendants().OfType<JValue>().Where(v => v.Type == JTokenType.String).Select(v => (string)v.Value!));
        Assert.NotEqual(Prepared(made.ToString()), Prepared(valuesOnly));
    }
}
