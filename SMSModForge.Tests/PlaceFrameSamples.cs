using System;
using System.Collections.Generic;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;

namespace SMSModForge.Tests;

/// <summary>
/// A place's frame for the card (Gpu.GpuPlaceScene), made without a window: one
/// of every kind of picture the place preview's scene holds - a level's art read
/// by its nearest pixel, an object turned and faded and drawn with the unlit
/// material, an NPC's pose read small, a tint through a picture's alpha, a
/// shadow's disc, a marker's line, a shader's picture left on the card - under a
/// round lamp with a glow, a cone and a free-shape light with one, seen zoomed in
/// and past the screen's edge. For the tests that hold the card's drawing to its
/// own rules (it writes all it reads; it is the same whatever drew before).
/// </summary>
internal static class PlaceFrameSamples
{
    private static byte[] Art(int w, int h, int seed, bool disc)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                double fx = (double)x / w, fy = (double)y / h;
                double a = 1.0;
                if (disc)
                {
                    double dx = (fx - 0.5) * 2, dy = (fy - 0.5) * 2;
                    a = Math.Clamp((0.9 - Math.Sqrt(dx * dx + dy * dy)) * 10.0, 0.0, 1.0);
                }
                double r = 0.5 + 0.4 * Math.Sin(fx * 7 + seed), g = 0.5 + 0.4 * Math.Sin(fy * 5 + seed * 2), b = ((x / 8 + y / 8 + seed) & 1) * 0.5 + 0.2;
                px[i] = (byte)(b * a * 255); px[i + 1] = (byte)(g * a * 255); px[i + 2] = (byte)(r * a * 255); px[i + 3] = (byte)(a * 255);
            }
        return px;
    }

    private static readonly byte[] Level = Art(512, 288, 1, false);
    private static readonly byte[] Thing = Art(96, 64, 2, true);
    private static readonly byte[] Pose = Art(120, 240, 3, true);
    private static readonly byte[] Moving = Art(300, 200, 4, false);
    private static readonly byte[] MovingMask = MaskOf(128);

    private static byte[] MaskOf(int size)
    {
        var px = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[(y * size + x) * 4 + 3] = (byte)(x * 255 / size);
        return px;
    }

    private static void Put(ref PlaceItem item, double m11, double m12, double m21, double m22, double x, double y)
    {
        item.M11 = m11; item.M12 = m12; item.M21 = m21; item.M22 = m22; item.OffsetX = x; item.OffsetY = y;
        item.Opacity = 1f;
    }

    /// <summary>The frame; <paramref name="milking"/> draws the shader's picture it reads from the card.</summary>
    public static PlaceFrame Make(BackdropScene scene, GpuMilking milking, BackdropRender.PostParts parts, bool lights = true)
    {
        var f = new PlaceFrame();
        {
            ref var item = ref f.Add();          // the level's art, over the whole canvas and past it
            item.Pixels = Level; item.PixelsW = 512; item.PixelsH = 288;
            item.Sampling = PlaceSampling.Nearest; item.Lit = 1;
            Put(ref item, 2100, 0, 0, 1180, -90, -50);
        }
        {
            ref var item = ref f.Add();          // a shader's picture, left on the card
            var picture = new PlaceCardPicture();
            f.DrawFirst.Add(() =>
            {
                milking.DrawOnCard(Moving, 300, 200, MovingMask, 128, 128, MilkingShader.Settings.Level, 2.5f, 300, 200, maskStill: true);
                picture.Drawn = () => milking.OnCard;
            });
            item.OnCard = picture;
            item.Sampling = PlaceSampling.Nearest; item.Lit = 1;
            Put(ref item, 600, 0, 0, 400, 1200, 80);
        }
        {
            ref var item = ref f.Add();          // an object: turned, faded, unlit
            item.Pixels = Thing; item.PixelsW = 96; item.PixelsH = 64;
            item.Sampling = PlaceSampling.Linear; item.Lit = 2;
            Put(ref item, 380, 120, -90, 250, 500, 300);
            item.Opacity = 0.7f;
        }
        {
            ref var item = ref f.Add();          // its tint
            item.Pixels = Thing; item.PixelsW = 96; item.PixelsH = 64;
            item.Kind = PlaceItemKind.Tint; item.Sampling = PlaceSampling.Linear;
            Put(ref item, 380, 120, -90, 250, 500, 300);
            item.R = 1f; item.G = 0.5f; item.B = 0.25f; item.A = 1f; item.Opacity = 0.4f;
        }
        {
            ref var item = ref f.Add();          // a shadow
            item.BuiltIn = PlaceBuiltIn.Disc; item.Kind = PlaceItemKind.Tint; item.Sampling = PlaceSampling.Smooth;
            Put(ref item, 260, 0, 0, 80, 880, 760);
            item.A = 0.5f;
        }
        {
            ref var item = ref f.Add();          // an NPC's pose, drawn smaller than it is
            item.Pixels = Pose; item.PixelsW = 120; item.PixelsH = 240;
            item.Sampling = PlaceSampling.Smooth; item.Lit = 1;
            Put(ref item, 90, 0, 0, 180, 960, 600);
        }
        {
            ref var item = ref f.Add();          // a marker's line
            item.BuiltIn = PlaceBuiltIn.Solid; item.Kind = PlaceItemKind.Tint; item.Sampling = PlaceSampling.Nearest;
            Put(ref item, 200, 30, -1, 6, 900, 560);
            item.R = 0.8f; item.G = 0.93f; item.B = 1f; item.A = 0.78f;
        }
        // Zoomed in on the right of the screen, a part of the view past its edge.
        f.ViewX = 700; f.ViewY = 150; f.ViewW = 1400; f.ViewH = 787.5;
        f.Vw = 640; f.Vh = 360;
        f.CanvasW = 1920; f.CanvasH = 1080;
        f.Parts = parts;
        f.Scene = scene;
        if (lights)
        {
            var free = LightShape.Free(new[] { -2f, 2f, 1.5f, -1.8f }, new[] { -1f, -1.2f, 1.3f, 1f }, 0.8f, 50, 0, 0, 50, 520, 250);
            f.Lights = new PlaceLights(new List<PlaceLights.Light>
            {
                new(600f, 300f, 220f, 0.9f, 0.8f, 0.6f, 0.5f, 0.5f),
                new(760f, 380f, 260f, 0.5f, 0.7f, 1.1f, 0.6f, 0.3f, Rotation: 150f, InnerRadiusMult: 1.25f, InnerAngle: 0.08f, OuterAngle: 0.2f),
                new(0f, 0f, 0f, 0.7f, 0.7f, 0.5f, 0.5f, 0.4f, Shape: free),
            }, (0.6f, 0.62f, 0.8f), scene.Backdrop.LookupDecode);
            f.Masked = true;
        }
        return f;
    }
}
