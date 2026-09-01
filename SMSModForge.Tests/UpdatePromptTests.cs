using System;
using System.IO;
using SMSModForge.Services;
using SMSModForge.View;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// What the update prompt says about the plugin, and when it offers to fix the
/// reason it cannot install one.
/// <para/>
/// The plugin and the editor are versioned together, so this line is the one
/// thing in the window somebody might need to act on before saying yes.
/// Naming a menu they would have to close the window to reach was a dead end;
/// the offer belongs where the fact is.
/// </summary>
public class UpdatePromptTests : IDisposable
{
    private readonly string _temp;

    public UpdatePromptTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "smsmf-prompt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_temp, "BepInEx", "plugins"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { /* temp */ }
    }

    private static ReleaseInfo Release(bool withPlugin) => new(
        Version: new Version(1, 2, 0),
        Name: "ModForge 1.2.0",
        Notes: "notes",
        EditorZipUrl: "https://example.invalid/Editor.zip",
        PluginZipUrl: withPlugin ? "https://example.invalid/Plugin.zip" : null,
        EditorZipBytes: 80_000_000);

    [Fact]
    public void With_the_folder_set_it_says_the_plugin_is_covered()
    {
        var (text, offer) = UpdateWindow.PluginLine(Release(withPlugin: true), _temp);

        Assert.Contains("will be updated", text);
        // The reassurance is the point of the sentence: this is the only thing
        // an update writes outside its own folder.
        Assert.Contains("packs are not touched", text);
        Assert.False(offer);
    }

    [Fact]
    public void With_no_folder_set_it_offers_to_set_one()
    {
        var (text, offer) = UpdateWindow.PluginLine(Release(withPlugin: true), "");

        Assert.True(offer, "the button is the whole point when the folder is unknown");
        Assert.Contains("does not know", text);
        // It must also say what happens if they decline, because declining is
        // a perfectly good answer and updating the editor alone still works.
        Assert.Contains("only the editor changes", text);
    }

    [Fact]
    public void A_folder_that_is_not_a_game_folder_is_the_same_as_none()
    {
        // The control for the case above: a stored path that no longer has
        // BepInEx in it - the game moved, or was reinstalled - must offer to fix
        // itself rather than quietly promising something it cannot do.
        string notAGame = Path.Combine(_temp, "somewhere-else");
        Directory.CreateDirectory(notAGame);

        var (_, offer) = UpdateWindow.PluginLine(Release(withPlugin: true), notAGame);

        Assert.True(offer);
    }

    [Fact]
    public void A_release_with_no_plugin_never_offers_the_folder()
    {
        // Nothing to install there, so asking where the game is would be asking
        // for something that will not be used.
        var (text, offer) = UpdateWindow.PluginLine(Release(withPlugin: false), "");

        Assert.False(offer);
        Assert.Contains("only the editor changes", text);

        // And still not, even with a perfectly good folder set.
        Assert.False(UpdateWindow.PluginLine(Release(withPlugin: false), _temp).OfferFolder);
    }

    [Fact]
    public void The_version_line_reports_the_version_the_check_compared()
    {
        // Reported from a real test run: the prompt said "you are running
        // 1.1.0" over an offer of 1.1.0, because the line read the assembly
        // while the comparison read the override. A sentence that contradicts
        // the window it is in.
        Environment.SetEnvironmentVariable(UpdateFeed.VersionOverrideVariable, "1.0.0");
        try
        {
            string line = UpdateWindow.VersionLine(Release(withPlugin: true));

            Assert.Contains("Version 1.2.0", line);
            Assert.Contains("running 1.0.0", line);
            Assert.Contains("76 MB", line);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UpdateFeed.VersionOverrideVariable, null);
        }

        // And with nothing overridden it is the real one, not a leftover.
        Assert.Contains(UpdateFeed.RunningVersion.ToString(3),
                        UpdateWindow.VersionLine(Release(withPlugin: true)));
    }

    [Fact]
    public void A_release_with_no_size_does_not_offer_to_download_zero_MB()
    {
        var sizeless = new ReleaseInfo(new Version(1, 2, 0), "n", "notes",
                                       "https://x/Editor.zip", null, 0);
        Assert.DoesNotContain("MB", UpdateWindow.VersionLine(sizeless));
    }
}
