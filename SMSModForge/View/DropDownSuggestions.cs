using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SMSModForge.Services;

namespace SMSModForge.View;

/// <summary>
/// A dropdown opened empty shows the options the record being edited makes
/// likely on top (the author, 1.8.0 - see <see cref="SmartSuggestions"/> for
/// how they are found).
/// <para/>
/// ONE DROPDOWN, NOTHING ELSE. The options move on screen only: the ranking is
/// put on the one box that opened (<see cref="RankingProperty"/>), and its
/// panel (<see cref="SuggestionsPanel"/>) lays the rows out in that order.
/// The list itself is not sorted - lists are shared between dropdowns, and a
/// sort on one would reorder every box bound to it, as a filter would narrow
/// them (<see cref="ComboBoxSearch"/>) - and the selection is not touched.
/// <para/>
/// Only an EMPTY box: one holding a choice was opened to change it, and one
/// with words typed in it is a search.
/// </summary>
public static class DropDownSuggestions
{
    /// <summary>The scores of the options on top, set on a box while it is
    /// open. Inherited, so its panels and rows - in the popup, which is the
    /// box's logical child - read it without looking for the box.</summary>
    public static readonly DependencyProperty RankingProperty =
        DependencyProperty.RegisterAttached(
            "Ranking", typeof(Ranking), typeof(DropDownSuggestions),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static Ranking? GetRanking(DependencyObject o) => (Ranking?)o.GetValue(RankingProperty);
    public static void SetRanking(DependencyObject o, Ranking? value) => o.SetValue(RankingProperty, value);

    /// <summary>What goes on top, and how far up.</summary>
    public sealed class Ranking
    {
        private readonly IReadOnlyDictionary<object, double> _scores;
        public Ranking(IReadOnlyDictionary<object, double> scores) => _scores = scores;

        public int Count => _scores.Count;

        /// <summary>An option's score, a heading's best one's, or 0.</summary>
        public double ScoreOf(object? item) => item switch
        {
            null => 0,
            CollectionViewGroup group => group.Items.Count == 0 ? 0 : group.Items.Max(ScoreOf),
            _ => _scores.TryGetValue(item, out double score) ? score : 0,
        };
    }

    internal static void Opened(ComboBox box)
    {
        box.ClearValue(RankingProperty);
        if (box.ItemsSource == null) return;   // rows written out in XAML are in the order meant
        bool empty = box.IsEditable ? string.IsNullOrWhiteSpace(box.Text) : box.SelectedItem == null;
        if (!empty) return;
        if (Window.GetWindow(box)?.DataContext is not ISuggestionSource source) return;

        string kind = OptionNotes.GetKind(box);
        var options = new List<SuggestionOption>(box.Items.Count);
        foreach (var item in box.Items)
        {
            if (item == null || item is UIElement) continue;
            options.Add(new SuggestionOption(item,
                Converters.SearchMatchConverter.TextOf(item, box),
                ComboBoxSearch.ValueOf(item, box.SelectedValuePath) is { } value && value != item ? value.ToString() : null,
                OptionNotes.NoteFor(kind, item)));
        }
        if (source.RankOptions(options) is { Count: > 0 } scores)
            box.SetValue(RankingProperty, new Ranking(scores));
    }

    internal static void Closed(ComboBox box) => box.ClearValue(RankingProperty);
}

/// <summary>
/// A dropdown's rows one under the other - as the stack it replaces - with
/// the suggested ones first (<see cref="DropDownSuggestions"/>), best first,
/// and a line under them. The rest keep the list's own order. Used for a
/// dropdown's rows and for the rows under each of its headings; a heading
/// goes as high as the best row under it.
/// <para/>
/// Not virtualizing, and nothing is lost by that: a dropdown's popup scrolls
/// its rows by pixel (no content scrolling), so the stack it replaces made
/// every row anyway.
/// </summary>
public sealed class SuggestionsPanel : Panel
{
    private double _lineAt = -1;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != DropDownSuggestions.RankingProperty) return;
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    /// <summary>The rows in the order they stand, and how many of the first
    /// are suggestions.</summary>
    internal List<UIElement> Ordered(out int suggested)
    {
        var rows = InternalChildren.Cast<UIElement>().ToList();
        suggested = 0;
        var ranking = DropDownSuggestions.GetRanking(this);
        if (ranking == null) return rows;

        var scored = rows.Select((row, at) => (row, at, score: ranking.ScoreOf((row as FrameworkElement)?.DataContext))).ToList();
        suggested = scored.Count(s => s.score > 0);
        return suggested == 0 ? rows : scored.OrderByDescending(s => s.score).ThenBy(s => s.at).Select(s => s.row).ToList();
    }

    /// <summary>
    /// The room the line under the suggestions gets to itself. Drawn on the
    /// panel, it is underneath the rows, and a row that paints its own
    /// background - every EX row does - hid it: in a gap of its own nothing
    /// can (found 2026-10-24, the first time the window tests really ran in EX).
    /// </summary>
    private const double LineGap = 3;

    protected override Size MeasureOverride(Size available)
    {
        double width = 0, height = 0;
        var row = new Size(available.Width, double.PositiveInfinity);
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(row);
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
        }
        if (HasLine(Ordered(out int suggested), suggested)) height += LineGap;
        return new Size(width, height);
    }

    /// <summary>The line goes under the last suggestion, and only when
    /// something visible follows it.</summary>
    private static bool HasLine(List<UIElement> rows, int suggested)
        => suggested > 0 && rows.Skip(suggested).Any(r => r.Visibility == Visibility.Visible);

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0, lineAt = -1;
        var rows = Ordered(out int suggested);
        bool line = HasLine(rows, suggested);
        for (int i = 0; i < rows.Count; i++)
        {
            var child = rows[i];
            child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
            if (line && i == suggested - 1)
            {
                lineAt = y + Math.Ceiling(LineGap / 2);   // the middle of its own gap
                y += LineGap;
            }
        }
        if (lineAt != _lineAt) { _lineAt = lineAt; InvalidateVisual(); }
        return final;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_lineAt < 0 || TryFindResource("Theme.Border") is not Brush ink) return;
        // On whole pixels: a panel can stand at a fraction of one, and a line
        // between two pixel rows is drawn as two faint ones.
        var at = new Rect(4, Math.Max(0, _lineAt - 1), Math.Max(0, ActualWidth - 8), 1);
        var guides = new GuidelineSet();
        guides.GuidelinesY.Add(at.Top);
        guides.GuidelinesY.Add(at.Bottom);
        dc.PushGuidelineSet(guides);
        dc.DrawRectangle(ink, null, at);
        dc.Pop();
    }
}

/// <summary>Visible on a suggested row: values[0] the row's item, values[1]
/// the box's <see cref="DropDownSuggestions.Ranking"/>.</summary>
public sealed class SuggestedConverter : IMultiValueConverter
{
    public static readonly SuggestedConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length >= 2 && values[1] is DropDownSuggestions.Ranking ranking && ranking.ScoreOf(values[0]) > 0
            ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
