using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Rendering;

/// <summary>
/// A place the bust preview draws a bust over, as the game has it behind one
/// (1.9.1, the author: "The default should be the player's room level, which
/// is where we load the game into"). Taken from the game by the in-game check
/// InGameSuiteTests.S4 and Tools/Game/backdrop-from-game.py, which holds the
/// shipped copy to what the game draws:
/// <list type="bullet">
/// <item>the room, as the bust shader's fake GI reads it: the Default layer as
/// lit, without the interface, the room's own buttons or the
/// post-processing, at the size the preview draws a bust at (960x540, two
/// screen pixels a texel);</item>
/// <item>what the room's lights add to a bust, in linear light, per screen
/// pixel (the game multiplies a bust by 1 there - measured);</item>
/// <item>the fake GI's settings (FakeGIManager's globals, live);</item>
/// <item>where a single bust rests (SpriteManager's anchor) and how far it
/// breathes.</item>
/// </list>
/// Screen coordinates here are the game's: pixels from the bottom left.
/// </summary>
public sealed class PreviewBackdrop
{
    public string Name { get; }
    public int ScreenWidth { get; }
    public int ScreenHeight { get; }

    /// <summary>The room, BGRA, rows top-down (8-bit, as the screen showed it
    /// with the post-processing off).</summary>
    public byte[] Room { get; }

    /// <summary>The room as the camera's target holds it - linear RGB floats,
    /// rows from the bottom, the camera's 11-11-10-bit values, above 1 where
    /// the room is bright; and the same with its lamps off, and each lamp alone
    /// (what a bust's shadow takes from).</summary>
    public float[] RoomHdr { get; }
    public float[] UnlitHdr { get; }
    public float[][] LampHdr { get; }

    /// <summary>The room as the camera's target holds it before the lamp's
    /// glow is added (its light volume off), RGB, rows from the bottom; null
    /// for a backdrop without it - and then no shadow is drawn.</summary>
    public float[]? RoomNoGlowHdr { get; }

    /// <summary>The lamp a bust's shadow is cast from, with URP's numbers for
    /// its light and shadow (null: none).</summary>
    public LampSettings? Lamp { get; }

    /// <summary>How the game's GPU decodes each byte of an sRGB lookup texture
    /// (256 values, from URP's falloff lookup read back in the game); null
    /// for the textbook curve.</summary>
    public float[]? LookupDecode { get; }

    /// <summary>The background the bust shader's fake GI reads
    /// (_CameraSortingLayerTexture): RGBA floats, rows from the bottom.</summary>
    public float[] Background { get; }
    public int BackgroundWidth { get; }
    public int BackgroundHeight { get; }

    /// <summary>URP's internal grading table (1024x32): RGBA floats, rows from the bottom.</summary>
    public float[] GradingTable { get; }

    /// <summary>The game's post-processing settings (post.json).</summary>
    internal GamePost.Settings Post { get; }

    public FakeGiSettings Gi { get; }

    /// <summary>A single bust's art rect at rest: its left and bottom edges on
    /// screen, its size in screen pixels.</summary>
    public int BustLeft { get; }
    public int BustRestBottom { get; }
    public int BustWidth { get; }
    public int BustHeight { get; }

    /// <summary>The breathing's nominal depth (screen pixels) and speed
    /// (radians a second): SpriteManager's breathingAmplitude through the
    /// camera, and breathingSpeed.</summary>
    public double BreathingPixels { get; }
    public double BreathingSpeed { get; }

    private readonly float[] _light;   // r, g, b per cell; rows from the bottom
    private readonly int _lightLeft, _lightBottom, _cell, _cols, _rows;

    private PreviewBackdrop(string name, int w, int h, byte[] room, FakeGiSettings gi, JObject bust,
                            float[] light, int left, int bottom, int cell, int cols, int rows,
                            float[] roomHdr, float[] unlitHdr, float[][] lampHdr, float[] background, int bw, int bh,
                            float[] table, GamePost.Settings post, float[]? noGlow, LampSettings? lamp, float[]? decode)
    {
        RoomNoGlowHdr = noGlow; Lamp = lamp; LookupDecode = decode;
        Name = name; ScreenWidth = w; ScreenHeight = h; Room = room; Gi = gi;
        RoomHdr = roomHdr; UnlitHdr = unlitHdr; LampHdr = lampHdr;
        Background = background; BackgroundWidth = bw; BackgroundHeight = bh;
        GradingTable = table; Post = post;
        BustLeft = (int)bust["left"]!; BustRestBottom = (int)bust["restBottom"]!;
        BustWidth = (int)bust["width"]!; BustHeight = (int)bust["height"]!;
        BreathingPixels = (double)bust["breathingPixels"]!; BreathingSpeed = (double)bust["breathingSpeed"]!;
        _light = light; _lightLeft = left; _lightBottom = bottom; _cell = cell; _cols = cols; _rows = rows;
    }

