using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace SMSModForge.Rendering;

/// <summary>
/// The game's own sprites and font atlases, decoded from an extraction folder.
/// <para/>
/// Everything is cached on first use and never re-read. That matters more than
/// it sounds: the vanilla UI draws 2556 sliced images from 51 sprites, so
/// almost every request is a repeat, and decoding a PNG per image would make
/// the preview's cost proportional to the wrong number.
/// </summary>
public sealed class VanillaUiAssets : IUiAssets
{
    private sealed class SpriteEntry
    {
        [JsonProperty("key")] public string Key { get; set; } = "";
        [JsonProperty("sprite")] public string Sprite { get; set; } = "";
        [JsonProperty("file")] public string File { get; set; } = "";
        [JsonProperty("border")] public float[] Border { get; set; } = new float[4];
        [JsonProperty("pixelsPerUnit")] public float PixelsPerUnit { get; set; } = 100f;
        [JsonProperty("texture")] public string Texture { get; set; } = "";
        [JsonProperty("textureRect")] public float[] TextureRect { get; set; } = new float[4];
    }

    private readonly string _root;
    private readonly Dictionary<string, string> _spriteFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _keysByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SliceBorder> _bordersByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _ppuByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _nameByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UiSprite?> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UiFontSet?> _fonts = new(StringComparer.OrdinalIgnoreCase);

    public VanillaUiAssets(string extractionRoot)
    {
        _root = extractionRoot ?? "";
        LoadSpriteIndex();
    }

    /// <summary>Whether this folder looks like an extraction at all, so a caller
    /// can say "run the extractor" rather than draw an empty rectangle.</summary>
    public bool IsAvailable => _spriteFiles.Count > 0;

