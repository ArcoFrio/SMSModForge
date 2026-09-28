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

    /// <summary>
    /// The check before every release: a pack from any earlier version, back
    /// to 1.0.0, opens with its migrations, saves, and is from then on a pack
    /// of this version - opening what was saved needs no further migration,
    /// and saving it again writes the same file. Through the real Load and
    /// Save, since the file they write is the one the game reads.
    /// </summary>
    [Fact]
    public void AnOlderPackMigratesOnceAndIsCurrentAfterOneSave()
    {
        var packs = Subjects().ToList();
        if (packs.Count == 0)
        {
            _out.WriteLine("SMSMODFORGE_OLD_PACKS not set - nothing to check.");
            return;
        }

        foreach (string path in packs)
        {
            string name = Path.GetFileName(path);
            var original = Newtonsoft.Json.JsonConvert.DeserializeObject<ModPack>(File.ReadAllText(path));
            if (original?.PackId == null) { _out.WriteLine($"{name}: not a pack"); continue; }

            string root = Path.Combine(Path.GetTempPath(), "smsforge-old-" + Guid.NewGuid().ToString("N"));
            try
            {
                string first = Path.Combine(root, "first"), second = Path.Combine(root, "second");
                Directory.CreateDirectory(first);
                File.Copy(path, Path.Combine(first, PackRepository.ManifestFileName));

                var pack = PackRepository.Load(first);
                var migration = PackRepository.LastMigration;
                string by = string.IsNullOrEmpty(original.ForgeVersion) ? "before 1.1" : original.ForgeVersion;
                _out.WriteLine($"── {name}  (written by {by})");
                _out.WriteLine("   " + (migration?.Migrated == true
                                            ? string.Join(" / ", migration.Changes.Select(c => c.What + " (" + c.Count + ")"))
                                            : "no migration"));

                PackRepository.Save(pack, first);
                string saved = File.ReadAllText(Path.Combine(first, PackRepository.ManifestFileName));
                if (migration?.Migrated == true)
                    Assert.Single(Directory.GetFiles(first, "modpack.pre-migration-*.json"));

                // Opening what was saved finds nothing left to do...
                var again = PackRepository.Load(first);
                var still = PackRepository.LastMigration;
                Assert.False(still?.Migrated == true, $"{name}: still migrating after a save - {still?.Describe()}");

                // ...and saving it again writes the same file.
                Directory.CreateDirectory(second);
                PackRepository.Save(again, second);
                Assert.Equal(saved, File.ReadAllText(Path.Combine(second, PackRepository.ManifestFileName)));

                // Nothing the author made went missing on the way.
                Assert.Equal(original.Dialogues.Count, again.Dialogues.Count);
                Assert.Equal(original.Places.Count, again.Places.Count);
                Assert.Equal(original.Variables.Count, again.Variables.Count);
                Assert.Equal(original.Quests.Count, again.Quests.Count);
                Assert.Equal(original.Scenes.Count, again.Scenes.Count);
                Assert.Equal(original.IntegrationRules.Count, again.IntegrationRules.Count);

                // And this version's checks all run on it. Only the manifest was
                // copied, so a missing picture is expected here and not counted.
                var issues = SMSModForge.Validation.PackValidator.Validate(again, first);
                var errors = issues.Where(i => i.Severity == SMSModForge.Validation.Severity.Error).ToList();
                _out.WriteLine($"   saved, reopened clean; {errors.Count} error(s) from validation"
                               + (errors.Count == 0 ? "" : ": " + string.Join(", ", errors.Select(e => e.Code).Distinct())));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }
    }

    /// <summary>
    /// The player's lines come through the migrations that changed the player
    /// (their typing voice became the game's, 2026-09-27) as they went in:
    /// every line spoken by the player still is, with the same words, in the
    /// file the game reads. Asked after a report of the player's lines playing
    /// as an empty box - which turned out to be a different, empty dialogue -
    /// so that a real cause would be caught here next time.
    /// </summary>
    [Fact]
    public void ThePlayersLinesSurviveThePlayersMigrations()
    {
        var packs = Subjects().ToList();
        if (packs.Count == 0)
        {
            _out.WriteLine("SMSMODFORGE_OLD_PACKS not set - nothing to check.");
            return;
        }

        foreach (string path in packs)
        {
            string name = Path.GetFileName(path);
            var original = Newtonsoft.Json.JsonConvert.DeserializeObject<ModPack>(File.ReadAllText(path));
            if (original?.PackId == null) { _out.WriteLine($"{name}: not a pack"); continue; }

            var before = PlayersLines(original);
            string root = Path.Combine(Path.GetTempPath(), "smsforge-player-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                File.Copy(path, Path.Combine(root, PackRepository.ManifestFileName));
                PackRepository.Save(PackRepository.Load(root), root);

                // What the game reads: the file, not the editor's model.
                var saved = Newtonsoft.Json.JsonConvert.DeserializeObject<ModPack>(
                    File.ReadAllText(Path.Combine(root, PackRepository.ManifestFileName)))!;
                var after = PlayersLines(saved);

                _out.WriteLine($"── {name}: {before.Count} line(s) for the player, {after.Count} after opening and saving");
                Assert.Equal(before.Count, after.Count);
                foreach (var line in before)
                {
                    Assert.True(after.TryGetValue(line.Key, out var text), $"{name}: the player's line {line.Key} is gone");
                    Assert.Equal(line.Value, text);
                }
                Assert.Contains(saved.Characters, c => c.IsPlayer);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }
    }

    /// <summary>The player's lines, by dialogue and line, with their words.</summary>
    private static Dictionary<string, string> PlayersLines(ModPack pack)
    {
        var lines = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var d in pack.Dialogues ?? new List<DialogueDef>())
            foreach (var n in d.Nodes)
                if (string.Equals(n.Actor, CharacterDef.PlayerKey, StringComparison.OrdinalIgnoreCase))
                    lines[d.Key + "/" + n.Id] = n.Text ?? "";
        return lines;
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
