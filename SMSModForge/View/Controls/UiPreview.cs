using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;

namespace SMSModForge.View.Controls;

/// <summary>
/// Preview for the UI tab: one vanilla UI base, drawn the way the game draws
/// it.
/// <para/>
/// Composed at the canvas's own resolution — 1920×1080 for almost every surface
/// in the game — and then fitted to whatever room it is given, the same
/// arrangement <see cref="PlacePreview"/> uses. Every coordinate inside is a
/// number the game would recognise, and only the last step is a scale.
/// <para/>
/// The drawing itself is <see cref="UiSceneRenderer"/>, which knows nothing
/// about WPF. This control's whole job is to decide when to run it, put the
/// result on screen, and say plainly when it could not draw something — a
/// preview that quietly omits a sprite is worse than one that refuses to
/// pretend.
/// </summary>
public sealed class UiPreview : Grid
{
    private readonly Image _image = new()
    {
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _message = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(24),
        Visibility = Visibility.Collapsed,
    };

    public UiPreview()
    {
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Children.Add(_image);
        Children.Add(_message);

        // Follows the editor's theme rather than being painted a fixed colour:
        // a UI surface is mostly transparent, and what shows through it is the
        // background, so a hard-coded one would be a lie at one of the two
        // themes.
        _message.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Muted");
        SetResourceReference(BackgroundProperty, "Theme.Control");
    }

    // ── What to draw ─────────────────────────────────────────────────

    public static readonly DependencyProperty BaseTokenProperty =
        DependencyProperty.Register(nameof(BaseToken), typeof(string), typeof(UiPreview),
            new PropertyMetadata("", OnInputChanged));

    /// <summary>Which vanilla UI to show, as a catalog token or id — for
    /// example <c>vanillaui:9_MainCanvas/Payout</c>. Empty shows nothing.</summary>
    public string BaseToken
    {
        get => (string)GetValue(BaseTokenProperty);
        set => SetValue(BaseTokenProperty, value);
    }

    public static readonly DependencyProperty ShowWholeSurfaceProperty =
        DependencyProperty.Register(nameof(ShowWholeSurface), typeof(bool), typeof(UiPreview),
            new PropertyMetadata(false, OnInputChanged));

    /// <summary>Draw everything on the canvas rather than the one base. Useful
    /// for seeing where a screen sits among the others; misleading as a default,
    /// because the game only ever shows one of them at a time.</summary>
    public bool ShowWholeSurface
    {
        get => (bool)GetValue(ShowWholeSurfaceProperty);
        set => SetValue(ShowWholeSurfaceProperty, value);
    }

    // ── What it managed to draw ──────────────────────────────────────

    private static readonly DependencyPropertyKey ReportKey =
        DependencyProperty.RegisterReadOnly(nameof(Report), typeof(UiRenderReport),
            typeof(UiPreview), new PropertyMetadata(null));

    public static readonly DependencyProperty ReportProperty = ReportKey.DependencyProperty;

    /// <summary>What the last render could not find. Read-only, and worth
    /// putting somewhere an author can see.</summary>
    public UiRenderReport? Report
    {
        get => (UiRenderReport?)GetValue(ReportProperty);
        private set => SetValue(ReportKey, value);
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((UiPreview)d).Refresh();

    // ── Drawing ──────────────────────────────────────────────────────

    /// <summary>Redraw. Cheap to call: the assets behind it are cached, so the
    /// cost is the composition itself.</summary>
    public void Refresh()
    {
        if (!VanillaUiLibrary.IsAvailable)
        {
            Say("The vanilla UI has not been extracted yet.\n\n" +
                "Run Tools ▸ SMSModForge ▸ Extract Vanilla UI Surfaces in the game's " +
                "Unity project, then Export TMP Font Assets, and put the result in " +
                "the editor's VanillaUi folder.");
            return;
        }

        var entry = VanillaUiCatalog.Find(BaseToken);
        if (entry == null)
        {
            Say(string.IsNullOrWhiteSpace(BaseToken)
                ? "Nothing selected."
                : $"There is no vanilla UI called “{BaseToken}”.");
            return;
        }

        var surface = VanillaUiLibrary.SurfaceFor(entry);
        if (surface == null)
        {
            Say($"“{entry.Surface.Path}” is in the catalog but its tree was not " +
                "shipped. The catalog and the surfaces come from the same " +
                "extraction, so one without the other means a partial copy.");
            return;
        }

        if (!surface.IsDrawable)
        {
            // Six of the game's canvases are like this: the Canvas component is
            // switched off, so Unity never gave the RectTransform a size and
            // every rectangle under it measured as a point. Saying so beats
            // showing an empty box that looks like a bug here.
            Say($"“{entry.Surface.Path}” cannot be drawn.\n\n" +
                "Its Canvas component is disabled in the game, so Unity never " +
                "gave it a size and nothing under it has a position to draw at.");
            return;
        }

        var report = new UiRenderReport();
        byte[] pixels;
        if (ShowWholeSurface)
        {
            pixels = UiSceneRenderer.Render(surface, VanillaUiLibrary.Assets, report);
        }
        else
        {
            var node = surface.Base(entry.Id);
            if (node == null)
            {
                Say($"“{entry.Id}” is not on “{entry.Surface.Path}” any more. " +
                    "The catalog and the surface may be from different extractions.");
                return;
            }
            pixels = UiSceneRenderer.RenderBase(surface, node, VanillaUiLibrary.Assets, report);
        }

        Report = report;

        if (pixels.Length == 0) { Say("Nothing was drawn."); return; }

        int w = (int)Math.Round(surface.Width), h = (int)Math.Round(surface.Height);
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null,
                                         pixels, w * 4);
        bitmap.Freeze();      // so it can be handed about without a dispatcher

        _image.Source = bitmap;
        _image.Visibility = Visibility.Visible;
        _message.Visibility = Visibility.Collapsed;
    }

    /// <summary>What the last render could not find, in one line, or empty when
    /// it found everything.</summary>
    public string Trouble()
    {
        var r = Report;
        if (r == null) return "";
        var parts = new System.Collections.Generic.List<string>();
        if (r.MissingSprites.Count > 0) parts.Add($"{r.MissingSprites.Count} missing sprite(s)");
        if (r.MissingFonts.Count > 0) parts.Add($"{r.MissingFonts.Count} missing font(s)");
        if (r.MissingGlyphs.Count > 0) parts.Add($"{r.MissingGlyphs.Count} unbaked glyph(s)");
        if (r.LegacyText.Count > 0) parts.Add($"{r.LegacyText.Count} legacy text object(s)");
        if (r.Untrustworthy.Count > 0)
            parts.Add($"{r.Untrustworthy.Count} object(s) whose position the game will change");
        return string.Join(", ", parts);
    }

    private void Say(string what)
    {
        _message.Text = what;
        _message.Visibility = Visibility.Visible;
        _image.Visibility = Visibility.Collapsed;
        _image.Source = null;
        Report = null;
    }
}
