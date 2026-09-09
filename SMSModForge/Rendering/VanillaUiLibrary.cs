using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Every sprite an author can pick, in order. Empty without an
    /// extraction, which leaves the picker empty rather than throwing.</summary>
    public static IEnumerable<string> SpriteNames
        => IsAvailable ? Assets.SpriteNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                       : Enumerable.Empty<string>();

    /// <summary>
    /// Every sound of the game's that the editor knows the name of.
    /// <para/>
    /// A name, not a file: a pack never ships one of these. The runtime asks
    /// the game for the clip it already has loaded, so what the editor needs is
    /// only to be able to offer the name rather than have it typed from memory.
    /// The files are here so a name can be checked against something audible.
    /// </summary>
    public static IEnumerable<string> SoundNames
    {
        get
        {
            lock (Gate)
            {
                if (_soundNames != null) return _soundNames;

                var found = new List<string>();
                try
                {
                    string dir = Path.Combine(Root, "Sounds");
                    if (Directory.Exists(dir))
                        foreach (string file in Directory.EnumerateFiles(dir))
                        {
                            string ext = Path.GetExtension(file);
                            if (!string.Equals(ext, ".ogg", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(ext, ".mp3", StringComparison.OrdinalIgnoreCase)) continue;

                            // The asset name is the file name: that is what the
                            // game calls the clip, and what the runtime looks up.
                            found.Add(Path.GetFileNameWithoutExtension(file));
                        }
                }
                catch
                {
                    // A missing or unreadable folder means no names to offer,
                    // which is a shorter list rather than a broken editor.
                }

                found.Sort(StringComparer.OrdinalIgnoreCase);
                _soundNames = found;
                return _soundNames;
            }
        }
    }

    private static List<string>? _soundNames;

    /// <summary>The file behind one of <see cref="SoundNames"/>, or null.
    /// For playing it in the editor; nothing else needs it.</summary>
    public static string? SoundFile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (string ext in new[] { ".ogg", ".wav", ".mp3" })
        {
            string path = Path.Combine(Root, "Sounds", name + ext);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>Every TextMeshPro font asset the extraction exported. Named
    /// assets rather than typefaces: the same face appears more than once with
    /// different outlines and shadows, and they are not interchangeable.</summary>
    public static IEnumerable<string> FontNames
    {
        get
        {
            lock (Gate)
            {
                if (_fontNames != null) return _fontNames;
                var found = new List<string>();
                try
                {
                    string dir = Path.Combine(Root, "Fonts");
                    if (Directory.Exists(dir))
                        foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
                        {
                            string name = Path.GetFileNameWithoutExtension(file);
                            if (name != "report") found.Add(name);
                        }
                }
                catch { /* an unreadable folder leaves an empty picker, not a crash */ }
                found.Sort(StringComparer.OrdinalIgnoreCase);
                return _fontNames = found;
            }
        }
    }

    private static List<string>? _fontNames;

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
            _fontNames = null;
            _soundNames = null;
        }
    }
}
