using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// The game's interface over the place preview (after 1.10.0, the author: "The UI texture shown on all previews should
/// match as well, and I think we should do a proper render there instead of just a PNG slapped onto it"): drawn by
/// <see cref="GameHud"/> from the game's own canvas, the place's buttons in its navigator, instead of a 2048x1136 cut
/// of a screenshot squeezed onto the 1920x1080 screen with the buttons drawn over it by hand. Without the game's
/// extracted interface the old picture and buttons are drawn, as before.
/// </summary>
public sealed partial class PlacePreview
{
    private static readonly Dictionary<string, BitmapSource> _huds = new(StringComparer.Ordinal);
    private static readonly object HudGate = new();

    /// <summary>The words on the place's buttons, in their order, as the game puts them on: a button without any
    /// says where it goes (Shared/NavigatorLabels).</summary>
    private List<string> ButtonWords()
    {
        var words = new List<string>();
        if (Buttons == null) return words;
        foreach (var item in Buttons)
            if (item is NavigatorButtonViewModel b)
                words.Add(string.IsNullOrEmpty(b.Label) ? Shared.NavigatorLabels.Fallback(b.Target ?? "") : b.Label);
            else words.Add("");
        return words;
    }

    /// <summary>Whether the interface shown is the game's own, drawn - for tests.</summary>
    internal bool HudDrawn { get; private set; }

    /// <summary>The game's interface with the place's buttons, kept by their words (the same list is drawn once).
    /// Null without the game's extracted interface.</summary>
    internal static BitmapSource? Hud(IReadOnlyList<string> words)
    {
        string key = string.Join("\u0001", words);
        lock (HudGate)
            if (_huds.TryGetValue(key, out var kept)) return kept;
        var pixels = GameHud.Render(words, out int w, out int h);
        if (pixels == null || pixels.Length == 0 || w <= 0 || h <= 0) return null;
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        bitmap.Freeze();
        lock (HudGate)
        {
            // A few lists at a time: the places of a pack, an author typing a button's words.
            if (_huds.Count >= 24) _huds.Clear();
            _huds[key] = bitmap;
        }
        return bitmap;
    }

    /// <summary>The game's interface over the preview, drawn: true when it could be (the buttons are in it), false
    /// to draw the old picture and buttons.</summary>
    private bool ShowGameHud()
    {
        var hud = Hud(ButtonWords());
        HudDrawn = hud != null;
        if (hud == null) return false;
        _overlayImage.Source = hud;
        _overlayImage.Visibility = Visibility.Visible;
        RenderOptions.SetBitmapScalingMode(_overlayImage, BitmapScalingMode.HighQuality);
        return true;
    }
}
