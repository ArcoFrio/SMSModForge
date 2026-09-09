using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// Turns a GIF into the frames the game plays.
/// <para/>
/// Decoding happens HERE, once, when the pack is saved — not in the game every
/// time a scene opens. Two reasons, and both were the deciding ones:
/// <list type="bullet">
///   <item>Quality. A GIF is not a stack of pictures: frames are patches, each
///   carrying a disposal rule saying what to do with the one underneath, and a
///   frame that only redraws the part that moved is the normal case rather than
///   the exception. Composing that correctly is what Windows' imaging stack
///   already does; a hand-written decoder in the runtime would be new code,
///   less tested, running on somebody else's machine.</item>
///   <item>Speed. The game opens a folder of PNGs through the loader it
///   already has, and nothing decodes while a scene is being shown.</item>
/// </list>
/// <para/>
/// The frames come out as ordinary PNGs, which means they go through exactly
/// the same fitting as a still scene does — so an animation of any resolution
/// occupies the space a 256x256 scene would, for free.
/// </summary>
public static class GifFrames
{
    /// <summary>What a decoded animation is, once it is on disk.</summary>
    public sealed class Decoded
    {
        /// <summary>How long each frame is held, in seconds, in order.</summary>
        [JsonProperty("delays")] public List<double> Delays { get; set; } = new();

        /// <summary>Pixel size of every frame — they are all composed onto one
        /// canvas, so one pair covers all of them.</summary>
        [JsonProperty("width")] public int Width { get; set; }
        [JsonProperty("height")] public int Height { get; set; }

        public int Count => Delays.Count;
    }

    /// <summary>What a GIF with no usable delay is played at. The number every
    /// browser settles on, so an animation looks here the way it looked
    /// wherever the author found it.</summary>
    public const double DefaultDelaySeconds = 0.1;

    /// <summary>
    /// One open GIF, composed a frame at a time.
    /// <para/>
    /// Exists because two things need the same pictures out of the same file
    /// and must not disagree about them: the save, which writes them to disk
    /// for the game, and the preview, which shows them to the author. The
    /// disposal rules are fiddly enough that a second implementation would have
    /// been a second set of bugs.
    /// <para/>
    /// Composition is FORWARD-ONLY and holds one canvas rather than all of
    /// them. A frame is a patch on the picture before it, so playing an
    /// animation is naturally a walk; keeping every composed frame would cost
    /// width × height × 4 bytes each, which for a large GIF sitting open in a
    /// preview is hundreds of megabytes for no gain. Asking for a frame already
    /// behind the walk simply starts it again.
    /// </summary>
    public sealed class Reader
    {
        private readonly GifBitmapDecoder _decoder;
        private readonly int[] _left;
        private readonly int[] _top;
        private readonly int[] _disposal;
        private readonly double[] _delays;

        /// <summary>The picture the next frame composes onto.</summary>
        private BitmapSource? _carried;

        /// <summary>How far the walk has got, or -1 before it starts.</summary>
        private int _composed = -1;

        /// <summary>Pixel size of the composed picture — one pair for all of
        /// them, since every frame lands on the same canvas.</summary>
        public int Width { get; }
        public int Height { get; }

        public int Count => _delays.Length;

        /// <summary>How long each frame is held, in seconds.</summary>
        public IReadOnlyList<double> Delays => _delays;

        private Reader(GifBitmapDecoder decoder, int width, int height)
        {
            _decoder = decoder;
            Width = width;
            Height = height;

            int count = decoder.Frames.Count;
            _left = new int[count];
            _top = new int[count];
            _disposal = new int[count];
            _delays = new double[count];

            for (int i = 0; i < count; i++)
            {
                var meta = decoder.Frames[i].Metadata as BitmapMetadata;
                _left[i] = ReadInt(meta, "/imgdesc/Left");
                _top[i] = ReadInt(meta, "/imgdesc/Top");
                _disposal[i] = ReadInt(meta, "/grctlext/Disposal");

                int hundredths = ReadInt(meta, "/grctlext/Delay");
                _delays[i] = hundredths > 0 ? hundredths / 100.0 : DefaultDelaySeconds;
            }
        }

