using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SMSModForge.Localization;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// A character standing in the place (1.10.0, the author: lights that light the
/// characters, "Let's go ahead"): one of the pack's outfits drawn where the game
/// shows a character in a dialogue, as the bust preview draws it over one of the
/// game's places - the same program, edge glow and post-processing
/// (<see cref="JigglePreview"/>) - over a backdrop made from this place as the
/// preview lights it (<see cref="PreviewBackdrop.ForPlace"/>): the lights that
/// light the characters multiply the bust, their glows are drawn over it, the
/// others' only behind it. Preview only: nothing is saved.
/// </summary>
public sealed partial class PlacePreview
{
    /// <summary>The pack's characters, whose outfits can stand in the place.</summary>
    public static readonly DependencyProperty CharactersProperty =
        DependencyProperty.Register(nameof(Characters), typeof(IEnumerable), typeof(PlacePreview),
            new PropertyMetadata(null, (d, e) => (d as PlacePreview)?.OnCharactersChanged(e.OldValue as IEnumerable, e.NewValue as IEnumerable)));

    /// <summary>Follow the list itself, not only which list it is: a pack opened fills the same one.</summary>
    private void OnCharactersChanged(IEnumerable? was, IEnumerable? now)
    {
        if (was is System.Collections.Specialized.INotifyCollectionChanged old) old.CollectionChanged -= OnCharacterListChanged;
        if (now is System.Collections.Specialized.INotifyCollectionChanged list) list.CollectionChanged += OnCharacterListChanged;
        RefreshStandInChoices();
    }

    private void OnCharacterListChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshStandInChoices();

    public IEnumerable? Characters
    {
        get => (IEnumerable?)GetValue(CharactersProperty);
        set => SetValue(CharactersProperty, value);
    }

    private ComboBox _standInBox = new();
    private OutfitViewModel? _standInOutfit;
    private JigglePreview? _standIn;
    private int _standInGeneration;
    private bool _standInScheduled;

    /// <summary>The scene the character stands in now - for tests.</summary>
    internal BackdropScene? StandInScene => _standIn?.SceneOverride;

    /// <summary>The character drawn in the place - for tests.</summary>
    internal JigglePreview? StandIn => _standIn;

