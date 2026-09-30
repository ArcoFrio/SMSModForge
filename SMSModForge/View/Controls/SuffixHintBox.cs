using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SMSModForge.View.Controls;

/// <summary>
/// A TextBox that trails a greyed-out hint immediately after whatever has been
/// typed, showing what the value will end up meaning.
/// <para/>
/// Written for the prefix fields, where the box holds only part of a filename
/// and the rest is appended by the game. "Mouth prefix" plus a separate note
/// underneath explaining that four numbered files are loaded is two things to
/// read and hold together; <c>TutorialArt/Busts/Bust1/Mouth</c><i>1.png</i>
/// shown as one line is the same fact where the eye already is.
/// <para/>
/// Follows the code-only control pattern of <see cref="PathPickerBox"/>: no
/// XAML, no template, just a Grid holding the box and the hint. The hint is not
/// hit-testable, so clicking anywhere in the field lands in the TextBox.
/// </summary>
public sealed class SuffixHintBox : Grid
{
    private readonly TextBox _box = new();
    private readonly TextBlock _hint = new();

    // ── Dependency properties ──────────────────────────────────────────

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SuffixHintBox),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((SuffixHintBox)d).ShowBorrowed()));

    /// <summary>The value — bind this where the TextBox's Text was bound.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty SuffixProperty =
        DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(SuffixHintBox),
            new PropertyMetadata("", OnSuffixChanged));

    /// <summary>What the game appends. Shown greyed after the typed text.</summary>
    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    private static void OnSuffixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SuffixHintBox)d).ShowBorrowed();

    public static readonly DependencyProperty PackRootProperty =
        DependencyProperty.Register(nameof(PackRoot), typeof(string), typeof(SuffixHintBox),
            new PropertyMetadata(null, (d, _) => ((SuffixHintBox)d).ShowBorrowed()));

    /// <summary>The pack's folder, to tell a prefix leading outside it
    /// (<see cref="OutsideLook"/>). None, and nothing is told.</summary>
    public string? PackRoot
    {
        get => (string?)GetValue(PackRootProperty);
        set => SetValue(PackRootProperty, value);
    }

    /// <summary>
    /// A prefix that borrows the game's own art rather than naming files shows
    /// as such (<see cref="BorrowedLook"/>), and its hint says so instead of
    /// listing file names the game will not look for.
    /// </summary>
    private void ShowBorrowed()
    {
        if (BorrowedLook.Apply(_box, Text))
        {
            // An en space: the file trims the text's own ends, and italics
            // lean into an ordinary one.
            _hint.Text = "\u2002" + Localization.Loc.T("picker.fromGame.hint");
            _box.ToolTip = BorrowedLook.Tip(Text);
        }
        else
        {
            _hint.Text = Suffix ?? "";
            _box.ClearValue(ToolTipProperty);
        }
        OutsideLook.Apply(_box, Text, PackRoot);
        Reposition();
    }

    // ── Construction ───────────────────────────────────────────────────

    public SuffixHintBox()
    {
        // A long path plus its hint will outgrow the field; cutting the hint off
        // at the edge is better than letting it spill across the next control.
        ClipToBounds = true;

        _box.SetBinding(TextBox.TextProperty, new Binding(nameof(Text))
        {
            Source = this,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });

        _hint.Foreground = Brushes.Gray;
        _hint.IsHitTestVisible = false;      // clicks belong to the box underneath
        _hint.HorizontalAlignment = HorizontalAlignment.Left;
        _hint.VerticalAlignment = VerticalAlignment.Top;

        Children.Add(_box);
        Children.Add(_hint);

        _box.TextChanged += (_, _) => Reposition();
        _box.SizeChanged += (_, _) => Reposition();
        IsEnabledChanged += (_, _) =>
        {
            // A disabled field is not going to load anything, so the promise of
            // what it would load is noise.
            _hint.Visibility = IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            Reposition();
        };
        Loaded += (_, _) => Reposition();
        Localization.LocText.Follow(this, ShowBorrowed);
    }

    /// <summary>
    /// Park the hint where the caret would sit after the last character.
    /// <para/>
    /// <see cref="TextBox.GetRectFromCharacterIndex(int)"/> is what makes this
    /// exact rather than approximate: it already accounts for the border, the
    /// padding, the font and any horizontal scroll, so the hint stays glued to
    /// the text instead of drifting as the box fills up.
    /// </summary>
    private void Reposition()
    {
        if (!_box.IsLoaded) return;
        _box.UpdateLayout();   // the rect is stale until the new text is laid out

        var caret = _box.GetRectFromCharacterIndex(_box.Text.Length);
        if (caret.IsEmpty || double.IsInfinity(caret.Right)) return;
        _hint.Margin = new Thickness(caret.Right, caret.Top, 0, 0);
    }
}
