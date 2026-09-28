using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SMSModForge.Localization;

namespace SMSModForge.View;

/// <summary>
/// Language ▸ Edit texts on screen: a pencil beside every text of the editor's
/// that is showing, which opens it to be written in the author's own words
/// (<see cref="UiTextEditWindow"/>, saved by <see cref="UiTextEdits"/>).
/// <para/>
/// Drawn on the window's adorner layer rather than put into the layout, so
/// turning it on moves nothing and turning it off leaves nothing behind. A
/// click anywhere but on a pencil goes through to the editor as usual.
/// <para/>
/// Which text an element shows is read from its bindings: every text of the
/// editor's is a live binding to <see cref="LocSource"/> (<c>{l:T key}</c>, or
/// <see cref="LocText"/> in code), or goes through a converter given its key.
/// So nothing about how a text is written had to change for this, and a text
/// added tomorrow has a pencil the day it is added. What is worked out in code
/// and set as plain words - a message, a status line - has none; "Find a text
/// to edit" reaches those.
/// </summary>
public sealed class UiTextMarks : Adorner
{
    /// <summary>One text on screen, by key, and which part of its element it is.</summary>
    public sealed record Found(string Key, string Part);

    private sealed record Mark(Rect Pencil, Rect Target, IReadOnlyList<Found> Texts);

    private static readonly Dictionary<Window, UiTextMarks> On = new();

    private readonly Window _window;
    private readonly UIElement _root;
    private readonly DispatcherTimer _later;
    private List<Mark> _marks = new();
    private Mark? _hover;

    /// <summary>How big a pencil is, in the window's units.</summary>
    private const double Size = 14;

    private UiTextMarks(Window window, UIElement root) : base(root)
    {
        _window = window;
        _root = root;
        IsHitTestVisible = true;
        _later = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _later.Tick += (_, _) => { _later.Stop(); Find(); };
    }

    /// <summary>Whether the pencils are on for <paramref name="window"/>.</summary>
    public static bool IsOn(Window window) => On.ContainsKey(window);

    /// <summary>Put the pencils on, or take them off.</summary>
    public static void Set(Window window, bool on)
    {
        if (on == IsOn(window)) return;
        if (!on)
        {
            var marks = On[window];
            On.Remove(window);
            marks.Detach();
            return;
        }
        if (window.Content is not UIElement root) return;
        var layer = AdornerLayer.GetAdornerLayer(root);
        if (layer == null) return;
        var added = new UiTextMarks(window, root);
        layer.Add(added);
        On[window] = added;
        added.Attach();
        added.Find();
    }

