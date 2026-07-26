using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SMSModForge.ViewModel;

namespace SMSModForge.Services;

/// <summary>
/// One live-preview quality preset. Two knobs drive cost:
/// <list type="bullet">
///   <item><see cref="MaxFps"/> — how often the CPU shader pass runs. This is
///   the dominant lever: the preview renders back-to-back as fast as the cap
///   allows, so halving the cap roughly halves CPU use.</item>
///   <item><see cref="SuperSample"/> — 4× sub-pixel anti-aliasing on the
///   jiggle shader. Off quarters the per-frame work but lets motion-boundary
///   "tearing" show on fast jiggle.</item>
/// </list>
/// </summary>
public sealed class PreviewQualityDef : ObservableObject
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Upper bound on shader passes per second.</summary>
    public int MaxFps { get; init; }

    /// <summary>True → 4× sub-pixel supersampling in <c>JiggleShader</c>.</summary>
    public bool SuperSample { get; init; }

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
    // Catalogue, best → cheapest. "Ultra" reproduces the original uncapped-ish
    // 60 Hz + 4× behaviour; "Medium" is the default — a lighter baseline now
    // that the NPC preview can render much larger surfaces than the bust one.
    public static IReadOnlyList<PreviewQualityDef> All { get; } = new[]
    {
        new PreviewQualityDef { Name = "Ultra",  MaxFps = 60, SuperSample = true,  Description = "60 fps, 4× anti-aliasing. Smoothest motion, highest CPU." },
        new PreviewQualityDef { Name = "High",   MaxFps = 30, SuperSample = true,  Description = "30 fps, 4× anti-aliasing. Same fidelity as Ultra at ~half the CPU." },
        new PreviewQualityDef { Name = "Medium", MaxFps = 30, SuperSample = false, Description = "30 fps, no anti-aliasing. Slight motion-edge shimmer; much lighter." },
        new PreviewQualityDef { Name = "Low",    MaxFps = 20, SuperSample = false, Description = "20 fps, no anti-aliasing. Lightest; choppier motion." },
    };

    // Index 2 ("Medium") is the default + fallback.
    private static readonly PreviewQualityDef Default = All[2];

    public static PreviewQualityDef Current { get; private set; } = Default;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SMSModForge", "preview.json");

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
        try { return File.Exists(FilePath) ? JsonConvert.DeserializeObject<string>(File.ReadAllText(FilePath)) : null; }
        catch { return null; }
    }

    private static void SaveName(string name)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(name));
        }
        catch { /* best-effort, same as RecentFilesService */ }
    }
}
