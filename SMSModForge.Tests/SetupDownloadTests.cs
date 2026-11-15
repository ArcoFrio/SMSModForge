extern alias setup;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using setup::SMSModForge.Setup;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A setup that carries nothing (1.9.0) downloads what it installs from ModForge's GitHub
/// release, checked against the size and SHA-256 GitHub lists. Driven through a stand-in
/// for the network: no test here touches GitHub, except the one asked for by
/// SMSMODFORGE_SETUP_ONLINE.
/// </summary>
public sealed class SetupDownloadTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-setupdl-" + Guid.NewGuid().ToString("N"));

    public SetupDownloadTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    /// <summary>The network, as a test says it is; every request it is asked for, kept.</summary>
    internal sealed class FakeNet : HttpMessageHandler
    {
        public readonly Dictionary<string, (HttpStatusCode Code, byte[] Body)> Answers = new();
        public readonly List<string> Asked = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            string url = request.RequestUri!.ToString();
            Asked.Add(url);
            var (code, body) = Answers.TryGetValue(url, out var a) ? a : (HttpStatusCode.NotFound, Array.Empty<byte>());
            return Task.FromResult(new HttpResponseMessage(code) { Content = new ByteArrayContent(body) });
        }
    }

    internal static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>GitHub's JSON for a release, in the shape the API really sends (read off the
    /// published v1.8.3, 2026-11-15): GitHub's dotted names, sizes, sha256 digests, and the
    /// setup itself among the assets.</summary>
    internal static string ReleaseJson(string version, byte[] editor, byte[] plugin, string? editorDigest = null)
        => "{\"tag_name\":\"v" + version + "\",\"name\":\"ModForge " + version + "\",\"assets\":["
         + Asset("Starmaker.-.ModForge.Editor." + version + ".zip", editor, editorDigest ?? "sha256:" + Sha(editor)) + ","
         + Asset("Starmaker.-.ModForge.Plugin." + version + ".zip", plugin, "sha256:" + Sha(plugin)) + ","
         + Asset("Starmaker.-.ModForge.Setup." + version + ".exe", new byte[] { 1, 2, 3 }, "sha256:00") + "]}";

    private static string Asset(string name, byte[] bytes, string digest)
        => "{\"name\":\"" + name + "\",\"size\":" + bytes.Length + ",\"digest\":\"" + digest
         + "\",\"browser_download_url\":\"" + Url(name) + "\"}";

    internal static string Url(string name) => "https://github.com/ArcoFrio/SMSModForge/releases/download/" + name;

    internal static string Api(string what) => "https://api.github.com/repos/ArcoFrio/SMSModForge/releases/" + what;

    [Fact]
    public void AReleaseIsReadTheWayGitHubSendsIt()
    {
        var found = ReleaseSource.Parse(ReleaseJson("1.9.0", new byte[10], new byte[4]))!;
        Assert.Equal("1.9.0", found.Version);
        Assert.Equal("Starmaker.-.ModForge.Editor.1.9.0.zip", found.Editor!.Name);
        Assert.Equal(10, found.Editor.Size);
        Assert.Equal(Sha(new byte[10]), found.Editor.Sha256);
        Assert.Equal("Starmaker.-.ModForge.Plugin.1.9.0.zip", found.Plugin!.Name);
        // The control: what is not a release is nothing, not an exception.
        Assert.Null(ReleaseSource.Parse("{\"message\":\"Not Found\"}"));
        Assert.Null(ReleaseSource.Parse("not json"));
    }

    [Fact]
    public async Task ADownloadIsCheckedBeforeItIsKept()
    {
        var bytes = Encoding.UTF8.GetBytes("the plugin");
        var net = new FakeNet();
        net.Answers[Url("p.zip")] = (HttpStatusCode.OK, bytes);
        var source = new ReleaseSource(net);
        string folder = Path.Combine(_root, "dl");

        string got = await source.FetchAsync(new ReleaseAsset("p.zip", Url("p.zip"), bytes.Length, Sha(bytes)), folder, null, default);
        Assert.Equal(bytes, File.ReadAllBytes(got));

        // Asked again, the file already there is the right one: not fetched twice.
        int asked = net.Asked.Count;
        await source.FetchAsync(new ReleaseAsset("p.zip", Url("p.zip"), bytes.Length, Sha(bytes)), folder, null, default);
        Assert.Equal(asked, net.Asked.Count);

        // A size or a digest that is not the release's is refused, and leaves nothing behind.
        var wrongSize = await Assert.ThrowsAsync<SetupProblem>(() =>
            source.FetchAsync(new ReleaseAsset("q.zip", Url("p.zip"), bytes.Length + 1, Sha(bytes)), folder, null, default));
        _out.WriteLine(wrongSize.Message);
        Assert.Equal("setup.download.problem.size", wrongSize.Key);
        var wrongSha = await Assert.ThrowsAsync<SetupProblem>(() =>
            source.FetchAsync(new ReleaseAsset("r.zip", Url("p.zip"), bytes.Length, Sha(new byte[1])), folder, null, default));
        _out.WriteLine(wrongSha.Message);
        Assert.Equal("setup.download.problem.changed", wrongSha.Key);
        // And one the release gives no checksum for: nothing could say it is the release's.
        var noSha = await Assert.ThrowsAsync<SetupProblem>(() =>
            source.FetchAsync(new ReleaseAsset("s.zip", Url("p.zip"), bytes.Length, null), folder, null, default));
        Assert.Equal("setup.download.problem.noChecksum", noSha.Key);
        Assert.False(File.Exists(Path.Combine(folder, "q.zip")));
        Assert.False(File.Exists(Path.Combine(folder, "r.zip")));
        Assert.False(File.Exists(Path.Combine(folder, "s.zip")));
        Assert.Empty(Directory.GetFiles(folder, "*.part"));
    }

    [Fact]
    public async Task ItFetchesItsOwnVersion_OrTheLatestWhenThatIsNotPublished()
    {
        var net = new FakeNet();
        net.Answers[Api("latest")] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(ReleaseJson("1.8.3", new byte[2], new byte[2])));
        var source = new ReleaseSource(net);

        // Not published (404): the latest.
        Assert.Equal("1.8.3", (await source.FindAsync("9.9.9", default)).Version);
        Assert.Contains(Api("tags/v9.9.9"), net.Asked);

        // Published: its own, not the latest.
        net.Answers[Api("tags/v1.9.0")] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(ReleaseJson("1.9.0", new byte[2], new byte[2])));
        Assert.Equal("1.9.0", (await source.FindAsync("1.9.0", default)).Version);
    }

    [Fact]
    public void ZipsBesideTheSetupAreFoundUnderEitherName()
    {
        string folder = Path.Combine(_root, "beside");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Starmaker.-.ModForge.Plugin.1.9.0.zip"), "");
        File.WriteAllText(Path.Combine(folder, "Starmaker - ModForge Editor 1.9.0.zip"), "");
        File.WriteAllText(Path.Combine(folder, "Something else.zip"), "");          // the control
        Assert.EndsWith("Starmaker.-.ModForge.Plugin.1.9.0.zip", Payload.Beside(folder, "Plugin"));
        Assert.EndsWith("Starmaker - ModForge Editor 1.9.0.zip", Payload.Beside(folder, "Editor"));
        Assert.Null(Payload.Beside(folder, "Setup"));
    }

    [Fact]
    public void AZipBesideTheSetupIsTakenOnlyWhenItIsTheSetupsOwnVersion()
    {
        // A plugin zip of an earlier version left in the same folder (Downloads) must not be
        // installed by a newer setup and registered under the newer one's number.
        string folder = Path.Combine(_root, "versions");
        Directory.CreateDirectory(folder);
        foreach (var v in new[] { "1.7.0", "1.9.0", "1.10.0" })
            File.WriteAllText(Path.Combine(folder, "Starmaker - ModForge Plugin " + v + ".zip"), "");
        Assert.EndsWith("Plugin 1.9.0.zip", Payload.Beside(folder, "Plugin", "1.9.0"));
        Assert.Null(Payload.Beside(folder, "Plugin", "1.8.0"));
        // Asked for the newest: by its number, so 1.10.0 is newer than 1.9.0 (a sort by the
        // name's letters put 1.9.0 last).
        Assert.EndsWith("Plugin 1.10.0.zip", Payload.Beside(folder, "Plugin"));
    }

    [Fact]
    public async Task ADownloadThatStallsFailsAsATimeout_WhileStopIsStillStop()
    {
        var net = new StallingNet();
        var source = new ReleaseSource(net) { StallTimeout = TimeSpan.FromMilliseconds(300), AskTimeout = TimeSpan.FromMilliseconds(300) };
        string folder = Path.Combine(_root, "stall");

        // No byte for longer than the stall time: a failure the page explains, not "stopped".
        var stalled = await Assert.ThrowsAsync<SetupProblem>(() =>
            source.FetchAsync(new ReleaseAsset("p.zip", "https://example.invalid/p.zip", 10, Sha(new byte[10])), folder, null, default));
        Assert.Equal("setup.download.problem.timeout", stalled.Key);
        Assert.Empty(Directory.GetFiles(folder, "*.part"));
        // ...and GitHub never answering about the release, the same.
        var asked = await Assert.ThrowsAsync<SetupProblem>(() => source.FindAsync("9.9.9", default));
        Assert.Equal("setup.download.problem.timeout", asked.Key);

        // The control: the person pressing Stop is passed on as stopped.
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var patient = new ReleaseSource(net) { StallTimeout = TimeSpan.FromSeconds(30) };
        var stopped = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            patient.FetchAsync(new ReleaseAsset("q.zip", "https://example.invalid/q.zip", 10, Sha(new byte[10])), folder, null, stop.Token));
        Assert.IsNotType<SetupProblem>(stopped);
        Assert.Empty(Directory.GetFiles(folder, "*.part"));
    }

    [Fact]
    public async Task GitHubsRefusalsAreToldInTheSetupsOwnWords()
    {
        var net = new FakeNet();
        net.Answers[Api("tags/v9.9.9")] = (HttpStatusCode.Forbidden, Array.Empty<byte>());
        var busy = await Assert.ThrowsAsync<SetupProblem>(() => new ReleaseSource(net).FindAsync("9.9.9", default));
        Assert.Equal("setup.download.problem.busy", busy.Key);
        net.Answers[Api("tags/v9.9.9")] = (HttpStatusCode.InternalServerError, Array.Empty<byte>());
        var error = await Assert.ThrowsAsync<SetupProblem>(() => new ReleaseSource(net).FindAsync("9.9.9", default));
        Assert.Equal("setup.download.problem.http", error.Key);
        Assert.Contains(("code", "500"), error.Gaps);
        // The control: a 404 is "not published", and the latest is asked for instead.
        net.Answers.Remove(Api("tags/v9.9.9"));
        var none = await Assert.ThrowsAsync<SetupProblem>(() => new ReleaseSource(net).FindAsync("9.9.9", default));
        Assert.Equal("setup.download.problem.noRelease", none.Key);
        Assert.Contains(Api("latest"), net.Asked);

        // Every problem is a text the setup carries, filled in, in German too.
        var english = SetupTexts.FromSelf(new CultureInfo("en-US"))!;
        var german = SetupTexts.FromSelf(new CultureInfo("de-DE"))!;
        foreach (var key in new[] { "noRelease", "noChecksum", "size", "changed", "busy", "http", "network", "timeout" })
        {
            string full = "setup.download.problem." + key;
            string said = german.F(full, ("file", "p.zip"), ("got", "1"), ("want", "2"), ("code", "500"));
            _out.WriteLine(key + ": " + said);
            Assert.DoesNotContain("setup.download", said);
            Assert.DoesNotContain("{", said);
            Assert.NotEqual(english.T(full), german.T(full));
        }
    }

    [Fact]
    public void OnlyTheUninstallersOwnNameBesideAnInstallUninstalls()
    {
        // The setup carries no editor since 1.9.0, so the setup saved into an install's
        // folder (a player downloading it there) looked like the uninstaller and took the
        // install away instead of installing.
        string install = Path.Combine(_root, "install");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, Manifest.FileName), "{}");
        Assert.True(Program.StartsAsUninstaller(Path.Combine(install, Installer.UninstallerExe), install, hasEditor: false));
        Assert.True(Program.StartsAsUninstaller(Path.Combine(install, Installer.UninstallerExe.ToUpperInvariant()), install, hasEditor: false));
        // The setup, the same program under its own name in the same folder: a setup.
        Assert.False(Program.StartsAsUninstaller(Path.Combine(install, "Starmaker - ModForge Setup 1.9.0.exe"), install, hasEditor: false));
        // A 1.8.x setup (it carries the editor) and an uninstaller with no install beside it.
        Assert.False(Program.StartsAsUninstaller(Path.Combine(install, Installer.UninstallerExe), install, hasEditor: true));
        string empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Assert.False(Program.StartsAsUninstaller(Path.Combine(empty, Installer.UninstallerExe), empty, hasEditor: false));
    }

    /// <summary>A network that answers a download's headers and then never sends a byte,
    /// and never answers GitHub's questions at all.</summary>
    private sealed class StallingNet : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                await Task.Delay(Timeout.Infinite, cancel);
                throw new InvalidOperationException("never");
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NeverStream()) };
        }

        private sealed class NeverStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel)
            {
                await Task.Delay(Timeout.Infinite, cancel);
                return 0;
            }
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
            {
                await Task.Delay(Timeout.Infinite, cancel);
                return 0;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    [Fact]
    public void ItCarriesItsOwnWordsInEveryLanguage()
    {
        // In the build itself, so a setup with nothing else speaks the player's language.
        var german = SetupTexts.FromSelf(new CultureInfo("de-DE"))!;
        var english = SetupTexts.FromSelf(new CultureInfo("en-US"))!;
        Assert.Equal("de", german.Code);
        Assert.NotEqual(english.T("setup.title"), german.T("setup.title"));
        Assert.Equal(SetupTexts.FromFolder(Path.Combine(TranslationCoverageTests.EditorDir, "Languages"), new CultureInfo("de-DE")).T("setup.title"),
                     german.T("setup.title"));
        // The control: a language ModForge does not ship reads English.
        Assert.Equal(english.T("setup.title"), SetupTexts.FromSelf(new CultureInfo("fi-FI"))!.T("setup.title"));
    }

    [Fact]
    public async Task TheRealLatestReleaseDownloadsAndChecks()
    {
        // Only when asked: this one talks to GitHub, and fetches the 4 MB plugin zip.
        if (Environment.GetEnvironmentVariable("SMSMODFORGE_SETUP_ONLINE") != "1") return;
        var source = new ReleaseSource();
        var release = await source.FindAsync("0.0.0", default);
        _out.WriteLine($"{release.Version}: {release.Editor?.Name} {release.Editor?.Size}, {release.Plugin?.Name} {release.Plugin?.Size} {release.Plugin?.Sha256}");
        Assert.NotNull(release.Editor);
        Assert.NotNull(release.Plugin!.Sha256);
        string got = await source.FetchAsync(release.Plugin, Path.Combine(_root, "real"), null, default);
        Assert.Null(ReleaseSource.Problem(got, release.Plugin));
        using var zip = ZipFile.OpenRead(got);
        Assert.Contains(zip.Entries, e => e.FullName.EndsWith("SMSModForge.PackPlugin.dll", StringComparison.Ordinal));
    }
}