    private sealed record StandInChoice(OutfitViewModel? Outfit, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>The chooser, at the top of the panel beside the picture.</summary>
    private UIElement BuildStandInRow()
    {
        var label = Chip("preview.standIn");
        label.Foreground = ChipText;
        label.FontSize = 11;
        label.Margin = new Thickness(0, 0, 0, 2);
        LocText.Bind(label, ToolTipProperty, "preview.standIn.tip");
        _standInBox = new ComboBox { FontSize = 11, Margin = new Thickness(0, 0, 0, 8), MinWidth = 140 };
        LocText.Bind(_standInBox, ToolTipProperty, "preview.standIn.tip");
        _standInBox.SelectionChanged += (_, _) => { if (!_refreshingChoices) SetStandIn((_standInBox.SelectedItem as StandInChoice)?.Outfit); };
        // Outfits added or renamed since: the list as it is when it opens.
        _standInBox.DropDownOpened += (_, _) => RefreshStandInChoices();
        var row = new StackPanel();
        TutorialAnchor.SetId(row, "panel:standIn");
        row.Children.Add(label);
        row.Children.Add(_standInBox);
        RefreshStandInChoices();
        return row;
    }

    /// <summary>None, then every outfit of the pack's own characters, then the game's characters'.</summary>
    private void RefreshStandInChoices()
    {
        var choices = new List<StandInChoice> { new(null, Loc.T("preview.standIn.none")) };
        // The characters drawn with the pack's own art first, then those wearing the game's busts.
        var all = (Characters?.OfType<CharacterViewModel>() ?? Enumerable.Empty<CharacterViewModel>()).ToList();
        foreach (var c in all.Where(c => c.IsPackBust).Concat(all.Where(c => !c.IsPackBust)))
            foreach (var o in c.Outfits)
                choices.Add(new(o, c.Outfits.Count > 1 ? c.Display + " · " + o.Key : c.Display));
        var keep = _standInOutfit;
        _refreshingChoices = true;
        try
        {
            _standInBox.ItemsSource = choices;
            _standInBox.SelectedItem = choices.FirstOrDefault(ch => ReferenceEquals(ch.Outfit, keep)) ?? choices[0];
        }
        finally { _refreshingChoices = false; }
        // The one standing is gone (another pack, the outfit removed): nobody.
        if (keep != null && !choices.Any(ch => ReferenceEquals(ch.Outfit, keep))) SetStandIn(null);
    }

    private bool _refreshingChoices;

    /// <summary>Stand <paramref name="outfit"/> in the place, or nobody.</summary>
    internal void SetStandIn(OutfitViewModel? outfit)
    {
        if (ReferenceEquals(outfit, _standInOutfit) && (outfit == null) == (_standIn == null)) return;
        _standInOutfit = outfit;
        _standInGeneration++;
        if (outfit == null)
        {
            if (_standIn != null) { _canvas.Children.Remove(_standIn); _standIn = null; }
            return;
        }
        if (_standIn == null)
        {
            _standIn = new JigglePreview
            {
                IsHitTestVisible = false, Breathing = true, Visibility = Visibility.Collapsed,
                // The frame is the game's screen pixels around the bust: two canvas pixels each.
                RenderTransform = new ScaleTransform(CanvasWidth / PlaceLights.ScreenW, CanvasHeight / PlaceLights.ScreenH),
            };
            // Over the place and its look, under the suns and the handles.
            _canvas.Children.Insert(_canvas.Children.IndexOf(_lookImage) + 1, _standIn);
        }
        _standIn.Visibility = Visibility.Collapsed;
        _standIn.SceneOverride = null;
        _standIn.PackRoot = PackRoot;
        _standIn.VanillaBustKey = outfit.IsVanillaBust ? outfit.GameObjectName : null;
        _standIn.Outfit = outfit;
        ScheduleStandIn();
    }

    /// <summary>Make the character's place again once what is drawn has settled (after a rebuild).</summary>
    private void ScheduleStandIn()
    {
        if (_standInOutfit == null || _standInScheduled) return;
        _standInScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _standInScheduled = false;
            BuildStandInScene();
        }));
    }

    /// <summary>
    /// The place as a bust's backdrop, from what the preview draws now: the scene at the game's screen (960x540),
    /// through the place's lights as the look lights it (<see cref="PlaceLights.Apply(ref float, ref float, ref float, float, float, float, PlaceLights.Glows)"/>):
    /// the room with every light's glow; the room as the edge glow reads it (without the glows drawn over the
    /// characters), one pixel of every 4x4 (the game's copy, 1.9.1); the glows drawn over the characters alone; and
    /// what the characters' layer's light multiplies a bust by. Worked out off the interface's thread.
    /// </summary>
    private void BuildStandInScene()
    {
        var standIn = _standIn;
        if (standIn == null || _placeholder.Visibility == Visibility.Visible) return;
        var like = Services.PreviewBackdropManager.GameLook;
        if (like == null)
        {
            // The game's look is still loading: again in a moment.
            var again = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            again.Tick += (_, _) => { again.Stop(); ScheduleStandIn(); };
            again.Start();
            return;
        }
        FlushRebuild();
        int generation = ++_standInGeneration;   // only the latest pass is shown
        const int W = PlaceLights.ScreenW, H = PlaceLights.ScreenH;
        var lights = LookLights ? _placeLights : null;
        var screen = new byte[W * H * 4];
        byte[]? lit = null;
        // The scene at the game's screen size. Drawing on the card, the card draws it from the scene's list
        // (1.10.0, PlacePreview.Card.cs) off this thread - and with the shaders' pictures as they are now, which
        // the world layer's own bitmaps do not have while the card draws the look. Else a capture of the world
        // layer, as before.
        Rendering.Gpu.PlaceFrame? frame = null;
        if (Services.PreviewRendererManager.UseCard && !_cardBroken)
        {
            frame = new Rendering.Gpu.PlaceFrame();
            if (DescribeScene(frame, forCard: true))
                Frame(frame, new Rect(0, 0, CanvasWidth, CanvasHeight), W, H, new BackdropRender.PostParts(false, false, false), like, lights);
            else frame = null;
        }
        if (frame == null)
        {
            var capture = new RenderTargetBitmap(W, H, 48, 48, PixelFormats.Pbgra32);
            capture.Render(_worldLayer);
            capture.CopyPixels(screen, W * 4, 0);
            if (lights != null)
                WithSilhouettes(() =>
                {
                    var mask = new RenderTargetBitmap(W, H, 48, 48, PixelFormats.Pbgra32);
                    mask.Render(_worldLayer);
                    lit = new byte[W * H * 4];
                    mask.CopyPixels(lit, W * 4, 0);
                });
        }
        var decode = like.Decode;
        string name = "place";
        var shadowLamp = lights != null ? ShadowLamp(lights) : null;
        Task.Run(() =>
        {
            if (frame != null)
            {
                var card = CardScene();
                if (card == null) throw new InvalidOperationException("no graphics card device");   // English on purpose: caught below, never shown
                if (frame.Masked) lit = new byte[W * H * 4];
                card.Capture(frame, screen, lit);
            }
            var room = new float[W * H * 3];
            var gi = new float[W * H * 3];
            var over = new float[W * H * 3];
            float[]? multiply = lights != null && lights.ChangesCharacters ? new float[W * H * 3] : null;
            // With a lamp that casts the bust's shadow: the place's other lights apart from it, and the room's colour.
            var others = shadowLamp != null
                ? new PlaceLights(lights!.Lights.Where(x => !x.Equals(shadowLamp.Value.Light)), lights.Global, lights.CharactersGlobal, like.Backdrop.LookupDecode)
                : null;
            float[]? albedo = others != null ? new float[W * H * 3] : null, noGlow = others != null ? new float[W * H * 3] : null;
            float[]? othersBehind = others != null ? new float[W * H * 3] : null, othersOver = others != null ? new float[W * H * 3] : null;
            Parallel.For(0, H, y =>
            {
                int src = (H - 1 - y) * W * 4;
                for (int x = 0; x < W; x++)
                {
                    int i = src + x * 4, o = (y * W + x) * 3;
                    float r = GamePost.Small(decode[screen[i + 2]], 6), g = GamePost.Small(decode[screen[i + 1]], 6), b = GamePost.Small(decode[screen[i]], 5);
                    float sx = x + 0.5f, sy = y + 0.5f, l = lit == null ? 1f : lit[i + 2] / 255f;
                    float rr = r, rg = g, rb = b, gr = r, gg = g, gb = b, or = 0f, og = 0f, ob = 0f;
                    lights?.Apply(ref rr, ref rg, ref rb, sx, sy, l, PlaceLights.Glows.All);
                    lights?.Apply(ref gr, ref gg, ref gb, sx, sy, l, PlaceLights.Glows.BehindCharacters);
                    lights?.Apply(ref or, ref og, ref ob, sx, sy, 0f, PlaceLights.Glows.OverCharacters);
                    room[o] = rr; room[o + 1] = rg; room[o + 2] = rb;
                    gi[o] = gr; gi[o + 1] = gg; gi[o + 2] = gb;
                    over[o] = or; over[o + 1] = og; over[o + 2] = ob;
                    if (multiply != null)
                    {
                        var (mr, mg, mb) = lights!.CharactersLightAt(sx, sy);
                        multiply[o] = mr; multiply[o + 1] = mg; multiply[o + 2] = mb;
                    }
                    if (others != null)
                    {
                        albedo![o] = r; albedo[o + 1] = g; albedo[o + 2] = b;
                        float lr = r, lg = g, lb = b, br = 0f, bg = 0f, bb = 0f, vr = 0f, vg = 0f, vb = 0f;
                        lights!.Apply(ref lr, ref lg, ref lb, sx, sy, l, PlaceLights.Glows.None);
                        others.Apply(ref br, ref bg, ref bb, sx, sy, 0f, PlaceLights.Glows.BehindCharacters);
                        others.Apply(ref vr, ref vg, ref vb, sx, sy, 0f, PlaceLights.Glows.OverCharacters);
                        othersBehind![o] = br; othersBehind[o + 1] = bg; othersBehind[o + 2] = bb;
                        othersOver![o] = vr; othersOver[o + 1] = vg; othersOver[o + 2] = vb;
                        noGlow![o] = GamePost.Small(lr + br, 6); noGlow[o + 1] = GamePost.Small(lg + bg, 6); noGlow[o + 2] = GamePost.Small(lb + bb, 5);
                    }
                }
            });
            int bw = W / 4, bh = H / 4;
            var background = new float[bw * bh * 4];
            for (int j = 0; j < bh; j++)
                for (int i = 0; i < bw; i++)
                {
                    int s = ((j * 4 + 1) * W + i * 4 + 1) * 3, o = (j * bw + i) * 4;
                    background[o] = gi[s]; background[o + 1] = gi[s + 1]; background[o + 2] = gi[s + 2]; background[o + 3] = 1f;
                }
            var roomBytes = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int s = (y * W + x) * 3, o = ((H - 1 - y) * W + x) * 4;
                    roomBytes[o] = BackdropRender.ToSrgbByte(room[s + 2]); roomBytes[o + 1] = BackdropRender.ToSrgbByte(room[s + 1]);
                    roomBytes[o + 2] = BackdropRender.ToSrgbByte(room[s]); roomBytes[o + 3] = 255;
                }
            var lamp = shadowLamp != null && others != null
                ? new PreviewBackdrop.PlaceLamp(shadowLamp.Value.Settings, gi, noGlow!, albedo!, others.Texels, othersBehind!, othersOver!)
                : null;
            var backdrop = PreviewBackdrop.ForPlace(name, like.Backdrop, roomBytes, room, background, bw, bh, over, multiply, lamp);
            var scene = new BackdropScene(backdrop);
            scene.Prepare();
            return scene;
        }).ContinueWith(t =>
        {
            if (t.IsFaulted && frame != null && !_cardBroken)
            {
                // The card's way failed: the processor's from here on, and this place again that way.
                _cardBroken = true;
                CardFailure = t.Exception?.GetBaseException().Message;
                CardLookOver();
                ScheduleStandIn();
                return;
            }
            if (!t.IsCompletedSuccessfully || generation != _standInGeneration || !ReferenceEquals(standIn, _standIn)) return;
            standIn.SceneOverride = t.Result;
            Canvas.SetLeft(standIn, t.Result.Left * CanvasWidth / W);
            Canvas.SetTop(standIn, CanvasHeight - (t.Result.FrameBottom + BackdropRender.FrameSize) * CanvasHeight / H);
            standIn.Visibility = Visibility.Visible;
            StandInBuilt++;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// The lamp that casts a character's shadow over the place (1.10.0): a light of the game's with its shadows on -
    /// in a place of the pack's, the template level's (its shadow, the Beach's, covers nearly the whole screen,
    /// measured 1.9.1) - lighting the place, round, all round and full from its middle (what <see cref="GameLamp"/>
    /// draws), and the only one: none when two cast shadows, or the place has none. A light the pack makes casts none.
    /// </summary>
    private (PlaceLights.Light Light, LampSettings Settings)? ShadowLamp(PlaceLights lights)
    {
        (PlaceLights.Light, LampSettings)? found = null;
        foreach (var entry in _scene)
        {
            var node = entry.Node;
            if (node?.Model.Light is not { } def || node.Model.RemoveLight || entry.Hidden) continue;
            if (entry.ParentInactive || entry.Conditional || !node.StartActive || entry.Choice != null) continue;
            var p = node.Model.Baseline?.ComponentValues.FirstOrDefault(c => c.Type == "Light2D")?.Params;
            if (p == null || !Bool(p, "m_ShadowsEnabled")) continue;
            if (!def.LightsThePlace || !def.IsRound || def.OuterAngle < 360f || def.InnerRadius > 0f) return null;
            var (sx, sy) = ScreenPoint(entry.World.Tx, entry.World.Ty);
            if (PlaceLights.FromLight(def, sx, sy, WorldRotation(entry.World)) is not { } light) continue;
            if (found != null) return null;
            bool volume = Bool(p, "m_ShadowVolumeIntensityEnabled");
            found = (light, new LampSettings(light.X, light.Y, light.Radius, light.R, light.G, light.B, light.Falloff, light.VolumeOpacity,
                                             F(p, "m_ShadowIntensity", 0.75f), volume ? F(p, "m_ShadowVolumeIntensity", 0.75f) : 0f,
                                             F(p, "m_ShadowSoftness", 0.3f), F(p, "m_ShadowSoftnessFalloffIntensity", 0.5f), 0.4f, (1f, 1f, 1f),
                                             GlowWithTheRoom: !def.GlowsOverCharacters));
        }
        return found;

        static bool Bool(System.Collections.Generic.IReadOnlyDictionary<string, object?> d, string k)
            => d.TryGetValue(k, out var v) && v is bool b && b;
        static float F(System.Collections.Generic.IReadOnlyDictionary<string, object?> d, string k, float fallback)
            => d.TryGetValue(k, out var v) && v != null && float.TryParse(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
                   System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : fallback;
    }

    /// <summary>How many times the character's place has been made - for tests.</summary>
    internal int StandInBuilt { get; private set; }
}
