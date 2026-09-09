using System;
using System.IO;
using System.Text;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Getting one frame out of a video Windows will not play.
/// <para/>
/// The trick is that a VP8 keyframe and a lossy WebP image are the same
/// bitstream — so a keyframe lifted out of a .webm and given a RIFF header is a
/// picture the shipped codec can open. Nothing is decoded or converted here;
/// the bytes are re-labelled.
/// <para/>
/// What has to be checked is that it takes the RIGHT bytes. A wrapper around
/// the wrong offset still produces a file, and a decoder handed an interframe
/// still produces a picture — of nothing recognisable. Both look like success
/// from the outside.
/// </summary>
public sealed class VideoStillTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public VideoStillTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "vstill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // ── Building a WebM to read ──────────────────────────────────────
    //
    // Written here rather than checked in, because a committed video would be
    // a binary blob nobody could review and the test would be proving something
    // about a file rather than about the code.

    private static byte[] Vint(long value, int length)
    {
        var made = new byte[length];
        for (int i = 0; i < length; i++) made[length - 1 - i] = (byte)(value >> (8 * i));
        made[0] |= (byte)(0x80 >> (length - 1));
        return made;
    }

    private static void Element(Stream to, uint id, byte[] body)
    {
        var idBytes = new System.Collections.Generic.List<byte>();
        for (int shift = 24; shift >= 0; shift -= 8)
        {
            byte b = (byte)(id >> shift);
            if (b != 0 || idBytes.Count > 0) idBytes.Add(b);
        }
        to.Write(idBytes.ToArray(), 0, idBytes.Count);

        var size = Vint(body.Length, body.Length < 0x7F ? 1 : 4);
        to.Write(size, 0, size.Length);
        to.Write(body, 0, body.Length);
    }

    private static byte[] Built(Action<MemoryStream> write)
    {
        using var buffer = new MemoryStream();
        write(buffer);
        return buffer.ToArray();
    }

    /// <summary>A VP8 frame header good enough to be recognised — the frame tag,
    /// the keyframe start code, and a size.</summary>
    private static byte[] Vp8Frame(bool keyframe, int width, int height, int payload)
    {
        var made = new byte[10 + payload];
        made[0] = (byte)(keyframe ? 0x10 : 0x11);   // bit 0 is the frame type
        made[3] = 0x9D; made[4] = 0x01; made[5] = 0x2A;
        made[6] = (byte)width; made[7] = (byte)(width >> 8);
        made[8] = (byte)height; made[9] = (byte)(height >> 8);
        for (int i = 10; i < made.Length; i++) made[i] = (byte)(i & 0xFF);
        return made;
    }

    private static byte[] BlockFor(int track, byte[] frame)
        => Built(b =>
        {
            var tn = Vint(track, 1);
            b.Write(tn, 0, tn.Length);
            b.WriteByte(0); b.WriteByte(0);          // timecode
            b.WriteByte(0x80);                        // flags: keyframe, no lacing
            b.Write(frame, 0, frame.Length);
        });

    /// <summary>A one-track WebM holding the frames given, in order.</summary>
    private string WriteWebm(string name, string codec, int trackType, params byte[][] frames)
    {
        var tracks = Built(t => Element(t, 0xAE, Built(e =>
        {
            Element(e, 0xD7, new byte[] { 1 });                       // TrackNumber
            Element(e, 0x83, new byte[] { (byte)trackType });         // TrackType
            Element(e, 0x86, Encoding.ASCII.GetBytes(codec));         // CodecID
        })));

        var cluster = Built(c =>
        {
            foreach (var f in frames) Element(c, 0xA3, BlockFor(1, f));
        });

        var segment = Built(s =>
        {
            Element(s, 0x1654AE6B, tracks);
            Element(s, 0x1F43B675, cluster);
        });

        string path = Path.Combine(_dir, name);
        using (var file = File.Create(path))
        {
            Element(file, 0x1A45DFA3, new byte[] { 0x42, 0x86, 0x81, 0x01 });   // EBML head
            Element(file, 0x18538067, segment);
        }
        return path;
    }

    /// <summary>The VP8 payload the wrapper produced, or null.</summary>
    private static byte[]? PayloadOf(byte[]? webp)
    {
        if (webp == null) return null;
        int length = webp[16] | (webp[17] << 8) | (webp[18] << 16) | (webp[19] << 24);
        var made = new byte[length];
        Array.Copy(webp, 20, made, 0, length);
        return made;
    }

    // ── The tests ────────────────────────────────────────────────────

    [Fact]
    public void AKeyframeComesOutWrappedAsAWebPFile()
    {
        var frame = Vp8Frame(keyframe: true, 960, 540, payload: 601);   // odd, so it pads
        string webm = WriteWebm("clip.webm", "V_VP8", 1, frame);

        var webp = VideoStill.AsWebP(webm);
        Assert.NotNull(webp);

        _out.WriteLine($"frame {frame.Length} bytes -> file {webp!.Length} bytes");

        Assert.Equal("RIFF", Encoding.ASCII.GetString(webp, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(webp, 8, 4));
        Assert.Equal("VP8 ", Encoding.ASCII.GetString(webp, 12, 4));

        // The declared size covers everything after it, pad byte included, and
        // a reader that trusts it must not run off the end.
        int declared = webp[4] | (webp[5] << 8) | (webp[6] << 16) | (webp[7] << 24);
        Assert.Equal(webp.Length - 8, declared);
        Assert.Equal(1, webp.Length % 2 == 0 ? 1 : 0);        // padded to even

        // And the payload is the frame itself, byte for byte. This is the whole
        // claim: nothing is decoded, nothing is re-encoded.
        Assert.Equal(frame, PayloadOf(webp));
    }

    [Fact]
    public void TheFirstKeyframeIsTaken()
    {
        // Not merely the first block. An interframe is only the DIFFERENCE from
        // the picture before it, so a decoder handed one produces a picture of
        // nothing - which still looks like the trick worked.
        var inter = Vp8Frame(keyframe: false, 960, 540, payload: 40);
        var key = Vp8Frame(keyframe: true, 960, 540, payload: 80);
        string webm = WriteWebm("later.webm", "V_VP8", 1, inter, key);

        Assert.Equal(key, PayloadOf(VideoStill.AsWebP(webm)));
    }

    [Theory]
    [InlineData("V_VP9", 1, "VP9 keyframes are not WebP images")]
    [InlineData("V_MPEG4/ISO/AVC", 1, "H.264 is a different bitstream entirely")]
    [InlineData("A_OPUS", 2, "an audio block is not a picture")]
    public void OnlyVp8VideoIsTaken(string codec, int trackType, string why)
    {
        // The controls, and they matter more than usual: every one of these
        // WOULD produce a file, and every file would decode to garbage that an
        // author might mistake for their own art having gone wrong.
        string webm = WriteWebm("other.webm", codec, trackType,
                                Vp8Frame(keyframe: true, 960, 540, payload: 40));

        _out.WriteLine($"{codec}: refused - {why}");
        Assert.Null(VideoStill.AsWebP(webm));
        Assert.Null(VideoStill.FirstFrame(webm));
    }

    [Fact]
    public void SomethingThatIsNotAWebMIsRefused()
    {
        string mp4 = Path.Combine(_dir, "clip.mp4");
        File.WriteAllBytes(mp4, new byte[] { 0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70 });
        Assert.Null(VideoStill.AsWebP(mp4));

        string missing = Path.Combine(_dir, "gone.webm");
        Assert.Null(VideoStill.AsWebP(missing));
        Assert.Null(VideoStill.FirstFrame(missing));

        string empty = Path.Combine(_dir, "empty.webm");
        File.WriteAllBytes(empty, Array.Empty<byte>());
        Assert.Null(VideoStill.AsWebP(empty));
    }

    [Fact]
    public void ATruncatedFileIsRefusedRatherThanGuessedAt()
    {
        string webm = WriteWebm("cut.webm", "V_VP8", 1,
                                Vp8Frame(keyframe: true, 960, 540, payload: 400));
        var whole = File.ReadAllBytes(webm);

        // The control first: intact, this file gives a frame. Without it every
        // assertion below would pass against a method that always returned
        // null.
        Assert.NotNull(VideoStill.AsWebP(webm));

        int refused = 0;
        for (int keep = 8; keep < whole.Length; keep += Math.Max(1, whole.Length / 20))
        {
            string part = Path.Combine(_dir, $"cut-{keep}.webm");
            File.WriteAllBytes(part, whole[..keep]);

            // Nothing at all, rather than the part of the frame that survived:
            // half a keyframe still decodes, into a picture an author would
            // reasonably read as their own art having gone wrong.
            Assert.Null(VideoStill.AsWebP(part));
            refused++;
        }

        _out.WriteLine($"{refused} truncations of a {whole.Length}-byte file, all refused");
        Assert.True(refused > 10);
    }

    [Fact]
    public void APaddedCodecNameStillReads()
    {
        // Some muxers zero-pad the codec id to a fixed width.
        string webm = WriteWebm("padded.webm", "V_VP8\0\0\0", 1,
                                Vp8Frame(keyframe: true, 320, 240, payload: 60));
        Assert.NotNull(VideoStill.AsWebP(webm));
    }

    [Fact]
    public void TheWrappedFrameIsAPictureWindowsWillOpen()
    {
        // The end of the chain, and the part that cannot be reasoned about:
        // whether this machine's imaging stack actually accepts the file. A
        // synthetic frame is not decodable, so this needs a real one - and the
        // only real one available is a pack's, which cannot be committed. The
        // check runs when SMSMODFORGE_TEST_VIDEO points at a VP8 .webm.
        string? real = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_VIDEO");
        if (string.IsNullOrWhiteSpace(real) || !File.Exists(real))
        {
            _out.WriteLine("SMSMODFORGE_TEST_VIDEO not set; skipping the decode.");
            return;
        }

        var frame = VideoStill.FirstFrame(real);
        Assert.NotNull(frame);
        _out.WriteLine($"{Path.GetFileName(real)} -> {frame!.PixelWidth}x{frame.PixelHeight}"
                       + $" {frame.Format}");

        Assert.True(frame.PixelWidth > 0 && frame.PixelHeight > 0);
        Assert.True(frame.IsFrozen, "a preview holds this across threads");
    }
}
