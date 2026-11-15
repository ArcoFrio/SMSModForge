using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace SMSModForge.View.Converters;

/// <summary>
/// One of the game's scene pictures (1.9.0), from its file beside the editor,
/// small: for the rows of the Scenes tab's list of the game's scenes and of its
/// picker.
/// <para/>
/// Decoded no bigger than a row draws it and remembered by path, because the
/// picker's list scrolls through a thousand of them and builds a row again each
/// time one comes back into view. Never holds the file open. An empty or missing
/// path is no picture rather than an error.
/// </summary>
public sealed class GameScenePictureConverter : IValueConverter
{
    public static readonly GameScenePictureConverter Instance = new();

    /// <summary>The longest side decoded: twice the largest a row draws one, for
    /// a screen at 200%.</summary>
    private const int Side = 96;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0) return null;
        lock (Gate)
        {
            if (Cache.TryGetValue(path, out var known)) return known;
        }
        BitmapSource? bitmap = null;
        try
        {
            if (File.Exists(path))
            {
                var img = new BitmapImage();
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.DecodePixelWidth = Side;
                img.StreamSource = fs;
                img.EndInit();
                img.Freeze();
                bitmap = img;
            }
        }
        catch (IOException) { }
        catch (NotSupportedException) { }
        catch (UnauthorizedAccessException) { }
        lock (Gate) Cache[path] = bitmap;
        return bitmap;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
