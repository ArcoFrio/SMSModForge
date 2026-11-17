using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SMSModForge.Localization;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;

namespace SMSModForge.Services;

/// <summary>
/// What the bust preview draws a bust over (1.9.1): one of the game's places as
/// the game has it behind a bust (<see cref="PreviewBackdrop"/>), or none - the
/// bust alone, as before. The author: "A backdrop list could be good, but I'm
/// expecting people to not change that at all so keep that in mind when
/// choosing how to represent it in the UI. The default should be the player's
/// room level, which is where we load the game into." So: a list under
/// Options, the player's room first and chosen.
/// </summary>
public sealed class PreviewBackdropDef : ObservableObject
{
    /// <summary>What the choice is saved as, and the shipped folder's name
    /// (Backdrops/&lt;Name&gt;); "None" for none. Never shown.</summary>
    public string Name { get; init; } = "";

    public string NameKey { get; init; } = "";

    /// <summary>The game's level it was captured in (its object's name): what
    /// sizes an NPC drawn over it, by the level's own scale.</summary>
    public string Level { get; init; } = "";

    /// <summary>The level's own scale, which an NPC there is drawn at (from the
    /// editor's catalogue of the game's levels; almost every one is 0.79).</summary>
    public float LevelScale
    {
        get
        {
            var s = SMSModForge.Model.VanillaLevelCatalog.FindLevel(Level)?.Hierarchy?.ScaleY ?? 0f;
            return s > 0f ? s : 0.79f;
        }
    }

    public string DisplayName => Loc.T(NameKey);
    public string Description => Loc.T(NameKey + ".tip");

    public bool IsNone => Name == "None";

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { _isCurrent = value; OnPropertyChanged(); }
    }

    private Task<BackdropScene?>? _loading;

    /// <summary>The backdrop placed for the preview, null while it loads (in the
    /// background, the first time it is asked for), if it is none, or if its
    /// files are missing. Its fake GI comes once made (<see cref="BackdropScene.Gi"/>).</summary>
    internal BackdropScene? Scene
    {
        get
        {
            if (IsNone) return null;
            _loading ??= Task.Run(() =>
            {
                try
                {
                    var scene = new BackdropScene(PreviewBackdrop.Load(Path.Combine(PreviewBackdrop.ShippedFolder, Name)));
                    scene.Prepare();
                    return scene;
                }
                catch (Exception) { return null; }   // a missing backdrop draws the bust alone
            });
            return _loading.IsCompletedSuccessfully ? _loading.Result : null;
        }
    }

    /// <summary>For tests: the scene, waited for.</summary>
    internal BackdropScene? SceneWaited() { _ = Scene; return _loading?.GetAwaiter().GetResult(); }
}

public static class PreviewBackdropManager
{
    public static IReadOnlyList<PreviewBackdropDef> All { get; } = new[]
    {
        new PreviewBackdropDef { Name = "MyRoom", NameKey = "previewBackdrop.room", Level = "5_MyRoom" },
        // Four of the game's levels besides (1.9.1, the author: "the 4 most diverse
        // and interesting ones ... One of the levels has to be the beach"):
        // InGameSuiteTests.LevelBackdrops says how they were chosen.
        new PreviewBackdropDef { Name = "Beach", NameKey = "previewBackdrop.beach", Level = "14_Beach" },
        new PreviewBackdropDef { Name = "Forest", NameKey = "previewBackdrop.forest", Level = "16_Forest" },
        new PreviewBackdropDef { Name = "Villa", NameKey = "previewBackdrop.villa", Level = "71_Villa_Inside" },
        new PreviewBackdropDef { Name = "Temple", NameKey = "previewBackdrop.temple", Level = "69_Temple_Inside" },
        new PreviewBackdropDef { Name = "None", NameKey = "previewBackdrop.none" },
    };

    private static readonly PreviewBackdropDef Default = All[0];

    /// <summary>The game's post-processing settings, grading table and sRGB
    /// decoding, which every place shares (the place preview's look): the
    /// player's room's, loaded whatever the bust preview draws over. Null
    /// while it loads.</summary>
    internal static BackdropScene? GameLook => All.First(d => !d.IsNone).Scene;

    public static PreviewBackdropDef Current { get; private set; } = Default;

    private static string FilePath => AppDataFolder.PathOf("preview-backdrop.json");

    private static bool _chosen;

    public static void Apply(PreviewBackdropDef def)
    {
        if (def == null) return;
        _chosen = true;
        foreach (var d in All) d.IsCurrent = ReferenceEquals(d, def);
        Current = def;
        Save(def.Name);
    }

    /// <summary>Restore the saved choice (or the player's room). Call once at
    /// startup; a choice already made in this process (a test's) stands.</summary>
    public static void ApplySaved()
    {
        if (_chosen) { _ = Current.Scene; return; }
        string? name = Load();
        Apply(All.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Default);
        _ = Current.Scene;   // start loading it now, not on the first frame
    }

    private static string? Load()
    {
        if (TestMode.Active) return null;
        try { return File.Exists(FilePath) ? JsonConvert.DeserializeObject<string>(File.ReadAllText(FilePath)) : null; }
        catch { return null; }
    }

    private static void Save(string name)
    {
        if (TestMode.Active) return;
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(name));
        }
        catch { /* best-effort, as the preview quality */ }
    }
}
