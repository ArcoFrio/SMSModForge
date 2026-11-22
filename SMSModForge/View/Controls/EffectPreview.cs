using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using FX = SMSModForge.Shared.Effects;

namespace SMSModForge.View.Controls;

/// <summary>
/// The FX tab's preview (1.10.0): an effect as the game will play it, near enough to judge - over the game's own
/// bedroom, in the game's 16:9, moving.
/// <para/>
/// Not the game's renderer: a small particle simulation of each template's shape (how they come, how they move,
/// how big, how long, how they fade - read from the game's own effects with the Debug bridge's "particles", and
/// for ModForge's own from ForgeShapes) with the author's settings over it, drawing the game's own pictures
/// (Resources/VanillaFx). One-shot effects play again every few seconds so there is something to look at.
/// Under the test harness it does not move: it draws the moment 1.2 seconds in, the same every time.
/// </summary>
public sealed class EffectPreview : FrameworkElement
{
    public static readonly DependencyProperty EffectProperty = DependencyProperty.Register(
        nameof(Effect), typeof(EffectDef), typeof(EffectPreview), new PropertyMetadata(null, (d, _) => ((EffectPreview)d).Restart()));

    /// <summary>Changes whenever anything about the effect does: the preview starts it again.</summary>
    public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(
        nameof(Version), typeof(int), typeof(EffectPreview), new PropertyMetadata(0, (d, _) => ((EffectPreview)d).Restart()));

    public static readonly DependencyProperty PackRootProperty = DependencyProperty.Register(
        nameof(PackRoot), typeof(string), typeof(EffectPreview), new PropertyMetadata(null, (d, _) => ((EffectPreview)d).Restart()));

    public EffectDef? Effect { get => (EffectDef?)GetValue(EffectProperty); set => SetValue(EffectProperty, value); }

    /// <summary>Only the effect, over whatever is under the control (the place preview's objects' effects).</summary>
    public static readonly DependencyProperty BareProperty = DependencyProperty.Register(
        nameof(Bare), typeof(bool), typeof(EffectPreview), new PropertyMetadata(false, (d, _) => ((EffectPreview)d).InvalidateVisual()));

    public bool Bare { get => (bool)GetValue(BareProperty); set => SetValue(BareProperty, value); }

    /// <summary>Where it plays (fractions of the control, from the bottom left), over the effect's own place - an
    /// object's, in the place preview. NaN: the effect's own. Moving it does not start the effect again.</summary>
    public static readonly DependencyProperty AtXProperty = DependencyProperty.Register(
        nameof(AtX), typeof(double), typeof(EffectPreview), new PropertyMetadata(double.NaN, (d, _) => ((EffectPreview)d).Moved()));
    public static readonly DependencyProperty AtYProperty = DependencyProperty.Register(
        nameof(AtY), typeof(double), typeof(EffectPreview), new PropertyMetadata(double.NaN, (d, _) => ((EffectPreview)d).Moved()));

    public double AtX { get => (double)GetValue(AtXProperty); set => SetValue(AtXProperty, value); }
    public double AtY { get => (double)GetValue(AtYProperty); set => SetValue(AtYProperty, value); }

    private void Moved()
    {
        if (_sim == null || double.IsNaN(AtX) || double.IsNaN(AtY)) return;
        _sim.MoveTo(AtX, AtY);
        InvalidateVisual();
    }
    public int Version { get => (int)GetValue(VersionProperty); set => SetValue(VersionProperty, value); }
    public string? PackRoot { get => (string?)GetValue(PackRootProperty); set => SetValue(PackRootProperty, value); }

