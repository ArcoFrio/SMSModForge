using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Newtonsoft.Json;
using SMSModForge.ViewModel;
using SMSModForge.Localization;

namespace SMSModForge.Services;

/// <summary>
/// One editor colour theme: a few colours an author of themes chooses, from
/// which every colour the editor draws with is worked out
/// (<see cref="ThemeManager.Palette"/>).
/// <para/>
/// Made the way VS Code's themes are (the author, 2026-10-21: themes that were
/// colour variations rather than themes, buttons the colour of the background,
/// a selected row white on white): a base - the window, the bars, the panels,
/// the boxes you type in, the text - and ONE accent, which marks what is
/// selected, focused or open. Everything else is derived from those, and every
/// text colour is pushed until it reads against what it is drawn on, so a new
/// theme cannot be unreadable by accident. Any derived colour can still be
/// given by hand.
/// </summary>
public sealed class ThemeDef : ObservableObject
{
    /// <summary>What the theme is saved as. Never shown - <see cref="DisplayName"/> is.</summary>
    public string Name { get; init; } = "";

    /// <summary>The key of its name in the English file.</summary>
    public string NameKey => "theme." + Name.ToLowerInvariant();

    /// <summary>One the person made (1.8.0): its name is theirs, shown as
    /// they wrote it, and it is kept in their settings rather than here.</summary>
    public bool IsCustom { get; init; }

    public string DisplayName => IsCustom ? Name : Loc.T(NameKey);

    /// <summary>What choosing it is saved as. A theme of one's own can be
    /// called anything, Light included, so its name is kept apart.</summary>
    public string SavedAs => IsCustom ? ThemeManager.CustomPrefix + Name : Name;

    /// <summary>Light text on dark surfaces. Decides which way derived colours
    /// lean, and the Light / Dark heading it is listed under.</summary>
    public bool IsDark { get; init; }

    /// <summary>
    /// Drawn the EX way (1.8.0): the same window and controls in another
    /// shape - cut corners, glows, lines for boxes, a condensed typeface
    /// (View/Themes/Ex.xaml). Listed under EX rather than Normal.
    /// </summary>
    public bool IsEx { get; init; }

    /// <summary>Behind everything: the tab pages.</summary>
    public string Window { get; init; } = "#F3F3F3";
    /// <summary>The menu bar, the tab strip, the status bar.</summary>
    public string Chrome { get; init; } = "#E8E8E8";
    /// <summary>Panels: a record's groups, the lists' frames.</summary>
    public string Surface { get; init; } = "#FFFFFF";
    /// <summary>Boxes to type in, lists, trees.</summary>
    public string Control { get; init; } = "#FFFFFF";
    /// <summary>Words.</summary>
    public string Text { get; init; } = "#1F1F1F";
    /// <summary>What is selected, focused, open; the main button.</summary>
    public string Accent { get; init; } = "#005FB8";

    // Worked out when not given.
    public string? ChromeText { get; init; }
    public string? Heading { get; init; }
    public string? Border { get; init; }
    public string? Button { get; init; }
    public string? Selection { get; init; }

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
    // ── The colours a theme provides, as resource keys ─────────────────
    // App.xaml's styles and the window bind these with DynamicResource, so a
    // theme switch re-skins everything live.

    public const string KeyWindow = "Theme.Window";
    public const string KeyChrome = "Theme.Chrome";
    public const string KeyChromeText = "Theme.ChromeText";
    public const string KeySurface = "Theme.Surface";
    public const string KeySurfaceAlt = "Theme.SurfaceAlt";
    public const string KeyControl = "Theme.Control";
    public const string KeyControlBorder = "Theme.ControlBorder";
    public const string KeyBorder = "Theme.Border";
    public const string KeyHover = "Theme.Hover";
    public const string KeyText = "Theme.Text";
    public const string KeyTextMuted = "Theme.TextMuted";
    public const string KeyTextDisabled = "Theme.TextDisabled";
    /// <summary>A group's title. Kept under its old key, which still reads it.</summary>
    public const string KeyAccentText = "Theme.AccentText";
    public const string KeyHeading = "Theme.Heading";
    public const string KeyAccent = "Theme.Accent";
    public const string KeyAccentHover = "Theme.AccentHover";
    public const string KeyAccentSoft = "Theme.AccentSoft";
    public const string KeyOnAccent = "Theme.OnAccent";
    /// <summary>The accent as words - a link, a count - darkened or lightened
    /// until it reads on a panel.</summary>
    public const string KeyLink = "Theme.Link";
    public const string KeyButton = "Theme.Button";
    public const string KeyButtonHover = "Theme.ButtonHover";
    public const string KeyButtonPressed = "Theme.ButtonPressed";
    public const string KeyButtonBorder = "Theme.ButtonBorder";
    public const string KeyButtonText = "Theme.ButtonText";
    public const string KeySelection = "Theme.Selection";
    public const string KeySelectionText = "Theme.SelectionText";
    public const string KeySelectionInactive = "Theme.SelectionInactive";
    public const string KeySelectionInactiveText = "Theme.SelectionInactiveText";
    public const string KeyTabActive = "Theme.TabActive";
    public const string KeyTabActiveText = "Theme.TabActiveText";
    public const string KeyTabInactive = "Theme.TabInactive";
    public const string KeyTabInactiveText = "Theme.TabInactiveText";
    public const string KeyTabHover = "Theme.TabHover";
    public const string KeyDanger = "Theme.Danger";
    public const string KeyWarning = "Theme.Warning";
    public const string KeySuccess = "Theme.Success";
    /// <summary>A note's box: the warning's or the danger's colour, faint.</summary>
    public const string KeyWarningSoft = "Theme.WarningSoft";
    public const string KeyDangerSoft = "Theme.DangerSoft";
    public const string KeySplitter = "Theme.Splitter";
    public const string KeyScrollTrack = "Theme.ScrollTrack";
    public const string KeyScrollThumb = "Theme.ScrollThumb";
    public const string KeyScrollThumbHover = "Theme.ScrollThumbHover";
    public const string KeyToolTip = "Theme.ToolTip";
    public const string KeyToolTipText = "Theme.ToolTipText";
    /// <summary>The status bar along the bottom: the bars' colour, or the
    /// accent for an EX theme, and its words.</summary>
    public const string KeyStatusBar = "Theme.StatusBar";
    public const string KeyStatusBarText = "Theme.StatusBarText";

