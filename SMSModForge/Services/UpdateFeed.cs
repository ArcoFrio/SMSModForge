using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Services;

/// <summary>
/// Where new versions are announced, and how the editor reads that.
/// <para/>
/// The releases of <c>ArcoFrio/SMSModForge</c>, through GitHub's public API.
/// Unauthenticated, because an editor that shipped a token would be handing
/// that token to everybody who unzipped it — which also means the repository
/// has to be public for any of this to answer. Until it is, every check comes
/// back "nothing", quietly, which is exactly what it does when somebody is
/// working on a train.
/// <para/>
/// Nothing here throws at its caller and nothing here blocks the editor. A
/// check that fails is a check that found no update: there is no version of
/// "your editor could not phone home" worth interrupting an author for. The
/// one exception is the manual Check for updates, which asks on purpose and so
/// deserves an answer either way — <see cref="CheckAsync"/> reports through
/// <paramref name="failure"/> for that caller alone.
/// </summary>
public static class UpdateFeed
{
    /// <summary>The repository releases are published from.</summary>
    public const string Repo = "ArcoFrio/SMSModForge";

    /// <summary>
    /// Point the check somewhere else — a file path or a URL serving the same
    /// JSON shape GitHub does.
    /// <para/>
    /// This is how the whole feature gets exercised without publishing a
    /// release: write a release payload naming a version above this one, set
    /// the variable, start the editor. See Tools/FakeRelease.py, which builds
    /// both the payload and the zips it points at.
    /// </summary>
    public const string FeedOverrideVariable = "SMSMODFORGE_UPDATE_FEED";

    /// <summary>
    /// Pretend to be an older editor than this one is, so a real release looks
    /// new. The other half of testing this without publishing anything.
    /// </summary>
    public const string VersionOverrideVariable = "SMSMODFORGE_UPDATE_PRETEND_VERSION";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub rejects a request with no User-Agent outright, and asks that
        // it name the application. The Accept header pins the response shape to
        // the documented one rather than whatever the default becomes.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SMSModForge/" + RunningVersion.ToString(3));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>
    /// The version this editor reports itself as, which the override can move
    /// so a published release looks newer than the running build.
    /// </summary>
    public static Version RunningVersion
    {
        get
        {
            var pretend = Environment.GetEnvironmentVariable(VersionOverrideVariable);
            if (TryParseVersion(pretend, out var faked)) return faked;

            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            return asm.GetName().Version ?? new Version(0, 0, 0);
        }
    }

    /// <summary>
    /// The latest release if it is newer than <see cref="RunningVersion"/>,
    /// otherwise null.
    /// </summary>
    /// <param name="failure">Set to something an author can read when the check
    /// could not be made at all. Only the manual check shows it.</param>
    public static async Task<ReleaseInfo?> CheckAsync(
        CancellationToken cancel = default, Action<string>? failure = null)
    {
        string json;
        try
        {
            json = await FetchAsync(cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            failure?.Invoke("The check was cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            failure?.Invoke("Could not reach the update server: " + ex.Message);
            return null;
        }

        ReleaseInfo? release;
        try
        {
            release = ParseLatest(json);
        }
        catch (Exception ex)
        {
            failure?.Invoke("The update server answered with something unreadable: " + ex.Message);
            return null;
        }

        if (release == null)
        {
            failure?.Invoke("No release has been published yet.");
            return null;
        }
        return release.Version > RunningVersion ? release : null;
    }

    private static async Task<string> FetchAsync(CancellationToken cancel)
    {
        var over = Environment.GetEnvironmentVariable(FeedOverrideVariable);
        if (!string.IsNullOrWhiteSpace(over))
        {
            // A local file is the common case for testing, so it is tried
            // first and by its own rules - no HTTP, no headers, no network.
            if (File.Exists(over)) return await File.ReadAllTextAsync(over, cancel).ConfigureAwait(false);
            return await Http.GetStringAsync(over, cancel).ConfigureAwait(false);
        }

        return await Http.GetStringAsync(
            $"https://api.github.com/repos/{Repo}/releases/latest", cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// Read one release out of the API's JSON. Public and separate from the
    /// fetch so it can be tested against a recorded payload rather than the
    /// network.
    /// </summary>
    /// <returns>The release, or null when the payload is not one (an empty
    /// object, or the "Not Found" GitHub sends for a private repository).</returns>
    public static ReleaseInfo? ParseLatest(string json)
    {
        var o = JObject.Parse(json);

        string tag = (string?)o["tag_name"] ?? "";
        if (!TryParseVersion(tag, out var version)) return null;

        // Assets are matched on what is in the name, not on the whole of it:
        // the file is called "Starmaker - ModForge Editor 1.1.0.zip", so any
        // exact match would have to be rebuilt for every version, and would
        // break the day somebody renames the zip.
        var assets = o["assets"] as JArray ?? new JArray();
        string? Find(string word) => assets
            .Select(a => (string?)a?["browser_download_url"])
            .Where(u => !string.IsNullOrEmpty(u))
            .FirstOrDefault(u => NameOf(u!).Contains(word, StringComparison.OrdinalIgnoreCase)
                                 && NameOf(u!).EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        long Size(string word) => assets
            .Where(a => ((string?)a?["name"] ?? "").Contains(word, StringComparison.OrdinalIgnoreCase))
            .Select(a => (long?)a?["size"] ?? 0)
            .FirstOrDefault();

        string name = (string?)o["name"] ?? "";
        return new ReleaseInfo(
            Version: version,
            Name: string.IsNullOrWhiteSpace(name) ? tag : name,
            Notes: ((string?)o["body"] ?? "").Replace("\r\n", "\n").Trim(),
            EditorZipUrl: Find("Editor"),
            PluginZipUrl: Find("Plugin"),
            EditorZipBytes: Size("Editor"));
    }

    private static string NameOf(string url)
    {
        int slash = url.LastIndexOf('/');
        return slash >= 0 ? url.Substring(slash + 1) : url;
    }

    /// <summary>
    /// Parse a release tag. Accepts <c>v1.2.0</c>, <c>1.2.0</c> and <c>1.2</c>;
    /// drops a prerelease suffix, so <c>v1.2.0-rc1</c> reads as 1.2.0.
    /// </summary>
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        string s = tag!.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
        int dash = s.IndexOf('-');
        if (dash >= 0) s = s.Substring(0, dash);
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s.Substring(0, plus);

        if (!Version.TryParse(s, out var parsed)) return false;

        // Version compares -1 as less than 0, so an unstated revision would make
        // 1.2.0 look OLDER than 1.2.0.0. Normalise to three parts, which is what
        // the tags carry.
        version = new Version(parsed.Major,
                              Math.Max(parsed.Minor, 0),
                              Math.Max(parsed.Build, 0));
        return true;
    }
}