    public EffectPreview()
    {
        Loaded += (_, _) => { Restart(); Animate(true); };
        Unloaded += (_, _) => Animate(false);
        IsVisibleChanged += (_, _) => Animate(IsVisible);
        SnapsToDevicePixels = true;
    }

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 480 : Math.Min(available.Width, 640);
        return new Size(w, w * 9 / 16);
    }

    // ── Time ───────────────────────────────────────────────────────────────────────────────────

    private bool _animating;
    private TimeSpan _last;
    private double _time;

    private void Animate(bool on)
    {
        on = on && IsLoaded && IsVisible && !Services.TestMode.Active;
        if (on == _animating) return;
        _animating = on;
        if (on) { _last = TimeSpan.Zero; CompositionTarget.Rendering += OnFrame; }
        else CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return;
        double dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.1, (now - _last).TotalSeconds);
        _last = now;
        Step(dt);
        InvalidateVisual();
    }

    private Sim? _sim;

    private void Restart()
    {
        _time = 0;
        _sim = Effect == null ? null : new Sim(Effect, PackRoot);
        if (_sim != null && !double.IsNaN(AtX) && !double.IsNaN(AtY)) _sim.MoveTo(AtX, AtY);
        if (_sim != null && Services.TestMode.Active)
            for (int i = 0; i < 72; i++) Step(1 / 60.0);
        InvalidateVisual();
    }

    private void Step(double dt)
    {
        _time += dt;
        _sim?.Step(dt);
    }

    /// <summary>How many particles are out now (tests).</summary>
    internal int Alive => _sim?.Count ?? 0;

    // ── Drawing ────────────────────────────────────────────────────────────────────────────────

    private static BitmapSource? _room;
    private static bool _roomLooked;

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Size(ActualWidth, ActualHeight);
        if (size.Width < 2 || size.Height < 2) return;
        var frame = new Rect(size);
        dc.PushClip(new RectangleGeometry(frame));
        if (Bare)
        {
            _sim?.Draw(dc, size);
            dc.Pop();
            return;
        }
        if (!_roomLooked)
        {
            _roomLooked = true;
            _room = LoadPicture(Path.Combine(AppContext.BaseDirectory, "Backdrops", "MyRoom", "room.png"));
        }
        if (_room != null) dc.DrawImage(_room, frame);
        else dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x2A, 0x24, 0x4A), Color.FromRgb(0x10, 0x0E, 0x1C), 90), null, frame);
        // A shade over the room, so a light effect reads on it as it does on the game's darker rooms.
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)), null, frame);
        _sim?.Draw(dc, size);
        dc.Pop();
    }

    // ── Pictures ───────────────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, BitmapSource?> Pictures = new(StringComparer.OrdinalIgnoreCase);

    internal static BitmapSource? LoadPicture(string path)
    {
        if (Pictures.TryGetValue(path, out var known)) return known;
        BitmapSource? made = null;
        try
        {
            if (File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                made = bmp;
            }
        }
        catch (Exception) { made = null; }
        Pictures[path] = made;
        return made;
    }

    /// <summary>One of the game's pictures, as the preview has it.</summary>
    internal static BitmapSource? GamePicture(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var slug = new System.Text.StringBuilder();
        foreach (char c in name) slug.Append(char.IsLetterOrDigit(c) ? c : '_');
        return LoadPicture(Path.Combine(AppContext.BaseDirectory, "VanillaFx", slug + ".png"));
    }

    private static readonly Dictionary<(BitmapSource, uint), BitmapSource> Tints = new();

    /// <summary>The picture times a colour (the game multiplies a particle's picture by its colour).</summary>
    internal static BitmapSource Tinted(BitmapSource src, Color c)
    {
        // Colours a little apart share a picture: a burst of a hundred shades would otherwise make a hundred.
        uint key = (uint)((c.R & 0xF0) << 16 | (c.G & 0xF0) << 8 | (c.B & 0xF0));
        if (key == 0xF0F0F0) return src;
        if (Tints.TryGetValue((src, key), out var known)) return known;
        if (Tints.Count > 400) Tints.Clear();
        var conv = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        conv.CopyPixels(px, stride, 0);
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = (byte)(px[i] * c.B / 255);
            px[i + 1] = (byte)(px[i + 1] * c.G / 255);
            px[i + 2] = (byte)(px[i + 2] * c.R / 255);
        }
        var made = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, stride);
        made.Freeze();
        Tints[(src, key)] = made;
        return made;
    }

    // ── The simulation ─────────────────────────────────────────────────────────────────────────

    private sealed class Particle
    {
        public double X, Y, Vx, Vy, Age, Life, Size, Rot, Spin;
        public Color Colour;
        public Point Out;
    }

    private sealed class Sim
    {
        private readonly EffectDef _def;
        private readonly FX.Template _t;
        private readonly EffectLook _look;
        private readonly List<Particle> _ps = new();
        private readonly Random _rng = new(1);
        private readonly BitmapSource? _picture;
        private readonly Color? _tint;
        private readonly double _amount, _size, _speed, _life, _scale, _gravity;
        private double _x, _y;

        /// <summary>Play it from here from now on (an object moved in the place preview).</summary>
        public void MoveTo(double x, double y)
        {
            double dx = x - _x, dy = y - _y;
            _x = x; _y = y;
            // What is already out moves with it, as a copy under a moved object does.
            foreach (var p in _ps) { p.X += dx * Aspect; p.Y += dy; }
        }
        private double _clock, _emitted, _cycle;
        private bool _burstDone;

        public int Count => _ps.Count;

        public Sim(EffectDef def, string? packRoot)
        {
            _def = def;
            _t = FX.Find(def.Template) ?? FX.Catalog[0];
            _look = EffectLook.For(_t);
            _amount = def.Amount ?? (_t.Kind == FX.Kind.Points && !def.ChangesGame ? FX.DefaultPointsCount : 1);
            _size = def.Size ?? 1;
            _speed = def.Speed ?? 1;
            _life = def.Lifetime ?? 1;
            _scale = def.Scale ?? 1;
            _gravity = def.Gravity.HasValue ? def.Gravity.Value * 9.81 / 10.8 : _look.Gravity;
            _x = def.X ?? _look.HomeX;
            _y = def.Y ?? _look.HomeY;
            _tint = ParseColour(def.Color);
            if (!string.IsNullOrEmpty(def.Texture) && !string.IsNullOrEmpty(packRoot))
                _picture = LoadPicture(Path.Combine(packRoot, def.Texture));
            _picture ??= GamePicture(def.GameTexture) ?? GamePicture(_look.Picture ?? _t.Texture);
            _cycle = _look.Loops ? 0 : _look.Replay;
        }

        public void Step(double dt)
        {
            _clock += dt;
            if (_t.Kind == FX.Kind.Flash || _t.Kind == FX.Kind.Popup)
            {
                if (_clock > Seconds + 0.9) _clock = 0;
                return;
            }
            if (!_look.Loops && _clock > _cycle) { _clock = 0; _burstDone = false; _ps.Clear(); }
            Emit(dt);
            for (int i = _ps.Count - 1; i >= 0; i--)
            {
                var p = _ps[i];
                p.Age += dt;
                if (p.Age >= p.Life) { _ps.RemoveAt(i); continue; }
                if (_t.Kind == FX.Kind.Points) continue;
                p.Vy -= _gravity * dt;
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                if (_look.Wobble > 0) p.X += Math.Sin((p.Age + p.Rot) * 2.3) * _look.Wobble * dt;
                p.Rot += p.Spin * dt;
            }
        }

        private double Seconds => _def.Seconds ?? (_t.Kind == FX.Kind.Flash ? FX.DefaultFlashSeconds : FX.DefaultPopupSeconds);

        private void Emit(double dt)
        {
            if (_t.Kind == FX.Kind.Points)
            {
                if (_burstDone) return;
                _burstDone = true;
                int n = (int)Math.Min(300, Math.Max(1, _def.ChangesGame ? FX.DefaultPointsCount * _amount : _amount));
                double spread = _def.Spread ?? FX.DefaultPointsSpread;
                for (int i = 0; i < n; i++)
                {
                    double a = _rng.NextDouble() * Math.PI * 2, far = 0.05 + _rng.NextDouble() * Math.Max(0, spread - 0.05);
                    _ps.Add(new Particle
                    {
                        X = _x * Aspect, Y = _y, Life = (_def.FloatSeconds ?? FX.DefaultFloatSeconds) + (_def.TravelSeconds ?? FX.DefaultTravelSeconds),
                        Out = new Point(_x * Aspect + Math.Cos(a) * far, _y + Math.Sin(a) * far), Size = 0.042 * _scale,
                    });
                }
                _cycle = _ps.Count > 0 ? _ps[0].Life + 0.8 : 3;
                return;
            }
            double seconds = _def.Seconds ?? 0;
            bool emitting = !(seconds > 0 && _look.Loops && _clock > seconds);
            if (!emitting) { if (_clock > seconds + _look.LifeMax * _life + 1) _clock = 0; return; }
            if (_look.BurstEvery > 0)
            {
                // Bursts again and again, each somewhere else (fireworks).
                if (Math.Floor(_clock / _look.BurstEvery) > Math.Floor((_clock - dt) / _look.BurstEvery) || _clock <= dt)
                {
                    double bx = 0.2 + _rng.NextDouble() * 0.6, by = 0.55 + _rng.NextDouble() * 0.35;
                    for (int i = 0; i < _look.Burst * _amount; i++) Spawn(bx, by);
                }
            }
            else if (!_burstDone && _look.Burst > 0)
            {
                _burstDone = true;
                for (int i = 0; i < _look.Burst * _amount; i++) Spawn();
            }
            _emitted += _look.Rate * _amount * dt;
            while (_emitted >= 1 && _ps.Count < 900) { _emitted -= 1; Spawn(); }
        }

        private const double Aspect = 16.0 / 9.0;

        private void Spawn(double? atX = null, double? atY = null)
        {
            double x = (atX ?? _x) * Aspect, y = atY ?? _y;
            double w = _look.SpawnWidth * _scale, h = _look.SpawnHeight * _scale;
            x += (_rng.NextDouble() - 0.5) * w;
            y += (_rng.NextDouble() - 0.5) * h;
            double speed = Lerp(_look.SpeedMin, _look.SpeedMax) * _speed * _scale;
            double angle = _look.Direction switch
            {
                Dir.Up => Math.PI / 2 + (_rng.NextDouble() - 0.5) * _look.Cone,
                Dir.Down => -Math.PI / 2 + (_rng.NextDouble() - 0.5) * _look.Cone,
                Dir.Left => Math.PI + (_rng.NextDouble() - 0.5) * _look.Cone,
                _ => _rng.NextDouble() * Math.PI * 2,
            };
            var colour = _look.ColourMin == _look.ColourMax ? _look.ColourMin : Mix(_look.ColourMin, _look.ColourMax, _rng.NextDouble());
            if (_tint is { } t) colour = Color.FromArgb((byte)(colour.A * t.A / 255), (byte)(colour.R * t.R / 255), (byte)(colour.G * t.G / 255), (byte)(colour.B * t.B / 255));
            _ps.Add(new Particle
            {
                X = x, Y = y,
                Vx = Math.Cos(angle) * speed, Vy = Math.Sin(angle) * speed,
                Life = Lerp(_look.LifeMin, _look.LifeMax) * _life,
                Size = Lerp(_look.SizeMin, _look.SizeMax) * _size * _scale,
                Rot = _look.Turn ? _rng.NextDouble() * 360 : 0,
                Spin = (_rng.NextDouble() - 0.5) * 2 * _look.Spin,
                Colour = colour,
            });
        }

        private double Lerp(double a, double b) => a + (b - a) * _rng.NextDouble();

        public void Draw(DrawingContext dc, Size size)
        {
            double h = size.Height;
            if (_t.Kind == FX.Kind.Flash)
            {
                double s = Seconds, k = _clock / s;
                double a = k < 0.15 ? k / 0.15 : k < 1 ? 1 - (k - 0.15) / 0.85 : 0;
                var c = _tint ?? Colors.White;
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(c.A * Math.Clamp(a, 0, 1)), c.R, c.G, c.B)), null, new Rect(size));
                return;
            }
            if (_t.Kind == FX.Kind.Popup)
            {
                double k = Math.Min(1, _clock / Seconds);
                if (_clock > Seconds) return;
                double alpha = k < 0.1 ? k / 0.1 : k > 0.65 ? 1 - (k - 0.65) / 0.35 : 1;
                var c = _tint ?? Colors.White;
                var fill = new SolidColorBrush(Color.FromArgb((byte)(c.A * alpha), c.R, c.G, c.B));
                var text = new FormattedText(string.IsNullOrEmpty(_def.Text) ? "+10" : _def.Text, CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI Black"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal),
                    Math.Max(6, h * 0.05 * (_def.Size ?? 1)), fill, 1.0);
                double px = _x * size.Width - text.Width / 2, py = (1 - (_y + 0.08 * k)) * h - text.Height / 2;
                var outline = text.BuildGeometry(new Point(px, py));
                dc.DrawGeometry(fill, new Pen(new SolidColorBrush(Color.FromArgb((byte)(160 * alpha), 0, 0, 0)), Math.Max(1, h / 220)), outline);
                return;
            }
            var picture = _picture;
            foreach (var p in _ps)
            {
                double k = p.Age / p.Life;
                double x, y, s;
                byte a;
                Color colour;
                if (_t.Kind == FX.Kind.Points)
                {
                    double floatS = _def.FloatSeconds ?? FX.DefaultFloatSeconds, travelS = _def.TravelSeconds ?? FX.DefaultTravelSeconds;
                    var end = new Point((_def.EndX ?? FX.PointsEndX) * Aspect, _def.EndY ?? FX.PointsEndY);
                    if (p.Age < floatS)
                    {
                        double f = p.Age / floatS;
                        x = p.X + (p.Out.X - p.X) * f; y = p.Y + (p.Out.Y - p.Y) * f;
                    }
                    else
                    {
                        double f = Math.Min(1, (p.Age - floatS) / travelS); f *= f;
                        x = p.Out.X + (end.X - p.Out.X) * f; y = p.Out.Y + (end.Y - p.Out.Y) * f;
                    }
                    s = p.Size; a = 255;
                    var icon = _t.Id == FX.FollowerPoints ? Color.FromRgb(0x8F, 0xDC, 0xFF) : Color.FromRgb(0xFF, 0x8F, 0xF8);
                    colour = _tint is { } t ? Color.FromRgb((byte)(icon.R * t.R / 255), (byte)(icon.G * t.G / 255), (byte)(icon.B * t.B / 255)) : icon;
                }
                else
                {
                    x = p.X; y = p.Y;
                    double grow = 1 + (_look.Grow - 1) * k;
                    s = p.Size * grow;
                    double fade = k < _look.FadeIn ? k / Math.Max(0.001, _look.FadeIn) : k > _look.FadeOut ? (1 - k) / Math.Max(0.001, 1 - _look.FadeOut) : 1;
                    a = (byte)(p.Colour.A * Math.Clamp(fade, 0, 1));
                    colour = p.Colour;
                }
                double px = x / Aspect * size.Width, py = (1 - y) * h, ps = Math.Max(1, s * h);
                if (picture == null)
                {
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, colour.R, colour.G, colour.B)), null, new Point(px, py), ps / 2, ps / 2);
                    continue;
                }
                double ratio = picture.PixelWidth / (double)Math.Max(1, picture.PixelHeight);
                double pw = ratio >= 1 ? ps * _look.Stretch : ps * ratio, ph = ratio >= 1 ? ps / ratio : ps;
                if (_look.Stretch > 1) { pw = ps * _look.Stretch; ph = ps * 0.35; }
                var img = Tinted(picture, Color.FromRgb(colour.R, colour.G, colour.B));
                dc.PushOpacity(a / 255.0 * (_def.Glow ? 0.85 : 1));
                bool turned = p.Rot != 0 || _look.AlongMotion;
                if (turned)
                {
                    double deg = _look.AlongMotion ? -Math.Atan2(p.Vy - _gravity * p.Age, p.Vx) * 180 / Math.PI : p.Rot;
                    dc.PushTransform(new RotateTransform(deg, px, py));
                }
                dc.DrawImage(img, new Rect(px - pw / 2, py - ph / 2, pw, ph));
                if (turned) dc.Pop();
                dc.Pop();
            }
        }
    }

    private static Color Mix(Color a, Color b, double t)
        => Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    internal static Color? ParseColour(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        return Services.ColorMath.TryParse(hex, out byte r, out byte g, out byte b, out byte a) ? Color.FromArgb(a, r, g, b) : null;
    }

    internal enum Dir { Random, Up, Down, Left }

    /// <summary>
    /// How a template's particles look and move in the preview, in the screen's height (a speed of 0.1 crosses a
    /// tenth of its height a second; y up). Read from the game's effects (the bridge's "particles" dumps, the
    /// game's 10.8-unit-high camera) and from ModForge's own ForgeShapes.
    /// </summary>
    internal sealed class EffectLook
    {
        public string? Picture;
        public bool Loops = true;
        public double HomeX = 0.5, HomeY = 0.5;
        public double SpawnWidth, SpawnHeight;
        public double Rate, Burst, BurstEvery;
        public double LifeMin = 1, LifeMax = 1;
        public double SpeedMin, SpeedMax;
        public Dir Direction = Dir.Random;
        public double Cone = Math.PI * 2;
        public double Gravity;
        public double SizeMin = 0.03, SizeMax = 0.05;
        public double Grow = 1;
        public double FadeIn, FadeOut = 0.7;
        public Color ColourMin = Colors.White, ColourMax = Colors.White;
        public double Spin;
        public bool Turn;
        public double Wobble;
        public double Stretch = 1;
        public bool AlongMotion;
        public double Replay = 2.5;

        private static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        public static EffectLook For(FX.Template t) => t.Name switch
        {
            "burst" => new() { Loops = false, Burst = 24, LifeMin = 0.7, LifeMax = 1.2, SpeedMin = 0.23, SpeedMax = 0.46, Gravity = 0.45, SizeMin = 0.023, SizeMax = 0.046, Grow = 0.7, FadeOut = 0.6, Spin = 1.5, Turn = true },
            "rising" => new() { Rate = 4, LifeMin = 2.5, LifeMax = 4, SpeedMin = 0.074, SpeedMax = 0.13, Direction = Dir.Up, Cone = 0.25, SpawnWidth = 0.28, SizeMin = 0.023, SizeMax = 0.046, Grow = 1.6, FadeIn = 0.15, FadeOut = 0.7, Wobble = 0.03 },
            "falling" => new() { Rate = 20, HomeY = 1.04, LifeMin = 8, LifeMax = 11, SpeedMin = 0.093, SpeedMax = 0.167, Direction = Dir.Down, Cone = 0.25, SpawnWidth = 1.95, SizeMin = 0.0065, SizeMax = 0.017, FadeIn = 0.05, FadeOut = 0.9, Spin = 0.8, Turn = true, Wobble = 0.04 },
            "drifting" => new() { Rate = 3, LifeMin = 5, LifeMax = 8, SpeedMin = 0.005, SpeedMax = 0.025, SpawnWidth = 1.57, SpawnHeight = 0.79, SizeMin = 0.014, SizeMax = 0.042, FadeIn = 0.2, FadeOut = 0.8, ColourMin = C(0xCCFFFFFF), ColourMax = C(0xCCFFFFFF), Wobble = 0.02 },
            "points" or "followerPoints" or "subscriberPoints" => new() { Loops = false, HomeX = FX.PointsStartX, HomeY = FX.PointsStartY },
            "flash" or "popup" => new() { Loops = false, HomeY = t.Name == "popup" ? 0.55 : 0.5 },

            "heartShower" => new() { Loops = false, Burst = 60, HomeY = 0.02, SpawnWidth = 1.8, LifeMin = 2, LifeMax = 3, SpeedMin = 0.2, SpeedMax = 0.4, Direction = Dir.Up, Cone = 0.4, SizeMin = 0.03, SizeMax = 0.07, FadeOut = 0.6, ColourMin = C(0xFFFF9AD6), ColourMax = C(0xFFFFC2E8), Replay = 3.5 },
            "giftHearts" => new() { Loops = false, Burst = 30, LifeMin = 0.8, LifeMax = 1.3, SpeedMin = 0.15, SpeedMax = 0.35, Gravity = 0.25, SizeMin = 0.025, SizeMax = 0.05, FadeOut = 0.6, ColourMin = C(0xFFFF6FB5), ColourMax = C(0xFFFFB3DA) },
            "loveHeart" => new() { Loops = false, Burst = 3, LifeMin = 1, LifeMax = 1.4, SpeedMin = 0.02, SpeedMax = 0.05, Direction = Dir.Up, Cone = 0.6, SizeMin = 0.03, SizeMax = 0.045, FadeOut = 0.6, ColourMin = C(0xFFFF4F6E), ColourMax = C(0xFFFF4F6E) },
            "sparkleRing" => new() { Loops = false, Burst = 10, LifeMin = 0.6, LifeMax = 1, SpeedMin = 0.03, SpeedMax = 0.08, SizeMin = 0.02, SizeMax = 0.04, FadeOut = 0.5, ColourMin = C(0xFFFFF2B0), ColourMax = C(0xFFFFFFFF), Spin = 2, Turn = true },
            "bigHeart" => new() { Loops = false, Burst = 1, LifeMin = 1, LifeMax = 1, SizeMin = 0.18, SizeMax = 0.18, Grow = 1.5, FadeOut = 0.5, ColourMin = C(0xFFE36AB0), ColourMax = C(0xFFE36AB0) },

            "campfire" => new() { Rate = 14, HomeY = 0.3, LifeMin = 0.4, LifeMax = 0.7, SpeedMin = 0.05, SpeedMax = 0.09, Direction = Dir.Up, Cone = 0.3, SpawnWidth = 0.03, SizeMin = 0.12, SizeMax = 0.16, Grow = 0.6, FadeIn = 0.15, FadeOut = 0.6, ColourMin = C(0xFFFF8A3A), ColourMax = C(0xFFFFD27A) },
            "stoveFlame" => new() { Rate = 14, LifeMin = 0.3, LifeMax = 0.5, SpeedMin = 0.04, SpeedMax = 0.07, Direction = Dir.Up, Cone = 0.4, SpawnWidth = 0.08, SizeMin = 0.07, SizeMax = 0.1, Grow = 0.6, FadeOut = 0.6, ColourMin = C(0xFFFF8A3A), ColourMax = C(0xFFFFD27A) },
            "candle" => new() { Rate = 6, LifeMin = 0.4, LifeMax = 0.6, SpeedMin = 0.005, SpeedMax = 0.01, Direction = Dir.Up, Cone = 0.2, SizeMin = 0.012, SizeMax = 0.016, FadeIn = 0.2, FadeOut = 0.7, ColourMin = C(0xFFFFB04A), ColourMax = C(0xFFFFE0A0) },
            "lightOrbs" => new() { Rate = 1.5, LifeMin = 4, LifeMax = 6, SpeedMin = 0.01, SpeedMax = 0.03, SpawnWidth = 1.6, SpawnHeight = 0.6, HomeY = 0.65, SizeMin = 0.15, SizeMax = 0.35, FadeIn = 0.3, FadeOut = 0.6, ColourMin = C(0x80A040FF), ColourMax = C(0x804060FF) },
            "halo" => new() { Rate = 20, LifeMin = 1, LifeMax = 1.5, SpeedMin = 0.01, SpeedMax = 0.02, SpawnWidth = 0.16, SpawnHeight = 0.04, SizeMin = 0.008, SizeMax = 0.014, FadeOut = 0.6, ColourMin = C(0xFFFF6A80), ColourMax = C(0xFFFFB0C0) },

            "rain" => new() { Rate = 160, HomeY = 1.05, SpawnWidth = 2.1, LifeMin = 0.6, LifeMax = 0.9, SpeedMin = 1.6, SpeedMax = 2.6, Direction = Dir.Down, Cone = 0.15, SizeMin = 0.012, SizeMax = 0.02, Stretch = 4, AlongMotion = true, FadeOut = 0.9, ColourMin = C(0x90B0D8FF), ColourMax = C(0xB0E0F0FF) },
            "snow" => new() { Rate = 40, HomeY = 1.04, SpawnWidth = 2.0, LifeMin = 8, LifeMax = 12, SpeedMin = 0.06, SpeedMax = 0.11, Direction = Dir.Down, Cone = 0.6, SizeMin = 0.005, SizeMax = 0.011, Wobble = 0.03, FadeIn = 0.05, FadeOut = 0.92 },
            "fog" => new() { Rate = 1.2, HomeY = 0.75, SpawnWidth = 1.9, SpawnHeight = 0.3, LifeMin = 8, LifeMax = 12, SpeedMin = 0.01, SpeedMax = 0.025, Direction = Dir.Left, Cone = 0.3, SizeMin = 0.35, SizeMax = 0.6, FadeIn = 0.3, FadeOut = 0.6, ColourMin = C(0x50FFFFFF), ColourMax = C(0x70FFFFFF) },
            "chimneySmoke" => new() { Rate = 3, LifeMin = 3, LifeMax = 4, SpeedMin = 0.03, SpeedMax = 0.05, Direction = Dir.Up, Cone = 0.3, SizeMin = 0.03, SizeMax = 0.04, Grow = 3, FadeIn = 0.1, FadeOut = 0.5, ColourMin = C(0x90B0B0B8), ColourMax = C(0x90D0D0D8), Wobble = 0.02, Turn = true },
            "speedLines" => new() { Rate = 25, HomeX = 1.05, SpawnHeight = 1.0, LifeMin = 0.4, LifeMax = 0.7, SpeedMin = 1.2, SpeedMax = 2, Direction = Dir.Left, Cone = 0.05, SizeMin = 0.01, SizeMax = 0.015, Stretch = 8, AlongMotion = true, FadeIn = 0.2, FadeOut = 0.6, ColourMin = C(0x80FFFFFF), ColourMax = C(0xC0FFFFFF) },
            "bubbles" => new() { Rate = 6, HomeY = 0.05, SpawnWidth = 1.6, LifeMin = 4, LifeMax = 6, SpeedMin = 0.06, SpeedMax = 0.12, Direction = Dir.Up, Cone = 0.2, SizeMin = 0.012, SizeMax = 0.03, Wobble = 0.03, FadeIn = 0.1, FadeOut = 0.85 },
            "ripples" => new() { Rate = 1.5, HomeY = 0.2, SpawnWidth = 0.3, SpawnHeight = 0.02, LifeMin = 1.5, LifeMax = 2, SizeMin = 0.02, SizeMax = 0.03, Grow = 5, FadeIn = 0.05, FadeOut = 0.2, Picture = "fx_circle_line2", ColourMin = C(0x90D0E8FF), ColourMax = C(0x90D0E8FF) },
            "drip" => new() { Rate = 1.2, HomeY = 0.6, LifeMin = 1, LifeMax = 1.2, Gravity = 0.25, SizeMin = 0.008, SizeMax = 0.01, Stretch = 2.5, AlongMotion = true, FadeOut = 0.85, ColourMin = C(0xD0F0F8FF), ColourMax = C(0xD0F0F8FF) },
            "seagulls" => new() { Rate = 0.6, HomeX = 1.05, HomeY = 0.8, SpawnHeight = 0.3, LifeMin = 14, LifeMax = 18, SpeedMin = 0.07, SpeedMax = 0.12, Direction = Dir.Left, Cone = 0.2, SizeMin = 0.015, SizeMax = 0.025, ColourMin = C(0xFF303040), ColourMax = C(0xFF303040), FadeIn = 0.05, FadeOut = 0.95 },
            "leaves" => new() { Rate = 4, HomeY = 1.04, SpawnWidth = 2.0, LifeMin = 8, LifeMax = 11, SpeedMin = 0.08, SpeedMax = 0.14, Direction = Dir.Down, Cone = 0.6, SizeMin = 0.012, SizeMax = 0.022, Spin = 1.5, Turn = true, Wobble = 0.06, FadeIn = 0.05, FadeOut = 0.92 },

            "portal" => new() { Rate = 6, LifeMin = 0.6, LifeMax = 0.9, SizeMin = 0.35, SizeMax = 0.38, Grow = 0.85, FadeIn = 0.2, FadeOut = 0.5, Spin = 2, Turn = true, Picture = "fx_circle_line2", ColourMin = C(0xFFE060FF), ColourMax = C(0xFFFF80E0) },
            "goldenAura" => new() { Rate = 25, LifeMin = 1, LifeMax = 2, SpeedMin = 0.01, SpeedMax = 0.04, SpawnWidth = 0.3, SpawnHeight = 0.3, SizeMin = 0.006, SizeMax = 0.012, FadeIn = 0.2, FadeOut = 0.6, ColourMin = C(0xFFFFD060), ColourMax = C(0xFFFFF0B0), Picture = "fx_glow1" },
            "magicBurst" => new() { Loops = false, Burst = 60, LifeMin = 0.6, LifeMax = 1.2, SpeedMin = 0.05, SpeedMax = 0.25, Direction = Dir.Up, Cone = 1.6, SizeMin = 0.01, SizeMax = 0.03, FadeOut = 0.5, ColourMin = C(0xFFFF60E0), ColourMax = C(0xFFFFC0F8) },
            "darkBats" => new() { Loops = false, Burst = 14, LifeMin = 1.2, LifeMax = 2, SpeedMin = 0.1, SpeedMax = 0.25, Direction = Dir.Up, Cone = 2.2, SizeMin = 0.03, SizeMax = 0.05, FadeOut = 0.7 },
            "magicPoof" => new() { Loops = false, Burst = 20, LifeMin = 0.5, LifeMax = 0.9, SpeedMin = 0.08, SpeedMax = 0.2, SizeMin = 0.012, SizeMax = 0.025, FadeOut = 0.5, Spin = 3, Turn = true },
            "electricBurst" => new() { Loops = false, Burst = 25, LifeMin = 0.3, LifeMax = 0.6, SpeedMin = 0.1, SpeedMax = 0.3, SizeMin = 0.02, SizeMax = 0.05, FadeOut = 0.4, ColourMin = C(0xFF60A0FF), ColourMax = C(0xFFC0E8FF), Spin = 4, Turn = true },
            "souls" => new() { Rate = 2, SpawnWidth = 0.8, SpawnHeight = 0.4, LifeMin = 3, LifeMax = 5, SpeedMin = 0.02, SpeedMax = 0.05, SizeMin = 0.015, SizeMax = 0.03, Wobble = 0.05, FadeIn = 0.3, FadeOut = 0.6, ColourMin = C(0xC060C0FF), ColourMax = C(0xC0A0E8FF), Picture = "fx_glow1" },
            "sparks" => new() { Rate = 8, HomeY = 0.75, LifeMin = 1.5, LifeMax = 2, SpeedMin = 0.03, SpeedMax = 0.15, Direction = Dir.Down, Cone = 2.4, Gravity = 0.36, SizeMin = 0.004, SizeMax = 0.008, Stretch = 3, AlongMotion = true, FadeOut = 0.6, ColourMin = C(0xFFFFC080), ColourMax = C(0xFFFFFFFF) },
            "gasLeak" => new() { Rate = 6, LifeMin = 1.5, LifeMax = 2.5, SpeedMin = 0.05, SpeedMax = 0.09, Direction = Dir.Up, Cone = 0.8, SizeMin = 0.03, SizeMax = 0.05, Grow = 2.5, FadeIn = 0.1, FadeOut = 0.4, ColourMin = C(0x70C0F0C0), ColourMax = C(0x70E0FFE0), Turn = true },

            "fireworks" => new() { Loops = true, Burst = 40, BurstEvery = 1.4, LifeMin = 1, LifeMax = 1.6, SpeedMin = 0.1, SpeedMax = 0.22, Gravity = 0.08, SizeMin = 0.006, SizeMax = 0.012, FadeOut = 0.5, ColourMin = C(0xFFFF5070), ColourMax = C(0xFF70C0FF) },
            "paperBurst" => new() { Loops = false, Burst = 80, HomeY = 0.6, LifeMin = 1.5, LifeMax = 2.5, SpeedMin = 0.2, SpeedMax = 0.55, Gravity = 0.4, SizeMin = 0.02, SizeMax = 0.045, FadeOut = 0.7, Spin = 4, Turn = true, Replay = 3.5 },
            "fightCloud" => new() { Rate = 10, LifeMin = 0.6, LifeMax = 1, SpeedMin = 0.03, SpeedMax = 0.09, SpawnWidth = 0.4, SpawnHeight = 0.2, SizeMin = 0.12, SizeMax = 0.22, Grow = 1.3, FadeIn = 0.1, FadeOut = 0.6, Spin = 1, Turn = true },
            _ => new(),
        };
    }
}
