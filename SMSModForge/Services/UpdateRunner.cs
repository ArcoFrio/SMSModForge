using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SMSModForge.Services;

/// <summary>
/// The whole update, from "is there one" to "the editor is closing to install
/// it", with the window told what to show at each step.
/// <para/>
/// It holds no UI of its own. The four callbacks are the entire surface: the
/// window supplies them, and how each one looks — a toast, a dialog, a window
/// closing — is the window's business. That keeps every decision about WHETHER
/// to do something here, where it can be read in one place, and every decision
/// about how it looks there, where the rest of the editor's look lives.
/// </summary>
public sealed class UpdateRunner
{
    /// <summary>Show a progress line that stays up. Called repeatedly.</summary>
    public Action<string>? Say { get; set; }

    /// <summary>Take the progress line down.</summary>
    public Action? Done { get; set; }

    /// <summary>Offer a release; true means install it. Null callback means
    /// nothing can be asked, so nothing is installed.</summary>
    public Func<ReleaseInfo, bool>? Ask { get; set; }

    /// <summary>An answer somebody is waiting for. How it is shown depends on
    /// who asked: a dialog for the manual check, a line at the top of the
    /// window for the check the editor made on its own.</summary>
    public Action<string>? Report { get; set; }

    /// <summary>Something went wrong. Always shown, always waited for, and never
    /// through the progress line — that gets overwritten by whatever the update
    /// says next, which is how a plugin that failed to install managed to say so
    /// for a fraction of a second and then not at all.</summary>
    public Action<string>? Problem { get; set; }

    /// <summary>Close the editor so the staged build can replace it. Returns
    /// false if the author cancelled — an unsaved pack, most likely — in which
    /// case the update is abandoned and the files stay as they are.</summary>
    public Func<bool>? CloseForUpdate { get; set; }

