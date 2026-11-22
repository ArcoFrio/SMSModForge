using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SMSModForge.Services;

namespace SMSModForge.View.Controls;

/// <summary>
/// A picture at full size over the window (1.10.0, the author: "add the possibility to click on them to open full
/// size with zoom in/out"): the guide's pictures open in it. It opens at the size that fits - never larger than the
/// picture itself - and the wheel zooms at the pointer, a drag moves it, a double-click goes between fitting and the
/// picture's own size. Keys: + and − zoom, 0 fits, 1 is its own size, Esc closes; so does a click beside the picture.
/// <para/>
/// Inside the window, not a window of its own: nothing to guard under the test harness, nothing on the taskbar, and
/// the editor stays where it was underneath. Drawn by <c>Theme.PictureViewerTemplate</c> - App.xaml's for Classic,
/// Ex.xaml's for EX, the same parts drawn their own ways. Its movements take <see cref="Motion"/>'s timing: it comes in
/// as a page does, and a zoom from a button or a double-click glides there (the wheel's is at once, as a wheel's
/// should be).
/// </summary>
[TemplatePart(Name = "PART_Stage", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_Image", Type = typeof(Image))]
[TemplatePart(Name = "PART_Root", Type = typeof(FrameworkElement))]
public sealed class PictureViewer : Control
{
    public static readonly RoutedCommand ZoomIn = new(nameof(ZoomIn), typeof(PictureViewer));
    public static readonly RoutedCommand ZoomOut = new(nameof(ZoomOut), typeof(PictureViewer));
    public static readonly RoutedCommand Fit = new(nameof(Fit), typeof(PictureViewer));
    public static readonly RoutedCommand ActualSize = new(nameof(ActualSize), typeof(PictureViewer));
    public static readonly RoutedCommand Close = new(nameof(Close), typeof(PictureViewer));

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(PictureViewer), new PropertyMetadata(null));
    public ImageSource? Source { get => (ImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(PictureViewer), new PropertyMetadata(""));
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(PictureViewer), new PropertyMetadata(false, (d, _) => ((PictureViewer)d).OpenedOrClosed()));
    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    private static readonly DependencyPropertyKey ZoomTextKey = DependencyProperty.RegisterReadOnly(
        nameof(ZoomText), typeof(string), typeof(PictureViewer), new PropertyMetadata("100 %"));
    public static readonly DependencyProperty ZoomTextProperty = ZoomTextKey.DependencyProperty;
    /// <summary>How big the picture is drawn against its own size, as the toolbar shows it.</summary>
    public string ZoomText => (string)GetValue(ZoomTextProperty);

    private FrameworkElement? _stage, _root;
    private Image? _image;
    private readonly MatrixTransform _place = new();
    private double _scale = 1, _x, _y;                 // where the picture is drawn: its pixel p at _scale * p + (_x, _y)
    private double _toScale = 1, _toX, _toY;           // where a glide is going
    private double _fromScale, _fromX, _fromY;
    private TimeSpan _glideStart, _glideLength;
    private bool _gliding;
    private Point? _dragFrom;
    private bool _dragged;
    private IInputElement? _opener;

    public const double Most = 8;

    public PictureViewer()
    {
        Visibility = Visibility.Collapsed;
        Focusable = true;
        FocusVisualStyle = null;
        CommandBindings.Add(new CommandBinding(ZoomIn, (_, _) => ZoomBy(1.25, null, glide: true)));
        CommandBindings.Add(new CommandBinding(ZoomOut, (_, _) => ZoomBy(1 / 1.25, null, glide: true)));
        CommandBindings.Add(new CommandBinding(Fit, (_, _) => GoTo(FitScale, glide: true)));
        CommandBindings.Add(new CommandBinding(ActualSize, (_, _) => GoTo(1, glide: true)));
        CommandBindings.Add(new CommandBinding(Close, (_, _) => IsOpen = false));
    }

    /// <summary>The keys, read here: a key gesture takes no bare digit or plus.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
        RoutedCommand? command = e.Key switch
        {
            Key.Escape => Close,
            Key.OemPlus or Key.Add => ZoomIn,
            Key.OemMinus or Key.Subtract => ZoomOut,
            Key.D0 or Key.NumPad0 => Fit,
            Key.D1 or Key.NumPad1 => ActualSize,
            _ => null,
        };
        if (command == null) return;
        command.Execute(null, this);
        e.Handled = true;
    }

    /// <summary>Open on a picture, from the control that opened it (the keyboard goes back there on closing).</summary>
    public void Open(ImageSource picture, string? caption, IInputElement? opener = null)
    {
        _opener = opener;
        Source = picture;
        Caption = caption ?? "";
        if (IsOpen) { UpdateLayout(); GoTo(FitScale, glide: false); }
        else IsOpen = true;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_stage != null)
        {
            _stage.SizeChanged -= StageResized;
            _stage.MouseWheel -= Wheel;
            _stage.MouseLeftButtonDown -= Down;
            _stage.MouseMove -= Moved;
            _stage.MouseLeftButtonUp -= Up;
        }
        _stage = GetTemplateChild("PART_Stage") as FrameworkElement;
        _image = GetTemplateChild("PART_Image") as Image;
        _root = GetTemplateChild("PART_Root") as FrameworkElement;
        if (_image != null)
        {
            _image.Stretch = Stretch.None;
            _image.HorizontalAlignment = HorizontalAlignment.Left;
            _image.VerticalAlignment = VerticalAlignment.Top;
            _image.RenderTransform = _place;
        }
        if (_stage == null) return;
        _stage.SizeChanged += StageResized;
        _stage.MouseWheel += Wheel;
        _stage.MouseLeftButtonDown += Down;
        _stage.MouseMove += Moved;
        _stage.MouseLeftButtonUp += Up;
    }

    private void OpenedOrClosed()
    {
        Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!IsOpen)
        {
            StopGlide();
            if (_opener is UIElement { IsVisible: true } back) back.Focus();
            _opener = null;
            return;
        }
        ApplyTemplate();
        UpdateLayout();
        GoTo(FitScale, glide: false);
        Focus();
        Motion.Enter(_root);
    }

    private Size Picture => Source is { } s ? new Size(Math.Max(1, s.Width), Math.Max(1, s.Height)) : new Size(1, 1);
    private Size Stage => _stage is { ActualWidth: > 0 } st ? new Size(st.ActualWidth, st.ActualHeight) : new Size(1, 1);

    /// <summary>The size that shows all of the picture, never larger than its own.</summary>
    public double FitScale => Math.Min(1, Math.Min(Stage.Width / Picture.Width, Stage.Height / Picture.Height));
    private double Least => Math.Min(FitScale, 1) * 0.5;

    /// <summary>How big the picture is drawn against its own size.</summary>
    public double Scale => _scale;

    private void StageResized(object sender, SizeChangedEventArgs e)
    {
        if (!IsOpen) return;
        // Kept fitting when it fitted; otherwise kept where it is, inside the bounds.
        if (Math.Abs(_toScale - FitOf(e.PreviousSize)) < 1e-6) GoTo(FitScale, glide: false);
        else { var t = Bounded(_toScale, _toX, _toY); Set(_toScale, t.X, t.Y, glide: false); }
    }

    private double FitOf(Size stage) => stage.Width <= 0 ? -1 : Math.Min(1, Math.Min(stage.Width / Picture.Width, stage.Height / Picture.Height));

    private void GoTo(double scale, bool glide)
    {
        scale = Math.Clamp(scale, Least, Most);
        var t = Bounded(scale, (Stage.Width - scale * Picture.Width) / 2, (Stage.Height - scale * Picture.Height) / 2);
        Set(scale, t.X, t.Y, glide);
    }

    /// <summary>Zoom by a factor about a point of the stage (its middle when null), that point staying put.</summary>
    private void ZoomBy(double factor, Point? about, bool glide)
    {
        var at = about ?? new Point(Stage.Width / 2, Stage.Height / 2);
        double from = _toScale, to = Math.Clamp(from * factor, Least, Most);
        if (Math.Abs(to - from) < 1e-9) return;
        double x = at.X - (at.X - _toX) * to / from, y = at.Y - (at.Y - _toY) * to / from;
        var t = Bounded(to, x, y);
        Set(to, t.X, t.Y, glide);
    }

    /// <summary>Where the picture may stand at a size: centred along a side it does not fill, never pulled in past an
    /// edge along one it does.</summary>
    private Point Bounded(double scale, double x, double y)
    {
        double w = scale * Picture.Width, h = scale * Picture.Height;
        x = w <= Stage.Width ? (Stage.Width - w) / 2 : Math.Clamp(x, Stage.Width - w, 0);
        y = h <= Stage.Height ? (Stage.Height - h) / 2 : Math.Clamp(y, Stage.Height - h, 0);
        return new Point(x, y);
    }

    private void Set(double scale, double x, double y, bool glide)
    {
        _toScale = scale; _toX = x; _toY = y;
        if (!glide || !Motion.Enabled) { StopGlide(); Draw(scale, x, y); return; }
        _fromScale = _scale; _fromX = _x; _fromY = _y;
        _glideLength = TimeSpan.FromMilliseconds(ThemeManager.Current.IsEx ? 220 : 140);
        _glideStart = TimeSpan.MinValue;
        if (!_gliding) { _gliding = true; CompositionTarget.Rendering += Glide; }
    }

    private void Glide(object? sender, EventArgs e)
    {
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        if (_glideStart == TimeSpan.MinValue) _glideStart = now;
        double t = Math.Clamp((now - _glideStart).TotalMilliseconds / _glideLength.TotalMilliseconds, 0, 1);
        // Classic eases out; EX eases out with a little overshoot.
        double k = ThemeManager.Current.IsEx
            ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 }.Ease(t)
            : 1 - Math.Pow(1 - t, 3);
        Draw(_fromScale + (_toScale - _fromScale) * k, _fromX + (_toX - _fromX) * k, _fromY + (_toY - _fromY) * k);
        if (t >= 1) { StopGlide(); Draw(_toScale, _toX, _toY); }
    }

    private void StopGlide()
    {
        if (!_gliding) return;
        _gliding = false;
        CompositionTarget.Rendering -= Glide;
    }

    private void Draw(double scale, double x, double y)
    {
        _scale = scale; _x = x; _y = y;
        // At its own size on whole pixels: as sharp as the editor itself.
        if (Math.Abs(scale - 1) < 1e-6) { x = Math.Round(x); y = Math.Round(y); }
        _place.Matrix = new Matrix(scale, 0, 0, scale, x, y);
        if (_image != null)
            RenderOptions.SetBitmapScalingMode(_image, scale >= 2 ? BitmapScalingMode.NearestNeighbor
                                                       : scale > 1 ? BitmapScalingMode.Linear : BitmapScalingMode.HighQuality);
        SetValue(ZoomTextKey, Math.Round(scale * 100) + " %");
    }

    private bool OnPicture(Point p)
        => p.X >= _x && p.Y >= _y && p.X <= _x + _scale * Picture.Width && p.Y <= _y + _scale * Picture.Height;

    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        ZoomBy(Math.Pow(1.2, e.Delta / 120.0), e.GetPosition(_stage), glide: false);
        e.Handled = true;
    }

    private void Down(object sender, MouseButtonEventArgs e)
    {
        var at = e.GetPosition(_stage);
        if (e.ClickCount == 2 && OnPicture(at))
        {
            // Between fitting and the picture's own size, about the point clicked.
            if (Math.Abs(_toScale - 1) < 1e-6 && FitScale < 1) GoTo(FitScale, glide: true);
            else ZoomBy(1 / _toScale, at, glide: true);
            _dragFrom = null;
            e.Handled = true;
            return;
        }
        _dragFrom = at;
        _dragged = false;
        _stage!.CaptureMouse();
        e.Handled = true;
    }

    private void Moved(object sender, MouseEventArgs e)
    {
        if (_dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(_stage);
        var by = at - from;
        if (!_dragged && Math.Abs(by.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(by.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragged = true;
        StopGlide();
        var t = Bounded(_toScale, _toX + by.X, _toY + by.Y);
        _dragFrom = at;
        Set(_toScale, t.X, t.Y, glide: false);
    }

    private void Up(object sender, MouseButtonEventArgs e)
    {
        var from = _dragFrom;
        _dragFrom = null;
        _stage?.ReleaseMouseCapture();
        // A click beside the picture closes it, as a click outside a lightbox does.
        if (from is { } at && !_dragged && !OnPicture(at)) IsOpen = false;
    }
}
