using System;
using System.Collections.Generic;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// The arrival animation, for whatever owns one - a whole screen or a single
/// object inside it.
/// <para/>
/// One view model for both, because it is the same moment either way: an object
/// a condition switches on is arriving exactly as a screen does, one level
/// down. The owner hands in how to read and write its own field, and everything
/// else is shared.
/// </summary>
public sealed class UiOpenViewModel : ObservableObject
{
    private readonly Func<UiOpenDef?> _get;
    private readonly Action<UiOpenDef?> _set;
    private readonly Action? _changed;

    /// <summary>The object this animation belongs to - a screen's root, or the
    /// object itself. The preview needs it to know what to move.</summary>
    public Func<UiNodeDef?> Target { get; }

    /// <summary>What is stored, for whoever is playing it back.</summary>
    public UiOpenDef? Def => _get();

    /// <summary>Whether this is the way out rather than the way in. It changes
    /// which direction the preview plays and how the summary reads; the stored
    /// fields are the same either way.</summary>
    public bool Closing { get; }

    public UiOpenViewModel(Func<UiOpenDef?> get, Action<UiOpenDef?> set,
                           Func<UiNodeDef?> target, bool closing = false,
                           Action? changed = null)
    {
        _get = get;
        _set = set;
        Target = target;
        Closing = closing;
        _changed = changed;
    }

    public static IReadOnlyList<string> Easings => UiOpenDef.Easings;

    /// <summary>
    /// Whether this thing animates at all. Ticking it starts from the game's
    /// own shop - fade in while unfolding from flat, over 0.3s - because that
    /// is the animation someone reaching for this has most likely seen, and it
    /// is a better starting point than an empty one that does nothing.
    /// </summary>
    public bool Animates
    {
        get => _get() != null;
        set
        {
            if (value == Animates) return;
            _set(value ? (Closing ? UiOpenDef.LikeTheGameClosing() : UiOpenDef.LikeTheGame())
                       : null);
            Bubble();
        }
    }

    public bool Fade
    {
        get => _get()?.Fade ?? false;
        set { var d = Ensure(); if (d.Fade == value) return; d.Fade = value; Bubble(); }
    }

    /// <summary>Whether it changes size on the way in. Off leaves the size
    /// alone, which is a fade on its own.</summary>
    public bool Grows
    {
        get => _get()?.ScaleFrom != null;
        set
        {
            var d = Ensure();
            if ((d.ScaleFrom != null) == value) return;
            d.ScaleFrom = value ? new[] { 1f, 0f, 1f } : null;
            Bubble();
        }
    }

    public double ScaleFromX
    {
        get => Axis(0);
        set => SetAxis(0, value);
    }

    public double ScaleFromY
    {
        get => Axis(1);
        set => SetAxis(1, value);
    }

    public double ScaleFromZ
    {
        get => Axis(2);
        set => SetAxis(2, value);
    }

    public double Duration
    {
        get => _get()?.Duration ?? 0.3;
        set
        {
            var d = Ensure();
            float f = (float)Math.Max(0, value);
            if (Math.Abs(d.Duration - f) < 0.0001f) return;
            d.Duration = f;
            Bubble();
        }
    }

    public string Easing
    {
        get => _get()?.Easing ?? UiOpenDef.QuadInOut;
        set
        {
            var d = Ensure();
            if (d.Easing == value) return;
            d.Easing = string.IsNullOrEmpty(value) ? UiOpenDef.QuadInOut : value;
            Bubble();
        }
    }

    /// <summary>A summary for a header, so a collapsed section still says what
    /// it is doing.</summary>
    public string Summary
    {
        get
        {
            var d = _get();
            if (d == null || !d.DoesAnything)
                return Closing ? "vanishes at once" : "appears at once";

            string what = Closing
                ? (d.Fade && d.ScaleFrom != null ? "fades and shrinks"
                 : d.Fade ? "fades out" : "shrinks")
                : (d.Fade && d.ScaleFrom != null ? "fades and grows"
                 : d.Fade ? "fades in" : "grows");

            return $"{what} over {d.Duration:0.##}s, {d.Easing}";
        }
    }

    private double Axis(int i)
    {
        var from = _get()?.ScaleFrom;
        return from != null && from.Length > i ? from[i] : 1;
    }

    private void SetAxis(int i, double value)
    {
        var d = Ensure();
        if (d.ScaleFrom == null) d.ScaleFrom = new[] { 1f, 1f, 1f };
        if (d.ScaleFrom.Length <= i) return;
        if (Math.Abs(d.ScaleFrom[i] - value) < 0.0001) return;
        d.ScaleFrom[i] = (float)value;
        Bubble();
    }

    private UiOpenDef Ensure()
    {
        var d = _get();
        if (d == null)
        {
            d = Closing ? UiOpenDef.LikeTheGameClosing() : UiOpenDef.LikeTheGame();
            _set(d);
        }
        return d;
    }

    /// <summary>Everything here changes everything else's answer, so the whole
    /// panel is refreshed rather than each property naming its neighbours.</summary>
    private void Bubble()
    {
        OnPropertyChanged(nameof(Animates));
        OnPropertyChanged(nameof(Fade));
        OnPropertyChanged(nameof(Grows));
        OnPropertyChanged(nameof(ScaleFromX));
        OnPropertyChanged(nameof(ScaleFromY));
        OnPropertyChanged(nameof(ScaleFromZ));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(Easing));
        OnPropertyChanged(nameof(Summary));
        _changed?.Invoke();
    }
}
