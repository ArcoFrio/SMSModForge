using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Services;

namespace SMSModForge.ViewModel;

/// <summary>
/// Themes of one's own (the author, 1.8.0: "How about giving users the
/// ability to create custom themes?"), as the window that makes them shows
/// them: a list, and for the one selected its name, its look - EX or Classic -
/// and the six colours every theme is made of.
/// <para/>
/// The editor is its own preview: the theme selected here is the one in use,
/// and every change shows on the whole window at once and is kept at once.
/// Words never become unreadable, whatever is picked - the palette pushes them
/// until they read (<see cref="ThemeManager.Palette"/>).
/// </summary>
public sealed class CustomThemesViewModel : ObservableObject
{
    private readonly Func<string?, string?> _pickColour;

    /// <param name="pickColour">Opens a colour picker on a colour and gives
    /// back the one chosen, or null when nothing was.</param>
    public CustomThemesViewModel(Func<string?, string?> pickColour)
    {
        _pickColour = pickColour;
        _selected = ThemeManager.Current.IsCustom ? ThemeManager.Current : null;
        NewCommand = new RelayCommand(New);
        DuplicateCommand = new RelayCommand(Duplicate, () => Selected != null);
        RemoveCommand = new RelayCommand(Remove, () => Selected != null);
        PickColourCommand = new RelayCommand(p => PickColour(p as string), _ => Selected != null);
    }

    public ObservableCollection<ThemeDef> Themes => ThemeManager.Custom;

    private ThemeDef? _selected;
    /// <summary>The theme being made - and the one in use.</summary>
    public ThemeDef? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            if (value != null) ThemeManager.Apply(value);
            RaiseAll();
        }
    }

    public bool HasSelection => Selected != null;

    public RelayCommand NewCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand PickColourCommand { get; }

    /// <summary>Its name; one already taken gets a number after it.</summary>
    public string Name
    {
        get => Selected?.Name ?? "";
        set
        {
            if (Selected == null) return;
            string name = ThemeManager.UniqueCustomName(value, Selected);
            if (name == Selected.Name) { OnPropertyChanged(); return; }
            Put(ThemeManager.With(Selected, name: name));
        }
    }

    /// <summary>Drawn the EX way, or the Classic way.</summary>
    public bool IsEx
    {
        get => Selected?.IsEx ?? true;
        set
        {
            if (Selected == null || value == Selected.IsEx) return;
            Put(ThemeManager.With(Selected, ex: value));
        }
    }

    /// <summary>Light or dark, as its background makes it.</summary>
    public bool IsDark => Selected?.IsDark ?? false;

    /// <summary>
    /// Some of its words cannot be made to read: the colours they are drawn
    /// on are too far apart for any one text colour (a mid-grey panel and a
    /// near-black box). Said rather than hidden - the editor pushes every word
    /// as far as it can, and only the colours themselves can do the rest.
    /// </summary>
    public bool HasUnreadable => Selected != null && ThemeManager.Unreadable(Selected).Count > 0;

    public string WindowColour  { get => Selected?.Window ?? "";  set => SetColour(value, nameof(WindowColour)); }
    public string ChromeColour  { get => Selected?.Chrome ?? "";  set => SetColour(value, nameof(ChromeColour)); }
    public string SurfaceColour { get => Selected?.Surface ?? ""; set => SetColour(value, nameof(SurfaceColour)); }
    public string ControlColour { get => Selected?.Control ?? ""; set => SetColour(value, nameof(ControlColour)); }
    public string TextColour    { get => Selected?.Text ?? "";    set => SetColour(value, nameof(TextColour)); }
    public string AccentColour  { get => Selected?.Accent ?? "";  set => SetColour(value, nameof(AccentColour)); }

    /// <summary>"#RRGGBB" for anything that reads as a colour (alpha dropped:
    /// a theme's colours are solid), or null.</summary>
    internal static string? Solid(string? text)
        => ColorMath.TryParse(text?.Trim(), out byte r, out byte g, out byte b, out _)
            ? $"#{r:X2}{g:X2}{b:X2}" : null;

    private void SetColour(string value, string which)
    {
        if (Selected == null) return;
        string? hex = Solid(value);
        // Not a colour: the box goes back to the one the theme has.
        if (hex == null) { OnPropertyChanged(which); return; }
        var t = Selected;
        Put(which switch
        {
            nameof(WindowColour) => ThemeManager.With(t, window: hex),
            nameof(ChromeColour) => ThemeManager.With(t, chrome: hex),
            nameof(SurfaceColour) => ThemeManager.With(t, surface: hex),
            nameof(ControlColour) => ThemeManager.With(t, control: hex),
            nameof(TextColour) => ThemeManager.With(t, text: hex),
            _ => ThemeManager.With(t, accent: hex),
        });
    }

    private void PickColour(string? which)
    {
        if (Selected == null || which == null) return;
        string current = (string)(GetType().GetProperty(which)?.GetValue(this) ?? "");
        string? chosen = _pickColour(current);
        if (chosen != null) SetColour(chosen, which);
    }

    /// <summary>A theme to start from: the one in use, colours and look,
    /// under a name of its own.</summary>
    private void New()
    {
        var from = ThemeManager.Current;
        var theme = ThemeManager.MakeCustom(ThemeManager.UniqueCustomName(Loc.T("theme.custom.newName")), from.IsEx,
                                            from.Window, from.Chrome, from.Surface, from.Control, from.Text, from.Accent);
        ThemeManager.PutCustom(null, theme);
        Selected = theme;
    }

    private void Duplicate()
    {
        if (Selected == null) return;
        var copy = ThemeManager.With(Selected, name: ThemeManager.UniqueCustomName(Selected.Name));
        ThemeManager.PutCustom(null, copy);
        Selected = copy;
    }

    private void Remove()
    {
        if (Selected == null) return;
        int at = Themes.IndexOf(Selected);
        ThemeManager.RemoveCustom(Selected);
        _selected = null;
        Selected = Themes.Count == 0 ? null : Themes[Math.Min(at, Themes.Count - 1)];
        RaiseAll();
    }

    /// <summary>The selected theme becomes <paramref name="now"/>, in the list,
    /// the file and on screen.</summary>
    private void Put(ThemeDef now)
    {
        var was = Selected;
        ThemeManager.PutCustom(was, now);
        _selected = now;
        ThemeManager.Apply(now);
        RaiseAll();
    }

    private void RaiseAll()
    {
        foreach (var p in new[] { nameof(Selected), nameof(HasSelection), nameof(Name), nameof(IsEx), nameof(IsDark), nameof(HasUnreadable),
                                  nameof(WindowColour), nameof(ChromeColour), nameof(SurfaceColour), nameof(ControlColour),
                                  nameof(TextColour), nameof(AccentColour) })
            OnPropertyChanged(p);
        DuplicateCommand.Raise();
        RemoveCommand.Raise();
        PickColourCommand.Raise();
    }
}
