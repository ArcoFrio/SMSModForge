using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The author's own backups stay out of an exported pack (found testing a
/// pack in the game, 2026-11-01: 22 copies of the manifest were 36 MB of a
/// 96 MB download), and the export says which were left out.
/// </summary>
public sealed class PackExportBackupsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SMSModForge-exportbackups-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Write(string rel, string content)
    {
        string path = Path.Combine(_root, "pack", rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Backups_are_left_out_and_named_and_everything_else_ships()
    {
        string manifest = "{ \"packId\": \"backups\", \"dialogues\": [] }";
        Write("modpack.json", manifest);
        // The author's copies.
        Write("modpack.json.bak", manifest);
        Write("modpack.json.bak-npcs", manifest);
        Write("modpack - Copy.json", "{ \"version\": \"1.0\", \"packId\": \"backups\" }");   // packId not first
        Write("Audio/old.ogg.bak2", "x");
        // The controls: files a pack ships that look a little like those.
        Write("notes.json", "{ \"title\": \"not a manifest\" }");
        Write("broken.json", "{ not json at all");
        Write("Audio/door.bak.wav", "x");                                       // a sound, named so
        Write("Data/modpack copy.json", "{ \"packId\": \"backups\" }");         // not beside the manifest

        string output = Path.Combine(_root, "out.smspack");
        var result = PackExporter.Export(Path.Combine(_root, "pack"), output);

        using var zip = ZipFile.OpenRead(output);
        var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "Audio/door.bak.wav", "Data/modpack copy.json", "broken.json", "modpack.json", "notes.json",
        }, names);

        var leftOut = result.BackupsLeftOut.Select(p => p.Replace('\\', '/')).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Audio/old.ogg.bak2", "modpack - Copy.json", "modpack.json.bak", "modpack.json.bak-npcs" }, leftOut);
        long expected = new[] { "modpack.json.bak", "modpack.json.bak-npcs", "modpack - Copy.json", "Audio/old.ogg.bak2" }
            .Sum(r => new FileInfo(Path.Combine(_root, "pack", r)).Length);
        Assert.Equal(expected, result.BackupBytes);
        Assert.Equal(5, result.FileCount);
    }

    [Fact]
    public void A_pack_without_backups_reports_none()
    {
        Write("modpack.json", "{ \"packId\": \"clean\" }");
        Write("Sprites/a.png", "x");
        var result = PackExporter.Export(Path.Combine(_root, "pack"), Path.Combine(_root, "out.smspack"));
        Assert.Empty(result.BackupsLeftOut);
        Assert.Equal(0, result.BackupBytes);
    }

    [Fact]
    public void The_message_names_the_first_five_and_says_when_there_are_more()
    {
        Assert.Equal("a, b", MainViewModel.BackupNames(new[] { "a", "b" }));
        Assert.Equal("a, b, c, d, e", MainViewModel.BackupNames(new[] { "a", "b", "c", "d", "e" }));
        Assert.Equal("a, b, c, d, e, …", MainViewModel.BackupNames(new[] { "a", "b", "c", "d", "e", "f" }));
    }
}
