using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Real packs, opened the way the editor opens them, and saved the way the
/// editor saves them.
/// <para/>
/// The seed/prune bargain only holds if loading puts back everything saving
/// left out. Every test around it uses a pack built in the test — which is a
/// pack in the shape this build writes. A pack written months ago by an older
/// build is the case that actually breaks, and no constructed fixture stands
/// in for one.
/// <para/>
/// Skipped unless <c>SMSMODFORGE_OLD_PACKS</c> names them (a file, or a folder
/// of them, semicolon-separated). The packs themselves are nobody's business
/// but their author's, so this ships as a harness with no subject.
/// </summary>
public sealed class OlderPackCompatibility
{
    private readonly ITestOutputHelper _out;
    public OlderPackCompatibility(ITestOutputHelper o) => _out = o;

    private static IEnumerable<string> Subjects()
    {
        string setting = Environment.GetEnvironmentVariable("SMSMODFORGE_OLD_PACKS") ?? "";
        foreach (string part in setting.Split(';'))
        {
            string one = part.Trim().Trim('"');
            if (one.Length == 0) continue;
            if (Directory.Exists(one))
                foreach (string f in Directory.GetFiles(one, "*.json")) yield return f;
            else if (File.Exists(one)) yield return one;
        }
    }

    [Fact]
    public void OpeningAndSavingAnOlderPackLosesNothing()
    {
        var packs = Subjects().ToList();
        if (packs.Count == 0)
        {
            _out.WriteLine("SMSMODFORGE_OLD_PACKS not set - nothing to check.");
            return;
        }

        foreach (string path in packs)
        {
            var raw = Newtonsoft.Json.JsonConvert.DeserializeObject<ModPack>(
                File.ReadAllText(path));
            if (raw?.PackId == null) { _out.WriteLine($"{Path.GetFileName(path)}: not a pack"); continue; }

            _out.WriteLine($"── {Path.GetFileName(path)}");

            // What the author had before this build existed.
            int charactersOnDisk = raw.Characters.Count;
            var speakers = Speakers(raw);

            CharacterMerge.Apply(raw);
            string written = PackRepository.SerializeAsSaved(raw);

            var again = Newtonsoft.Json.JsonConvert.DeserializeObject<ModPack>(written)!;
            CharacterMerge.Apply(again);

            _out.WriteLine($"   on disk {charactersOnDisk} characters -> "
                           + $"{raw.Characters.Count} in the editor -> "
                           + $"{Newtonsoft.Json.JsonConvert.DeserializeObject<ModPack>(written)!.Characters.Count} written");

            // 1. Nobody the pack SPEAKS THROUGH may go missing, whether the
            //    manifest still names them or the built-in cast supplies them.
            foreach (string who in speakers)
                Assert.True(again.Characters.Any(
                        c => string.Equals(c.Key, who, StringComparison.OrdinalIgnoreCase)),
                    $"{Path.GetFileName(path)}: speaker '{who}' is gone");

            // 2. Every character the author had is still there, unchanged.
            foreach (var was in raw.Characters)
            {
                var now = again.Characters.FirstOrDefault(
                    c => string.Equals(c.Key, was.Key, StringComparison.Ordinal));
                Assert.True(now != null, $"{Path.GetFileName(path)}: '{was.Key}' is gone");

                Assert.Equal(Newtonsoft.Json.JsonConvert.SerializeObject(was),
                             Newtonsoft.Json.JsonConvert.SerializeObject(now));
            }

            // 3. And the pack does not drift by being opened and closed - a
            //    manifest that changes every time it is touched makes every
            //    diff unreadable.
            Assert.Equal(written, PackRepository.SerializeAsSaved(again));

            // 4. Nothing outside the characters moved either.
            Assert.Equal(raw.Dialogues.Count, again.Dialogues.Count);
            Assert.Equal(raw.Places.Count, again.Places.Count);
            Assert.Equal(raw.Variables.Count, again.Variables.Count);

            _out.WriteLine("   clean");
        }
    }

    /// <summary>Every character key the pack gives a line to.</summary>
    private static HashSet<string> Speakers(ModPack pack)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in pack.Dialogues ?? new List<DialogueDef>())
            foreach (var n in d.Nodes)
                if (!string.IsNullOrEmpty(n.Actor)) found.Add(n.Actor);
        return found;
    }
}