    private void Attach()
    {
        _window.LayoutUpdated += Soon;
        _window.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled), handledEventsToo: true);
        Loc.Changed += LanguageChanged;
    }

    private void Detach()
    {
        _later.Stop();
        _window.LayoutUpdated -= Soon;
        _window.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
        Loc.Changed -= LanguageChanged;
        AdornerLayer.GetAdornerLayer(_root)?.Remove(this);
    }

    private void Scrolled(object sender, ScrollChangedEventArgs e) => Soon(sender, e);

    private void LanguageChanged()
    {
        if (Dispatcher.CheckAccess()) Soon(this, EventArgs.Empty);
        else Dispatcher.BeginInvoke(() => Soon(this, EventArgs.Empty));
    }

    /// <summary>Look again once the window has settled: layout runs many
    /// times a second while anything moves, and walking the window each time
    /// would be most of what it did.</summary>
    private void Soon(object? sender, EventArgs e)
    {
        _later.Stop();
        _later.Start();
    }

    // ── Finding the texts ─────────────────────────────────────────────

    /// <summary>The properties a text of the editor's is put in. Checked by
    /// name as well as among an element's own values, because one a style
    /// gives it is not its own.</summary>
    private static readonly (DependencyProperty Property, string Part)[] Parts =
    {
        (TextBlock.TextProperty, "text"),
        (AccessText.TextProperty, "text"),
        (ContentControl.ContentProperty, "text"),
        (HeaderedContentControl.HeaderProperty, "text"),
        (HeaderedItemsControl.HeaderProperty, "text"),
        (Window.TitleProperty, "text"),
        (FrameworkElement.ToolTipProperty, "tooltip"),
    };

    /// <summary>
    /// The editor's texts <paramref name="d"/> shows, by key: the ones it is
    /// bound to, and - for a text block - those its runs are.
    /// </summary>
    public static IReadOnlyList<Found> TextsOf(DependencyObject d)
    {
        var found = new List<Found>();
        void Add(string? key, string part)
        {
            if (!string.IsNullOrEmpty(key) && !found.Any(f => f.Key == key)) found.Add(new Found(key!, part));
        }

        foreach (var (property, part) in Parts)
            Add(KeyIn(BindingOperations.GetBindingExpressionBase(d, property)), part);

        var values = d.GetLocalValueEnumerator();
        while (values.MoveNext())
        {
            var property = values.Current.Property;
            if (Parts.Any(p => p.Property == property)) continue;
            Add(KeyIn(BindingOperations.GetBindingExpressionBase(d, property)), "text");
        }

        if (d is TextBlock block)
            foreach (var inline in Inlines(block.Inlines))
                foreach (var f in TextsOf(inline)) Add(f.Key, f.Part);
        return found;
    }

    private static IEnumerable<Inline> Inlines(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            if (inline is Span span)
                foreach (var inner in Inlines(span.Inlines)) yield return inner;
        }
    }

    /// <summary>The key a binding reads a text by, or null for any other binding.</summary>
    private static string? KeyIn(BindingExpressionBase? expression)
    {
        if (expression is not BindingExpression one) return null;
        var binding = one.ParentBinding;
        if (ReferenceEquals(binding.Source, LocSource.Instance))
        {
            string path = binding.Path?.Path ?? "";
            return path.Length > 2 && path[0] == '[' && path[^1] == ']' ? path.Substring(1, path.Length - 2) : null;
        }
        // A sentence worked out from a value, by a converter given its key.
        if (binding.Converter is FormatConverter or PluralConverter && binding.ConverterParameter is string key
            && (Loc.English.Has(key) || Loc.English.Has(key + ".other")))
            return key;
        return null;
    }

    /// <summary>Walk what is showing, and put a pencil beside each text in it.</summary>
    private void Find()
    {
        var marks = new List<Mark>();
        try
        {
            var visible = new Rect(_root.RenderSize);
            Walk(_root, visible, marks);
        }
        catch (InvalidOperationException) { /* a visual left the tree mid-walk: next time */ }
        _marks = marks;
        if (_hover != null && !_marks.Any(m => m.Target == _hover.Target)) _hover = null;
        InvalidateVisual();
    }

    private void Walk(DependencyObject d, Rect clip, List<Mark> marks)
    {
        if (d is UIElement ui && !ui.IsVisible) return;
        if (d is AdornerLayer || ReferenceEquals(d, this)) return;

        if (d is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
        {
            Rect bounds;
            try { bounds = fe.TransformToAncestor(_root).TransformBounds(new Rect(fe.RenderSize)); }
            catch (InvalidOperationException) { return; }

            // What scrolls is only seen through its viewport.
            if (fe is ScrollContentPresenter)
            {
                clip.Intersect(bounds);
                if (clip.IsEmpty) return;
            }

            var texts = TextsOf(fe);
            if (texts.Count > 0)
            {
                var seen = Rect.Intersect(bounds, clip);
                if (!seen.IsEmpty && seen.Width >= 2 && seen.Height >= 2)
                {
                    double x = Math.Min(seen.Right - Size / 2, clip.Right - Size);
                    double y = Math.Max(seen.Top - Size / 2, clip.Top);
                    marks.Add(new Mark(new Rect(Math.Max(x, clip.Left), y, Size, Size), seen, texts));
                }
            }
        }

        int count = VisualTreeHelper.GetChildrenCount(d);
        for (int i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(d, i), clip, marks);
    }

    /// <summary>The keys of every text with a pencil now. For the tests.</summary>
    internal IReadOnlyList<string> KeysShown => _marks.SelectMany(m => m.Texts).Select(f => f.Key).Distinct().ToList();

    /// <summary>The marks on <paramref name="window"/>, if they are on.</summary>
    internal static UiTextMarks? For(Window window) => On.TryGetValue(window, out var m) ? m : null;

    /// <summary>Look now, rather than when the window settles. For the tests.</summary>
    internal void FindNow() => Find();

    // ── Drawing, and the pencil being clicked ─────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        var accent = (Brush?)TryFindResource("Theme.Accent") ?? Brushes.DodgerBlue;
        // White on the accent in every theme: the theme's own text-on-accent
        // colour is dark in some, and a dark pencil on a dark dot is no pencil.
        var ink = Brushes.White;
        if (_hover != null)
            dc.DrawRectangle(null, new Pen(accent, 1.5) { DashStyle = DashStyles.Dash }, _hover.Target);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var glyph = new FormattedText("✎", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                      new Typeface("Segoe UI Symbol"), Size - 4, ink, dpi); // English on purpose: a font's name, never shown
        foreach (var mark in _marks)
        {
            var r = mark.Pencil;
            dc.DrawEllipse(accent, new Pen(ink, 1), new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
            dc.DrawText(glyph, new Point(r.X + (r.Width - glyph.Width) / 2, r.Y + (r.Height - glyph.Height) / 2));
        }
    }

    /// <summary>Only a pencil is hit: everything else is the editor's, as ever.</summary>
    protected override HitTestResult? HitTestCore(PointHitTestParameters p)
        => At(p.HitPoint) != null ? new PointHitTestResult(this, p.HitPoint) : null;

    private Mark? At(Point p)
    {
        // The last drawn is on top.
        for (int i = _marks.Count - 1; i >= 0; i--)
            if (_marks[i].Pencil.Contains(p)) return _marks[i];
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var over = At(e.GetPosition(this));
        if (over?.Target != _hover?.Target)
        {
            _hover = over;
            InvalidateVisual();
        }
        Cursor = over != null ? Cursors.Hand : null;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover == null) return;
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var mark = At(e.GetPosition(this));
        if (mark == null) return;
        e.Handled = true;
        UiTextEditWindow.Open(_window, mark.Texts);
    }
}
