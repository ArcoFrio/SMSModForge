using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.ViewModel;

namespace SMSModForge.View;

/// <summary>
/// The readable name of what an editable dropdown holds, drawn faintly at the box's right (1.10.0). A box stores
/// the pack's own word for a thing - place:TheGallery, vanilla:5_MyRoom, HerFirstPose - while its list shows
/// "This pack — The Gallery"; the review of the tutorials found beginners reading the box and not knowing what it
/// named. The note is the option's own label (less the stored word it ends with), or the scene's name for a scene's
/// key; nothing when the box holds something no option names.
/// <para/>
/// Drawn over the box rather than beside it, so no row grows or moves and both looks get it with no template of
/// their own; it gives way to what is typed (it is drawn only in the room the words leave, and not at all while
/// the box has the keyboard or its list is open), and a click goes through it to the box.
/// </summary>
public static class TokenNote
{
    public static readonly DependencyProperty ShowProperty =
        DependencyProperty.RegisterAttached("Show", typeof(bool), typeof(TokenNote), new PropertyMetadata(false, OnShow));

    public static bool GetShow(DependencyObject o) => (bool)o.GetValue(ShowProperty);
    public static void SetShow(DependencyObject o, bool value) => o.SetValue(ShowProperty, value);

    /// <summary>The readable name the box shows, or "" for none. Read by the adorner, and by tests.</summary>
    public static readonly DependencyProperty NoteProperty =
        DependencyProperty.RegisterAttached("Note", typeof(string), typeof(TokenNote), new PropertyMetadata(""));

    public static string GetNote(DependencyObject o) => (string)o.GetValue(NoteProperty);
    private static void SetNote(DependencyObject o, string value) => o.SetValue(NoteProperty, value);

    private static readonly DependencyProperty AdornerProperty =
        DependencyProperty.RegisterAttached("Adorner", typeof(NoteAdorner), typeof(TokenNote), new PropertyMetadata(null));

    /// <summary>The adorner drawing the note on this box, while the box is on screen (for tests).</summary>
    internal static Adorner? AdornerOf(ComboBox box) => (NoteAdorner?)box.GetValue(AdornerProperty);

