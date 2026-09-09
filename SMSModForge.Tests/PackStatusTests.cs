using System.Linq;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What the game's menu says about an installed pack.
/// <para/>
/// That banner is the only place a player ever learns something is off, so the
/// decision behind it is worth checking rather than merely compiling — it lives
/// in the runtime, which loads into a game this repository cannot start.
/// <para/>
/// Nothing here stops a pack loading. Every one of these is attempted anyway,
/// because a pack that half-works is more use than a pack that refuses; the row
/// is what explains the difference.
/// </summary>
public sealed class PackStatusTests
{
    private readonly ITestOutputHelper _out;
    public PackStatusTests(ITestOutputHelper o) => _out = o;

    private const string Running = "1.8E";
    private const string Runtime = "1.1.0";

    private static PackStatus.Facts Pack(
        bool readable = true, string game = Running, string forge = Runtime,
        string folder = "Mods", string shadowedIn = "")
        => new PackStatus.Facts
        {
            Readable = readable,
            GameVersion = game,
            RunningGameVersion = Running,
            ForgeVersion = forge,
            RuntimeForgeVersion = Runtime,
            Folder = folder,
            ShadowedIn = shadowedIn,
        };

    private PackStatus.Report Judge(PackStatus.Facts facts)
    {
        var report = PackStatus.Of(facts);
        _out.WriteLine($"{report.Level}: '{PackStatus.Suffix(report)}'");
        return report;
    }

    // ── The ordinary pack ────────────────────────────────────────────

    [Fact]
    public void APackThatIsFineIsSaidNothingAbout()
    {
        // The control for everything below. A verdict that fired on a correct
        // install would paint every pack as a problem, which teaches people to
        // ignore the colour - and then the real ones go unread too.
        var report = Judge(Pack());

        Assert.Equal(PackStatus.Level.Fine, report.Level);
        Assert.Empty(report.Tags);
        Assert.Equal("", PackStatus.Suffix(report));
    }

    // ── Errors: it will not work as authored ─────────────────────────

    [Fact]
    public void AnArchiveThatWillNotOpenSaysSo()
    {
        var report = Judge(Pack(readable: false));

        Assert.Equal(PackStatus.Level.Error, report.Level);
        Assert.Equal("Could not be read", Assert.Single(report.Tags));
    }

    [Fact]
    public void NothingElseIsGuessedAtAboutAnUnreadableArchive()
    {
        // It told us nothing about itself, so a second tag would be an
        // invention - and would send somebody chasing a ModForge version on a
        // file that is probably a broken download.
        var report = Judge(Pack(readable: false, game: "1.7A", forge: "", shadowedIn: "ModPacks"));
        Assert.Single(report.Tags);
    }

    [Fact]
    public void APackForAnotherBuildOfTheGameIsAnError()
    {
        var report = Judge(Pack(game: "1.7A"));

        Assert.Equal(PackStatus.Level.Error, report.Level);
        Assert.Contains("Incompatible game version (1.7A)", report.Tags);
    }

    [Fact]
    public void APackFromANewerModForgeIsAnError()
    {
        // Something it asks for arrived after this runtime was built, so it is
        // genuinely missing.
        var report = Judge(Pack(forge: "1.3.0"));

        Assert.Equal(PackStatus.Level.Error, report.Level);
        Assert.Contains("Needs ModForge 1.3.0", report.Tags);
    }

    // ── Warnings: it works, but it is not what you think ─────────────

    [Fact]
    public void APackFromAnOlderModForgeIsAWarning()
    {
        var report = Judge(Pack(forge: "1.0.0"));

        Assert.Equal(PackStatus.Level.Warning, report.Level);
        Assert.Contains("Built for ModForge 1.0.0", report.Tags);
    }

    [Fact]
    public void APackWithNoModForgeStampIsAWarning()
    {
        // "We cannot check" is a different thing from "we checked and it is
        // fine", and passing over it in silence would say the second.
        foreach (string missing in new[] { "", "   ", "who knows" })
        {
            var report = Judge(Pack(forge: missing));

            Assert.Equal(PackStatus.Level.Warning, report.Level);
            Assert.Contains("No ModForge version recorded", report.Tags);
        }
    }