        /// <summary>
        /// Open a GIF, or null when the file is not one this can read.
        /// <para/>
        /// The caller reports that rather than shipping — or previewing — a
        /// scene that shows nothing.
        /// </summary>
        public static Reader? Open(string gifAbsPath)
        {
            if (string.IsNullOrWhiteSpace(gifAbsPath) || !File.Exists(gifAbsPath)) return null;

            GifBitmapDecoder decoder;
            try
            {
                using var file = File.OpenRead(gifAbsPath);
                decoder = new GifBitmapDecoder(file, BitmapCreateOptions.PreservePixelFormat,
                                               BitmapCacheOption.OnLoad);
            }
            catch (Exception) { return null; }        // not a GIF, or unreadable

            if (decoder.Frames.Count == 0) return null;

            int width = decoder.Frames[0].PixelWidth;
            int height = decoder.Frames[0].PixelHeight;
            if (width <= 0 || height <= 0) return null;

            return new Reader(decoder, width, height);
        }

        /// <summary>
        /// The whole picture at <paramref name="index"/>, frozen.
        /// <para/>
        /// Walking backwards is allowed and costs a restart, which is what a
        /// looping preview does once per loop and nothing else does at all.
        /// </summary>
        public BitmapSource Compose(int index)
        {
            if (index < 0) index = 0;
            if (index >= Count) index = Count - 1;

            // There is no route from frame 5 back to frame 2: each one is a
            // patch on the last. Start the walk again.
            if (index <= _composed)
            {
                _carried = null;
                _composed = -1;
            }

            BitmapSource? made = null;
            for (int i = _composed + 1; i <= index; i++) made = Step(i);
            return made!;
        }

        /// <summary>Compose one frame onto what has been carried forward.</summary>
        private BitmapSource Step(int i)
        {
            var frame = _decoder.Frames[i];

            var visual = new DrawingVisual();
            using (var draw = visual.RenderOpen())
            {
                // Everything already on the canvas, unless this frame says to
                // clear it first.
                if (_carried != null && _disposal[i] != 2)
                    draw.DrawImage(_carried, new Rect(0, 0, Width, Height));

                draw.DrawImage(frame,
                               new Rect(_left[i], _top[i], frame.PixelWidth, frame.PixelHeight));
            }

            var canvas = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            canvas.Render(visual);
            canvas.Freeze();

            // Disposal 3 means "put back what was there before this frame", so
            // the next frame composes onto the older picture rather than this
            // one. Anything else carries this frame forward.
            if (_disposal[i] != 3) _carried = canvas;

            _composed = i;
            return canvas;
        }
    }

    /// <summary>
    /// Decode <paramref name="gifAbsPath"/> into <paramref name="framesAbsDir"/>
    /// as numbered PNGs plus a manifest of delays.
    /// <para/>
    /// Returns null when the file is not a GIF this can read; the caller
    /// reports that rather than shipping a scene that shows nothing.
    /// <para/>
    /// The folder is emptied of previously written frames first, so replacing a
    /// 40-frame GIF with a 12-frame one does not leave 28 strays behind for the
    /// runtime to play.
    /// </summary>
    public static Decoded? Decode(string gifAbsPath, string framesAbsDir)
    {
        var reader = Reader.Open(gifAbsPath);
        if (reader == null) return null;

        Directory.CreateDirectory(framesAbsDir);
        foreach (string stale in Directory.GetFiles(framesAbsDir, "*.png"))
            try { File.Delete(stale); } catch (IOException) { }

        var made = new Decoded { Width = reader.Width, Height = reader.Height };

        for (int i = 0; i < reader.Count; i++)
        {
            var canvas = reader.Compose(i);

            using (var outFile = File.Create(
                       Path.Combine(framesAbsDir, Shared.MediaKinds.FrameName(i))))
            {
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(canvas));
                png.Save(outFile);
            }

            made.Delays.Add(reader.Delays[i]);
        }

        File.WriteAllText(Path.Combine(framesAbsDir, Shared.MediaKinds.FramesManifest),
                          JsonConvert.SerializeObject(made, Formatting.Indented));
        return made;
    }

    /// <summary>Read a decoded animation's manifest back, or null.</summary>
    public static Decoded? Read(string framesAbsDir)
    {
        string path = Path.Combine(framesAbsDir, Shared.MediaKinds.FramesManifest);
        if (!File.Exists(path)) return null;
        try { return JsonConvert.DeserializeObject<Decoded>(File.ReadAllText(path)); }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// One metadata number, or 0.
    /// <para/>
    /// Every field read here is optional in the format and routinely absent —
    /// a GIF with no graphic control extension has no delay and no disposal —
    /// so a missing one is a default rather than a problem.
    /// </summary>
    private static int ReadInt(BitmapMetadata? meta, string query)
    {
        if (meta == null) return 0;
        try
        {
            object? value = meta.GetQuery(query);
            return value == null
                ? 0
                : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception) { return 0; }
    }
}
