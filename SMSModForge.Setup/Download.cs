using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SMSModForge.Setup;

/// <summary>One file a release offers: its name, where to fetch it, its size and,
/// when GitHub gives one, its SHA-256.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

/// <summary>A release, and the two zips an install needs from it.</summary>
public sealed record ReleaseFound(string Version, ReleaseAsset? Editor, ReleaseAsset? Plugin);

/// <summary>
/// Why a download failed, as a text of the setup's own (setup.download.problem.*)
/// with its gaps, so the failure page says it in the player's language rather than in
/// the English of an exception.
/// </summary>
public sealed class SetupProblem : Exception
{
    public string Key { get; }
    public (string Name, string Value)[] Gaps { get; }

    public SetupProblem(string key, params (string Name, string Value)[] gaps)
        : base(key + (gaps.Length == 0 ? "" : " (" + string.Join(", ", gaps.Select(g => g.Name + "=" + g.Value)) + ")"))
    {
        Key = key;
        Gaps = gaps;
    }
}

/// <summary>
/// Where a setup that carries nothing gets what it installs (1.9.0): the zips on
/// ModForge's GitHub release, the same two the editor's own updates download.
/// <para/>
/// The release of the setup's own version, so a setup installs what it says it
/// does; the latest when that one is not there (a setup built before its release was
/// published). Every file is checked against the size and the SHA-256 GitHub lists
/// before anything is unpacked: a download that stopped half-way, or was changed on the
/// way, is refused rather than installed - and so is one the release gives no SHA-256
/// for, since nothing could then say it is the release's.
/// </summary>
public sealed class ReleaseSource
{
    public const string Repo = "ArcoFrio/SMSModForge";

    /// <summary>Where the releases are, for a person: said when the download fails.</summary>
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases";

    /// <summary>
    /// Point the setup at another release description - a file, or a URL serving GitHub's
    /// JSON - for trying it without publishing anything (as the editor's
    /// SMSMODFORGE_UPDATE_FEED does).
    /// </summary>
    public const string FeedVariable = "SMSMODFORGE_SETUP_FEED";

    private readonly HttpClient _http;

    /// <summary>How long asking GitHub about a release may take before it counts as
    /// failed: the page says "Finding ModForge on GitHub" meanwhile.</summary>
    public TimeSpan AskTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a download may go without a byte arriving before it counts as
    /// failed - a connection that stalled, rather than a slow one.</summary>
    public TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public ReleaseSource(HttpMessageHandler? handler = null)
    {
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(30);
        // GitHub refuses a request with no User-Agent. Nothing about the person in it.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SMSModForge-Setup/" + SMSModForge.Shared.ForgeVersion.Current);
    }

    /// <summary>The release to install from: this version's, else the latest.</summary>
    public async Task<ReleaseFound> FindAsync(string version, CancellationToken cancel)
    {
        string? feed = Environment.GetEnvironmentVariable(FeedVariable);
        if (!string.IsNullOrWhiteSpace(feed))
        {
            string json = File.Exists(feed) ? await File.ReadAllTextAsync(feed, cancel) : await GetJson(feed, cancel) ?? "";
            return Parse(json) ?? throw new SetupProblem("setup.download.problem.noRelease");
        }

        string api = "https://api.github.com/repos/" + Repo + "/releases/";
        string? own = await GetJson(api + "tags/v" + version, cancel);
        var found = own == null ? null : Parse(own);
        if (found != null && found.Plugin != null) return found;

        string? latest = await GetJson(api + "latest", cancel);
        return (latest == null ? null : Parse(latest))
               ?? throw new SetupProblem("setup.download.problem.noRelease");
    }

    /// <summary>The JSON at <paramref name="url"/>, or null for a 404 (no such release).</summary>
    private async Task<string?> GetJson(string url, CancellationToken cancel)
    {
        using var asking = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        asking.CancelAfter(AskTimeout);
        return await Net(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _http.SendAsync(request, asking.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(asking.Token);
        }, cancel);
    }