    /// <summary>Background for a GameObject row the vanilla level owns, and for
    /// one the pack has actually altered. Derived from the theme's own Surface
    /// rather than authored per theme, and OPAQUE on purpose: these rows nest,
    /// and a translucent tint let a parent's colour show through every child
    /// that sat inside it — which is what made a fresh child look like an
    /// untouched vanilla object, and an NPC look like a changed one.</summary>
    public const string KeyVanillaSurface = "Theme.VanillaSurface";
    public const string KeyChangedSurface = "Theme.ChangedSurface";
    public const string KeyWashOpacity = "Theme.WashOpacity";

    /// <summary>
    /// The ink a formatting tag is shown in inside a dialogue line, and the
    /// faint chip it sits on.
    /// <para/>
    /// A tag is an instruction rather than words, so it is drawn differently
    /// from the sentence around it — but it was drawn as flat grey, which on a
    /// white box is washed out and on a dark one is barely there. A HUE tells it
    /// apart from the words without having to be dimmer than them, and the chip
    /// says where the tag starts and stops.
    /// </summary>
    public const string KeyMarkup = "Theme.Markup";
    public const string KeyMarkupBack = "Theme.MarkupBack";

    /// <summary>
    /// The hue markup is pulled toward. Fixed rather than taken from the theme's
    /// own accent, so markup means the same on every theme.
    /// <para/>
    /// It is mixed into the theme's TEXT colour, which is the one thing
    /// guaranteed to contrast with the box the tag is written in — so the same
    /// rule gives a deep blue on the light themes and a pale one on the dark,
    /// rather than a single colour that only works on half of them.
    /// </summary>
    private static readonly Color MarkupHue = Color.FromRgb(0x3B, 0x82, 0xC4);

    /// <summary>WCAG's ratio for words; every text colour is held to it.</summary>
    public const double ReadableContrast = 4.5;

    /// <summary>
    /// Every text colour with what the editor draws it on. The theme tests
    /// hold every built-in theme to these; Your themes says when one of the
    /// person's own falls short - which their colours can make unavoidable:
    /// no one text colour reads on both a mid-grey panel and a near-black box.
    /// </summary>
    public static readonly (string Text, string[] On)[] TextPairs =
    {
        (KeyText, new[] { KeySurface, KeyControl, KeySelection, KeySelectionInactive, KeySurfaceAlt }),
        (KeyChromeText, new[] { KeyChrome }),
        (KeyTextMuted, new[] { KeySurface, KeyControl, KeyWindow, KeySurfaceAlt, KeyChrome }),
        (KeyHeading, new[] { KeySurface, KeyAccentSoft }),
        (KeyButtonText, new[] { KeyButton, KeyButtonHover, KeyButtonPressed }),
        (KeySelectionText, new[] { KeySelection }),
        (KeySelectionInactiveText, new[] { KeySelectionInactive }),
        (KeyTabActiveText, new[] { KeyTabActive }),
        (KeyTabInactiveText, new[] { KeyTabInactive }),
        (KeyOnAccent, new[] { KeyAccent }),
        (KeyLink, new[] { KeySurface, KeyControl }),
        (KeyDanger, new[] { KeySurface, KeyControl }),
        (KeyWarning, new[] { KeySurface, KeyControl }),
        (KeySuccess, new[] { KeySurface, KeyControl }),
        (KeyToolTipText, new[] { KeyToolTip }),
    };

    /// <summary>What a text colour must reach. OnAccent is black or white,
    /// whichever reads better, and a few mid-tone accents cannot give 4.5
    /// either way; the only words on them are short labels, held at 3:1.</summary>
    public static double NeededFor(string textKey) => textKey == KeyOnAccent ? 3.0 : ReadableContrast;

    /// <summary>The pairs of a theme whose words fall short, as "text on
    /// what = ratio"; empty for a theme that reads everywhere.</summary>
    public static List<string> Unreadable(ThemeDef theme)
    {
        var p = Palette(theme);
        var shortfalls = new List<string>();
        foreach (var (text, on) in TextPairs)
            foreach (var bg in on)
            {
                double ratio = Contrast(p[text], p[bg]);
                if (ratio < NeededFor(text)) shortfalls.Add($"{text} on {bg} = {ratio:0.00}");
            }
        return shortfalls;
    }

