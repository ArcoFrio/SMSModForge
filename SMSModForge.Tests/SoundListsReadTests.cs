using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SMSModForge.Services.Audio;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A save reads only the saved manifest's two sound lists now, instead of parsing
/// all of it (1.9.0). What it reads must be exactly what parsing all of it gave -
/// the same lists, and nothing where parsing failed.
/// </summary>
public sealed class SoundListsReadTests
{
    private readonly ITestOutputHelper _out;
    public SoundListsReadTests(ITestOutputHelper o) => _out = o;

    /// <summary>How it was read before: all of it, then the two lists.</summary>
    private static (JToken? Sfx, JToken? Music)? Whole(string json)
    {
        try
        {
            var o = JObject.Parse(json);
            return (o["sfx"], o["music"]);
        }
        catch (JsonException) { return null; }
    }

    private static IEnumerable<(string Name, string Json)> Cases()
    {
        const string lists = @"""sfx"": [ { ""audioPath"": ""Sfx/a.ogg"", ""edit"": { ""source"": ""Sfx/a.wav"" } } ],
                               ""music"": [ { ""audioPath"": ""Music/m.ogg"" } ]";
        yield return ("both", "{ \"packId\": \"p\", " + lists + ", \"dialogues\": [ { \"nodes\": [1, 2, { \"x\": [] }] } ] }");
        yield return ("neither", "{ \"packId\": \"p\", \"n\": 3 }");
        yield return ("twice, the last kept", "{ \"sfx\": [1], \"music\": [2], \"sfx\": [3] }");
        yield return ("not lists", "{ \"sfx\": \"text\", \"music\": null }");
        yield return ("a date in a list", "{ \"sfx\": [ \"2026-11-15T10:00:00Z\" ] }");
        yield return ("comments", "/* before */ { // inside\n \"sfx\": [1] /* after */ } // end");
        yield return ("empty", "");
        yield return ("an array", "[1, 2]");
        yield return ("cut short", "{ \"sfx\": [1, 2");
        yield return ("cut after a list", "{ \"sfx\": [1], ");
        yield return ("bad inside a skipped value", "{ \"other\": [1, }, \"sfx\": [1] }");
        yield return ("more after", "{ \"sfx\": [1] } { }");
        yield return ("text after", "{ \"sfx\": [1] } x");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SamplePack", "modpack.json"))) dir = dir.Parent;
        if (dir != null) yield return ("SamplePack", File.ReadAllText(Path.Combine(dir.FullName, "SamplePack", "modpack.json")));
        string? real = Environment.GetEnvironmentVariable("SMSMODFORGE_PERF_PACK");
        if (!string.IsNullOrWhiteSpace(real) && File.Exists(Path.Combine(real, "modpack.json")))
            yield return ("real", File.ReadAllText(Path.Combine(real, "modpack.json")));
    }

    [Fact]
    public void TheTwoListsReadAloneAreWhatTheWholeParseGave()
    {
        int failed = 0;
        foreach (var (name, json) in Cases())
        {
            var whole = Whole(json);
            var alone = SfxEdits.SoundListsOf(json);
            _out.WriteLine($"{name}: {(whole == null ? "fails" : "reads")}");
            Assert.Equal(whole == null, alone == null);
            if (whole == null) { failed++; continue; }
            Assert.True(JToken.DeepEquals(whole.Value.Sfx, alone!.Value.Sfx), name + ": sfx differs");
            Assert.True(JToken.DeepEquals(whole.Value.Music, alone.Value.Music), name + ": music differs");
            Assert.Equal(whole.Value.Sfx?.Type, alone.Value.Sfx?.Type);
        }
        // The control: some of the cases do fail to parse, so "nothing" is compared too.
        Assert.True(failed >= 5, failed + " cases failed to parse");
    }
}
