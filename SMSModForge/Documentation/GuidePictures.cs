using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SMSModForge.Documentation;

/// <summary>
/// The guide's pictures (Resources/GuidePictures): one of each page's part of the editor, and since 1.10.0 one for each
/// section the editor shows a part of (Sections/&lt;section&gt;.png) - the author: "We should be not only telling the
/// user where something is, but also adding a small image to show them". Each is taken in both looks by
/// GuidePictureShots; the one of the look the editor is in comes first.
/// </summary>
public static class GuidePictures
{
    private static readonly Dictionary<string, ImageSource?> _loaded = new();

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Resources", "GuidePictures");

    private static readonly Dictionary<string, IReadOnlyDictionary<string, int>> _marks = new();

    /// <summary>
    /// The number a section's picture boxes a bullet's control with - red, as in a beginner's tutorial - or 0 when it
    /// boxes none. Read from Sections/marks[.EX].json, written as the pictures are taken: each look's pictures are
    /// laid out by its own window, so each has its own numbers.
    /// </summary>
    public static int MarkOf(string bulletKey)
    {
        string look = Services.ThemeManager.Current.IsEx ? ".EX" : "";
        if (!_marks.TryGetValue(look, out var table))
        {
            table = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string name in new[] { "marks" + look + ".json", "marks.json" })
            {
                string path = Path.Combine(Folder, "Sections", name);
                if (!File.Exists(path)) continue;
                try
                {
                    table = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(path))
                            ?? new Dictionary<string, int>();
                }
                catch (Exception) { }
                break;
            }
            _marks[look] = table;
        }
        return table.TryGetValue(bulletKey, out int n) ? n : 0;
    }

    /// <summary>The picture by its name under <see cref="Folder"/> ("places", "Sections/places.heading.lights"), in
    /// the look the editor is in when there is one for it, or null when there is none.</summary>
    public static ImageSource? Load(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        bool ex = Services.ThemeManager.Current.IsEx;
        foreach (string look in ex ? new[] { name + ".EX", name } : new[] { name })
        {
            if (_loaded.TryGetValue(look, out var known)) { if (known != null) return known; continue; }
            string path = new[] { ".png", ".jpg" }.Select(ext => Path.Combine(Folder, look + ext)).FirstOrDefault(File.Exists) ?? "";
            ImageSource? found = null;
            if (path.Length > 0)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    found = bmp;
                }
                catch (Exception) { found = null; }
            }
            _loaded[look] = found;
            if (found != null) return found;
        }
        return null;
    }
}
