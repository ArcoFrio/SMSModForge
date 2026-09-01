using System;
using SMSModForge.Services;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Reading a release off the API, without the API.
/// <para/>
/// The fetch and the parse are separate for this reason: everything that can be
/// wrong about understanding a release — a tag the editor cannot read, an asset
/// named differently from last time, a payload that is not a release at all —
/// is wrong in a string, and a string needs no network to hand to a test.
/// </summary>
public class UpdateFeedTests
{
    /// <summary>A release payload shaped like GitHub's, with the assets named
    /// the way the published zips are.</summary>
    private const string LatestJson = """
    {
      "tag_name": "v1.2.0",
      "name": "ModForge 1.2.0",
      "body": "### Added\r\n\r\n- A thing.\r\n",
      "assets": [
        { "name": "Starmaker - ModForge Editor 1.2.0.zip",
          "size": 80899092,
          "browser_download_url": "https://example.invalid/dl/Starmaker%20-%20ModForge%20Editor%201.2.0.zip" },
        { "name": "Starmaker - ModForge Plugin 1.2.0.zip",
          "size": 1031733,
          "browser_download_url": "https://example.invalid/dl/Starmaker%20-%20ModForge%20Plugin%201.2.0.zip" }
      ]
    }
    """;

    [Fact]
    public void Reads_the_version_name_notes_and_both_zips()
    {
        var r = UpdateFeed.ParseLatest(LatestJson)!;

        Assert.Equal(new Version(1, 2, 0), r.Version);
        Assert.Equal("1.2.0", r.VersionText);
        Assert.Equal("ModForge 1.2.0", r.Name);
        Assert.Equal("### Added\n\n- A thing.", r.Notes);   // newlines normalised, trimmed
        Assert.Contains("Editor", r.EditorZipUrl);
        Assert.Contains("Plugin", r.PluginZipUrl);
        Assert.Equal(80899092, r.EditorZipBytes);
    }

    [Fact]
    public void Tells_the_two_zips_apart_whichever_order_they_are_in()
    {
        // Asset order is whatever they happened to be uploaded in, so it must
        // not be load-bearing: the plugin is first here, and the editor still
        // has to come back as the editor.
        var r = UpdateFeed.ParseLatest("""
        {
          "tag_name": "v1.2.0",
          "assets": [
            { "name": "Starmaker - ModForge Plugin 1.2.0.zip", "size": 1031733,
              "browser_download_url": "https://example.invalid/dl/Plugin-1.2.0.zip" },
            { "name": "Starmaker - ModForge Editor 1.2.0.zip", "size": 80899092,
              "browser_download_url": "https://example.invalid/dl/Editor-1.2.0.zip" }
          ]
        }
        """)!;

        Assert.Equal("https://example.invalid/dl/Editor-1.2.0.zip", r.EditorZipUrl);
        Assert.Equal("https://example.invalid/dl/Plugin-1.2.0.zip", r.PluginZipUrl);
        Assert.Equal(80899092, r.EditorZipBytes);
    }

    [Fact]
    public void A_release_with_no_assets_yet_is_still_a_release()
    {
        // Assets upload after the release is created, so a check landing in
        // that window sees a version with nothing to download. It must read as
        // "nothing to install", not as a crash or as a phantom update.
        var r = UpdateFeed.ParseLatest("""{ "tag_name": "v2.0.0", "name": "", "assets": [] }""")!;

        Assert.Equal(new Version(2, 0, 0), r.Version);
        Assert.Equal("v2.0.0", r.Name);      // falls back to the tag
        Assert.Null(r.EditorZipUrl);
        Assert.Null(r.PluginZipUrl);
    }

    [Fact]
    public void Anything_that_is_not_a_release_reads_as_no_release()
    {
        // What a private repository answers with, which is what the editor gets
        // today, and what it will get again if the repo is ever made private.
        Assert.Null(UpdateFeed.ParseLatest("""{ "message": "Not Found" }"""));
        Assert.Null(UpdateFeed.ParseLatest("{}"));
        // A tag that is not a version at all.
        Assert.Null(UpdateFeed.ParseLatest("""{ "tag_name": "nightly" }"""));
    }

    [Theory]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("V1.2.0", 1, 2, 0)]
    [InlineData(" v1.2.0 ", 1, 2, 0)]
    [InlineData("v1.2", 1, 2, 0)]          // an unstated patch is zero, not missing
    [InlineData("v1.2.0-rc1", 1, 2, 0)]    // prerelease suffix dropped
    [InlineData("v1.2.0+build7", 1, 2, 0)]
    public void Reads_the_tags_releases_are_actually_named(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateFeed.TryParseVersion(tag, out var v));
        Assert.Equal(new Version(major, minor, build), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nightly")]
    [InlineData("v")]
    [InlineData("latest")]
    public void Refuses_a_tag_it_cannot_read(string? tag)
        => Assert.False(UpdateFeed.TryParseVersion(tag, out _));

    [Fact]
    public void A_two_part_tag_is_not_older_than_its_three_part_self()
    {
        // Version fills an unstated part with -1, which sorts BELOW 0: without
        // normalising, "v1.2" would read as older than 1.2.0 and an editor on
        // 1.2.0 would be offered 1.2 as an upgrade forever.
        Assert.True(UpdateFeed.TryParseVersion("v1.2", out var two));
        Assert.True(UpdateFeed.TryParseVersion("v1.2.0", out var three));
        Assert.Equal(three, two);
        Assert.False(two > three);
    }

    [Fact]
    public void Version_order_is_numeric_not_alphabetical()
    {
        UpdateFeed.TryParseVersion("v1.10.0", out var ten);
        UpdateFeed.TryParseVersion("v1.9.0", out var nine);
        Assert.True(ten > nine, "1.10.0 has to beat 1.9.0");
    }

    [Fact]
    public void The_running_version_can_be_faked_for_testing()
    {
        // The other half of trying this without publishing anything.
        var real = UpdateFeed.RunningVersion;
        Environment.SetEnvironmentVariable(UpdateFeed.VersionOverrideVariable, "0.0.1");
        try
        {
            Assert.Equal(new Version(0, 0, 1), UpdateFeed.RunningVersion);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UpdateFeed.VersionOverrideVariable, null);
        }
        Assert.Equal(real, UpdateFeed.RunningVersion);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_feed_can_be_pointed_at_a_local_file()
    {
        // The seam the end-to-end test runs through, checked here so a typo in
        // it is not something you find out about while watching an editor try
        // to replace itself.
        string file = System.IO.Path.GetTempFileName();
        await System.IO.File.WriteAllTextAsync(file, LatestJson);
        Environment.SetEnvironmentVariable(UpdateFeed.FeedOverrideVariable, file);
        Environment.SetEnvironmentVariable(UpdateFeed.VersionOverrideVariable, "0.0.1");
        try
        {
            var release = await UpdateFeed.CheckAsync();
            Assert.NotNull(release);
            Assert.Equal("1.2.0", release!.VersionText);

            // The control: the same feed, read by an editor that is already
            // newer, has to come back with nothing.
            Environment.SetEnvironmentVariable(UpdateFeed.VersionOverrideVariable, "2.0.0");
            Assert.Null(await UpdateFeed.CheckAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable(UpdateFeed.FeedOverrideVariable, null);
            Environment.SetEnvironmentVariable(UpdateFeed.VersionOverrideVariable, null);
            System.IO.File.Delete(file);
        }
    }
}
