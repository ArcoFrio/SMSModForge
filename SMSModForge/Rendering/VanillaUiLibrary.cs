using System;
using System.Collections.Generic;
using System.IO;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// The vanilla UI as the running editor sees it: the shipped folder, the
/// surfaces in it, and the sprites and fonts they draw from.
/// <para/>
/// One place for the three things that must agree — the catalog names a base,
/// the surface holds its tree, and the assets draw it — so a caller never has
/// to know that they arrive from three different extractions of the same
/// scene.
/// <para/>
/// Surfaces are cached as they are asked for rather than up front. Loading all
/// forty-nine costs several megabytes of parsed objects to answer a question
/// about one of them, and an author looks at one screen at a time.
/// </summary>
public static class VanillaUiLibrary
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, VanillaUiSurface?> Surfaces =
        new(StringComparer.OrdinalIgnoreCase);
    private static VanillaUiAssets? _assets;

    /// <summary>Where the extraction was deployed to, beside the exe.</summary>
    public static string Root => Path.Combine(AppContext.BaseDirectory, "VanillaUi");

    /// <summary>Whether there is anything to draw with. False means the
    /// extraction was never run or never shipped — worth saying out loud, since
    /// every other symptom of it looks like a broken preview.</summary>
    public static bool IsAvailable
        => File.Exists(Path.Combine(Root, "index.json")) && Assets.IsAvailable;

    public static VanillaUiAssets Assets
    {
        get
        {
            lock (Gate) return _assets ??= new VanillaUiAssets(Root);
        }
    }

    /// <summary>
    /// The surface a base lives on, by the base's catalog id or token.
    /// <para/>
    /// The file name is derived the way the extractor wrote it — the surface's
    /// path with anything a file name cannot hold replaced. That is a shared
    /// convention between two programs, so it is spelled out in one place here
    /// rather than guessed at each call site.
    /// </summary>
    public static VanillaUiSurface? SurfaceFor(VanillaUiCatalog.Base? entry)
        => entry == null ? null : Surface(entry.Surface.Path);

    public static VanillaUiSurface? Surface(string? surfacePath)
    {
        if (string.IsNullOrWhiteSpace(surfacePath)) return null;
        lock (Gate)
        {
            if (Surfaces.TryGetValue(surfacePath, out var cached)) return cached;
            var loaded = VanillaUiSurface.Load(
                Path.Combine(Root, "Surfaces", FileNameFor(surfacePath) + ".json"));
            Surfaces[surfacePath] = loaded;
            return loaded;
        }
    }

    /// <summary>The node a catalog base refers to, ready to render.</summary>
    public static VanillaUiSurface.Node? Node(VanillaUiCatalog.Base? entry)
    {
        var surface = SurfaceFor(entry);
        return surface == null || entry == null ? null : surface.Base(entry.Id);
    }

    /// <summary>The extractor's file-naming rule: invalid characters and path
    /// separators become underscores. Kept here so the editor and the Unity
    /// script cannot drift apart on it silently — a mismatch shows as a surface
    /// that simply will not load.</summary>
    public static string FileNameFor(string surfacePath)
    {
        var made = surfacePath.ToCharArray();
        var invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < made.Length; i++)
            if (made[i] == '/' || Array.IndexOf(invalid, made[i]) >= 0) made[i] = '_';
        return new string(made).Trim();
    }

    /// <summary>Forget everything loaded. For tests, and for the day an author
    /// re-runs the extraction while the editor is open.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Surfaces.Clear();
            _assets = null;
        }
    }
}
