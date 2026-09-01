using System;

namespace SMSModForge.Services;

/// <summary>
/// One published release, as the editor needs to see it: what it is called,
/// what changed, and where the two zips are.
/// </summary>
/// <param name="Version">Parsed from the tag. <c>v1.2.0</c> and <c>1.2.0</c>
/// both work; anything after a dash (a prerelease suffix) is dropped.</param>
/// <param name="Name">The release's title, for the prompt's heading. Falls back
/// to the tag when a release was published without one.</param>
/// <param name="Notes">The release body, as written on the release. Markdown,
/// shown as text — see UpdateWindow for why it is not rendered.</param>
/// <param name="EditorZipUrl">The editor download, or null if the release has
/// no editor asset (a plugin-only release, or one still being uploaded).</param>
/// <param name="PluginZipUrl">The plugin download, or null.</param>
/// <param name="EditorZipBytes">Size of the editor asset, for the prompt.</param>
public sealed record ReleaseInfo(
    Version Version,
    string Name,
    string Notes,
    string? EditorZipUrl,
    string? PluginZipUrl,
    long EditorZipBytes)
{
    /// <summary>What the prompt calls it: "1.2.0", from the parsed version
    /// rather than the raw tag, so a stray "v" never reaches the author.</summary>
    public string VersionText => Version.ToString(3);
}
