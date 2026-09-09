using System;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace SMSModForge.Model;

/// <summary>
/// One frame out of a video Windows will not play.
/// <para/>
/// The preview hands a video to a media player and holds it on its first frame.
/// When Windows has no decoder for the format that fails, and what the author
/// got was a paragraph explaining why they could not see their own scene. A
/// still is most of what a preview is for, so it is worth some trouble.
/// <para/>
/// The trouble is this: <b>a VP8 keyframe and a lossy WebP image are the same
/// bitstream</b>. WebP's lossy mode is VP8 intra coding, wrapped in a RIFF
/// container instead of a video one. So a keyframe lifted out of a .webm and
/// given a WebP header is a picture Windows can open with the codec it already
/// ships — no decoding here, no library, no external tool. The bytes are
/// re-labelled, not converted.
/// <para/>
/// This covers VP8, which is the case that fails: WPF's media player is built
/// on the old Windows Media pipeline, which never learned VP8, while the engine
/// brings its own decoder and plays it perfectly. Anything else returns null
/// and the preview says so rather than showing a picture it is guessing at.
/// <para/>
/// Nothing here reads the whole file. It walks the container's headers, seeking
/// past everything it does not need, and stops at the first keyframe — which
/// sits near the front, because that is where a video starts.
/// </summary>
public static class VideoStill
{
    // EBML element ids, marker bits included, as they appear in the file.
    private const uint Segment = 0x18538067;
    private const uint Tracks = 0x1654AE6B;
    private const uint TrackEntry = 0xAE;
    private const uint TrackNumber = 0xD7;
    private const uint TrackType = 0x83;
    private const uint CodecId = 0x86;
    private const uint Cluster = 0x1F43B675;
    private const uint SimpleBlock = 0xA3;
    private const uint BlockGroup = 0xA0;
    private const uint Block = 0xA1;

    /// <summary>TrackType 1. The audio track is 2, and its blocks are not
    /// pictures.</summary>
    private const int VideoTrackType = 1;

    /// <summary>The one codec whose keyframes are WebP images.</summary>
    private const string Vp8 = "V_VP8";

    /// <summary>A ceiling on one frame, so a corrupt length cannot ask for a
    /// gigabyte. A 4K keyframe is a few megabytes; this is generous.</summary>
    private const int LargestFrame = 16 * 1024 * 1024;

    /// <summary>How many elements the walk will look at before giving up. The
    /// first keyframe is near the front of any real file; a number this size is
    /// only reached by something malformed.</summary>
    private const int ElementBudget = 20000;

    /// <summary>
    /// The first frame of <paramref name="absolutePath"/>, or null when this
    /// cannot produce one.
    /// <para/>
    /// Null is the ordinary answer for most files — an MP4, a VP9 WebM, a
    /// machine without the WebP codec — and callers are expected to have
    /// something to say in that case.
    /// </summary>
    public static BitmapSource? FirstFrame(string absolutePath)
    {
        byte[]? webp = AsWebP(absolutePath);
        if (webp == null) return null;

        try
        {
            using var bytes = new MemoryStream(webp);
            var decoder = BitmapDecoder.Create(bytes, BitmapCreateOptions.PreservePixelFormat,
                                               BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;

            var frame = decoder.Frames[0];
            if (frame.CanFreeze) frame.Freeze();
            return frame;
        }
        catch (Exception)
        {
            // No WebP codec registered on this machine, or the frame was not
            // whole. Either way there is no picture to show.
            return null;
        }
    }

    /// <summary>
    /// The first VP8 keyframe of a WebM, re-labelled as a WebP file — or null.
    /// <para/>
    /// Separated from the decode so a test can check the extraction without
    /// depending on which image codecs the machine running it happens to have.
    /// </summary>
    internal static byte[]? AsWebP(string absolutePath)
    {
        byte[]? frame = FirstKeyframe(absolutePath);
        if (frame == null) return null;

        // RIFF chunks are word-aligned, so an odd-length payload carries a
        // trailing pad byte that is not counted in the chunk size.
        int pad = frame.Length & 1;
        var made = new byte[12 + 8 + frame.Length + pad];

        Ascii(made, 0, "RIFF");
        WriteU32(made, 4, 4 + 8 + frame.Length + pad);   // everything after this field
        Ascii(made, 8, "WEBP");
        Ascii(made, 12, "VP8 ");                          // the space is part of the name
        WriteU32(made, 16, frame.Length);
        Buffer.BlockCopy(frame, 0, made, 20, frame.Length);

        return made;
    }

    // ── Walking the container ────────────────────────────────────────

    /// <summary>What the walk is looking for and what it has found.</summary>
    private sealed class Scan
    {
        public int Vp8Track = -1;
        public byte[]? Frame;
        public int Budget = ElementBudget;
    }

    private static byte[]? FirstKeyframe(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath)) return null;