    [Fact]
    public void APackInstalledTwiceIsAWarningThatNamesTheLiveCopy()
    {
        // This is how somebody edits a file all evening and sees nothing
        // change, so the row has to say WHICH one is being loaded.
        var report = Judge(Pack(shadowedIn: "ModPacks"));

        Assert.Equal(PackStatus.Level.Warning, report.Level);
        Assert.Contains("Installed twice, using from Mods folder", report.Tags);
    }

    // ── Several at once ──────────────────────────────────────────────

    [Fact]
    public void EveryProblemIsMentionedAndTheWorstSetsTheLevel()
    {
        // A pack can be several things at once. Mentioning only the worst would
        // send somebody to fix one problem while the other stayed, and they
        // would think they were finished.
        var report = Judge(Pack(game: "1.7A", forge: "1.0.0", shadowedIn: "ModPacks"));

        Assert.Equal(PackStatus.Level.Error, report.Level);
        Assert.Equal(3, report.Tags.Count);
        Assert.Contains("Incompatible game version (1.7A)", report.Tags);
        Assert.Contains("Built for ModForge 1.0.0", report.Tags);
        Assert.Contains("Installed twice, using from Mods folder", report.Tags);
    }

    [Fact]
    public void AWarningNeverQuietensAnError()
    {
        // The duplicate check runs last and raises the level; if it assigned
        // instead, a broken pack that happened to be installed twice would go
        // amber and read as harmless.
        var report = Judge(Pack(forge: "1.3.0", shadowedIn: "ModPacks"));

        Assert.Equal(PackStatus.Level.Error, report.Level);
        Assert.Contains("Needs ModForge 1.3.0", report.Tags);
        Assert.Contains("Installed twice, using from Mods folder", report.Tags);
    }

    // ── What is not evidence of anything ─────────────────────────────

    [Fact]
    public void AGameVersionThatCannotBeReadIsNotAMismatch()
    {
        // The stamp comes off the vanilla menu text and the pack's manifest.
        // Either being absent means we do not know, and a red row for "we do
        // not know" would appear on every pack written before the stamp.
        Assert.Equal(PackStatus.Level.Fine, PackStatus.Of(new PackStatus.Facts
        {
            GameVersion = "", RunningGameVersion = Running,
            ForgeVersion = Runtime, RuntimeForgeVersion = Runtime,
        }).Level);

        Assert.Equal(PackStatus.Level.Fine, PackStatus.Of(new PackStatus.Facts
        {
            GameVersion = "1.8E", RunningGameVersion = "",
            ForgeVersion = Runtime, RuntimeForgeVersion = Runtime,
        }).Level);
    }

    [Fact]
    public void TheSuffixReadsAsSomethingAPersonCanScan()
    {
        var report = PackStatus.Of(Pack(forge: "1.0.0", shadowedIn: "ModPacks"));
        string suffix = PackStatus.Suffix(report);
        _out.WriteLine("row would read: My Pack  v1.2.0" + suffix);

        Assert.StartsWith(" -  ", suffix);
        Assert.Equal(2, suffix.Split(new[] { " -  " }, System.StringSplitOptions.None).Length - 1);
    }

    // ── How many lines it takes ──────────────────────────────────────
    //
    // The menu places each row a fixed distance below the last, so whatever
    // draws them has to know how many there will be. Letting the text engine
    // wrap meant a row silently became two, the next row was placed as though
    // it had not, and the two landed on top of each other - which is exactly
    // what a screenshot of it looked like before this existed.

    private const int Width = 40;

    [Fact]
    public void APackWithNothingWrongIsOneLine()
    {
        var lines = PackStatus.Rows("My Pack  v1.2.0", PackStatus.Of(Pack()), Width);

        _out.WriteLine(string.Join("\n", lines));
        Assert.Equal("  \u2022 My Pack  v1.2.0", Assert.Single(lines));
    }

    [Fact]
    public void APacksProblemsFollowItOnTheSameLine()
    {
        var lines = PackStatus.Rows("TestPack", PackStatus.Of(Pack(forge: "")), Width);

        _out.WriteLine(string.Join("\n", lines));

        // The name and its problem read as one line, wrapped only where the
        // box runs out. Rejoined, that is exactly what it says.
        Assert.StartsWith("  \u2022 TestPack -  ", lines[0]);
        Assert.Equal("  \u2022 TestPack -  No ModForge version recorded", Rejoin(lines));
    }

