using System.Collections.Generic;
using SMSModForge.Localization;

namespace SMSModForge.Model;

/// <summary>
/// Catalog of frames shipped under <c>Resources\VanillaFrames\</c>. Each entry
/// names a PNG that's copied next to the editor exe at build time and bundled
/// next to the BepInEx plugin DLL at runtime — the pack plugin loads them by
/// the same stable <see cref="FileName"/>.
/// <para/>
/// The list is hand-curated rather than directory-scanned so the editor
/// dropdown stays predictable across machines (and so new frames need an
/// explicit entry rather than appearing because a stray PNG was dropped into
/// the resources folder).
/// </summary>
public static class VanillaFrames
{
    public sealed record VanillaFrame(string FileName, string NameKey)
    {
        public string DisplayName => Loc.T(NameKey);
    }

    /// <summary>The frames shipped with the editor.</summary>
    public static readonly IReadOnlyList<VanillaFrame> All = new VanillaFrame[]
    {
        new("PhotoFrame.png", "scenes.frame.photo"),
        new("SexyFrame.png",  "scenes.frame.sexy"),
    };
}
