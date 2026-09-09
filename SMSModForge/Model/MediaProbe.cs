using System;
using System.IO;

namespace SMSModForge.Model;

/// <summary>
/// What a file the author picked actually is, read from the file itself.
/// <para/>
/// Which KIND it is comes from the extension, because that is what an author
/// controls and what they expect: rename a thing to .png and the tool should
/// treat it as a still, not quietly animate it. Everything else — whether a
/// video carries sound, how big its picture is — is read from the bytes,
/// because an extension cannot know it and asking the author to tell us
/// something the file already says is how forms get long.
/// <para/>
/// No codec and no decoding. Both container formats say what tracks they hold
/// in a header a few hundred bytes in, so this opens the file, reads that, and
/// closes it. That is what lets the volume slider appear the moment a path is
/// typed, rather than after a media pipeline spins up — and it is why this
/// works the same on a machine with no codecs installed.
/// </summary>
public static class MediaProbe
{
    /// <summary>What kind of art a path refers to.</summary>
    public enum MediaKind
    {
        /// <summary>A still image — the only thing scenes used to be.</summary>
        Still,

        /// <summary>An animated GIF: decoded to frames, played by swapping
        /// them.</summary>
        Gif,

        /// <summary>A video: played by the engine's own video player.</summary>
        Video,

        /// <summary>An extension this tool does not handle.</summary>
        Unknown,
    }

    /// <summary>The extensions each kind is recognised by. Deliberately a short
    /// list of what the engine can actually play, rather than everything that
    /// might decode: a format that loads on the author's machine and not in the
    /// game is the worst outcome available.</summary>
    public static MediaKind KindOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return MediaKind.Unknown;

        // Through the shared rule, so the editor and the game can never
        // disagree about which files animate.
        switch (SMSModForge.Shared.MediaKinds.KindOf(path!))
        {
            case SMSModForge.Shared.MediaKinds.Still: return MediaKind.Still;
            case SMSModForge.Shared.MediaKinds.Gif:   return MediaKind.Gif;
            case SMSModForge.Shared.MediaKinds.Video: return MediaKind.Video;
            default:                                  return MediaKind.Unknown;
        }
    }

    /// <summary>Whether a path names something that moves.</summary>
    public static bool IsAnimated(string? path)
    {
        var kind = KindOf(path);
        return kind == MediaKind.Gif || kind == MediaKind.Video;
    }

    /// <summary>
    /// Whether a video carries an audio track.
    /// <para/>
    /// Null means "could not tell" — an unreadable file, a container this does
    /// not parse — and a caller should treat that differently from a confident
    /// "no". Offering a volume slider for a silent video is a small confusion;
    /// hiding one for a video that does have sound leaves an author unable to
    /// turn it down.
    /// </summary>
    public static bool? HasAudio(string? path)
    {
        if (KindOf(path) != MediaKind.Video) return false;
        if (!File.Exists(path)) return null;

        try
        {
            using var file = File.OpenRead(path!);
            return Path.GetExtension(path!).ToLowerInvariant() == ".webm"
                ? WebmHasAudio(file)
                : Mp4HasAudio(file);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    // ── MP4 / MOV: ISO base media format ─────────────────────────────
    //
    // Boxes all the way down: a 4-byte big-endian size, a 4-byte type, then
    // either payload or more boxes. A track says what it carries in its
    // handler box — 'soun' for audio, 'vide' for picture — so finding one
    // 'hdlr' whose handler is 'soun' is the whole question.

    private static bool? Mp4HasAudio(Stream file)
    {
        bool sawAnyBox = false;
        bool found = Walk(file, 0, file.Length, 0, ref sawAnyBox);

        // Nothing that even looked like a box: not an MP4, whatever it is
        // called. Saying "no audio" about a file we could not read at all
        // would be a guess wearing a fact's clothes.
        return sawAnyBox ? found : (bool?)null;
    }

    private static bool Walk(Stream file, long start, long end, int depth, ref bool sawAnyBox)
    {
        // The tracks live at moov/trak/mdia/hdlr, so four is as deep as this
        // ever needs to go. The bound also stops a malformed file walking
        // forever.
        if (depth > 6) return false;

        long at = start;
        var header = new byte[8];

        while (at + 8 <= end)
        {
            file.Position = at;
            if (!ReadExactly(file, header, 8)) return false;

            long size = ReadU32(header, 0);
            string type = Ascii(header, 4);
            long body = at + 8;

            if (size == 1)
            {
                // 64-bit size, in the eight bytes after the type.
                var big = new byte[8];
                if (!ReadExactly(file, big, 8)) return false;
                size = (long)ReadU64(big, 0);
                body += 8;
            }
            else if (size == 0)
            {
                size = end - at;              // runs to the end of its parent
            }

            if (size < 8 || at + size > end) return false;
            sawAnyBox = true;

            if (type == "hdlr")
            {
                // version(1) + flags(3) + predefined(4), then the handler.
                var hdlr = new byte[12];
                file.Position = body;
                if (ReadExactly(file, hdlr, 12) && Ascii(hdlr, 8) == "soun") return true;
            }
            else if (type == "moov" || type == "trak" || type == "mdia")
            {
                if (Walk(file, body, at + size, depth + 1, ref sawAnyBox)) return true;
            }

            at += size;
        }
        return false;
    }

    // ── WebM / MKV: EBML ─────────────────────────────────────────────
    //
    // A full EBML parser is a lot of machinery for one bit of information.
    // What is actually needed is whether any TrackEntry says TrackType 2
    // (audio), and the TrackType element is a distinctive three-byte run:
    // id 0x83, size 0x81, value 0x02. Scanning the header region for it
    // answers the question without modelling the format.
    //
    // Bounded to the first megabyte because the Tracks element sits near the
    // front, before the clusters - so this reads a header, not a movie.

    private static bool? WebmHasAudio(Stream file)
    {
        var head = new byte[4];
        if (!ReadExactly(file, head, 4)) return null;
        if (head[0] != 0x1A || head[1] != 0x45 || head[2] != 0xDF || head[3] != 0xA3)
            return null;                       // not EBML at all

        file.Position = 0;
        long span = Math.Min(file.Length, 1 << 20);
        var buffer = new byte[span];
        int read = file.Read(buffer, 0, buffer.Length);

        for (int i = 0; i + 2 < read; i++)
            if (buffer[i] == 0x83 && buffer[i + 1] == 0x81 && buffer[i + 2] == 0x02)
                return true;
        return false;
    }

    // ── Bytes ────────────────────────────────────────────────────────

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

    private static long ReadU32(byte[] b, int at)
        => ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3];

    private static ulong ReadU64(byte[] b, int at)
    {
        ulong v = 0;
        for (int i = 0; i < 8; i++) v = (v << 8) | b[at + i];
        return v;
    }

    private static string Ascii(byte[] b, int at)
        => "" + (char)b[at] + (char)b[at + 1] + (char)b[at + 2] + (char)b[at + 3];
}
