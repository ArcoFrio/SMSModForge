using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace SMSModForge.View.Controls;

/// <summary>
/// Small pictures of files, decoded at the size they are shown and kept for a
/// while (1.6.3).
/// <para/>
/// Made for the previews on an action row, which come and go with every node
/// selected: a bust's picture is a couple of thousand pixels tall, and decoding
/// it whole to draw it the size of a thumbnail - again each time the node is
/// selected - is most of what such a preview would cost. Decoded to the size
/// asked for instead, and remembered by the file's path, size and time written,
/// so a file changed on disk is decoded again rather than shown as it was.
/// <para/>
/// Safe to call from any thread: what it hands back is frozen.
/// </summary>
internal static class Thumbnails
{
    /// <summary>How many are remembered. Enough for every row of a busy node
    /// and the one before it; the oldest goes first.</summary>
    private const int Kept = 48;

    private readonly record struct Key(string Path, DateTime Written, long Length, int Side);

    private static readonly object Gate = new();
    private static readonly Dictionary<Key, LinkedListNode<(Key Key, BitmapSource? Bitmap)>> ByKey = new();
    private static readonly LinkedList<(Key Key, BitmapSource? Bitmap)> Order = new();

    /// <summary>The picture of <paramref name="absolutePath"/> already decoded
    /// no larger than <paramref name="side"/>, if there is one.</summary>
    public static bool TryCached(string absolutePath, int side, out BitmapSource? bitmap)
    {
        bitmap = null;
        var key = KeyOf(absolutePath, side);
        if (key == null) return false;
        lock (Gate)
        {
            if (!ByKey.TryGetValue(key.Value, out var node)) return false;
            Order.Remove(node);
            Order.AddFirst(node);
            bitmap = node.Value.Bitmap;
            return true;
        }
    }

    /// <summary>
    /// The picture of <paramref name="absolutePath"/>, its longer side no more
    /// than <paramref name="side"/> pixels (a smaller picture is left as it
    /// is), frozen. Null when the file is missing or not a picture.
    /// </summary>
    public static BitmapSource? Load(string absolutePath, int side)
    {
        if (TryCached(absolutePath, side, out var hit)) return hit;
        var key = KeyOf(absolutePath, side);
        if (key == null) return null;

        var bitmap = Decode(absolutePath, side);
        lock (Gate)
        {
            if (!ByKey.ContainsKey(key.Value))
            {
                ByKey[key.Value] = Order.AddFirst((key.Value, bitmap));
                while (Order.Count > Kept)
                {
                    ByKey.Remove(Order.Last!.Value.Key);
                    Order.RemoveLast();
                }
            }
        }
        return bitmap;
    }

    private static Key? KeyOf(string absolutePath, int side)
    {
        try
        {
            var info = new FileInfo(absolutePath);
            if (!info.Exists) return null;
            return new Key(info.FullName.ToUpperInvariant(), info.LastWriteTimeUtc, info.Length, side);
        }
        catch (Exception) { return null; }
    }

    private static BitmapSource? Decode(string absolutePath, int side)
    {
        try
        {
            // The header first, for the size: it says which way to shrink,
            // and whether to at all.
            int width, height;
            using (var head = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var frame = BitmapDecoder.Create(head, BitmapCreateOptions.DelayCreation,
                                                 BitmapCacheOption.None).Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
            }

            using var fs = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = fs;
            if (Math.Max(width, height) > side)
            {
                if (width >= height) img.DecodePixelWidth = side;
                else img.DecodePixelHeight = side;
            }
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception) { return null; }
    }
}