    /// <summary>
    /// A call to the network, its failures said as the setup's own texts. A wait cut
    /// short by a timeout is told apart from the person pressing Stop: only Stop
    /// cancels <paramref name="cancel"/>, and only that is passed on as cancelled.
    /// </summary>
    private static async Task<T> Net<T>(Func<Task<T>> call, CancellationToken cancel)
    {
        try { return await call(); }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new SetupProblem("setup.download.problem.timeout");
        }
        catch (HttpRequestException e)
        {
            throw e.StatusCode switch
            {
                // GitHub's answer to too many questions from one network.
                HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => new SetupProblem("setup.download.problem.busy"),
                null => new SetupProblem("setup.download.problem.network"),
                var code => new SetupProblem("setup.download.problem.http", ("code", ((int)code).ToString(CultureInfo.InvariantCulture))),
            };
        }
    }

    /// <summary>
    /// A release out of GitHub's JSON, or null when it is not one. The zips are told
    /// apart by what is in their names, as the editor's updates do: GitHub turns the
    /// spaces of "Starmaker - ModForge Editor 1.9.0.zip" into dots.
    /// </summary>
    public static ReleaseFound? Parse(string json)
    {
        JsonNode? o;
        try { o = JsonNode.Parse(json); }
        catch (Exception) { return null; }
        string tag = o?["tag_name"]?.GetValue<string>() ?? "";
        if (tag.Length == 0) return null;
        var assets = (o?["assets"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(a => new ReleaseAsset(
            a["name"]?.GetValue<string>() ?? "",
            a["browser_download_url"]?.GetValue<string>() ?? "",
            a["size"]?.GetValue<long>() ?? 0,
            DigestOf(a["digest"]?.GetValue<string>()))).ToList();
        ReleaseAsset? Zip(string word) => assets.FirstOrDefault(a =>
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && a.Url.Length > 0
            && a.Name.Replace('.', ' ').Contains("ModForge " + word, StringComparison.OrdinalIgnoreCase));
        return new ReleaseFound(tag.TrimStart('v', 'V'), Zip("Editor"), Zip("Plugin"));
    }

    private static string? DigestOf(string? digest)
        => digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest.Substring(7).ToLowerInvariant() : null;

    /// <summary>
    /// Download one file into <paramref name="folder"/>, checked; its path. A file
    /// already there that is the right one is kept, so starting again after a failure
    /// does not fetch it twice.
    /// </summary>
    public async Task<string> FetchAsync(ReleaseAsset asset, string folder, IProgress<long>? progress, CancellationToken cancel)
    {
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, SafeName(asset.Name));
        // Hashing 80 MB takes a moment: off the window's thread.
        if (File.Exists(target) && await Task.Run(() => Problem(target, asset)) == null) { progress?.Report(asset.Size); return target; }

        string part = target + ".part";
        if (File.Exists(part)) File.Delete(part);
        if (asset.Url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || File.Exists(asset.Url))
        {
            // A local file (tests, and a feed pointing at zips on disk).
            string from = asset.Url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(asset.Url).LocalPath : asset.Url;
            File.Copy(from, part, overwrite: true);
            progress?.Report(new FileInfo(part).Length);
        }
        else
        {
            try
            {
                await Net(async () =>
                {
                    // A stall is no byte for StallTimeout; the clock starts again with
                    // every one that arrives.
                    using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                    stall.CancelAfter(StallTimeout);
                    using var response = await _http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                    response.EnsureSuccessStatusCode();
                    await using var body = await response.Content.ReadAsStreamAsync(stall.Token);
                    await using var file = File.Create(part);
                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await body.ReadAsync(buffer, stall.Token)) > 0)
                    {
                        stall.CancelAfter(StallTimeout);
                        await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                        done += read;
                        progress?.Report(done);
                    }
                    return done;
                }, cancel);
            }
            catch (Exception)
            {
                // Stopped or failed: nothing half-made is left behind.
                try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { }
                throw;
            }
        }

        var problem = await Task.Run(() => Problem(part, asset));
        if (problem != null)
        {
            File.Delete(part);
            throw problem;
        }
        if (File.Exists(target)) File.Delete(target);
        File.Move(part, target);
        return target;
    }

    /// <summary>What is wrong with a downloaded file, or null when it is the one listed:
    /// its size and its SHA-256 both the release's. A file the release gives no SHA-256
    /// for is refused - nothing could say it is the release's.</summary>
    public static SetupProblem? Problem(string file, ReleaseAsset asset)
    {
        long size = new FileInfo(file).Length;
        if (asset.Size > 0 && size != asset.Size)
            return new SetupProblem("setup.download.problem.size", ("file", asset.Name),
                                    ("got", size.ToString(CultureInfo.InvariantCulture)),
                                    ("want", asset.Size.ToString(CultureInfo.InvariantCulture)));
        if (asset.Sha256 == null) return new SetupProblem("setup.download.problem.noChecksum", ("file", asset.Name));
        using var stream = File.OpenRead(file);
        string sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return sha == asset.Sha256 ? null : new SetupProblem("setup.download.problem.changed", ("file", asset.Name));
    }

    private static string SafeName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length == 0 ? "download.zip" : name;
    }

    /// <summary>Where downloads wait until they are installed: one folder per version,
    /// in the person's temporary folder. The setup empties it once they are.</summary>
    public static string DownloadFolder(string version)
        => Path.Combine(Path.GetTempPath(), "SMSModForge-setup", version);
}
