using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Telling an author their pack and their ModForge do not match.
/// <para/>
/// The failure this exists for is silent: a pack written by a newer ModForge
/// loads into an older runtime perfectly happily and simply does less than it
/// should, because the actions and fields it uses arrived after that runtime
/// was built. Nothing crashes, so nothing is reported, and the author is left
/// with a pack that works on their machine and not on anybody else's.
/// <para/>
/// Both directions are worth saying, and they are not the same kind of
/// problem: a pack NEWER than the runtime is an error, because something it
/// asks for is genuinely missing; a pack OLDER is a warning, because it should
/// work but is not what the tool would write today.
/// </summary>
public sealed class ForgeVersionTests
{
    private readonly ITestOutputHelper _out;
    public ForgeVersionTests(ITestOutputHelper o) => _out = o;

    // ── The number itself ────────────────────────────────────────────

    [Fact]
    public void TheSharedConstantIsWhatTheEditorActuallyShipsAs()
    {
        // The constant is compiled into the runtime plugin, which reads it to
        // judge every pack. If somebody bumped the csproj and not this, every
        // one of those judgements would be made against the wrong number - and
        // nothing would fail until a player saw a wrong label.
        var assembly = typeof(ModPack).Assembly.GetName().Version!;
        string built = $"{assembly.Major}.{assembly.Minor}.{assembly.Build}";

        _out.WriteLine($"assembly {built}, constant {ForgeVersion.Current}");
        Assert.Equal(built, ForgeVersion.Current);
    }

    [Fact]
    public void TheRuntimeAndTheEditorReadAVersionTheSameWay()
    {
        // Two parsers exist because the runtime compiles on an older language
        // version than the editor. They must agree, or "1.2.0" means one thing
        // in the tool and another in the game.
        string[] cases =
        {
            "0.0.0", "1.2.3", "10.0.41", " 1.2.3 ", "1.02.3",
            "", "1.2", "1.2.3.4", "1.2.x", "v1.2.3", "-1.0.0", "1..3", "1.2.",
        };

        foreach (string text in cases)
        {
            var editor = PackVersion.Parse(text);
            var runtime = ForgeVersion.Parse(text);

            _out.WriteLine($"'{text}' -> editor {(editor?.ToString() ?? "refused")}, "
                           + $"runtime {(runtime == null ? "refused" : string.Join(".", runtime))}");

            Assert.Equal(editor == null, runtime == null);
            if (editor != null)
                Assert.Equal(new[] { editor.Value.Major, editor.Value.Minor, editor.Value.Patch },
                             runtime);
        }
    }

    // ── The verdict ──────────────────────────────────────────────────

    [Fact]
    public void APackFromANewerModForgeIsAnError()
    {
        Assert.Equal(ForgeVersion.Standing.PackIsNewer, ForgeVersion.Judge("1.3.0", "1.1.0"));
        Assert.Equal(ForgeVersion.Standing.PackIsNewer, ForgeVersion.Judge("1.1.1", "1.1.0"));
        Assert.Equal(ForgeVersion.Standing.PackIsNewer, ForgeVersion.Judge("2.0.0", "1.9.9"));
    }

    [Fact]
    public void APackFromAnOlderModForgeIsAWarning()
    {
        Assert.Equal(ForgeVersion.Standing.PackIsOlder, ForgeVersion.Judge("1.0.0", "1.1.0"));
        Assert.Equal(ForgeVersion.Standing.PackIsOlder, ForgeVersion.Judge("1.1.0", "1.1.1"));
        Assert.Equal(ForgeVersion.Standing.PackIsOlder, ForgeVersion.Judge("0.9.9", "1.0.0"));
    }

    [Fact]
    public void AMatchIsSaidNothingAbout()
    {
        // The control. A verdict that fired on a matching pair would paint
        // every correctly-installed pack as a problem, which teaches people to
        // ignore the colour.
        Assert.Equal(ForgeVersion.Standing.Matches, ForgeVersion.Judge("1.1.0", "1.1.0"));
        Assert.Equal(ForgeVersion.Standing.Matches, ForgeVersion.Judge(" 1.1.0 ", "1.1.0"));
    }

    [Fact]
    public void APackWithNoStampIsNotJudged()
    {
        // Packs written before ModForge recorded this have no stamp, and
        // guessing at one would produce a confident wrong answer about every
        // pack in existence.
        Assert.Equal(ForgeVersion.Standing.Unknown, ForgeVersion.Judge("", "1.1.0"));
        Assert.Equal(ForgeVersion.Standing.Unknown, ForgeVersion.Judge(null!, "1.1.0"));
        Assert.Equal(ForgeVersion.Standing.Unknown, ForgeVersion.Judge("who knows", "1.1.0"));
        Assert.Equal(ForgeVersion.Standing.Unknown, ForgeVersion.Judge("1.1.0", "not a version"));
    }

    // ── The stamp on a pack ──────────────────────────────────────────

    private sealed class Scratch : IDisposable
    {
        public string Path { get; }
        public Scratch()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          "smsforge-fv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    [Fact]
    public void SavingStampsTheModForgeThatWroteIt()
    {
        using var dir = new Scratch();
        var pack = PackRepository.CreateEmpty("stamp.pack");
        Assert.Equal("", pack.ForgeVersion);

        PackRepository.Save(pack, dir.Path);

        _out.WriteLine($"saved with forgeVersion {pack.ForgeVersion}");
        Assert.Equal(ForgeVersion.Current, pack.ForgeVersion);

        // And it is really in the file, since that is what the runtime reads.
        var written = Newtonsoft.Json.Linq.JObject.Parse(
            File.ReadAllText(Path.Combine(dir.Path, "modpack.json")));
        Assert.Equal(ForgeVersion.Current, (string?)written["forgeVersion"]);
    }

    [Fact]
    public void APackFromBeforeTheStampGetsOneOnLoadAndIsToldAboutIt()
    {
        var old = new ModPack { PackId = "old.pack", Version = "0.4.0" };
        Assert.Equal("", old.ForgeVersion);

        var report = PackMigration.Apply(old);
        _out.WriteLine(report.Describe());

        Assert.Equal(ForgeVersion.Current, old.ForgeVersion);
        Assert.Contains(report.Changes, c => c.What.Contains("ModForge"));
    }

    [Fact]
    public void UpdatingModForgeIsNotReportedAsAChangeToThePack()
    {
        // The control, and the reason the stamp is refreshed on SAVE rather
        // than in the migration: reporting a migration every time somebody
        // updates ModForge would tell an author their pack had changed when the
        // only thing that changed was the tool - and would take a full backup
        // of the manifest to say it.
        var pack = new ModPack { PackId = "old.pack", Version = "1.0.0", ForgeVersion = "0.9.0" };

        var report = PackMigration.Apply(pack);

        Assert.Equal("0.9.0", pack.ForgeVersion);
        Assert.DoesNotContain(report.Changes, c => c.What.Contains("ModForge"));
    }

    [Fact]
    public void TheToolsVersionIsNotPartOfWhatAReleaseIsNumberedFor()
    {
        // An author who updates ModForge and publishes has not added anything
        // to their pack, and a version bump announcing it would be numbering
        // somebody else's work.
        var pack = new ModPack { PackId = "v.pack", Version = "1.0.0", ForgeVersion = "1.0.0" };
        string before = PackRepository.SerializeAsSaved(pack);

        pack.ForgeVersion = "9.9.9";
        string after = PackRepository.SerializeAsSaved(pack);

        Assert.NotEqual(before, after);   // it really did change in the file
        Assert.Equal(VersionBump.Change.None, VersionBump.Classify(before, after));
    }
}
