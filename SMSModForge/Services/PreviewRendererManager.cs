using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;
using SMSModForge.ViewModel;

namespace SMSModForge.Services;

/// <summary>One choice of what draws the live previews (Options ▸ Draw previews on).</summary>
public sealed class PreviewRendererDef : ObservableObject
{
    public string Name { get; init; } = "";

    public string NameKey => "previewRenderer." + Name.ToLowerInvariant();

    public string DisplayName => Loc.T(NameKey);

    public string Description => Loc.T(NameKey + ".tip");

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { _isCurrent = value; OnPropertyChanged(); }
    }
}

/// <summary>
/// What draws the live previews: the graphics card or the processor (1.9.1,
/// the author: "maybe we could even decide on load whether we should use CPU
/// or GPU rendering (some PCs might not have dedicated GPUs, or just CPUs that
/// would get better FPS in general for this)"). Automatic, the default, times
/// both on the shipped room when the editor starts and takes the faster; the
/// other two say which. The card draws the game's own programs
/// (<see cref="GpuBody"/>); a PC without a card that can falls back to the
/// processor whatever is chosen.
/// <para/>
/// Under the test suite nothing is measured and Automatic draws on the
/// processor, so the suite's frames stay the processor's; a test that wants
/// the card applies it.
/// </summary>
public static class PreviewRendererManager
{
    public static readonly IReadOnlyList<PreviewRendererDef> All = new[]
    {
        new PreviewRendererDef { Name = "Automatic" },
        new PreviewRendererDef { Name = "Card" },
        new PreviewRendererDef { Name = "Processor" },
    };

    private static readonly PreviewRendererDef Default = All[0];

    public static PreviewRendererDef Current { get; private set; } = Default;

    /// <summary>Automatic's answer: true for the card, false for the
    /// processor, null until measured.</summary>
    internal static bool? MeasuredCard { get; private set; }

    /// <summary>What was measured, for the record (milliseconds a frame).</summary>
    internal static string? Measurement { get; private set; }

    /// <summary>Whether the previews draw on the card now.</summary>
    public static bool UseCard => Current.Name switch
    {
        "Card" => GpuDevice.Shared != null,
        "Processor" => false,
        _ => MeasuredCard == true,
    };

    private static string FilePath => AppDataFolder.PathOf("preview-renderer.json");

    public static void Apply(PreviewRendererDef def)
    {
        if (def == null) return;
        foreach (var d in All) d.IsCurrent = ReferenceEquals(d, def);
        Current = def;
        if (TestMode.Active) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(def.Name));
        }
        catch { /* best-effort, as the quality's */ }
    }

    /// <summary>Restore the saved choice and, outside the test suite, time
    /// both in the background. Call once at startup.</summary>
    public static void ApplySaved()
    {
        string? name = null;
        if (!TestMode.Active)
        {
            try { name = File.Exists(FilePath) ? JsonConvert.DeserializeObject<string>(File.ReadAllText(FilePath)) : null; }
            catch { name = null; }
        }
        var def = All.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Default;
        foreach (var d in All) d.IsCurrent = ReferenceEquals(d, def);
        Current = def;
        if (!TestMode.Active) Task.Run(() => { try { Measure(); } catch (Exception e) { Measurement = "not measured: " + e.Message; } });   // English on purpose: a measurement note kept for the record, never shown
    }

    /// <summary>
    /// Time the bust preview's frame both ways on the shipped room, as the
    /// preview draws it at Ultra - a bust moving by every layer, its shadow,
    /// its edge glow, the post-processing, at the game's density - and keep the
    /// faster. The card's time includes the shadow it is handed and bringing
    /// its frame back; the processor's runs on its own copy of the edge glow,
    /// so a preview drawing meanwhile is not disturbed.
    /// </summary>
    internal static void Measure()
    {
        var scene = PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited();
        if (scene == null || scene.Lamp == null) { MeasuredCard = false; Measurement = "no backdrop with a lamp"; return; }   // English on purpose: a measurement note kept for the record, never shown
        using var card = GpuFrame.TryCreate();
        if (card == null) { MeasuredCard = false; Measurement = "no card: " + GpuDevice.SharedFailure; return; }   // English on purpose: a measurement note kept for the record, never shown
        var backdrop = scene.Backdrop;
        var lamp = scene.Lamp;
        var gi = new FakeGi(backdrop);
        const int S = JiggleShader.Size, N = BackdropRender.FrameSize;
        var art = new byte[S * S * 4];
        var mask = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int i = (y * S + x) * 4;
                double dx = (x - 128) / 100.0, dy = (y - 140) / 110.0;
                bool inside = dx * dx + dy * dy < 1;
                art[i] = 120; art[i + 1] = 150; art[i + 2] = 200; art[i + 3] = (byte)(inside ? 255 : 0);
                mask[i] = 160; mask[i + 1] = 160; mask[i + 2] = 160; mask[i + 3] = 255;
            }
        var p = new JiggleParams { Speed = 3f, Strength = -0.02f, Frequency = 4f, NoiseScale = 5f, NoiseSpeed = 0.5f, NoiseStrength = 0.06f };
        var giArt = new FakeGi.Art(art, S, S, gi.Reach);
        var caster = new BustShadow.Caster(art, S, S, 100f);
        var shadow = new float[lamp.Shadow.W * lamp.Shadow.H];
        var target = BackdropRender.Scene(scene);
        var background = new float[backdrop.BackgroundWidth * backdrop.BackgroundHeight * 4];
        var body = new float[N * N * 4];
        var coverage = new float[N * N];
        var output = new byte[N * N * 4];
        var work = new GamePost.Work();
        int bottom = backdrop.BustRestBottom;
        var parts = BackdropRender.PostParts.All;

        double Time(Action<float> frame, int runs)
        {
            frame(1f);                                    // warm: compile or load, upload, first touches
            var clock = Stopwatch.StartNew();
            for (int k = 0; k < runs; k++) frame(2f + k * 0.016f);
            return clock.Elapsed.TotalMilliseconds / runs;
        }
        void Shadow(float t) => lamp.Shadow.Draw(caster, scene.Left + N / 2f, bottom + 0.3f * MathF.Sin(t) + N / 2f, 2f, 1f, shadow);
        double onCard = Time(t =>
        {
            Shadow(t);
            card.Draw(lamp, shadow, scene, art, mask, p, (1f, 1f, 1f, 1f), t, true, bottom, true, 0f, null, null, null, parts, JiggleShader.GameZoom, output);
        }, 10);
        double onProcessor = Time(t =>
        {
            Shadow(t);
            lamp.Room(shadow, target, background, backdrop.BackgroundWidth, backdrop.BackgroundHeight);
            gi.UseBackground(background);
            BackdropRender.Body(art, mask, p, (1f, 1f, 1f, 1f), t, gi, giArt, scene.Left, scene.FrameBottom, bottom, body, true, null, 0f, true, scene.Decode, JiggleShader.GameZoom);
            Array.Clear(coverage);
            BackdropRender.Layer(target, scene, body, coverage);
            lamp.Glow(target, shadow, BackdropRender.Covered(scene, coverage));
            BackdropRender.Present(target, scene, parts, output, work);
        }, 3);
        MeasuredCard = onCard < onProcessor;
        Measurement = FormattableString.Invariant($"card {onCard:0.0} ms, processor {onProcessor:0.0} ms a frame at Ultra ({GpuDevice.Shared?.Adapter})");   // English on purpose: a measurement note kept for the record, never shown
    }
}