    // ── The catalogue ───────────────────────────────────────────────────
    // Light first, then dark; "Light" is the default and the fallback. The
    // first ten names are the themes ModForge has had since 1.1, kept so a
    // saved choice still finds its theme - reworked, not replaced.
    public static IReadOnlyList<ThemeDef> All { get; } = new[]
    {
        // Light
        new ThemeDef { Name = "Light",     Window = "#F3F3F3", Chrome = "#E8E8E8", Surface = "#FFFFFF", Control = "#FFFFFF", Text = "#1F1F1F", Accent = "#005FB8" },
        new ThemeDef { Name = "Slate",     Window = "#E9EEF3", Chrome = "#D3DCE6", Surface = "#F7F9FB", Control = "#FFFFFF", Text = "#1B2733", Accent = "#3B6EA8" },
        new ThemeDef { Name = "Forest",    Window = "#E8EFE5", Chrome = "#D0E0C8", Surface = "#F6FAF4", Control = "#FFFFFF", Text = "#1E2C1B", Accent = "#2E7D32" },
        new ThemeDef { Name = "Plum",      Window = "#EFE9F3", Chrome = "#DDCDE6", Surface = "#FAF6FC", Control = "#FFFFFF", Text = "#2A1E30", Accent = "#7B3FA0" },
        new ThemeDef { Name = "Ocean",     Window = "#E4EFF1", Chrome = "#C8DFE3", Surface = "#F4FAFB", Control = "#FFFFFF", Text = "#13292D", Accent = "#00838F" },
        new ThemeDef { Name = "Sandstone", Window = "#F3ECD9", Chrome = "#E5D8B9", Surface = "#FDF6E3", Control = "#FFFBEF", Text = "#3B3021", Accent = "#B5651D" },
        new ThemeDef { Name = "Rose",      Window = "#F6EAED", Chrome = "#EACCD4", Surface = "#FDF5F7", Control = "#FFFFFF", Text = "#31191F", Accent = "#B4325A" },
        new ThemeDef { Name = "Paper",     Window = "#F2EFE8", Chrome = "#E3DED2", Surface = "#FBF9F4", Control = "#FFFFFF", Text = "#2B2622", Accent = "#C0392B" },
        new ThemeDef { Name = "Daylight",  Window = "#EDF1F6", Chrome = "#D9E2EC", Surface = "#FFFFFF", Control = "#FFFFFF", Text = "#1C2430", Accent = "#D46A00" },
        // Dark
        new ThemeDef { Name = "Dark",       IsDark = true, Window = "#1E1E1E", Chrome = "#181818", Surface = "#252526", Control = "#313131", Text = "#D4D4D4", Accent = "#0078D4" },
        new ThemeDef { Name = "Midnight",   IsDark = true, Window = "#0F1724", Chrome = "#0A101A", Surface = "#152033", Control = "#1C2A40", Text = "#DCE6F2", Accent = "#4FC3F7" },
        new ThemeDef { Name = "Carbon",     IsDark = true, Window = "#121212", Chrome = "#0A0A0A", Surface = "#1B1B1B", Control = "#252525", Text = "#E6E6E6", Accent = "#D4A72C" },
        new ThemeDef { Name = "Graphite",   IsDark = true, Window = "#21252B", Chrome = "#1B1E23", Surface = "#282C34", Control = "#2F343D", Text = "#C9CFD9", Accent = "#61AFEF" },
        new ThemeDef { Name = "Nightshade", IsDark = true, Window = "#21222C", Chrome = "#191A21", Surface = "#282A36", Control = "#343746", Text = "#F8F8F2", Accent = "#BD93F9" },
        new ThemeDef { Name = "Fjord",      IsDark = true, Window = "#2B303B", Chrome = "#242933", Surface = "#323845", Control = "#3B4252", Text = "#ECEFF4", Accent = "#88C0D0" },
        new ThemeDef { Name = "Ember",      IsDark = true, Window = "#1E1F1C", Chrome = "#171814", Surface = "#272822", Control = "#33342D", Text = "#F8F8F2", Accent = "#FD971F" },
        new ThemeDef { Name = "DeepSea",    IsDark = true, Window = "#00212B", Chrome = "#001A22", Surface = "#002B36", Control = "#073642", Text = "#E3E9E6", Accent = "#2AA198" },
        new ThemeDef { Name = "Moss",       IsDark = true, Window = "#161D17", Chrome = "#111712", Surface = "#1C251D", Control = "#243026", Text = "#DCE8D9", Accent = "#8BC34A" },
        new ThemeDef { Name = "Wine",       IsDark = true, Window = "#1E1418", Chrome = "#160E11", Surface = "#26191F", Control = "#321F28", Text = "#F0E1E6", Accent = "#E0607E" },
        // EX (1.8.0): near-black bases with one loud accent, and two light
        // ones with the same loudness on a cool and a warm paper.
        new ThemeDef { Name = "Frost", IsEx = true, Window = "#EBEEF5", Chrome = "#DDE2EC", Surface = "#F8F9FC", Control = "#FFFFFF", Text = "#141826", Accent = "#5B2EFF" },
        new ThemeDef { Name = "Volt",  IsEx = true, Window = "#F1F1EA", Chrome = "#E3E4D8", Surface = "#FBFBF5", Control = "#FFFFFF", Text = "#191B12", Accent = "#D4006E" },
        // More of both (the author, 1.8.0: "a lot less themes than the normal
        // variant"), placed round the colour wheel where EX had none, so each
        // Classic theme has an EX one near it to be moved to (ExEquivalent).
        new ThemeDef { Name = "Cobalt", IsEx = true, Window = "#EAF0F7", Chrome = "#DCE5F0", Surface = "#F7FAFD", Control = "#FFFFFF", Text = "#0F1A2A", Accent = "#0062FF" },
        new ThemeDef { Name = "Mint",   IsEx = true, Window = "#E9F2EE", Chrome = "#D8E8E0", Surface = "#F6FBF8", Control = "#FFFFFF", Text = "#10211A", Accent = "#00A86B" },
        new ThemeDef { Name = "Solar",  IsEx = true, Window = "#F5F0E6", Chrome = "#EAE1D0", Surface = "#FCF9F2", Control = "#FFFFFF", Text = "#22190E", Accent = "#F26A00" },
        new ThemeDef { Name = "Neon",  IsEx = true, IsDark = true, Window = "#0E0B13", Chrome = "#08060C", Surface = "#16121D", Control = "#1E1927", Text = "#ECE8F4", Accent = "#FF2E63" },
        new ThemeDef { Name = "Pulse", IsEx = true, IsDark = true, Window = "#081016", Chrome = "#04090D", Surface = "#0E1820", Control = "#14222C", Text = "#E2F1F7", Accent = "#00E0FF" },
        new ThemeDef { Name = "Venom", IsEx = true, IsDark = true, Window = "#0B0F0B", Chrome = "#060906", Surface = "#121812", Control = "#19221A", Text = "#E5F1E1", Accent = "#9BF22E" },
        new ThemeDef { Name = "Blaze", IsEx = true, IsDark = true, Window = "#110C08", Chrome = "#0B0805", Surface = "#1A130D", Control = "#241A12", Text = "#F4EADD", Accent = "#FF8A1F" },
        new ThemeDef { Name = "Phantom", IsEx = true, IsDark = true, Window = "#0D0B14", Chrome = "#08070D", Surface = "#15121F", Control = "#1D1929", Text = "#ECE7F7", Accent = "#A855FF" },
        new ThemeDef { Name = "Abyss",   IsEx = true, IsDark = true, Window = "#070B14", Chrome = "#04070D", Surface = "#0C1220", Control = "#121A2C", Text = "#E1E8F7", Accent = "#2F7BFF" },
        new ThemeDef { Name = "Aurum",   IsEx = true, IsDark = true, Window = "#0E0D09", Chrome = "#090805", Surface = "#17150E", Control = "#201D13", Text = "#F2ECD9", Accent = "#FFC233" },
        new ThemeDef { Name = "Jade",    IsEx = true, IsDark = true, Window = "#08100E", Chrome = "#050A09", Surface = "#0E1916", Control = "#14221E", Text = "#DFF2EC", Accent = "#1DE9B6" },
    };

