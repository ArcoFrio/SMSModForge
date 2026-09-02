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
    }

    private readonly string _root;
    private readonly Dictionary<string, string> _spriteFiles = new(StringComparer.OrdinalIgnoreCase);
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
            foreach (var e in entries)
                if (!string.IsNullOrEmpty(e.Key) && !string.IsNullOrEmpty(e.File))
                    _spriteFiles[e.Key] = e.File;
        }
        catch { /* an unreadable index leaves nothing to draw, and says so via IsAvailable */ }
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