    /// <summary>What the room's lights add to a bust at a screen point (pixels
    /// from the bottom left; a pixel's centre is +0.5), linear light: between
    /// the measured cells' centres, and the nearest edge beyond them.</summary>
    public (float r, float g, float b) LightAt(float x, float y)
    {
        float fx = (x - _lightLeft) / _cell - 0.5f, fy = (y - _lightBottom) / _cell - 0.5f;
        fx = Math.Clamp(fx, 0f, _cols - 1); fy = Math.Clamp(fy, 0f, _rows - 1);
        int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, _cols - 1), y1 = Math.Min(y0 + 1, _rows - 1);
        float ax = fx - x0, ay = fy - y0;
        float C(int cx, int cy, int c) => _light[(cy * _cols + cx) * 3 + c];
        float At(int c)
        {
            float bottom = C(x0, y0, c) + (C(x1, y0, c) - C(x0, y0, c)) * ax;
            float top = C(x0, y1, c) + (C(x1, y1, c) - C(x0, y1, c)) * ax;
            return bottom + (top - bottom) * ay;
        }
        return (At(0), At(1), At(2));
    }

    /// <summary>
    /// What the characters' layer's light texture is at each screen pixel (RGB, rows from the bottom): what a bust
    /// is multiplied by (1.10.0). Null for the game's places the backdrops were captured in - a bust there takes the
    /// global light alone, white (measured) - and for a place of the pack's with no light on the characters.
    /// </summary>
    public float[]? CharactersLight { get; private set; }

    /// <summary>Made from a place of the pack's (<see cref="ForPlace"/>), not captured: drawn on the processor.</summary>
    public bool IsPlace { get; private set; }

    /// <summary>
    /// For a place's lamp (1.10.0), what is known exactly rather than worked out from captures: the room's colour
    /// before any light (<see cref="Albedo"/>, RGB per screen pixel, rows from the bottom); the light texture of the
    /// place's other lights with the global one (<see cref="LampBase"/>, RGB per texel of the light texture, rows
    /// from the bottom); and the glows of the other lights drawn with the room (<see cref="OthersBehindGlow"/>) and
    /// over the characters (<see cref="OthersOverGlow"/>), RGB per screen pixel. Null for a captured backdrop.
    /// </summary>
    public float[]? Albedo { get; private set; }
    public float[]? LampBase { get; private set; }
    public float[]? OthersBehindGlow { get; private set; }
    public float[]? OthersOverGlow { get; private set; }

    /// <summary>
    /// A place of the pack's as a bust's backdrop (1.10.0), made from the place preview rather than captured from
    /// the game - the place as the preview draws it, which InGameSuiteTests.S11 holds to the game: the room as lit
    /// (<paramref name="roomTopDown"/>, BGRA rows top-down; <paramref name="roomHdr"/>, linear RGB rows from the
    /// bottom), what the bust's edge glow reads (<paramref name="background"/>), what the place's lights add over a
    /// bust (<paramref name="overBust"/>, RGB per screen pixel) and what they multiply it by
    /// (<paramref name="charactersLight"/>). Where a bust rests, its breathing, the edge glow's settings and the
    /// game's post-processing are the same in every place (the five captured backdrops agree): taken from
    /// <paramref name="like"/>. No lamp casts a bust's shadow: a light the pack makes casts none.
    /// </summary>
    internal static PreviewBackdrop ForPlace(string name, PreviewBackdrop like, byte[] roomTopDown, float[] roomHdr, float[] background,
                                             int bw, int bh, float[] overBust, float[]? charactersLight, PlaceLamp? lamp = null)
    {
        var bust = new JObject
        {
            ["left"] = like.BustLeft, ["restBottom"] = like.BustRestBottom, ["width"] = like.BustWidth, ["height"] = like.BustHeight,
            ["breathingPixels"] = like.BreathingPixels, ["breathingSpeed"] = like.BreathingSpeed,
        };
        int w = like.ScreenWidth, h = like.ScreenHeight;
        // What the lights add over a bust, a cell a screen pixel.
        if (lamp != null)
            return new PreviewBackdrop(name, w, h, roomTopDown, like.Gi, bust, lamp.OthersOverGlow, 0, 0, 1, w, h,
                                       lamp.Room, lamp.Room, Array.Empty<float[]>(), background, bw, bh, like.GradingTable, like.Post,
                                       lamp.RoomNoGlow, lamp.Settings, like.LookupDecode)
            {
                CharactersLight = charactersLight, IsPlace = true, Albedo = lamp.Albedo, LampBase = lamp.Base,
                OthersBehindGlow = lamp.OthersBehindGlow, OthersOverGlow = lamp.OthersOverGlow,
            };
        return new PreviewBackdrop(name, w, h, roomTopDown, like.Gi, bust, overBust, 0, 0, 1, w, h,
                                   roomHdr, roomHdr, Array.Empty<float[]>(), background, bw, bh, like.GradingTable, like.Post,
                                   null, null, like.LookupDecode) { CharactersLight = charactersLight, IsPlace = true };
    }

    /// <summary>
    /// A place's lamp that casts a bust's shadow (1.10.0: the template level's light, shadows on in the game), with
    /// the place around it: its settings; the room as seen, with the glows drawn with the room
    /// (<see cref="Room"/>), and without the lamp's (<see cref="RoomNoGlow"/>), RGB rows from the bottom; and what
    /// <see cref="Albedo"/>, <see cref="LampBase"/>, <see cref="OthersBehindGlow"/> and
    /// <see cref="OthersOverGlow"/> hold.
    /// </summary>
    internal sealed record PlaceLamp(LampSettings Settings, float[] Room, float[] RoomNoGlow, float[] Albedo, float[] Base,
                                     float[] OthersBehindGlow, float[] OthersOverGlow);

    /// <summary>The shipped backdrops' folder: Backdrops beside the editor.</summary>
    public static string ShippedFolder => Path.Combine(AppContext.BaseDirectory, "Backdrops");

    /// <summary>A backdrop from its folder (room.png, lights.png, backdrop.json).</summary>
    public static PreviewBackdrop Load(string folder)
    {
        var json = JObject.Parse(File.ReadAllText(Path.Combine(folder, "backdrop.json")));
        int w = (int)json["screen"]![0]!, h = (int)json["screen"]![1]!;
        var room = Pixels(Path.Combine(folder, "room.png"), PixelFormats.Bgra32, out int rw, out int rh);
        if (rw != w || rh != h) throw new InvalidDataException($"{folder}: room.png is {rw}x{rh}, backdrop.json says {w}x{h}");

        var lights = (JObject)json["lights"]!;
        int cols = (int)lights["columns"]!, rows = (int)lights["rows"]!;
        float scale = (float)lights["scale"]!;
        var raw = Pixels(Path.Combine(folder, "lights.png"), PixelFormats.Rgb48, out int lw, out int lh);
        if (lw != cols || lh != rows) throw new InvalidDataException($"{folder}: lights.png is {lw}x{lh}, backdrop.json says {cols}x{rows}");
        // 16 bits a channel, little-endian as WPF hands them over; the PNG's rows
        // run from the top, the grid's from the bottom.
        var light = new float[cols * rows * 3];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols * 3; c++)
            {
                int at = ((rows - 1 - r) * cols * 3 + c) * 2;
                light[r * cols * 3 + c] = (raw[at] | raw[at + 1] << 8) / 65535f * scale;
            }

        var gi = (JObject)json["gi"]!;
        var tint = (JArray)gi["GITint"]!;
        var settings = new FakeGiSettings
        {
            Enabled = (float)gi["GIEnabled"]!, Intensity = (float)gi["GIIntensity"]!, BlurRadius = (float)gi["GIBlurRadius"]!,
            BlurAmount = (float)gi["GIBlurAmount"]!, EdgeWidth = (float)gi["GIEdgeWidth"]!, Saturation = (float)gi["GISaturation"]!,
            Brightness = (float)gi["GIBrightness"]!, RimPower = (float)gi["RimPower"]!, RimSoftness = (float)gi["RimSoftness"]!,
            TintR = (float)tint[0], TintG = (float)tint[1], TintB = (float)tint[2],
        };
        float[] Hdr(string file, int channels, out int hw, out int hh)
        {
            // Half floats' bits in a 16-bit RGB PNG, rows from the top.
            var bits = Pixels(Path.Combine(folder, file), PixelFormats.Rgb48, out hw, out hh);
            var o = new float[hw * hh * channels];
            for (int y = 0; y < hh; y++)
                for (int x = 0; x < hw; x++)
                    for (int c = 0; c < 3; c++)
                    {
                        int at = (((hh - 1 - y) * hw + x) * 3 + c) * 2;
                        o[(y * hw + x) * channels + c] = (float)BitConverter.UInt16BitsToHalf((ushort)(bits[at] | bits[at + 1] << 8));
                    }
            if (channels == 4) for (int i = 3; i < o.Length; i += 4) o[i] = 1f;
            return o;
        }
        var roomHdr = Hdr("room-hdr.png", 3, out _, out _);
        var unlitHdr = Hdr("unlit-hdr.png", 3, out _, out _);
        var lamps = new System.Collections.Generic.List<float[]>();
        for (int k = 0; File.Exists(Path.Combine(folder, $"lamp-{k}-hdr.png")); k++) lamps.Add(Hdr($"lamp-{k}-hdr.png", 3, out _, out _));
        var background = Hdr("background.png", 4, out int bw, out int bh);
        var lutBytes = Pixels(Path.Combine(folder, "post-lut.png"), PixelFormats.Bgra32, out int lw2, out int lh2);
        var table = new float[lw2 * lh2 * 4];
        for (int y = 0; y < lh2; y++)
            for (int x = 0; x < lw2; x++)
            {
                int i = ((lh2 - 1 - y) * lw2 + x) * 4, o = (y * lw2 + x) * 4;
                table[o] = lutBytes[i + 2] / 255f; table[o + 1] = lutBytes[i + 1] / 255f; table[o + 2] = lutBytes[i] / 255f; table[o + 3] = 1f;
            }
        var post = GamePost.Settings.From(JObject.Parse(File.ReadAllText(Path.Combine(folder, "post.json"))));
        var noGlow = File.Exists(Path.Combine(folder, "room-noglow-hdr.png")) ? Hdr("room-noglow-hdr.png", 3, out _, out _) : null;
        LampSettings? lamp = null;
        if (json["lamp"] is JObject l)
        {
            var colour = (JArray)l["colour"]!; var global = (JArray)l["global"]!;
            lamp = new LampSettings((float)l["x"]!, (float)l["y"]!, (float)l["radius"]!, (float)colour[0], (float)colour[1], (float)colour[2],
                                    (float)l["falloff"]!, (float)l["volumeOpacity"]!, (float)l["shadowIntensity"]!, (float)l["shadowVolumeIntensity"]!,
                                    (float)l["shadowSoftness"]!, (float)l["shadowSoftnessFalloff"]!, (float)l["textureScale"]!,
                                    ((float)global[0], (float)global[1], (float)global[2]),
                                    l["layers"] is JArray layers && layers.Count > 0 && layers.All(n => (string?)n == "Default"));
        }
        var decode = json["lookupDecode"] is JArray d ? d.Select(v => (float)v).ToArray() : null;
        return new PreviewBackdrop((string?)json["name"] ?? Path.GetFileName(folder), w, h, room, settings, (JObject)json["bust"]!,
                                   light, (int)lights["left"]!, (int)lights["bottom"]!, (int)lights["cell"]!, cols, rows,
                                   roomHdr, unlitHdr, lamps.ToArray(), background, bw, bh, table, post, noGlow, lamp, decode);
    }

    private static byte[] Pixels(string path, PixelFormat format, out int w, out int h)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(frame, format, null, 0);
        w = converted.PixelWidth; h = converted.PixelHeight;
        int stride = w * format.BitsPerPixel / 8;
        var px = new byte[stride * h];
        converted.CopyPixels(px, stride, 0);
        return px;
    }
}