    private void LoadSpriteIndex()
    {
        try
        {
            string path = Path.Combine(_root, "Sprites", "index.json");
            if (!File.Exists(path)) return;
            var entries = JsonConvert.DeserializeObject<List<SpriteEntry>>(File.ReadAllText(path));
            if (entries == null) return;
            // Nine sprite names in the game belong to more than one crop -
            // icon_trophy has three, function_icon_check two. Letting the first
            // win meant an authored image could name a sprite and get a
            // different picture, which showed up as a seeded Quitagme drawing
            // its toggle's checkmark 55 pixels' worth differently from the game.
            //
            // So the colliding ones get a #n suffix, ordered by texture and crop
            // rather than by the order the file happens to list them in. That
            // ordering is a fact about the art, so the same sprite keeps the same
            // name across a re-extraction - which a manifest depends on, and
            // which the extraction's own keys do NOT provide, since those are
            // numbered by discovery order.
            var byName = new Dictionary<string, List<SpriteEntry>>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.Key) || string.IsNullOrEmpty(e.File)) continue;
                _spriteFiles[e.Key] = e.File;
                if (!byName.TryGetValue(e.Sprite, out var sharing))
                    byName[e.Sprite] = sharing = new List<SpriteEntry>();
                sharing.Add(e);
            }

            foreach (var pair in byName)
            {
                var sharing = pair.Value;
                if (sharing.Count > 1)
                    sharing.Sort((a, b) =>
                    {
                        int by = string.CompareOrdinal(a.Texture, b.Texture);
                        if (by != 0) return by;
                        for (int i = 0; i < 4; i++)
                        {
                            by = At(a.TextureRect, i).CompareTo(At(b.TextureRect, i));
                            if (by != 0) return by;
                        }
                        return string.CompareOrdinal(a.Key, b.Key);
                    });

                for (int i = 0; i < sharing.Count; i++)
                {
                    var e = sharing[i];
                    string name = sharing.Count == 1 ? pair.Key : pair.Key + "#" + (i + 1);
                    _keysByName[name] = e.Key;
                    _nameByKey[e.Key] = name;
                    _bordersByName[name] = e.Border is { Length: > 3 }
                        ? new SliceBorder(e.Border[0], e.Border[1], e.Border[2], e.Border[3])
                        : default;
                    _ppuByName[name] = e.PixelsPerUnit;
                }
            }

            static float At(float[]? a, int i) => a != null && a.Length > i ? a[i] : 0f;
        }
        catch { /* an unreadable index leaves nothing to draw, and says so via IsAvailable */ }
    }

    /// <summary>The name an authored image should carry for a sprite the
    /// extraction keyed. Unambiguous, and stable across re-extraction, which the
    /// key itself is not.</summary>
    public string NameForKey(string key)
        => !string.IsNullOrEmpty(key) && _nameByKey.TryGetValue(key, out string? n) ? n : "";

    /// <summary>Every sprite an author can choose from, by name.</summary>
    public IEnumerable<string> SpriteNames => _keysByName.Keys;

    /// <summary>The crop a sprite NAME refers to. Authored images name a
    /// sprite; the extraction keys them by texture and crop.</summary>
    /// <summary>A sprite's nine-slice border, which is a property of the art
    /// rather than of whoever used it. An author picking a sprite gets its
    /// slicing without having to know the numbers.</summary>
    public SliceBorder BorderByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return default;
        if (_bordersByName.TryGetValue(name, out var b)) return b;
        return _bordersByName.TryGetValue(name + "#1", out b) ? b : default;
    }

    /// <summary>How many texture pixels a sprite packs into a canvas unit.
    /// Most are 100, but 55 of the game's sliced images use a 200.</summary>
    public float PixelsPerUnitByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return 100f;
        if (_ppuByName.TryGetValue(name, out float v) && v > 0) return v;
        return _ppuByName.TryGetValue(name + "#1", out v) && v > 0 ? v : 100f;
    }

    /// <summary>
    /// Where the pack's own art lives, so the preview can draw it.
    /// <para/>
    /// Without this a pack-shipped picture is simply absent from the preview -
    /// the extraction knows the game's 840 sprites and nothing else - so a shop
    /// full of the pack's own item art previewed as empty frames while being
    /// perfectly correct in the game. The runtime has always read these; only
    /// the preview could not.
    /// </summary>
    public string? PackRoot { get; set; }

    public UiSprite? SpriteByName(string name)
    {
        // A name carrying a separator or an extension is a file the pack ships,
        // not one of the game's sprites - the same test the runtime uses to
        // tell them apart.
        if (LooksLikeFile(name))
        {
            var fromPack = PackSprite(name);
            if (fromPack != null) return fromPack;
        }
        return Sprite(KeyFor(name));
    }

    private static bool LooksLikeFile(string name)
        => !string.IsNullOrEmpty(name)
        && (name.IndexOf('/') >= 0 || name.IndexOf(System.IO.Path.DirectorySeparatorChar) >= 0 || name.IndexOf('.') >= 0);

    private readonly Dictionary<string, UiSprite?> _packSprites = new(StringComparer.OrdinalIgnoreCase);

    private UiSprite? PackSprite(string name)
    {
        if (string.IsNullOrEmpty(PackRoot)) return null;
        if (_packSprites.TryGetValue(name, out var cached)) return cached;

        UiSprite? made = null;
        try
        {
            string path = Path.Combine(PackRoot, name.Replace('/', Path.DirectorySeparatorChar));

            // Case matters on the way in even where the file system does not
            // care: a pack authored as ".PNG" against a file named ".png" works
            // on Windows and fails wherever it is read case-sensitively, so the
            // real name is found rather than assumed.
            if (!File.Exists(path)) path = FindIgnoringCase(path);
            if (path.Length > 0) made = DecodePremultiplied(path);
        }
        catch { /* an unreadable file draws nothing, which is visible */ }

        _packSprites[name] = made;
        return made;
    }

    private static string FindIgnoringCase(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            string want = Path.GetFileName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return "";

            foreach (string file in Directory.EnumerateFiles(dir))
                if (string.Equals(Path.GetFileName(file), want, StringComparison.OrdinalIgnoreCase))
                    return file;
        }
        catch { }
        return "";
    }

    /// <summary>Where a sprite's PNG is on disk, or empty when it has none.
    /// For showing one at its own size - a picker listing 840 names is
    /// guesswork without a picture, since most of the names describe
    /// nothing.</summary>
    public string FileFor(string name)
    {
        string key = KeyFor(name);
        return !string.IsNullOrEmpty(key) && _spriteFiles.TryGetValue(key, out string? file)
             ? Path.Combine(_root, "Sprites", file)
             : "";
    }

    /// <summary>The key a name refers to. A bare name that only exists in
    /// suffixed form falls back to the first crop, so a manifest written before
    /// the suffixes existed - or typed by hand - still finds a picture rather
    /// than silently drawing nothing.</summary>
    private string KeyFor(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        if (_keysByName.TryGetValue(name, out string? key)) return key;
        return _keysByName.TryGetValue(name + "#1", out key) ? key : "";
    }

    public UiSprite? Sprite(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (_sprites.TryGetValue(key, out var cached)) return cached;

        UiSprite? made = null;
        if (_spriteFiles.TryGetValue(key, out string? file))
            made = DecodePremultiplied(Path.Combine(_root, "Sprites", file));

        // Cached even when it failed. A missing sprite is reported once per
        // render rather than retried for every one of the hundreds of objects
        // that might use it.
        _sprites[key] = made;
        return made;
    }

    public UiFontSet? Font(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_fonts.TryGetValue(name, out var cached)) return cached;

        UiFontSet? made = null;
        var font = TmpFont.Load(Path.Combine(_root, "Fonts", Safe(name) + ".json"));
        if (font != null && font.Atlas.Pages.Count > 0 && !string.IsNullOrEmpty(font.Atlas.Pages[0]))
        {
            var alpha = DecodeAlpha(Path.Combine(_root, "Fonts", font.Atlas.Pages[0]));
            if (alpha != null)
                made = new UiFontSet(font, alpha.Value.Pixels, alpha.Value.Width, alpha.Value.Height);
        }
        _fonts[name] = made;
        return made;
    }

    // ── Decoding ─────────────────────────────────────────────────────

    private static UiSprite? DecodePremultiplied(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var frame = Read(path);
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight;
            var pixels = new byte[w * h * 4];
            converted.CopyPixels(pixels, w * 4, 0);
            return new UiSprite(pixels, w, h);
        }
        catch { return null; }
    }

    /// <summary>A font atlas as one byte per texel. TextMeshPro keeps
    /// everything in the alpha channel and leaves RGB at zero, so carrying the
    /// other three would be three quarters waste on a 1024×1024 image.</summary>
    private static (byte[] Pixels, int Width, int Height)? DecodeAlpha(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var frame = Read(path);
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight;
            var full = new byte[w * h * 4];
            converted.CopyPixels(full, w * 4, 0);

            var alpha = new byte[w * h];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = full[i * 4 + 3];
            return (alpha, w, h);
        }
        catch { return null; }
    }

    /// <summary>Read a PNG without holding the file open — the extraction folder
    /// is one an author may well be regenerating while the editor is running.</summary>
    private static BitmapFrame Read(string path)
    {
        using var stream = File.OpenRead(path);
        return BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                                  BitmapCacheOption.OnLoad);
    }

    private static string Safe(string name)
    {
        foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        return name;
    }
}
