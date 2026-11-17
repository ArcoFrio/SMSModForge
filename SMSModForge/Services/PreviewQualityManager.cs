using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SMSModForge.ViewModel;
using SMSModForge.Localization;

namespace SMSModForge.Services;

/// <summary>
/// One live-preview quality preset: how much of the game's look the previews
/// draw, every one of them as often as the display shows a frame (1.9.1, the
/// author: "we'd want them all to capped at display refresh rate, just
/// reducing how much of a load it puts on the pc in general"; "Ultra should be
/// the 1:1 comparison to what we see in the game, while the other options
/// scale down or remove its components").
/// <para/>
/// Until then (1.9.1's first take) a preset was a frame rate - 60, 30, 20 and
/// 15 a second - and drew the same picture at each. Before that, Ultra and
/// High supersampled the jiggle shader 4x, a smoothing the game does not do.
/// <para/>
/// Measure processor use with QueryProcessCycleTime, not TotalProcessorTime:
/// Windows charges the latter a whole 15.6 ms tick to whatever runs at each
/// tick, and a short pass started just after one is barely charged at all.
/// </summary>
public sealed class PreviewQualityDef : ObservableObject
{
    /// <summary>What the preset is saved as. Never shown - <see cref="DisplayName"/> is.</summary>
    public string Name { get; init; } = "";

    /// <summary>The key of its name in the English file; its description
    /// is the same key and <c>.tip</c>.</summary>
    public string NameKey => "previewQuality." + Name.ToLowerInvariant();

    public string DisplayName => Loc.T(NameKey);

    public string Description => Loc.T(NameKey + ".tip");

    /// <summary>The edge glow a bust takes from what is behind it (the game's fake GI).</summary>
    public bool EdgeGlow { get; init; }

    /// <summary>The edge glow's blur worked out at every pixel from the room as
    /// a bust's shadow leaves it, as the game does - rather than from a table
    /// made once from the room without the shadow.</summary>
    public bool EdgeGlowExact { get; init; }

    /// <summary>The post-processing's bloom.</summary>
    public bool Bloom { get; init; }

    /// <summary>The post-processing's anamorphic flares - the light streaks.</summary>
    public bool Flares { get; init; }

    /// <summary>The post-processing's colour: tonemapping, vignette, URP's grading.</summary>
    public bool Colour { get; init; }

    /// <summary>How finely a bust or an NPC with no backdrop is sampled (1.9.1, the
    /// author: "we still want the graphics options to affect the performance on no
    /// backdrops"): the game's own at Ultra - two by two samples in each preview
    /// pixel, as the game draws on a 1920x1080 screen - fewer below. Over a
    /// backdrop every quality keeps the game's; the parts above are what it saves there.</summary>
    public Rendering.JiggleShader.Sampling Sampling { get; init; } = Rendering.JiggleShader.Sampling.Game;

    private bool _isCurrent;
    /// <summary>True for the active preset — drives the menu checkmark.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set { _isCurrent = value; OnPropertyChanged(); }
    }
}

/// <summary>
/// Holds the preview-quality presets, tracks the active one, and persists the
/// choice across launches. <see cref="View.Controls.JigglePreview"/> reads
/// <see cref="Current"/> every frame, so switching presets takes effect live.
/// </summary>
public static class PreviewQualityManager
{
    // Catalogue, best -> cheapest. Ultra is the game's own picture; each level
    // below leaves out the costliest part left (measured on the author's
    // machine, a frame of the bust over the room: the flares 12 ms, the bloom
    // 5, the exact edge glow 5 more than its table, the colour 5).
    public static IReadOnlyList<PreviewQualityDef> All { get; } = new[]
    {
        // With no backdrop (Sampling): Ultra the game's 2x2 samples a pixel; High keeps the two
        // rows (which keep a wave's steps the game's size), one across; Medium one a pixel;
        // Low one per 2x2 pixels - a bust's own texels.
        new PreviewQualityDef { Name = "Ultra",  EdgeGlow = true, EdgeGlowExact = true, Bloom = true, Flares = true, Colour = true,
                                Sampling = new(2, 2, 1) },
        new PreviewQualityDef { Name = "High",   EdgeGlow = true, Bloom = true, Colour = true, Sampling = new(1, 2, 1) },
        new PreviewQualityDef { Name = "Medium", EdgeGlow = true, Colour = true, Sampling = new(1, 1, 1) },
        new PreviewQualityDef { Name = "Low",    Colour = true, Sampling = new(1, 1, 2) },
    };

    // Index 2 ("Medium") is the default + fallback.
    private static readonly PreviewQualityDef Default = All[2];

    public static PreviewQualityDef Current { get; private set; } = Default;

    private static string FilePath => AppDataFolder.PathOf("preview.json");

    /// <summary>Select <paramref name="def"/> as the active preset + persist it.</summary>
    public static void Apply(PreviewQualityDef def)
    {
        if (def == null) return;
        foreach (var d in All) d.IsCurrent = ReferenceEquals(d, def);
        Current = def;
        SaveName(def.Name);
    }

    /// <summary>Restore the persisted preset (or the default). Call once at startup.</summary>
    public static void ApplySaved()
    {
        string? name = LoadName();
        var def = All.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
                  ?? Default;
        Apply(def);
    }

    private static string? LoadName()
    {
        // Nothing the test suite does is the author's: see TestMode.
        if (TestMode.Active) return null;
        try { return File.Exists(FilePath) ? JsonConvert.DeserializeObject<string>(File.ReadAllText(FilePath)) : null; }
        catch { return null; }
    }

    private static void SaveName(string name)
    {
        if (TestMode.Active) return;
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(name));
        }
        catch { /* best-effort, same as RecentFilesService */ }
    }
}