/// <summary>
/// The lamp a bust's shadow is cast from: where it is (screen pixels from the
/// bottom left) and how far it reaches, its colour (linear, times its
/// intensity), its falloff and glow, its shadow's numbers, the light texture
/// scale and the global light under it - all as the game's Light2D and 2D
/// renderer hold them.
/// <para><see cref="GlowWithTheRoom"/> (1.9.2): the lamp lights only the
/// room's layer (Default), the case for most of the game's level lamps. URP
/// draws a light's glow with the layer batch that ends at the highest layer
/// it lights (DrawLight2DPass: <c>endLayerValue == GetTopMostLitLayer()</c>),
/// and copies the edge glow's background right after that batch's glow
/// (Renderer2D's render graph), so such a lamp's glow is in the room before
/// the busts are drawn and in that background, and never over a bust. A lamp
/// that also lights a layer above (the PC's room's: Photos) glows over them.
/// The backdrop's "layers"; none listed is the second kind, as before.</para>
/// </summary>
public sealed record LampSettings(float X, float Y, float Radius, float R, float G, float B, float Falloff, float VolumeOpacity,
                                  float ShadowIntensity, float ShadowVolumeIntensity, float Softness, float SoftnessFalloff,
                                  float TextureScale, (float r, float g, float b) Global, bool GlowWithTheRoom = false);

/// <summary>The bust shader's fake GI settings: FakeGIManager's globals
/// (_Global_GI*, _Global_Rim*), as the game sets them in a scene.</summary>
public sealed class FakeGiSettings
{
    public float Enabled { get; init; } = 1f;
    public float Intensity { get; init; } = 1f;
    public float BlurRadius { get; init; } = 0.11f;
    public float BlurAmount { get; init; } = 1f;
    public float EdgeWidth { get; init; } = 0.04f;
    public float Saturation { get; init; } = 1.2f;
    public float Brightness { get; init; } = 1f;
    public float TintR { get; init; } = 1f;
    public float TintG { get; init; } = 1f;
    public float TintB { get; init; } = 1f;
    public float RimPower { get; init; } = 1f;
    public float RimSoftness { get; init; } = 1.6f;
}