    public static ThemeDef Current { get; private set; } = All[0];

    /// <summary>After a theme is applied: for controls that draw their own
    /// pixels rather than bind the theme's brushes, which then draw again.</summary>
    public static event Action? Changed;

    /// <summary>The Themes menu's groups (the author, 2026-10-21: light and
    /// dark apart in the list; 1.8.0: Normal and EX, each light and dark).</summary>
    public static IReadOnlyList<ThemeDef> LightThemes { get; } = All.Where(t => !t.IsDark && !t.IsEx).ToArray();
    public static IReadOnlyList<ThemeDef> DarkThemes { get; } = All.Where(t => t.IsDark && !t.IsEx).ToArray();
    public static IReadOnlyList<ThemeDef> ExLightThemes { get; } = All.Where(t => !t.IsDark && t.IsEx).ToArray();
    public static IReadOnlyList<ThemeDef> ExDarkThemes { get; } = All.Where(t => t.IsDark && t.IsEx).ToArray();

    private static string FilePath => AppDataFolder.PathOf("theme.json");

    // ── Themes of one's own (1.8.0) ─────────────────────────────────────
    // The author: "How about giving users the ability to create custom
    // themes?" Made of exactly what a built-in theme is made of - a look (EX
    // or Classic) and the six colours - so the palette works them out the
    // same way, words pushed until they read whatever is picked.

    /// <summary>What a saved choice of one's own theme starts with.</summary>
    public const string CustomPrefix = "custom:";

    /// <summary>The person's own themes, in the order they made them; the
    /// Themes menu lists them under Yours.</summary>
    public static System.Collections.ObjectModel.ObservableCollection<ThemeDef> Custom { get; } = new();

    private static string CustomPath => AppDataFolder.PathOf("custom-themes.json");

