using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.Services;
using SMSModForge.View.Converters;

namespace SMSModForge.View;

/// <summary>
/// An editable dropdown whose name reads with a capital first letter while
/// nobody is typing in it (the author, 1.8.0: "make every actor name in the
/// dialogue node window start with a capital letter, purely aesthetic").
/// <para/>
/// The box's own text is the value as stored - a speaker's key, often lower
/// case - and stays so: re-casing it would write the new case back into the
/// line, and fight the typing while the box is in use. So the box keeps its
/// text and, while it is not being edited, hides it under the same words drawn
/// capitalised, in its own font, place and colour (red still means unknown).
/// Clicking into it shows what is really there.
/// </summary>
public static class CapitalizedBox
{
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.RegisterAttached(
        "IsOn", typeof(bool), typeof(CapitalizedBox), new PropertyMetadata(false, OnIsOnChanged));

    public static bool GetIsOn(DependencyObject d) => (bool)d.GetValue(IsOnProperty);
    public static void SetIsOn(DependencyObject d, bool value) => d.SetValue(IsOnProperty, value);

    private static readonly DependencyProperty AdornerProperty = DependencyProperty.RegisterAttached(
        "Adorner", typeof(NameAdorner), typeof(CapitalizedBox));

    /// <summary>What the box shows now, for a test to read: the capitalised
    /// name while it is drawn over the box, otherwise null.</summary>
    internal static string? ShownOver(ComboBox box)
        => box.GetValue(AdornerProperty) is NameAdorner a && a.Showing ? a.Text : null;

    private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox box || e.NewValue is not true) return;
        box.Loaded += (_, _) => Attach(box);
        box.IsKeyboardFocusWithinChanged += (_, _) => Refresh(box);
        box.DropDownOpened += (_, _) => Refresh(box);
        box.DropDownClosed += (_, _) => Refresh(box);
        // EX and Classic draw the box with different templates: a theme change
        // brings a new text box, and the name has to follow it there.
        Action rethemed = () => box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                                                           new Action(() => Attach(box)));
        box.Loaded += (_, _) => { ThemeManager.Changed -= rethemed; ThemeManager.Changed += rethemed; };
        box.Unloaded += (_, _) => ThemeManager.Changed -= rethemed;
        if (box.IsLoaded) Attach(box);
    }

    private static void Attach(ComboBox box)
    {
        box.ApplyTemplate();
        if (box.Template?.FindName("PART_EditableTextBox", box) is not TextBox text) return;
        if (box.GetValue(AdornerProperty) is NameAdorner old)
        {
            if (ReferenceEquals(old.AdornedElement, text)) { Refresh(box); return; }
            AdornerLayer.GetAdornerLayer(old.AdornedElement)?.Remove(old);
            ((TextBox)old.AdornedElement).Opacity = 1;
        }
        var layer = AdornerLayer.GetAdornerLayer(text);
        if (layer == null) return;
        var adorner = new NameAdorner(text);
        layer.Add(adorner);
        box.SetValue(AdornerProperty, adorner);
        text.TextChanged += (_, _) => Refresh(box);
        text.SizeChanged += (_, _) => Refresh(box);
        Refresh(box);
    }

    private static void Refresh(ComboBox box)
    {
        if (box.GetValue(AdornerProperty) is not NameAdorner adorner) return;
        var text = (TextBox)adorner.AdornedElement;
        string shown = CapitalizedConverter.Of(text.Text);
        adorner.Showing = !box.IsKeyboardFocusWithin && !box.IsDropDownOpen && shown != text.Text;
        adorner.Text = shown;
        text.Opacity = adorner.Showing ? 0 : 1;
        adorner.InvalidateVisual();
    }

    /// <summary>The name drawn where the box draws its own text.</summary>
    private sealed class NameAdorner : Adorner
    {
        public NameAdorner(TextBox adorned) : base(adorned) => IsHitTestVisible = false;

        public bool Showing { get; set; }
        public string Text { get; set; } = "";

        protected override void OnRender(DrawingContext dc)
        {
            if (!Showing || Text.Length == 0) return;
            var box = (TextBox)AdornedElement;
            Rect at = box.GetRectFromCharacterIndex(0);
            if (at.IsEmpty) return;
            var typeface = new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch);
            var words = new FormattedText(Text, CultureInfo.CurrentCulture, box.FlowDirection, typeface,
                                          box.FontSize, box.Foreground, VisualTreeHelper.GetDpi(box).PixelsPerDip);
            dc.PushClip(new RectangleGeometry(new Rect(box.RenderSize)));
            dc.DrawText(words, new Point(at.Left, at.Top));
            dc.Pop();
        }
    }
}
