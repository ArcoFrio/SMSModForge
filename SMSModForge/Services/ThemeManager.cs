using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json;
using SMSModForge.ViewModel;

namespace SMSModForge.Services;

/// <summary>
/// One editor colour theme. Colours are stored as hex strings and parsed to
/// brushes by <see cref="ThemeManager.Apply"/>, which writes them into
/// <see cref="Application.Resources"/> under the <c>Theme.*</c> keys that
/// App.xaml's styles bind to via <c>DynamicResource</c> — so applying a theme
/// re-skins the window live.
/// <para/>
/// All themes are light-surfaced (dark text stays readable): the editor has
/// many plain <c>TextBlock</c> labels using the default near-black foreground,
/// so the themes vary tint + accent, not the light/dark polarity.
/// </summary>
public sealed class ThemeDef : ObservableObject
{
    public string Name { get; init; } = "";

    /// <summary>App background, behind the tab content.</summary>
    public string Window { get; init; } = "#F2F2F2";
    /// <summary>Menu bar + status bar background.</summary>
    public string Chrome { get; init; } = "#E4E4E4";
    /// <summary>Menu bar + status bar text.</summary>
    public string ChromeText { get; init; } = "#1E1E1E";
    /// <summary>GroupBox / panel fill.</summary>
    public string Surface { get; init; } = "#FBFBFB";
    /// <summary>GroupBox border accent.</summary>
    public string Accent { get; init; } = "#B8B8B8";
    /// <summary>GroupBox header text accent.</summary>
    public string AccentText { get; init; } = "#333333";
    /// <summary>Default label / body text foreground.</summary>
    public string Text { get; init; } = "#1E1E1E";
    /// <summary>Input control (TextBox / ComboBox / list / tree) background.</summary>
    public string Control { get; init; } = "#FFFFFF";

    private bool _isCurrent;
    /// <summary>True for the active theme — drives the menu checkmark.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set { _isCurrent = value; OnPropertyChanged(); }
    }
}

/// <summary>
/// Holds the theme catalogue, applies a theme to the live application
/// resources, and persists the choice across launches.
/// </summary>
public static class ThemeManager
{
    public const string KeyWindow = "Theme.Window";
    public const string KeyChrome = "Theme.Chrome";
    public const string KeyChromeText = "Theme.ChromeText";
    public const string KeySurface = "Theme.Surface";
    public const string KeyAccent = "Theme.Accent";
    public const string KeyAccentText = "Theme.AccentText";
    public const string KeyText = "Theme.Text";
    public const string KeyControl = "Theme.Control";

    // Catalogue. Index 0 ("Light") is the neutral default + fallback. Light
    // themes keep dark Text on a near-white Control; the dark themes flip both
    // (light Text on a dark Control) — and every text-bearing surface below is
    // themed so the single light foreground never lands on a light background.
    public static IReadOnlyList<ThemeDef> All { get; } = new[]
    {
        new ThemeDef { Name = "Light",     Window = "#F2F2F2", Chrome = "#E4E4E4", ChromeText = "#1E1E1E", Surface = "#FBFBFB", Accent = "#B8B8B8", AccentText = "#333333", Text = "#1E1E1E", Control = "#FFFFFF" },
        new ThemeDef { Name = "Slate",     Window = "#E8ECF1", Chrome = "#C9D3DF", ChromeText = "#1B2733", Surface = "#F5F8FB", Accent = "#3F6184", AccentText = "#2F4A63", Text = "#1B2733", Control = "#FFFFFF" },
        new ThemeDef { Name = "Forest",    Window = "#E9F0E7", Chrome = "#CBDCC4", ChromeText = "#1E2C1B", Surface = "#F4F8F2", Accent = "#3E7A48", AccentText = "#2E5A34", Text = "#1E2C1B", Control = "#FFFFFF" },
        new ThemeDef { Name = "Plum",      Window = "#EEE8F1", Chrome = "#D8CADE", ChromeText = "#2A1E30", Surface = "#F8F4FA", Accent = "#7A4A8F", AccentText = "#5A356B", Text = "#2A1E30", Control = "#FFFFFF" },
        new ThemeDef { Name = "Ocean",     Window = "#E5EFF1", Chrome = "#C3DBDF", ChromeText = "#16282B", Surface = "#F2F8F9", Accent = "#2A7E88", AccentText = "#1F5E66", Text = "#16282B", Control = "#FFFFFF" },
        new ThemeDef { Name = "Sandstone", Window = "#F2ECE3", Chrome = "#E0D2BE", ChromeText = "#2E2415", Surface = "#FAF6EF", Accent = "#9A7634", AccentText = "#6B532E", Text = "#2E2415", Control = "#FFFFFF" },
        new ThemeDef { Name = "Rose",      Window = "#F4EAEC", Chrome = "#E2C9CF", ChromeText = "#301E22", Surface = "#FBF4F5", Accent = "#A8546A", AccentText = "#7E3A4D", Text = "#301E22", Control = "#FFFFFF" },
        new ThemeDef { Name = "Dark",      Window = "#1E1E1E", Chrome = "#2D2D30", ChromeText = "#E4E4E4", Surface = "#252526", Accent = "#4B4B52", AccentText = "#CFCFCF", Text = "#E4E4E4", Control = "#333338" },
        new ThemeDef { Name = "Midnight",  Window = "#141C28", Chrome = "#1D2A3A", ChromeText = "#DCE6F2", Surface = "#1A2634", Accent = "#3C5A7C", AccentText = "#B7CCE6", Text = "#DCE6F2", Control = "#223247" },
        new ThemeDef { Name = "Carbon",    Window = "#161616", Chrome = "#232323", ChromeText = "#E0E0E0", Surface = "#1C1C1C", Accent = "#5A5A5A", AccentText = "#BFBFBF", Text = "#E6E6E6", Control = "#2A2A2A" },
    };

    public static ThemeDef Current { get; private set; } = All[0];

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SMSModForge", "theme.json");

    /// <summary>Apply <paramref name="theme"/> to the running app + persist it.</summary>
    public static void Apply(ThemeDef theme)
    {
        if (theme == null || Application.Current == null) return;
        var r = Application.Current.Resources;
        r[KeyWindow] = Brush(theme.Window);
        r[KeyChrome] = Brush(theme.Chrome);
        r[KeyChromeText] = Brush(theme.ChromeText);
        r[KeySurface] = Brush(theme.Surface);
        r[KeyAccent] = Brush(theme.Accent);
        r[KeyAccentText] = Brush(theme.AccentText);
        r[KeyText] = Brush(theme.Text);
        r[KeyControl] = Brush(theme.Control);

        foreach (var t in All) t.IsCurrent = ReferenceEquals(t, theme);
        Current = theme;
        SaveName(theme.Name);
    }

    /// <summary>Apply the persisted theme (or the default). Call once at
    /// startup, before the main window is shown.</summary>
    public static void ApplySaved()
    {
        string? name = LoadName();
        var theme = All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?? All[0];
        Apply(theme);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
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