    private static void OnShow(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox box) return;
        box.Loaded -= Attach; box.Unloaded -= Detach;
        if (!(bool)e.NewValue) { Detach(box, null!); return; }
        box.Loaded += Attach; box.Unloaded += Detach;
        if (box.IsLoaded) Attach(box, null!);
    }

    private static void Attach(object sender, RoutedEventArgs e)
    {
        var box = (ComboBox)sender;
        if (!box.IsEditable || box.GetValue(AdornerProperty) != null) { Update(box); return; }
        var layer = AdornerLayer.GetAdornerLayer(box);
        if (layer == null) return;
        var adorner = new NoteAdorner(box);
        layer.Add(adorner);
        box.SetValue(AdornerProperty, adorner);
        // The text box inside is the template's and is made again with it (a theme put on): the routed event is
        // the box's own, whatever template it has.
        box.AddHandler(TextBoxBase.TextChangedEvent, (TextChangedEventHandler)Changed);
        box.SelectionChanged += Changed;
        box.IsKeyboardFocusWithinChanged += Redraw;
        box.DropDownOpened += Changed;
        box.DropDownClosed += Changed;
        box.SizeChanged += Changed;
        adorner.ItemsChanged = (_, _) => Update(box);
        ((INotifyCollectionChanged)box.Items).CollectionChanged += adorner.ItemsChanged;
        Update(box);
    }

    private static void Detach(object sender, RoutedEventArgs e)
    {
        var box = (ComboBox)sender;
        if (box.GetValue(AdornerProperty) is not NoteAdorner adorner) return;
        AdornerLayer.GetAdornerLayer(box)?.Remove(adorner);
        box.ClearValue(AdornerProperty);
        box.RemoveHandler(TextBoxBase.TextChangedEvent, (TextChangedEventHandler)Changed);
        box.SelectionChanged -= Changed;
        box.IsKeyboardFocusWithinChanged -= Redraw;
        box.DropDownOpened -= Changed;
        box.DropDownClosed -= Changed;
        box.SizeChanged -= Changed;
        if (adorner.ItemsChanged != null) ((INotifyCollectionChanged)box.Items).CollectionChanged -= adorner.ItemsChanged;
    }

    private static void Changed(object sender, EventArgs e) { if (sender is ComboBox box) Update(box); }

    private static void Redraw(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is ComboBox box) ((Adorner?)box.GetValue(AdornerProperty))?.InvalidateVisual();
    }

    private static void Update(ComboBox box)
    {
        SetNote(box, Readable(box, box.Text ?? ""));
        ((Adorner?)box.GetValue(AdornerProperty))?.InvalidateVisual();
    }

    /// <summary>What the box's word reads as: the label of the option storing it, or the scene it is the key of.</summary>
    internal static string Readable(ComboBox box, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return "";
        foreach (var item in box.Items)
            if (item is NavigatorTargetOption o && string.Equals(o.Token, text, StringComparison.Ordinal))
                return Shorten(o.DisplayLabel, o.Token);
        // A list of bare keys (the Scene category's): the pack's own scene by its key.
        if (Window.GetWindow(box)?.DataContext is MainViewModel vm)
            foreach (var scene in vm.Scenes)
                if (string.Equals(scene.Key, text, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(scene.DisplayName) && scene.DisplayName.Trim() != text)
                    return scene.DisplayName.Trim();
        return "";
    }

    /// <summary>"This pack — The Gallery (place:TheGallery)" as "This pack — The Gallery": the stored word is
    /// already in the box. Nothing when the label says no more than the box does.</summary>
    internal static string Shorten(string label, string token)
    {
        label = label.Trim();
        int open = label.LastIndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && label.EndsWith(")", StringComparison.Ordinal))
        {
            string inner = label.Substring(open + 2, label.Length - open - 3);
            if (inner.Length > 0 && token.EndsWith(inner, StringComparison.Ordinal)) label = label.Substring(0, open).Trim();
        }
        return string.Equals(label, token, StringComparison.Ordinal) ? "" : label;
    }

    /// <summary>Draws the note in the room the box's words leave, right-aligned before the arrow.</summary>
    private sealed class NoteAdorner : Adorner
    {
        public NoteAdorner(ComboBox box) : base(box) { IsHitTestVisible = false; }

        /// <summary>The box's list changing (its options are kept in step in place), kept to be taken off again.</summary>
        public NotifyCollectionChangedEventHandler? ItemsChanged { get; set; }

        /// <summary>Where the note was last drawn, in the box's coordinates; empty when it was not (for tests).</summary>
        public Rect Drawn { get; private set; } = Rect.Empty;

        protected override void OnRender(DrawingContext dc)
        {
            Drawn = Rect.Empty;
            var box = (ComboBox)AdornedElement;
            string note = GetNote(box);
            if (note.Length == 0 || box.IsKeyboardFocusWithin || box.IsDropDownOpen || !box.IsVisible) return;
            if (box.Template?.FindName("PART_EditableTextBox", box) is not TextBox words || !words.IsVisible
                || words.ActualWidth <= 0) return;
            // Where the words end, as the text box laid them out - not worked out again here.
            Rect caret = words.GetRectFromCharacterIndex(words.Text.Length);
            if (caret.IsEmpty) return;
            Rect area = words.TransformToAncestor(box).TransformBounds(new Rect(words.RenderSize));
            double wordsEnd = words.TranslatePoint(new Point(caret.Right, 0), box).X;
            double right = area.Right - 6, room = right - (wordsEnd + 14);
            if (room < 36) return;

            var face = new Typeface(words.FontFamily, FontStyles.Italic, FontWeights.Normal, words.FontStretch);
            var ink = box.TryFindResource("Theme.TextMuted") as Brush ?? Brushes.Gray;
            var text = new FormattedText(note, CultureInfo.CurrentUICulture, words.FlowDirection, face, words.FontSize * 0.92,
                                         ink, VisualTreeHelper.GetDpi(box).PixelsPerDip)
            {
                MaxTextWidth = room, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis,
            };
            double width = Math.Min(text.WidthIncludingTrailingWhitespace, room);
            var at = new Point(right - width, area.Top + (area.Height - text.Height) / 2);
            dc.DrawText(text, at);
            Drawn = new Rect(at, new Size(width, text.Height));
        }
    }

    /// <summary>Where the box's note was last drawn, in its coordinates; empty when none was (for tests).</summary>
    internal static Rect DrawnOn(ComboBox box) => box.GetValue(AdornerProperty) is NoteAdorner a ? a.Drawn : Rect.Empty;
}