    /// <summary>True while the check is one the editor made on its own, which
    /// is the difference between "worth interrupting an author for" and not.
    /// A FAILED install still reports either way - by then something was
    /// started and stopping halfway without a word would be worse.</summary>
    private bool _quiet;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    static UpdateRunner()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SMSModForge/" + UpdateFeed.RunningVersion.ToString(3));
    }

    /// <summary>
    /// The check the editor makes on its own, when it starts.
    /// <para/>
    /// Silent about everything except finding something: no network, no
    /// releases, a rate limit, a repository that is not public yet — none of
    /// those are the author's problem to be told about while they are opening a
    /// pack.
    /// </summary>
    public async Task OnStartupAsync(CancellationToken cancel = default)
    {
        _quiet = true;

        // The harness builds real windows, and a window that phones home on
        // Loaded would have every test run reaching out from the machine of
        // whoever ran it - slowly, and for an answer nothing reads.
        if (TestMode.Active) return;
        if (!EditorPrefs.CheckForUpdatesOnStart) return;

        // Anything staged by a previous update has served its purpose by now:
        // this editor is running, so it either worked or was abandoned.
        UpdateInstaller.CleanStaging();

        var release = await UpdateFeed.CheckAsync(cancel).ConfigureAwait(true);
        if (release == null) return;

        await OfferAsync(release, cancel).ConfigureAwait(true);
    }

    /// <summary>
    /// The check somebody asked for from the menu, which answers either way —
    /// a manual check that says nothing is indistinguishable from one that did
    /// not run.
    /// </summary>
    public async Task OnDemandAsync(CancellationToken cancel = default)
    {
        _quiet = false;
        string? failure = null;
        var release = await UpdateFeed.CheckAsync(cancel, f => failure = f).ConfigureAwait(true);

        if (release == null)
        {
            Report?.Invoke(failure ?? $"You are up to date — this is {UpdateFeed.RunningVersion.ToString(3)}.");
            return;
        }
        await OfferAsync(release, cancel).ConfigureAwait(true);
    }

    private async Task OfferAsync(ReleaseInfo release, CancellationToken cancel)
    {
        if (release.EditorZipUrl == null)
        {
            // A release exists but its editor asset is missing or still
            // uploading. Saying "there is an update" and then failing to
            // install it would be worse than the silence.
            if (!_quiet)
                Report?.Invoke($"Version {release.VersionText} has been published, but "
                             + "its editor download is not there yet. Try again shortly.");
            return;
        }

        if (!EditorPrefs.InstallUpdatesWithoutAsking)
        {
            if (Ask?.Invoke(release) != true) return;
        }

        await InstallAsync(release, cancel).ConfigureAwait(true);
    }

    /// <summary>
    /// Download, unpack, put the plugin in place, and hand over to the staged
    /// build. Everything before the hand-over is reversible by doing nothing.
    /// </summary>
    public async Task InstallAsync(ReleaseInfo release, CancellationToken cancel = default)
    {
        try
        {
            string folder = UpdateInstaller.StagingFor(release.VersionText);
            Directory.CreateDirectory(folder);

            // ── The editor ────────────────────────────────────────────
            Say?.Invoke($"Downloading {release.VersionText}…");
            string editorZip = Path.Combine(folder, "editor.zip");
            var progress = new Progress<double>(p =>
                Say?.Invoke($"Downloading {release.VersionText}… {p * 100:0}%"));
            await UpdateInstaller.DownloadAsync(Http, release.EditorZipUrl!, editorZip, progress, cancel)
                                 .ConfigureAwait(true);

            Say?.Invoke("Unpacking…");
            string? staged = UpdateInstaller.StageEditorZip(editorZip, release.VersionText, out var problem);
            if (staged == null)
            {
                Fail(problem ?? "The download could not be unpacked.");
                return;
            }

            // ── The plugin, while the editor still runs ───────────────
            //
            // Before the hand-over on purpose. Nothing holds these files open,
            // so they can be replaced now, and doing it here means a failure
            // here is a failure with the old editor still on screen to say so.
            if (release.PluginZipUrl != null && UpdateInstaller.IsGameFolder(EditorPrefs.GameFolder))
            {
                Say?.Invoke("Updating the plugin…");
                string trouble = "";
                try
                {
                    string pluginZip = Path.Combine(folder, "plugin.zip");
                    await UpdateInstaller.DownloadAsync(Http, release.PluginZipUrl, pluginZip, null, cancel)
                                         .ConfigureAwait(true);

                    var plugin = UpdateInstaller.ApplyPluginZip(pluginZip, EditorPrefs.GameFolder);
                    if (!plugin.Complete)
                        trouble = string.Join("\n", plugin.Failed.Select(f => $"  {f.File} — {f.Why}"));
                }
                catch (Exception ex)
                {
                    trouble = "  " + ex.Message;
                }

                // Not fatal: an editor newer than its plugin is a mismatch worth
                // stopping to say, but it is not worth throwing away an editor
                // update that is already downloaded and unpacked. Said BEFORE the
                // hand-over, while there is still an editor on screen to say it.
                if (trouble.Length > 0)
                    Problem?.Invoke(
                        "The editor will still be updated, but the plugin in your game "
                      + "folder could not be replaced:\n\n" + trouble
                      + "\n\nThe usual reason is something holding the file open — the "
                      + "game, or a build that put it there. Close it and run "
                      + "Options ▸ Check for updates now again, or unpack the plugin zip "
                      + "over your game folder by hand. Until then the two are out of "
                      + "step, and a pack written against one can fail against the other.");
            }

            // ── Hand over ─────────────────────────────────────────────
            Say?.Invoke($"Restarting to finish {release.VersionText}…");

            if (!UpdateInstaller.LaunchApplier(staged, UpdateInstaller.InstallFolder, out var launchProblem))
            {
                Fail(launchProblem ?? "The update could not be started.");
                return;
            }

            // The applier is waiting on this process. If the author cancels the
            // close - an unsaved pack they want to keep - it gives up after its
            // wait and nothing has been replaced.
            if (CloseForUpdate?.Invoke() == false) Done?.Invoke();
        }
        catch (OperationCanceledException)
        {
            Done?.Invoke();
        }
        catch (Exception ex)
        {
            Fail("The update could not be installed: " + ex.Message);
        }
    }

    private void Fail(string message)
    {
        Done?.Invoke();
        // Through Problem where there is one: this ends the update, and ending
        // it in a line the next thing overwrites is how nothing gets said.
        (Problem ?? Report)?.Invoke(message);
    }
}