/// <summary>The setup that carries nothing, through its window: it downloads only what the
/// install needs, installs it, and says what went wrong when it cannot.</summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class SetupDownloadWindowTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-setupdlw-" + Guid.NewGuid().ToString("N"));
    private readonly string _settingsWas = Places.EditorSettings;

    public SetupDownloadWindowTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_root);
        Program.Testing = true;
        Places.EditorSettings = Path.Combine(_root, "editor settings");
    }

    public void Dispose()
    {
        Places.EditorSettings = _settingsWas;
        try { Directory.Delete(_root, true); } catch (IOException) { }
        // What the downloads left for the test versions.
        foreach (var v in new[] { "7.7.1", "7.7.2" })
            try { Directory.Delete(ReleaseSource.DownloadFolder(v), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static SetupTexts English()
        => SetupTexts.FromFolder(Path.Combine(TranslationCoverageTests.EditorDir, "Languages"), new CultureInfo("en-US"));

    private string Game()
    {
        string game = Path.Combine(_root, "Starmaker Story");
        Directory.CreateDirectory(Path.Combine(game, GameFolder.Data));
        File.WriteAllText(Path.Combine(game, GameFolder.Exe), "");
        return game;
    }

    /// <summary>A setup with nothing in it but the uninstaller.</summary>
    private static Payload Empty() => new()
    {
        Uninstaller = () => new MemoryStream(Encoding.UTF8.GetBytes("uninstaller")),
    };

    private static byte[] Zip(Payload p, Func<Payload, Func<Stream?>> which)
    {
        using var s = which(p)()!;
        var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    private (SetupWindow Window, SetupDownloadTests.FakeNet Net) Open(SetupTests.FakeWindows windows, string version, HttpStatusCode release = HttpStatusCode.OK)
    {
        var fake = SetupTests.FakePayload();
        byte[] editor = Zip(fake, p => p.Editor), plugin = Zip(fake, p => p.Plugin);
        var net = new SetupDownloadTests.FakeNet();
        net.Answers[SetupDownloadTests.Api("latest")] = (release, Encoding.UTF8.GetBytes(SetupDownloadTests.ReleaseJson(version, editor, plugin)));
        net.Answers[SetupDownloadTests.Url("Starmaker.-.ModForge.Editor." + version + ".zip")] = (HttpStatusCode.OK, editor);
        net.Answers[SetupDownloadTests.Url("Starmaker.-.ModForge.Plugin." + version + ".zip")] = (HttpStatusCode.OK, plugin);
        var window = new SetupWindow(English(), Empty(), windows, Game(), Path.Combine(_root, "ModForge"), new ReleaseSource(net))
        {
            Left = -10000, Top = -10000, ShowInTaskbar = false,
        };
        window.Show();
        window.UpdateLayout();
        return (window, net);
    }

    private static void Press(System.Windows.Controls.Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        WindowHarness.Pump();
    }

    private static void UntilEnd(SetupWindow window, SetupTexts texts)
    {
        var deadline = DateTime.Now.AddSeconds(20);
        bool Busy() => window.Step != SetupWindow.EndStep || window.EndHeading == texts.T("setup.installing")
                       || window.EndHeading == texts.T("setup.download.finding") || window.EndHeading.StartsWith("Downloading", StringComparison.Ordinal);
        while (Busy() && DateTime.Now < deadline) { WindowHarness.Pump(); Thread.Sleep(20); }
        window.UpdateLayout();
    }

    [Fact]
    public void OnlyThePluginDownloadsOnlyThePlugin_ThenInstallsIt()
    {
        WindowHarness.Run(_ =>
        {
            var texts = English();
            var windows = new SetupTests.FakeWindows(Path.Combine(_root, "windows"));
            var (window, net) = Open(windows, "7.7.1");
            Assert.True(window.PluginOnly.IsChecked);
            Press(window.Next);                         // to the game page, which installs
            Press(window.Next);
            UntilEnd(window, texts);
            foreach (var asked in net.Asked) _out.WriteLine(asked);

            Assert.Null(window.Problem);
            Assert.Equal(texts.T("setup.done.pluginHeading"), window.EndHeading);
            Assert.Equal("7.7.1", window.Downloaded);
            Assert.Contains(net.Asked, u => u.Contains("ModForge.Plugin."));
            // The control: the editor - many times larger - was never fetched.
            Assert.DoesNotContain(net.Asked, u => u.Contains("ModForge.Editor."));
            string game = Path.Combine(_root, "Starmaker Story");
            Assert.True(File.Exists(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
            // Registered as the version it installed.
            Assert.Equal("7.7.1", Manifest.Read(Path.Combine(_root, "ModForge"))!.Version);
            // And the downloads, installed, are not left in the player's temporary folder.
            Assert.False(Directory.Exists(ReleaseSource.DownloadFolder("7.7.1")));
            window.Close();
        });
    }

    [Fact]
    public void AnInstallWithTheEditorIsOfferedTheEditorAgain()
    {
        WindowHarness.Run(_ =>
        {
            // Updating an install that has the editor: "Only the plugin" chosen by default
            // would leave that editor a version behind the plugin.
            Directory.CreateDirectory(Path.Combine(_root, "ModForge"));
            File.WriteAllText(Path.Combine(_root, "ModForge", Installer.EditorExe), "");
            var (window, _) = Open(new SetupTests.FakeWindows(Path.Combine(_root, "windows")), "7.7.1");
            Assert.True(window.Everything.IsChecked);
            Assert.False(window.PluginOnly.IsChecked);
            window.Close();
            // The control, the folder with no editor: OnlyThePlugin... above, checked there.
        });
    }

    [Fact]
    public void EverythingDownloadsBothZips_AndADownloadThatFailsInstallsNothing()
    {
        WindowHarness.Run(_ =>
        {
            var texts = English();
            var windows = new SetupTests.FakeWindows(Path.Combine(_root, "windows"));
            var (window, net) = Open(windows, "7.7.2");
            window.Everything.IsChecked = true;
            WindowHarness.Pump();
            Press(window.Next);
            Press(window.Next);
            Press(window.Next);                         // the folder page installs
            UntilEnd(window, texts);
            Assert.Null(window.Problem);
            Assert.True(File.Exists(Path.Combine(_root, "ModForge", Installer.EditorExe)));
            Assert.Contains(net.Asked, u => u.Contains("ModForge.Editor."));
            window.Close();

            // GitHub answering with an error: nothing installed, and the page says where the
            // zips can be had to install without the internet.
            var other = new SetupTests.FakeWindows(Path.Combine(_root, "windows2"));
            Directory.Delete(Path.Combine(_root, "Starmaker Story"), true);
            var (failing, _) = Open(other, "7.7.2", HttpStatusCode.InternalServerError);
            // The editor installed above makes "Everything" the default now.
            failing.PluginOnly.IsChecked = true;
            WindowHarness.Pump();
            Press(failing.Next);
            Press(failing.Next);
            UntilEnd(failing, texts);
            _out.WriteLine(failing.EndText);
            Assert.Equal(texts.T("setup.download.failedHeading"), failing.EndHeading);
            Assert.Contains(ReleaseSource.ReleasesPage, failing.EndText);
            // What happened, in the setup's own words rather than an exception's English.
            Assert.Contains(texts.F("setup.download.problem.http", ("code", "500")), failing.EndText);
            Assert.DoesNotContain("Response status code", failing.EndText);
            Assert.DoesNotContain("setup.download.problem", failing.EndText);
            Assert.Null(other.Registered);
            Assert.False(File.Exists(Path.Combine(_root, "Starmaker Story", "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
            failing.Close();
        });
    }
}