        try
        {
            using var file = new FileStream(absolutePath, FileMode.Open, FileAccess.Read,
                                            FileShare.Read);

            // EBML's magic number. An MP4 stops here, which is the intent:
            // this trick is about VP8 and nothing else.
            var magic = new byte[4];
            if (!ReadExactly(file, magic, 4)) return null;
            if (magic[0] != 0x1A || magic[1] != 0x45 || magic[2] != 0xDF || magic[3] != 0xA3)
                return null;

            file.Position = 0;
            var scan = new Scan();
            Walk(file, file.Length, scan);
            return scan.Frame;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Walk elements from the current position to <paramref name="end"/>,
    /// descending into the few that hold what is wanted and seeking past the
    /// rest.
    /// </summary>
    private static void Walk(Stream file, long end, Scan scan)
    {
        while (file.Position < end && scan.Frame == null && scan.Budget-- > 0)
        {
            uint id = ReadId(file);
            if (id == 0) return;

            long size = ReadSize(file);
            if (size == Unreadable) return;

            long body = file.Position;

            // An unknown size runs to the end of whatever contains it. Live
            // muxers write clusters that way, so this is a normal file rather
            // than a broken one.
            long stop = size < 0 ? end : Math.Min(body + size, end);
            if (stop < body) return;

            switch (id)
            {
                case Segment:
                case Cluster:
                case BlockGroup:
                    Walk(file, stop, scan);
                    break;

                case Tracks:
                    WalkTracks(file, stop, scan);
                    break;

                case SimpleBlock:
                case Block:
                    // Only a block whose whole declared length is present. A
                    // truncated file would otherwise yield a truncated frame,
                    // and half a keyframe still decodes - into a picture of
                    // the top of the scene above grey, which an author would
                    // reasonably read as their art being broken.
                    if (size >= 0 && body + size <= end) TakeFrame(file, body, body + size, scan);
                    break;
            }

            if (scan.Frame != null) return;

            // Nothing else can be skipped past when its length was never
            // written down.
            if (size < 0) return;
            file.Position = stop;
        }
    }

    /// <summary>Find the VP8 video track's number.</summary>
    private static void WalkTracks(Stream file, long end, Scan scan)
    {
        while (file.Position < end && scan.Budget-- > 0)
        {
            uint id = ReadId(file);
            if (id == 0) return;

            long size = ReadSize(file);
            if (size < 0) return;

            long stop = Math.Min(file.Position + size, end);
            if (id == TrackEntry) ReadTrackEntry(file, stop, scan);
            file.Position = stop;
        }
    }

    private static void ReadTrackEntry(Stream file, long end, Scan scan)
    {
        int number = -1;
        int type = -1;
        string codec = "";

        while (file.Position < end && scan.Budget-- > 0)
        {
            uint id = ReadId(file);
            if (id == 0) return;

            long size = ReadSize(file);
            if (size < 0 || size > 4096) return;

            long stop = Math.Min(file.Position + size, end);
            if (id == TrackNumber) number = (int)ReadUInt(file, (int)size);
            else if (id == TrackType) type = (int)ReadUInt(file, (int)size);
            else if (id == CodecId) codec = ReadAscii(file, (int)size);
            file.Position = stop;
        }

        if (scan.Vp8Track < 0 && number > 0 && type == VideoTrackType
            && string.Equals(codec, Vp8, StringComparison.Ordinal))
            scan.Vp8Track = number;
    }

    /// <summary>
    /// Take this block's frame if it is the VP8 track's, and a keyframe.
    /// <para/>
    /// A block starts with its track number, a two-byte timecode and a byte of
    /// flags; the frame follows. An interframe is only the DIFFERENCE from the
    /// picture before it, so decoding one on its own gives rubbish — which is
    /// why the frame type is checked rather than assumed, even though the first
    /// block of a file is a keyframe in every file that plays.
    /// </summary>
    private static void TakeFrame(Stream file, long body, long end, Scan scan)
    {
        if (scan.Vp8Track < 0) return;

        file.Position = body;
        long track = ReadSize(file);              // a vint, written like a size
        if (track != scan.Vp8Track) return;

        file.Position += 2;                       // timecode, relative to the cluster
        int flags = file.ReadByte();
        if (flags < 0) return;

        // Lacing packs several frames into one block behind a header this does
        // not read. Rare in video, and the next block will do.
        if ((flags & 0x06) != 0) return;

        long length = end - file.Position;
        if (length < 10 || length > LargestFrame) return;

        var frame = new byte[length];
        if (!ReadExactly(file, frame, (int)length)) return;

        // The VP8 frame tag: bit 0 of the first byte is the frame type, 0 for a
        // keyframe, and a keyframe is followed by a fixed start code.
        if ((frame[0] & 1) != 0) return;
        if (frame[3] != 0x9D || frame[4] != 0x01 || frame[5] != 0x2A) return;

        scan.Frame = frame;
    }

    // ── EBML's numbers ───────────────────────────────────────────────
    //
    // Both are variable-length: the count of leading zero bits in the first
    // byte says how many bytes follow. An id keeps its marker bit, because the
    // marker is part of how ids are written down; a size drops it, because a
    // size is a number.

    /// <summary>What <see cref="ReadSize"/> returns when the stream ended.</summary>
    private const long Unreadable = -2;

    private static uint ReadId(Stream file)
    {
        int first = file.ReadByte();
        if (first < 0) return 0;

        int length = first >= 0x80 ? 1 : first >= 0x40 ? 2 : first >= 0x20 ? 3
                   : first >= 0x10 ? 4 : 0;
        if (length == 0) return 0;               // not a valid id

        uint id = (uint)first;
        for (int i = 1; i < length; i++)
        {
            int next = file.ReadByte();
            if (next < 0) return 0;
            id = (id << 8) | (uint)next;
        }
        return id;
    }

    /// <summary>A size, -1 when it is the "unknown" pattern, or
    /// <see cref="Unreadable"/> at the end of the stream.</summary>
    private static long ReadSize(Stream file)
    {
        // Zero is not a length marker either: it would claim nine or more
        // bytes, which EBML does not define.
        int first = file.ReadByte();
        if (first <= 0) return Unreadable;

        int length = 0;
        for (int i = 0; i < 8; i++)
            if ((first & (0x80 >> i)) != 0) { length = i + 1; break; }
        if (length == 0) return Unreadable;

        long value = first & (0xFF >> length);
        long allOnes = 0xFFL >> length;

        for (int i = 1; i < length; i++)
        {
            int next = file.ReadByte();
            if (next < 0) return Unreadable;
            value = (value << 8) | (uint)next;
            allOnes = (allOnes << 8) | 0xFF;
        }

        // Every bit set means "this runs until its parent does".
        return value == allOnes ? -1 : value;
    }

    /// <summary>A big-endian unsigned integer of the given length.</summary>
    private static long ReadUInt(Stream file, int length)
    {
        if (length <= 0 || length > 8) return -1;

        long value = 0;
        for (int i = 0; i < length; i++)
        {
            int next = file.ReadByte();
            if (next < 0) return -1;
            value = (value << 8) | (uint)next;
        }
        return value;
    }

    private static string ReadAscii(Stream file, int length)
    {
        if (length <= 0 || length > 256) return "";

        var bytes = new byte[length];
        if (!ReadExactly(file, bytes, length)) return "";

        // Codec ids are zero-padded in some muxers.
        int real = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, real < 0 ? length : real);
    }

    private static bool ReadExactly(Stream file, byte[] into, int count)
    {
        int got = 0;
        while (got < count)
        {
            int n = file.Read(into, got, count - got);
            if (n <= 0) return false;
            got += n;
        }
        return true;
    }

    // ── RIFF's numbers ───────────────────────────────────────────────

    private static void Ascii(byte[] into, int at, string four)
    {
        for (int i = 0; i < four.Length; i++) into[at + i] = (byte)four[i];
    }

    private static void WriteU32(byte[] into, int at, int value)
    {
        into[at] = (byte)value;                  // little-endian, unlike EBML
        into[at + 1] = (byte)(value >> 8);
        into[at + 2] = (byte)(value >> 16);
        into[at + 3] = (byte)(value >> 24);
    }
}
