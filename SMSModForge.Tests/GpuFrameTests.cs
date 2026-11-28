using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The bust preview's whole frame over the shipped room on the graphics card
/// (GpuFrame) is the processor's, step for step as JigglePreview runs them:
/// the lamp's shadow (drawn on the processor either way), the room lit
/// through it, the body with its edge glow from that room, a face over it,
/// the intro's silhouette, the lamp's glow, the post-processing - the
/// person's shape moving by every layer, at the game's zoom. Compared on the
/// screen's bytes. Within Direct3D's rounding, ties included (GpuBodyTests,
/// GpuPostTests). Control: the card without the edge glow is visibly not the
/// processor's frame with it.
/// </summary>
public sealed class GpuFrameTests
{
    private readonly ITestOutputHelper _out;
    public GpuFrameTests(ITestOutputHelper o) => _out = o;

    /// <summary>Options ▸ Draw previews on ▸ Automatic times both on the shipped
    /// room's frame at Ultra and takes the faster - on the author's PC (RTX 4070
    /// Ti SUPER, i9-14900K) the card, measured each time.</summary>
    [Fact]
    public void AutomaticTimesBothAndTakesTheFaster()
    {
        // The answer is the process's: put it back, or every test after this one in the same tester finds Automatic
        // on the card (PreviewRendererTests did, 2026-11-19).
        var wasCard = Services.PreviewRendererManager.MeasuredCard;
        var wasNote = Services.PreviewRendererManager.Measurement;
        try
        {
            Services.PreviewRendererManager.Measure();
            _out.WriteLine(Services.PreviewRendererManager.Measurement);
            var m = System.Text.RegularExpressions.Regex.Match(Services.PreviewRendererManager.Measurement ?? "",
                                                               @"card ([\d.]+) ms, processor ([\d.]+) ms");
            Assert.True(m.Success, "nothing was measured: " + Services.PreviewRendererManager.Measurement);
            double card = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            double processor = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(card < processor, Services.PreviewRendererManager.MeasuredCard);
        }
        finally
        {
            Services.PreviewRendererManager.MeasuredCard = wasCard;
            Services.PreviewRendererManager.Measurement = wasNote;
        }
    }