    /// <summary>The theme a saved choice names, built-in or one's own; null
    /// for one that no longer exists.</summary>
    public static ThemeDef? Find(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved)) return null;
        if (saved.StartsWith(CustomPrefix, StringComparison.Ordinal))
        {
            string name = saved.Substring(CustomPrefix.Length);
            return Custom.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        return All.FirstOrDefault(t => string.Equals(t.Name, saved, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A theme of one's own from its look and colours. Light or dark is not
    /// asked: it is whichever the background is, which is what decides how
    /// everything else is worked out.
    /// </summary>
    public static ThemeDef MakeCustom(string name, bool ex, string window, string chrome, string surface,
                                      string control, string text, string accent)
    {
        var back = Hex(window);
        return new ThemeDef
        {
            Name = name, IsCustom = true, IsEx = ex,
            IsDark = Contrast(Colors.White, back) > Contrast(Colors.Black, back),
            Window = window, Chrome = chrome, Surface = surface, Control = control, Text = text, Accent = accent,
        };
    }

    /// <summary>The same theme with other colours, look or name - a theme is
    /// never changed in place, so whatever holds the old one keeps a whole one.</summary>
    public static ThemeDef With(ThemeDef t, string? name = null, bool? ex = null, string? window = null, string? chrome = null,
                                string? surface = null, string? control = null, string? text = null, string? accent = null)
        => MakeCustom(name ?? t.Name, ex ?? t.IsEx, window ?? t.Window, chrome ?? t.Chrome, surface ?? t.Surface,
                      control ?? t.Control, text ?? t.Text, accent ?? t.Accent);

    /// <summary>A name no theme of one's own has yet: the name asked for, or
    /// it with a number after.</summary>
    public static string UniqueCustomName(string wanted, ThemeDef? except = null)
    {
        wanted = string.IsNullOrWhiteSpace(wanted) ? Loc.T("theme.custom.newName") : wanted.Trim();
        bool Taken(string n) => Custom.Any(t => !ReferenceEquals(t, except) && string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase));
        if (!Taken(wanted)) return wanted;
        for (int i = 2; ; i++)
            if (!Taken(wanted + " " + i)) return wanted + " " + i;
    }

    /// <summary>Put <paramref name="now"/> where <paramref name="was"/> is (or
    /// at the end), keep the file in step, and - if it was the one in use -
    /// show it at once.</summary>
    public static void PutCustom(ThemeDef? was, ThemeDef now)
    {
        int at = was == null ? -1 : Custom.IndexOf(was);
        if (at >= 0) Custom[at] = now; else Custom.Add(now);
        SaveCustom();
        if (was != null && ReferenceEquals(Current, was)) Apply(now);
    }

    /// <summary>Take a theme of one's own away; if it was in use, the
    /// default takes its place.</summary>
    public static void RemoveCustom(ThemeDef theme)
    {
        if (!Custom.Remove(theme)) return;
        SaveCustom();
        if (ReferenceEquals(Current, theme)) Apply(Default);
    }

    private sealed class CustomFile
    {
        public string Name { get; set; } = "";
        public bool Ex { get; set; }
        public string Window { get; set; } = "";
        public string Chrome { get; set; } = "";
        public string Surface { get; set; } = "";
        public string Control { get; set; } = "";
        public string Text { get; set; } = "";
        public string Accent { get; set; } = "";
    }

    /// <summary>Read the person's own themes. A colour that does not read
    /// leaves that theme out rather than the editor unable to start.</summary>
    internal static void LoadCustom()
    {
        Custom.Clear();
        try
        {
            if (!File.Exists(CustomPath)) return;
            var saved = JsonConvert.DeserializeObject<List<CustomFile>>(File.ReadAllText(CustomPath)) ?? new();
            foreach (var c in saved)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(c.Name) || Custom.Any(t => string.Equals(t.Name, c.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    var theme = MakeCustom(c.Name.Trim(), c.Ex, c.Window, c.Chrome, c.Surface, c.Control, c.Text, c.Accent);
                    _ = Palette(theme);   // every colour reads, or this one is skipped
                    Custom.Add(theme);
                }
                catch (Exception) { /* a theme that cannot be read is left out */ }
            }
        }
        catch (Exception) { /* an unreadable file: no themes of one's own, nothing lost on disk */ }
    }

    private static void SaveCustom()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CustomPath)!);
            File.WriteAllText(CustomPath, JsonConvert.SerializeObject(Custom.Select(t => new CustomFile
            {
                Name = t.Name, Ex = t.IsEx, Window = t.Window, Chrome = t.Chrome, Surface = t.Surface,
                Control = t.Control, Text = t.Text, Accent = t.Accent,
            }).ToList(), Formatting.Indented));
        }
        catch (Exception) { /* best-effort, like the theme choice */ }
    }

    // ── Working out the colours ─────────────────────────────────────────

    /// <summary>
    /// Every colour of a theme by its resource key: what it gives, and what is
    /// worked out from that. Public so a test can hold every pair the editor
    /// draws text with to <see cref="ReadableContrast"/> without a window.
    /// </summary>
    public static IReadOnlyDictionary<string, Color> Palette(ThemeDef t)
    {
        bool dark = t.IsDark;
        Color window = Hex(t.Window), chrome = Hex(t.Chrome), surface = Hex(t.Surface),
              control = Hex(t.Control), text = Hex(t.Text), accent = Hex(t.Accent);
        var p = new Dictionary<string, Color>();

        // Words sit on the accent - a Save button, a toggle that is in - in
        // white or near-black, whichever reads better; and the accent is moved
        // in lightness only as far as they need. Sandstone's #B5651D sat in the
        // middle, where both read at 4.3 (found when the contrast checks
        // reached the mask editor's tool buttons, after 1.8.0).
        var onAccent = Contrast(Colors.White, accent) >= Contrast(Color.FromRgb(0x11, 0x11, 0x11), accent)
            ? Colors.White : Color.FromRgb(0x11, 0x11, 0x11);
        accent = Readable(accent, onAccent);

        p[KeyWindow] = window;
        p[KeyChrome] = chrome;
        p[KeySurface] = surface;
        p[KeyControl] = control;
        p[KeyAccent] = accent;
        p[KeyText] = Readable(text, surface, control);
        p[KeyChromeText] = t.ChromeText is { } ct ? Hex(ct) : Readable(text, chrome);
        p[KeySurfaceAlt] = Blend(surface, text, dark ? 0.045 : 0.035);
        p[KeyBorder] = t.Border is { } b ? Hex(b) : Blend(surface, text, dark ? 0.20 : 0.17);
        p[KeyControlBorder] = Blend(control, text, dark ? 0.30 : 0.32);
        p[KeyHover] = Under(surface, text, dark ? 0.09 : 0.06, text);
        p[KeyTextMuted] = Readable(Blend(text, surface, 0.36), surface, control, p[KeySurfaceAlt], window, chrome);
        p[KeyTextDisabled] = Blend(text, surface, 0.55);

        p[KeyAccentHover] = dark ? Blend(accent, Colors.White, 0.15) : Blend(accent, Colors.Black, 0.12);
        p[KeyAccentSoft] = Blend(control, accent, dark ? 0.28 : 0.16);

        // A group's title - and the title of a box drawn on the soft accent,
        // like a dialogue's pinned "Required" level row.
        var heading = t.Heading is { } h ? Hex(h) : Blend(text, accent, dark ? 0.45 : 0.55);
        p[KeyHeading] = p[KeyAccentText] = Readable(heading, surface, p[KeyAccentSoft]);
        p[KeyOnAccent] = onAccent;
        // On the bars too: the EX rail draws the open tab's icon with it.
        p[KeyLink] = Readable(accent, surface, control, chrome);

        // Buttons stand off the panel they are on - they used to be its colour.
        var button = t.Button is { } bt ? Hex(bt) : Blend(surface, text, dark ? 0.13 : 0.08);
        p[KeyButton] = button;
        p[KeyButtonBorder] = Blend(surface, text, dark ? 0.30 : 0.28);
        p[KeyButtonHover] = Under(button, accent, dark ? 0.32 : 0.20, text);
        p[KeyButtonPressed] = Under(button, accent, dark ? 0.50 : 0.34, text);
        p[KeyButtonText] = Readable(text, button, p[KeyButtonHover], p[KeyButtonPressed]);

        // What is selected: the accent while the list has the keyboard, a
        // neutral tint when it has not - never the system's light grey.
        // The words in a selected row are the theme's own (a row's text is
        // not told it is selected), so the selection gives way to them.
        var selection = t.Selection is { } s ? Hex(s) : Under(control, accent, dark ? 0.40 : 0.26, text);
        p[KeySelection] = selection;
        p[KeySelectionText] = Readable(text, selection);
        p[KeySelectionInactive] = Under(control, text, dark ? 0.17 : 0.11, text);
        p[KeySelectionInactiveText] = Readable(text, p[KeySelectionInactive]);

        // Tabs: the open one is the panel's colour under an accent bar; the
        // rest sit back in the strip.
        p[KeyTabActive] = surface;
        p[KeyTabActiveText] = Readable(text, surface);
        p[KeyTabInactive] = chrome;
        p[KeyTabInactiveText] = Readable(Blend(p[KeyChromeText], chrome, 0.25), chrome);
        p[KeyTabHover] = Blend(chrome, text, dark ? 0.10 : 0.07);

        p[KeyDanger] = Readable(dark ? Color.FromRgb(0xF4, 0x87, 0x71) : Color.FromRgb(0xC4, 0x2B, 0x1C), surface, control);
        p[KeyWarning] = Readable(dark ? Color.FromRgb(0xE9, 0xC4, 0x6A) : Color.FromRgb(0x9A, 0x67, 0x00), surface, control);
        p[KeySuccess] = Readable(dark ? Color.FromRgb(0x89, 0xD1, 0x85) : Color.FromRgb(0x2E, 0x7D, 0x32), surface, control);

        p[KeyWarningSoft] = Blend(surface, p[KeyWarning], dark ? 0.16 : 0.12);
        p[KeyDangerSoft] = Blend(surface, p[KeyDanger], dark ? 0.16 : 0.10);

        p[KeySplitter] = p[KeyBorder];
        p[KeyScrollTrack] = Blend(window, text, dark ? 0.03 : 0.025);
        p[KeyScrollThumb] = Blend(surface, text, dark ? 0.28 : 0.25);
        p[KeyScrollThumbHover] = Blend(surface, text, dark ? 0.42 : 0.40);
        p[KeyToolTip] = dark ? Blend(chrome, text, 0.10) : Blend(control, chrome, 0.35);
        p[KeyToolTipText] = Readable(text, p[KeyToolTip]);
        p[KeyStatusBar] = t.IsEx ? accent : chrome;
        p[KeyStatusBarText] = t.IsEx ? p[KeyOnAccent] : p[KeyChromeText];

        // Blended toward a hue rather than alpha-composited, so the result is
        // opaque and still tracks whether the theme is light or dark.
        p[KeyVanillaSurface] = Blend(surface, Color.FromRgb(0x5B, 0x8F, 0xCB), 0.16);
        p[KeyChangedSurface] = Blend(surface, accent, dark ? 0.24 : 0.18);

        // Six parts of the way from the theme's own text colour to the markup
        // hue: far enough to read as a different KIND of thing, near enough to
        // keep the text colour's contrast with the box behind it.
        var ink = Blend(p[KeyText], MarkupHue, 0.6);
        // Faint: a chip that says where the tag starts, not a highlighter -
        // and the ink pushed until it reads on the chip as well as the box.
        var chip = Blend(control, ink, 0.12);
        p[KeyMarkup] = Readable(ink, control, chip);
        p[KeyMarkupBack] = chip;
        return p;
    }

    /// <summary>Apply <paramref name="theme"/> to the running app + persist it.</summary>
    public static void Apply(ThemeDef theme)
    {
        if (theme == null || Application.Current == null) return;
        FollowWindows();
        var r = Application.Current.Resources;
        var p = Palette(theme);
        foreach (var (key, colour) in p) r[key] = Frozen(colour);
        // A glow on a dark theme only. On a light one the same halo reads as
        // a blur rather than as light, and the words inside it lose their
        // sharp edges (the author, 1.8.0: "look more like blurs than actual
        // glow"); the accent border and underline say the same thing there.
        r[KeyGlow] = theme.IsDark ? Glow(p[KeyAccent], 9, 0.7) : null;
        r[KeyGlowSoft] = theme.IsDark ? Glow(p[KeyAccent], 6, 0.45) : null;
        r[KeyFontFamily] = theme.IsEx ? ExFont : NormalFont;
        ApplyStructure(theme.IsEx);
        // A speaker's colour washing a node row (a pastel, usually) lifts a
        // dark row far more than a light one: half as strong on a dark theme.
        r[KeyWashOpacity] = theme.IsDark ? 0.5 : 1.0;

        // WPF's own templates - anything not re-templated in App.xaml, and the
        // parts of those that are - draw selection, disabled text, tooltips and
        // 3-D edges in the system's colours: light ones, under a dark theme's
        // light words. The white-on-white selected dialogue was exactly that.
        // The system keys are answered with the theme's.
        void Sys(ResourceKey brush, ResourceKey? colour, Color c)
        {
            r[brush] = Frozen(c);
            if (colour != null) r[colour] = c;
        }
        Sys(SystemColors.HighlightBrushKey, SystemColors.HighlightColorKey, p[KeySelection]);
        Sys(SystemColors.HighlightTextBrushKey, SystemColors.HighlightTextColorKey, p[KeySelectionText]);
        Sys(SystemColors.InactiveSelectionHighlightBrushKey, null, p[KeySelectionInactive]);
        Sys(SystemColors.InactiveSelectionHighlightTextBrushKey, null, p[KeySelectionInactiveText]);
        Sys(SystemColors.ControlBrushKey, SystemColors.ControlColorKey, p[KeySurface]);
        Sys(SystemColors.ControlTextBrushKey, SystemColors.ControlTextColorKey, p[KeyText]);
        Sys(SystemColors.GrayTextBrushKey, SystemColors.GrayTextColorKey, p[KeyTextDisabled]);
        Sys(SystemColors.WindowBrushKey, SystemColors.WindowColorKey, p[KeyControl]);
        Sys(SystemColors.WindowTextBrushKey, SystemColors.WindowTextColorKey, p[KeyText]);
        Sys(SystemColors.MenuBrushKey, SystemColors.MenuColorKey, p[KeyChrome]);
        Sys(SystemColors.MenuBarBrushKey, SystemColors.MenuBarColorKey, p[KeyChrome]);
        Sys(SystemColors.MenuTextBrushKey, SystemColors.MenuTextColorKey, p[KeyChromeText]);
        Sys(SystemColors.MenuHighlightBrushKey, SystemColors.MenuHighlightColorKey, p[KeyHover]);
        Sys(SystemColors.InfoBrushKey, SystemColors.InfoColorKey, p[KeyToolTip]);
        Sys(SystemColors.InfoTextBrushKey, SystemColors.InfoTextColorKey, p[KeyToolTipText]);
        Sys(SystemColors.ControlLightBrushKey, SystemColors.ControlLightColorKey, p[KeyBorder]);
        Sys(SystemColors.ControlLightLightBrushKey, SystemColors.ControlLightLightColorKey, p[KeySurface]);
        Sys(SystemColors.ControlDarkBrushKey, SystemColors.ControlDarkColorKey, p[KeyButtonBorder]);
        Sys(SystemColors.ControlDarkDarkBrushKey, SystemColors.ControlDarkDarkColorKey, p[KeyText]);
        Sys(SystemColors.ActiveBorderBrushKey, SystemColors.ActiveBorderColorKey, p[KeyBorder]);
        Sys(SystemColors.InactiveBorderBrushKey, SystemColors.InactiveBorderColorKey, p[KeyBorder]);
        Sys(SystemColors.HotTrackBrushKey, SystemColors.HotTrackColorKey, p[KeyLink]);

        foreach (var t in All.Concat(Custom)) t.IsCurrent = ReferenceEquals(t, theme);
        Current = theme;
        foreach (Window window in Application.Current.Windows) NativeWindowLook.Apply(window, theme, p);
        SaveName(theme.SavedAs);
        Changed?.Invoke();
    }

    // ── The EX structure ─────────────────────────────────────────────────

    public const string KeyFontFamily = "Theme.FontFamily";
    public const string KeyGlow = "Ex.Glow";
    public const string KeyGlowSoft = "Ex.GlowSoft";

    /// <summary>The typeface the editor's words are in: the system's own for
    /// a Normal theme - Yu Gothic UI on a Japanese Windows, not Segoe -
    /// and Bahnschrift's condensed lines for an EX one, falling back to the
    /// system's for letters it does not have.</summary>
    private static FontFamily NormalFont => SystemFonts.MessageFontFamily;
    private static FontFamily ExFont => new("Bahnschrift, " + SystemFonts.MessageFontFamily.Source);

    private static ResourceDictionary? _ex;
    private static readonly Dictionary<object, object?> _normal = new();
    private static bool _structureIsEx;

    /// <summary>Whether the EX templates are the ones in use. For tests.</summary>
    internal static bool StructureIsEx => _structureIsEx;

    /// <summary>
    /// Every resource View/Themes/Ex.xaml gives - templates for buttons,
    /// boxes, lists, tabs, groups... - put over App.xaml's of the same key
    /// while an EX theme is on, and App.xaml's own put back for a Normal one.
    /// The styles reach their templates through DynamicResource, so every
    /// control in every open window takes the other template where it stands.
    /// </summary>
    private static void ApplyStructure(bool ex)
    {
        if (ex == _structureIsEx) return;
        var r = Application.Current.Resources;
        _ex ??= new ResourceDictionary { Source = new Uri("pack://application:,,,/SMSModForge;component/View/Themes/Ex.xaml", UriKind.Absolute) };
        foreach (var key in _ex.Keys)
        {
            if (ex)
            {
                if (!_normal.ContainsKey(key)) _normal[key] = r.Contains(key) ? r[key] : null;
                r[key] = _ex[key];
            }
            else if (_normal.TryGetValue(key, out var normal))
            {
                if (normal != null) r[key] = normal;
                else r.Remove(key);
            }
        }
        _structureIsEx = ex;
    }

    private static DropShadowEffect Glow(Color accent, double blur, double opacity)
    {
        var glow = new DropShadowEffect { Color = accent, ShadowDepth = 0, BlurRadius = blur, Opacity = opacity };
        glow.Freeze();
        return glow;
    }

    private static bool _following;

    /// <summary>
    /// Every window, as it opens, takes the theme's typeface and has its title
    /// bar drawn to match (<see cref="NativeWindowLook"/>) - the ones open when
    /// the theme changes are done in <see cref="Apply"/>.
    /// </summary>
    private static void FollowWindows()
    {
        if (_following) return;
        _following = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is not Window window) return;
            window.SetResourceReference(Control.FontFamilyProperty, KeyFontFamily);
            NativeWindowLook.Apply(window, Current, Palette(Current));
        }));
        foreach (Window window in Application.Current.Windows)
            window.SetResourceReference(Control.FontFamilyProperty, KeyFontFamily);
    }

    /// <summary>The theme a first start opens in, and the one an unknown saved
    /// name falls back to: Frost, an EX theme (the author, 1.8.0).</summary>
    public static ThemeDef Default => All.First(t => t.Name == "Frost");

    /// <summary>Apply the persisted theme (or the default). Call once at
    /// startup, before the main window is shown.</summary>
    public static void ApplySaved()
    {
        LoadCustom();
        string? name = LoadName();
        var (theme, moved) = ThemeAtStart(name, File.Exists(MovedToExPath));
        if (moved)
        {
            MovedAtStart = (Find(name)!, theme);
            SaveName(theme.Name);
        }
        MarkMovedToEx();
        Apply(theme);
    }

    /// <summary>
    /// The theme this start moved the person from, and the EX one it moved
    /// them to - once, at the first start of 1.8.0 - so the window can say so
    /// and say how to go back (the author, 1.8.0). Null at every other start.
    /// </summary>
    public static (ThemeDef From, ThemeDef To)? MovedAtStart { get; private set; }

    /// <summary>
    /// The theme a start opens in, from the saved name and whether this
    /// person has been moved to EX before. Once - at the first start of
    /// 1.8.0 - a Classic choice becomes the EX theme most like it (the author:
    /// "so they also get to experiment the UI switch firsthand"); after that
    /// a Classic theme picked again stays picked.
    /// </summary>
    internal static (ThemeDef Theme, bool Moved) ThemeAtStart(string? savedName, bool movedBefore)
    {
        var saved = Find(savedName);
        if (saved == null) return (Default, false);
        if (saved.IsEx || saved.IsCustom || movedBefore) return (saved, false);
        return (ExEquivalent(saved), true);
    }

    /// <summary>The EX theme most like a Classic one: light for light, dark
    /// for dark, and of those the accent nearest in hue.</summary>
    public static ThemeDef ExEquivalent(ThemeDef theme)
    {
        if (theme.IsEx) return theme;
        double hue = Hue(Hex(theme.Accent));
        return All.Where(t => t.IsEx && t.IsDark == theme.IsDark)
                  .OrderBy(t => HueApart(Hue(Hex(t.Accent)), hue))
                  .First();
    }

    /// <summary>The colour wheel's angle of a colour, 0-360.</summary>
    internal static double Hue(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d < 1e-9) return 0;
        double h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60 + 360) % 360;
    }

    private static double HueApart(double a, double b) => Math.Min(Math.Abs(a - b), 360 - Math.Abs(a - b));

    /// <summary>Written at the first start of 1.8.0: whoever has it has been
    /// moved to EX once, and is not moved again.</summary>
    private static string MovedToExPath => AppDataFolder.PathOf("theme-moved-to-ex.json");

    private static void MarkMovedToEx()
    {
        if (TestMode.Active) return;   // as SaveName: a run never writes a person's settings
        try
        {
            if (File.Exists(MovedToExPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(MovedToExPath)!);
            File.WriteAllText(MovedToExPath, JsonConvert.SerializeObject(ForgeVersionText));
        }
        catch { /* best-effort: at worst they are moved once more */ }
    }

    private static string ForgeVersionText => Shared.ForgeVersion.Current;

    // ── Colour arithmetic ───────────────────────────────────────────────

    internal static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>Mix <paramref name="amount"/> of <paramref name="toward"/> into
    /// <paramref name="base"/>, staying fully opaque.</summary>
    internal static Color Blend(Color @base, Color toward, double amount)
    {
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromRgb(Mix(@base.R, toward.R), Mix(@base.G, toward.G), Mix(@base.B, toward.B));
    }

    /// <summary>WCAG's contrast ratio between two colours, 1 to 21.</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    /// <summary>
    /// <paramref name="base"/> with as much of <paramref name="toward"/> as
    /// <paramref name="amount"/> asks, or less - as much as still leaves
    /// <paramref name="ink"/> readable on it. For the colours words sit on:
    /// a selection, a hover, a pressed button.
    /// </summary>
    internal static Color Under(Color @base, Color toward, double amount, Color ink)
    {
        for (double a = amount; a > 0.03; a -= 0.02)
        {
            var c = Blend(@base, toward, a);
            if (Contrast(ink, c) >= ReadableContrast) return c;
        }
        return Blend(@base, toward, 0.03);
    }

    /// <summary>
    /// <paramref name="ink"/>, moved toward black or white - whichever it is
    /// already leaning to - until it reads against every one of
    /// <paramref name="backgrounds"/>. A theme's words never come out
    /// unreadable on a surface somebody forgot to check.
    /// </summary>
    internal static Color Readable(Color ink, params Color[] backgrounds)
    {
        var leaning = Luminance(ink) >= Luminance(backgrounds[0]) ? Colors.White : Colors.Black;
        var c = Pushed(ink, leaning, backgrounds);
        if (Worst(c, backgrounds) >= ReadableContrast) return c;
        // Pushed all the way and still short: a background in the middle -
        // a mid grey, which white does not read on - that only the other end
        // reads on. A theme of one's own can be any colours at all (1.8.0);
        // whichever end reads better wins.
        var other = Pushed(ink, leaning == Colors.White ? Colors.Black : Colors.White, backgrounds);
        return Worst(other, backgrounds) > Worst(c, backgrounds) ? other : c;
    }

    private static Color Pushed(Color ink, Color toward, Color[] backgrounds)
    {
        var c = ink;
        for (int step = 0; step <= 20 && backgrounds.Any(b => Contrast(c, b) < ReadableContrast); step++)
            c = Blend(ink, toward, step * 0.05);
        return c;
    }

    private static double Worst(Color ink, Color[] backgrounds) => backgrounds.Min(b => Contrast(ink, b));

    // ── Remembering the choice ──────────────────────────────────────────

    private static string? LoadName()
    {
        try { return File.Exists(FilePath) ? JsonConvert.DeserializeObject<string>(File.ReadAllText(FilePath)) : null; }
        catch { return null; }
    }

    private static void SaveName(string name)
    {
        // The tests switch themes and put back the one they started with, which
        // under the harness is the default rather than the person's own - so
        // saving would reset the theme of whoever ran them.
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
