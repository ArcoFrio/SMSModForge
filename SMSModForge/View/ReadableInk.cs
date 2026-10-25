using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Rendering;
using SMSModForge.Services;

namespace SMSModForge.View;

/// <summary>
/// A colour from the game - a speaker's name colour, the game's bracket
/// blue, a colour a line asks for - made readable on whatever the editor
/// draws it on (the author, 1.8.0: in the plain rows of a dark theme they did
/// not read, or were not there at all). Its hue is kept and its lightness
/// moved, toward white on a dark background and toward black on a light one,
/// only as far as it takes (<see cref="ThemeManager.Readable"/>). The Game look
/// row shows them as the game does, on the game's own dark panel.
/// <para/>
/// What is behind the words is read off the screen - the first solid fill
/// under them, a selected row's colour included - and read again when the
/// theme changes or the row is selected.
/// </summary>
public static class ReadableInk
{
    /// <summary>A name colour (hex, as the game takes it) for a TextBlock's
    /// words; empty for the theme's own text colour.</summary>
    public static readonly DependencyProperty HexProperty =
        DependencyProperty.RegisterAttached("Hex", typeof(string), typeof(ReadableInk),
            new PropertyMetadata(null, (d, _) => { if (d is TextBlock t) { Watch(t, () => Apply(t)); Apply(t); } }));

    public static string? GetHex(DependencyObject d) => (string?)d.GetValue(HexProperty);
    public static void SetHex(DependencyObject d, string? value) => d.SetValue(HexProperty, value);

    private static void Apply(TextBlock text)
    {
        if (!TmpColor.TryParse(GetHex(text), out var c) || c.A == 0)
        {
            text.ClearValue(TextBlock.ForegroundProperty);
            text.ClearValue(UIElement.EffectProperty);
            return;
        }
        var ink = On(Color.FromRgb(c.R, c.G, c.B), Behind(text));
        text.Foreground = new SolidColorBrush(ink);
        // In a dark EX theme, a very slight glow in the name's own colour (the
        // author, 1.8.0), as the coloured words of the line have (TextGlow).
        // Not in a light one: there a halo reads as a smudge, not as light.
        if (ThemeManager.Current.IsEx && ThemeManager.Current.IsDark)
            text.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = ink, ShadowDepth = 0, BlurRadius = 7, Opacity = 0.55,
            };
        else text.ClearValue(UIElement.EffectProperty);
    }

    /// <summary><paramref name="ink"/> as it is, when it reads on
    /// <paramref name="behind"/>; moved in lightness until it does otherwise.</summary>
    public static Color On(Color ink, Color behind) => ThemeManager.Readable(ink, behind);

    /// <summary>The first solid fill under <paramref name="element"/>, walking up
    /// what draws it; the theme's control colour when nothing there is solid.</summary>
    public static Color Behind(DependencyObject element)
    {
        for (var up = VisualTreeHelper.GetParent(element); up != null; up = VisualTreeHelper.GetParent(up))
        {
            var fill = up switch
            {
                Border b => b.Background,
                Panel p => p.Background,
                Control c => c.Background,
                _ => null,
            };
            if (fill is SolidColorBrush { Color.A: 255 } solid && solid.Opacity >= 1) return solid.Color;
        }
        return (Application.Current?.TryFindResource(ThemeManager.KeyControl) as SolidColorBrush)?.Color ?? Colors.White;
    }

    /// <summary>
    /// Call <paramref name="again"/> whenever what is behind
    /// <paramref name="element"/> may have changed: it was laid out in a new
    /// place, the theme changed, the row it is in was selected or let go.
    /// Watched only while on screen, so a row nobody sees costs nothing.
    /// </summary>
    internal static void Watch(FrameworkElement element, Action again)
    {
        if (element.GetValue(WatcherProperty) != null) return;
        var watcher = new Watcher(element, again);
        element.SetValue(WatcherProperty, watcher);
        element.Loaded += (_, _) => watcher.Start();
        element.Unloaded += (_, _) => watcher.Stop();
        if (element.IsLoaded) watcher.Start();
    }

    private static readonly DependencyProperty WatcherProperty =
        DependencyProperty.RegisterAttached("Watcher", typeof(object), typeof(ReadableInk));

    private sealed class Watcher
    {
        private readonly FrameworkElement _element;
        private readonly Action _again;
        private ListBoxItem? _row;

        public Watcher(FrameworkElement element, Action again) { _element = element; _again = again; }

        public void Start()
        {
            Stop();
            ThemeManager.Changed += Later;
            _row = FindRow(_element);
            if (_row != null)
            {
                _row.Selected += OnRow;
                _row.Unselected += OnRow;
            }
            Later();
        }

        public void Stop()
        {
            ThemeManager.Changed -= Later;
            if (_row != null)
            {
                _row.Selected -= OnRow;
                _row.Unselected -= OnRow;
                _row = null;
            }
        }

        private void OnRow(object sender, RoutedEventArgs e) => Later();

        /// <summary>After the fill under it has been drawn: a selection's colour
        /// is set by a trigger, which runs after the event.</summary>
        private void Later() => _element.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, _again);


        private static ListBoxItem? FindRow(DependencyObject from)
        {
            for (var up = VisualTreeHelper.GetParent(from); up != null; up = VisualTreeHelper.GetParent(up))
                if (up is ListBoxItem row) return row;
            return null;
        }
    }
}
