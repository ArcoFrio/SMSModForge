using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The contract every change to saved-pack structure has to keep.
/// <para/>
/// Written as tests rather than only as a rule in CLAUDE.md because the two
/// halves that actually protect an author — never writing on load, and keeping
/// the original once before the first save — are both invisible when they
/// break. A pack silently rewritten on open looks exactly like a pack that was
/// not.
/// </summary>
public sealed class PackMigrationTests
{
    private readonly ITestOutputHelper _out;
    public PackMigrationTests(ITestOutputHelper o) => _out = o;

    /// <summary>A pack on disk in the shape an older build wrote: actors
    /// instead of characters.</summary>
    private static string WriteOldPack(string root)
    {
        Directory.CreateDirectory(root);
        string manifest = Path.Combine(root, "modpack.json");
        File.WriteAllText(manifest, """
        {
          "packId": "old.pack",
          "actors": [
            { "key": "amber", "displayName": "Amber", "defaultBustKey": "Amber_Base" }
          ],
          "characters": []
        }
        """);
        return manifest;
    }

    [Fact]
    public void OpeningAnOldPackChangesNothingOnDisk()
    {
        // The rule that matters most and is easiest to break: an author who
        // opens a pack to look at it and closes it has changed nothing.
        using var dir = new Scratch();
        string manifest = WriteOldPack(dir.Path);

        string before = File.ReadAllText(manifest);
        DateTime stamp = File.GetLastWriteTimeUtc(manifest);

        var pack = PackRepository.Load(dir.Path);

        Assert.Equal(before, File.ReadAllText(manifest));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(manifest));

        // But in memory it is current.
        Assert.Empty(pack.Actors);
        Assert.Contains(pack.Characters, c => c.Key == "amber");
    }

    [Fact]
    public void TheAuthorIsToldWhatHappened()
    {
        using var dir = new Scratch();
        WriteOldPack(dir.Path);
        PackRepository.Load(dir.Path);

        var report = PackRepository.LastMigration;
        Assert.NotNull(report);
        Assert.True(report!.Migrated);

        string said = report.Describe();
        _out.WriteLine(said);

        Assert.Contains("earlier version", said);
        Assert.Contains("untouched until you save", said);
        Assert.NotEmpty(report.Changes);
    }

    [Fact]
    public void APackAlreadyCurrentIsNotReportedAsMigrated()
    {
        // The control. Every migration has to be idempotent, because this runs
        // on every load - including the load straight after a save. A pack that
        // reports a migration each time it is opened would warn the author for
        // ever and back itself up repeatedly.
        using var dir = new Scratch();
        var pack = PackRepository.CreateEmpty("fresh.pack");
        PackRepository.Save(pack, dir.Path);

        PackRepository.Load(dir.Path);
        var report = PackRepository.LastMigration;

        Assert.NotNull(report);
        Assert.False(report!.Migrated);
        Assert.Empty(report.Describe());
    }

    [Fact]
    public void TheFirstSaveKeepsTheOriginalAndLaterOnesDoNot()
    {
        using var dir = new Scratch();
        string manifest = WriteOldPack(dir.Path);
        string original = File.ReadAllText(manifest);

        var pack = PackRepository.Load(dir.Path);
        PackRepository.Save(pack, dir.Path);

        var backups = Directory.GetFiles(dir.Path, "modpack.pre-migration-*.json");
        _out.WriteLine("kept: " + string.Join(", ", backups.Select(Path.GetFileName)));

        // Exactly one, and it holds what the author had - not the migrated form.
        string kept = Assert.Single(backups);
        Assert.Equal(original, File.ReadAllText(kept));
        Assert.Contains("\"actors\"", File.ReadAllText(kept));

        // Saving again does not make a second, and cannot overwrite the first
        // with an already-migrated file.
        PackRepository.Save(pack, dir.Path);
        PackRepository.Save(pack, dir.Path);
        Assert.Single(Directory.GetFiles(dir.Path, "modpack.pre-migration-*.json"));
        Assert.Equal(original, File.ReadAllText(kept));
    }

    [Fact]
    public void APackThatNeededNothingGetsNoBackup()
    {
        using var dir = new Scratch();
        var pack = PackRepository.CreateEmpty("fresh.pack");
        PackRepository.Save(pack, dir.Path);
        PackRepository.Load(dir.Path);
        PackRepository.Save(pack, dir.Path);

        Assert.Empty(Directory.GetFiles(dir.Path, "modpack.pre-migration-*.json"));
    }

    [Fact]
    public void ABackupIsNeverExported()
    {
        // A second, older manifest inside the archive would be read by the
        // plugin. This is the one failure of the five that reaches players.
        Assert.True(PackMigration.IsBackup("modpack.pre-migration-20260908-161500.json"));
        Assert.True(PackMigration.IsBackup(
            Path.Combine("some", "where", "modpack.pre-migration-20260101-000000.json")));

        Assert.False(PackMigration.IsBackup("modpack.json"));
        Assert.False(PackMigration.IsBackup("modpack.json.bak"));
        Assert.False(PackMigration.IsBackup(""));
        Assert.False(PackMigration.IsBackup(null));
    }

    [Fact]
    public void TwoMigrationsOfOneKindReadAsOneLineWithACount()
    {
        var report = new PackMigration.Report();
        report.Note("Actors folded into characters", 3);
        report.Note("Actors folded into characters", 2);
        report.Note("Something else");

        Assert.Equal(2, report.Changes.Count);
        Assert.Equal(5, report.Changes[0].Count);
        Assert.Equal("Actors folded into characters (5)", report.Changes[0].ToString());
        Assert.Equal("Something else", report.Changes[1].ToString());

        // Nothing is not news.
        var quiet = new PackMigration.Report();
        quiet.Note("Ignored", 0);
        Assert.False(quiet.Migrated);
    }

    private sealed class Scratch : IDisposable
    {
        public string Path { get; }
        public Scratch()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          "smsmodforge-migrate-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