    [Fact]
    public void ALineTooLongToFitIsWrappedRatherThanLeftToOverlap()
    {
        // The failure this replaced: the text engine wrapped the row, the next
        // row was placed one fixed stride down as though it had not, and the
        // two landed on top of each other.
        var lines = PackStatus.Rows("SMSAndroidsPack",
                                    PackStatus.Of(Pack(forge: "", shadowedIn: "ModPacks")),
                                    Width);

        _out.WriteLine(string.Join("\n", lines));

        Assert.True(lines.Count > 1, "this one cannot fit on a single line");
        Assert.All(lines, l => Assert.True(l.Length <= Width, l));

        // Every word survives the wrap, in order.
        Assert.Contains("No ModForge version recorded", Rejoin(lines));
        Assert.Contains("Installed twice, using from Mods folder", Rejoin(lines));
    }

    /// <summary>The wrapped lines read back as the one line they came from.</summary>
    private static string Rejoin(System.Collections.Generic.List<string> lines)
        => string.Join(" ", lines.Select(
            (l, i) => i == 0 ? l : l.Substring(PackStatus.Indent.Length)));

    [Fact]
    public void NoLineIsWiderThanTheBox()
    {
        // The one that matters. Every combination of problems, against a name
        // long enough to wrap on its own.
        foreach (var facts in new[]
                 {
                     Pack(),
                     Pack(forge: ""),
                     Pack(game: "1.7A", forge: "1.0.0", shadowedIn: "ModPacks"),
                     Pack(readable: false),
                     Pack(forge: "1.3.0", shadowedIn: "ModPacks"),
                 })
        {
            foreach (string label in new[]
                     {
                         "Short",
                         "A Pack With A Rather Long Display Name Indeed v10.20.30",
                     })
            {
                var lines = PackStatus.Rows(label, PackStatus.Of(facts), Width);
                foreach (string line in lines)
                    Assert.True(line.Length <= Width, line.Length + " chars: " + line);
            }
        }
    }

    [Fact]
    public void AWrappedLineIsIndentedUnderTheOneItContinues()
    {
        var lines = PackStatus.Rows(
            "A Pack With A Rather Long Display Name Indeed v10.20.30",
            PackStatus.Of(Pack()), Width);

        _out.WriteLine(string.Join("\n", lines));

        Assert.True(lines.Count > 1);
        for (int i = 1; i < lines.Count; i++)
            Assert.StartsWith(PackStatus.Indent, lines[i]);
    }

    [Fact]
    public void AWordTooLongToBreakIsLeftWholeRatherThanCut()
    {
        // A pack id run together without spaces is more use to somebody whole
        // than chopped in half - and cutting it would also produce a line the
        // next row could not be positioned against.
        string runOn = new string('x', 60);
        var lines = PackStatus.Wrap(runOn + " and then some more words here",
                                    Width, PackStatus.Indent);

        _out.WriteLine(string.Join("\n", lines));
        Assert.Contains(lines, l => l.Contains(runOn));
    }

    [Fact]
    public void WrappingNeverLosesOrInventsText()
    {
        // The control for the whole mechanism: a wrap that dropped a word would
        // be invisible until somebody noticed a tag had gone missing.
        const string text = "  \u2022 Some Pack Name That Goes On For A While v1.2.3";
        var lines = PackStatus.Wrap(text, Width, PackStatus.Indent);

        Assert.Equal(text, Rejoin(lines));
    }

    [Fact]
    public void WrappingSurvivesNonsense()
    {
        Assert.Empty(PackStatus.Wrap("", Width, PackStatus.Indent));
        Assert.Empty(PackStatus.Wrap(null!, Width, PackStatus.Indent));
        Assert.Single(PackStatus.Wrap("short", 0, PackStatus.Indent));
        Assert.Single(PackStatus.Wrap("short", Width, null!));
    }

    [Fact]
    public void NothingAtAllIsNotACrash()
    {
        // It runs while a menu is being built; throwing here would take the
        // banner with it.
        Assert.Equal(PackStatus.Level.Fine, PackStatus.Of(null!).Level);
        Assert.Equal("", PackStatus.Suffix(null!));
    }
}