    /// <summary>A backdrop without a lamp (most of the game's levels: a global
    /// light only): the card's frame is the processor's there too. The PC's
    /// room with its lamp taken out stands in for one.</summary>
    [Fact]
    public void WithoutALampTheCardsFrameIsTheProcessorsToo()
    {
        using var card = GpuFrame.TryCreate();
        Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
        string folder = Path.Combine(Path.GetTempPath(), "smsmodforge-nolamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string shipped = Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom");
            foreach (var f in Directory.GetFiles(shipped)) if (!f.EndsWith("room-noglow-hdr.png")) File.Copy(f, Path.Combine(folder, Path.GetFileName(f)));
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(folder, "backdrop.json")));
            json.Remove("lamp");
            File.WriteAllText(Path.Combine(folder, "backdrop.json"), json.ToString());
            var scene = new BackdropScene(PreviewBackdrop.Load(folder));
            var gi = scene.Prepare();
            Assert.Null(scene.Lamp);
            const int S = JiggleShader.Size, N = BackdropRender.FrameSize;
            var art = new byte[S * S * 4];
            var mask = new byte[S * S * 4];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var c = InGameSuitePacks.Looks.FigureAt(x, y);
                    int i = (y * S + x) * 4;
                    art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
                    mask[i] = (byte)(x * 255 / S); mask[i + 1] = (byte)(y * 255 / S); mask[i + 2] = 200; mask[i + 3] = 255;
                }
            var p = new JiggleParams { Speed = 3f, Strength = -0.02f, Frequency = 4f, NoiseScale = 5f, NoiseSpeed = 0.5f, NoiseStrength = 0.06f };
            const float Time = 5.3f;
            int bustBottom = scene.Backdrop.BustRestBottom - 2;
            var parts = BackdropRender.PostParts.All;
            var target = BackdropRender.Scene(scene);
            var linear = new float[N * N * 4];
            BackdropRender.Body(art, mask, p, (1f, 1f, 1f, 1f), Time, gi, new FakeGi.Art(art, S, S, gi.Reach), scene.Left, scene.FrameBottom, bustBottom,
                                linear, true, null, 0f, true, scene.Decode, JiggleShader.GameZoom);
            var coverage = new float[N * N];
            BackdropRender.Layer(target, scene, linear, coverage);
            BackdropRender.Lamps(target, scene, coverage);
            var cpu = new byte[N * N * 4];
            BackdropRender.Present(target, scene, parts, cpu, new GamePost.Work());
            var onCard = new byte[N * N * 4];
            card!.DrawNoLamp(scene, art, mask, p, (1f, 1f, 1f, 1f), Time, true, bustBottom, true, 0f, null, null, null, parts, JiggleShader.GameZoom, onCard);
            var noGi = new byte[N * N * 4];
            card.DrawNoLamp(scene, art, mask, p, (1f, 1f, 1f, 1f), Time, false, bustBottom, true, 0f, null, null, null, parts, JiggleShader.GameZoom, noGi);
            int same = 0, overOne = 0;
            for (int i = 0; i < cpu.Length; i++)
            {
                int d = Math.Abs(cpu[i] - onCard[i]);
                if (d == 0) same++;
                if (d > 1) overOne++;
            }
            int controlOff = Enumerable.Range(0, cpu.Length).Count(i => Math.Abs(cpu[i] - noGi[i]) > 1);
            _out.WriteLine($"without a lamp: {same} of {cpu.Length} bytes the processor's, {overOne} more than a level off; without the edge glow {controlOff}");
            Assert.True(controlOff > 500, "control: the card without the edge glow passes for the processor's frame with it");
            Assert.True(overOne <= cpu.Length / 5000, $"the card's frame is more than a level from the processor's at {overOne} bytes");
            Assert.True(same > cpu.Length * 0.98, $"only {same} of {cpu.Length} bytes are the processor's");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    /// <summary>
    /// A wet bust's drops (after 1.10.0; measured in the game, InGameSuiteTests.S20): the card blends them over the
    /// frame before the post as the processor does (WetDrops.BlendInto). Over the PC's room without its lamp, as above,
    /// at the moment tests draw. Control: the card's frame without the drops is not the processor's with them.
    /// </summary>
    [Fact]
    public void AWetBustsDropsOnTheCardAreTheProcessors()
    {
        using var card = GpuFrame.TryCreate();
        Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
        Assert.NotNull(WetDrops.Texture);
        string folder = Path.Combine(Path.GetTempPath(), "smsmodforge-wetbust-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string shipped = Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom");
            foreach (var f in Directory.GetFiles(shipped)) if (!f.EndsWith("room-noglow-hdr.png")) File.Copy(f, Path.Combine(folder, Path.GetFileName(f)));
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(folder, "backdrop.json")));
            json.Remove("lamp");
            File.WriteAllText(Path.Combine(folder, "backdrop.json"), json.ToString());
            var scene = new BackdropScene(PreviewBackdrop.Load(folder));
            var gi = scene.Prepare();
            const int S = JiggleShader.Size, N = BackdropRender.FrameSize;
            var art = new byte[S * S * 4];
            var mask = new byte[S * S * 4];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var c = InGameSuitePacks.Looks.FigureAt(x, y);
                    int i = (y * S + x) * 4;
                    art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
                }
            var p = new JiggleParams();
            const float Time = 5.3f;
            int bustBottom = scene.Backdrop.BustRestBottom;
            var b = scene.Backdrop;
            double ppu = b.ScreenHeight / 10.8;
            var emitter = WetDrops.OnBust(scene.Left, bustBottom, N, b.ScreenHeight, ppu, 12345u);
            var drops = WetDrops.OnBustAt(WetDrops.TestTime, emitter, b.ScreenWidth * 100 / ppu, b.ScreenHeight * 100 / ppu);
            Assert.True(drops != null && drops.Count >= 1, "none of the bust's drops is on the screen");   // two or three are, as in the game
            var parts = BackdropRender.PostParts.All;
            byte[] Processor(bool wet)
            {
                var target = BackdropRender.Scene(scene);
                var linear = new float[N * N * 4];
                BackdropRender.Body(art, mask, p, (1f, 1f, 1f, 1f), Time, gi, new FakeGi.Art(art, S, S, gi.Reach), scene.Left, scene.FrameBottom, bustBottom,
                                    linear, true, null, 0f, true, scene.Decode, JiggleShader.GameZoom);
                var coverage = new float[N * N];
                BackdropRender.Layer(target, scene, linear, coverage);
                BackdropRender.Lamps(target, scene, coverage);
                if (wet) WetDrops.BlendInto(target, drops!);
                var bytes = new byte[N * N * 4];
                BackdropRender.Present(target, scene, parts, bytes, new GamePost.Work());
                return bytes;
            }
            var cpu = Processor(true);
            var cpuDry = Processor(false);
            var onCard = new byte[N * N * 4];
            card!.DrawNoLamp(scene, art, mask, p, (1f, 1f, 1f, 1f), Time, true, bustBottom, true, 0f, null, null, null, parts, JiggleShader.GameZoom, onCard, drops);
            var dry = new byte[N * N * 4];
            card.DrawNoLamp(scene, art, mask, p, (1f, 1f, 1f, 1f), Time, true, bustBottom, true, 0f, null, null, null, parts, JiggleShader.GameZoom, dry);
            int Off(byte[] a, byte[] c) => Enumerable.Range(0, a.Length).Count(i => (i & 3) != 3 && Math.Abs(a[i] - c[i]) > 1);
            int wetDrawn = Off(cpu, cpuDry), overOne = Off(cpu, onCard), controlOff = Off(cpu, dry);
            _out.WriteLine($"{drops!.Count} drops: they change {wetDrawn} bytes on the processor; the card's frame more than a level from it at {overOne}; the card's without them at {controlOff}");
            Assert.True(wetDrawn > 100, "the drops changed too little of the frame to tell");
            Assert.True(controlOff >= wetDrawn / 2, "control: the card's frame without the drops passes for the processor's with them");
            Assert.True(overOne <= wetDrawn / 50 + cpu.Length / 5000, $"the card's wet frame is more than a level from the processor's at {overOne} bytes");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    /// <summary>
    /// A bust's shower over a room with a lamp (after 1.10.0): its drops blended before the lamp's glow, which goes
    /// over them as over the bust (InGameSuiteTests.S22) - on the card (GpuFrame's DropsPass) as on the processor
    /// (JigglePreview: WetDrops.BlendInto with the frame's coverage, then the glow). Control: the card's frame without
    /// the drops is not the processor's with them.
    /// </summary>
    [Fact]
    public void AShowerOverABustWithALampOnTheCardIsTheProcessors()
    {
        using var card = GpuFrame.TryCreate();
        Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
        var backdrop = PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom"));
        var scene = new BackdropScene(backdrop);
        var gi = scene.Prepare();
        var lamp = scene.Lamp!;
        const int S = JiggleShader.Size, N = BackdropRender.FrameSize;
        var art = new byte[S * S * 4];
        var mask = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var c = InGameSuitePacks.Looks.FigureAt(x, y);
                int i = (y * S + x) * 4;
                art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
            }
        var p = new JiggleParams();
        const float Time = 6.21f;
        int bustBottom = backdrop.BustRestBottom;
        var shadow = new float[lamp.Shadow.W * lamp.Shadow.H];
        lamp.Shadow.Draw(new BustShadow.Caster(art, S, S, 100f), scene.Left + N / 2f, bustBottom - 0.4f + N / 2f, 2f, 1f, shadow);
        double ppu = backdrop.ScreenHeight / 10.8;
        var emitter = WetDrops.OnBust(scene.Left, bustBottom, N, backdrop.ScreenHeight, ppu, 12345u, WetDrops.Kind.Shower);
        var drops = WetDrops.OnBustAt(WetDrops.TestTime, emitter, backdrop.ScreenWidth * 100 / ppu, backdrop.ScreenHeight * 100 / ppu);
        Assert.True(drops != null && drops.Count > 100, "the shower is not on the screen");
        var parts = new BackdropRender.PostParts(false, false, true);
        byte[] Cpu(bool wet)
        {
            var target = BackdropRender.Scene(scene);
            var background = new float[backdrop.BackgroundWidth * backdrop.BackgroundHeight * 4];
            lamp.Room(shadow, target, background, backdrop.BackgroundWidth, backdrop.BackgroundHeight);
            gi.UseBackground(background);
            var linear = new float[N * N * 4];
            BackdropRender.Body(art, mask, p, (1f, 1f, 1f, 1f), Time, gi, new FakeGi.Art(art, S, S, gi.Reach), scene.Left, scene.FrameBottom, bustBottom,
                                linear, true, null, 0f, true, scene.Decode, JiggleShader.GameZoom);
            var coverage = new float[N * N];
            BackdropRender.Layer(target, scene, linear, coverage);
            if (wet) WetDrops.BlendInto(target, drops!, scene, coverage);
            lamp.Glow(target, shadow, BackdropRender.Covered(scene, coverage));
            var output = new byte[N * N * 4];
            BackdropRender.Present(target, scene, parts, output, new GamePost.Work());
            return output;
        }
        byte[] Card(WetDrops.Over? with)
        {
            var output = new byte[N * N * 4];
            card!.Draw(lamp, shadow, scene, art, mask, p, (1f, 1f, 1f, 1f), Time, true, bustBottom, true, 0f, null, null, null, parts,
                       JiggleShader.GameZoom, output, with);
            return output;
        }
        var cpu = Cpu(true);
        var cpuDry = Cpu(false);
        var onCard = Card(drops);
        var dry = Card(null);
        int Off(byte[] a, byte[] c) => Enumerable.Range(0, a.Length).Count(i => (i & 3) != 3 && Math.Abs(a[i] - c[i]) > 1);
        int wetDrawn = Off(cpu, cpuDry), overOne = Off(cpu, onCard), controlOff = Off(cpu, dry);
        _out.WriteLine($"{drops!.Count} drops: they change {wetDrawn} bytes on the processor; the card's frame more than a level from it at {overOne}; the card's without them at {controlOff}");
        Assert.True(wetDrawn > 500, "the drops changed too little of the frame to tell");
        Assert.True(controlOff >= wetDrawn / 2, "control: the card's frame without the drops passes for the processor's with them");
        Assert.True(overOne <= wetDrawn / 50 + cpu.Length / 5000, $"the card's wet frame is more than a level from the processor's at {overOne} bytes");
    }

    [Theory]
    [InlineData("MyRoom", 0f, true)]
    [InlineData("MyRoom", 0.55f, true)]
    [InlineData("MyRoom", 0f, false)]
    [InlineData("Beach", 0f, true)]   // a lamp whose glow is drawn with the room, under the bust (1.9.2)
    public void TheCardsFrameIsTheProcessors(string backdropName, float silhouette, bool bodyShows)
    {
        using var card = GpuFrame.TryCreate();
        Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
        var backdrop = PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", backdropName));
        var scene = new BackdropScene(backdrop);
        var gi = scene.Prepare();
        var lamp = scene.Lamp!;
        const int S = JiggleShader.Size, N = BackdropRender.FrameSize;
        var art = new byte[S * S * 4];
        var mask = new byte[S * S * 4];
        var face = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var c = InGameSuitePacks.Looks.FigureAt(x, y);
                int i = (y * S + x) * 4;
                art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
                mask[i] = (byte)(x * 255 / S); mask[i + 1] = (byte)(y * 255 / S); mask[i + 2] = 200; mask[i + 3] = 255;
                // A face: a soft-edged mark on the head.
                double d = Math.Sqrt((x - 128) * (x - 128) + (y - 90) * (y - 90));
                if (d < 14) { face[i] = 40; face[i + 1] = 60; face[i + 2] = 200; face[i + 3] = (byte)(d < 10 ? 255 : (14 - d) * 60); }
            }
        var p = new JiggleParams { Speed = 3f, Strength = -0.02f, Frequency = 4f, NoiseScale = 5f, NoiseSpeed = 0.5f, NoiseStrength = 0.06f };
        var tint = (1f, 1f, 1f, 1f);
        const float Time = 6.21f;
        int bustBottom = backdrop.BustRestBottom + 1;
        float shadowY = backdrop.BustRestBottom + 0.6f + N / 2f;
        var caster = new BustShadow.Caster(art, S, S, 100f);
        var shadow = new float[lamp.Shadow.W * lamp.Shadow.H];
        lamp.Shadow.Draw(caster, scene.Left + N / 2f, shadowY, 2f, bodyShows ? 1f : 0f, shadow);
        int zoom = JiggleShader.GameZoom;

        // The processor, as JigglePreview runs it.
        byte[] Cpu(BackdropRender.PostParts parts)
        {
            var target = BackdropRender.Scene(scene);
            var background = new float[backdrop.BackgroundWidth * backdrop.BackgroundHeight * 4];
            lamp.Room(shadow, target, background, backdrop.BackgroundWidth, backdrop.BackgroundHeight);
            gi.UseBackground(background);
            var giArt = new FakeGi.Art(art, S, S, gi.Reach);
            var linear = new float[N * N * 4];
            var linearSilhouette = silhouette > 0f ? new float[N * N * 4] : null;
            BackdropRender.Body(art, mask, p, tint, Time, gi, giArt, scene.Left, scene.FrameBottom, bustBottom, linear, bodyShows, linearSilhouette, silhouette, true, scene.Decode, zoom);
            var coverage = new float[N * N];
            BackdropRender.Layer(target, scene, linear, coverage);
            if (bodyShows) BackdropRender.OverlayLayer(target, scene, face, bustBottom, coverage);
            if (linearSilhouette != null) BackdropRender.Layer(target, scene, linearSilhouette, coverage);
            lamp.Glow(target, shadow, BackdropRender.Covered(scene, coverage));
            var output = new byte[N * N * 4];
            BackdropRender.Present(target, scene, parts, output, new GamePost.Work());
            return output;
        }
        byte[] Card(BackdropRender.PostParts parts, bool giOn)
        {
            var output = new byte[N * N * 4];
            card!.Draw(lamp, shadow, scene, art, mask, p, tint, Time, giOn, bustBottom, bodyShows, silhouette,
                       bodyShows ? face : null, null, null, parts, zoom, output);
            return output;
        }

        foreach (var parts in new[] { BackdropRender.PostParts.All, new BackdropRender.PostParts(false, false, true) })
        {
            var cpu = Cpu(parts);
            var onCard = Card(parts, true);
            int same = 0, overOne = 0;
            for (int i = 0; i < cpu.Length; i++)
            {
                int d = Math.Abs(cpu[i] - onCard[i]);
                if (d == 0) same++;
                if (d > 1) overOne++;
            }
            var noGi = Card(parts, false);
            int controlOff = Enumerable.Range(0, cpu.Length).Count(i => Math.Abs(cpu[i] - noGi[i]) > 1);
            _out.WriteLine($"{backdropName}: silhouette {silhouette}, body {bodyShows}, bloom {parts.Bloom}: {same} of {cpu.Length} bytes the processor's, {overOne} more than a level off; "
                           + $"the card without the edge glow: {controlOff} over a level");
            if (bodyShows) Assert.True(controlOff > 500, "control: the card without the edge glow passes for the processor's frame with it");
            Assert.True(overOne <= cpu.Length / 5000, $"the card's frame is more than a level from the processor's at {overOne} bytes");
            Assert.True(same > cpu.Length * 0.98, $"only {same} of {cpu.Length} bytes are the processor's");
        }
    }
}
