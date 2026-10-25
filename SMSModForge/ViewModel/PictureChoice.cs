using System.Windows.Media;

namespace SMSModForge.ViewModel;

/// <summary>
/// One of the buttons a line's outfit or expression is picked with (the
/// author, 1.8.0: instead of a box anything could be typed into, "some buttons
/// that sync to the existing expressions for the character ... The buttons
/// should have a picture"). What it stands for, what it says, its picture -
/// a bust, or an expression icon - and whether it is the line's.
/// <para/>
/// Selection is shown, never two-way bound: a list rebuilt under a two-way
/// selection empties it and writes the emptiness back into the line, which is
/// how the editor's dropdowns used to lose a node's outfit.
/// </summary>
public sealed class PictureChoice : ObservableObject
{
    public PictureChoice(string value, string label, string? icon = null, bool isUnknown = false, string? tip = null)
    {
        Value = value;
        Label = label;
        Icon = icon;
        IsUnknown = isUnknown;
        Tip = tip ?? label;
    }

    /// <summary>What picking it writes into the line.</summary>
    public string Value { get; }

    public string Label { get; }

    /// <summary>What hovering over it says: the whole label, which a button
    /// may cut short, or what a choice like Keep means.</summary>
    public string Tip { get; }

    /// <summary>The expression icon, for a face (<see cref="Model.ExpressionIcons"/>).</summary>
    public string? Icon { get; }

    /// <summary>A value the line holds that is not one of the speaker's - shown
    /// so it can be seen and replaced, marked as wrong.</summary>
    public bool IsUnknown { get; }

    private ImageSource? _picture;
    /// <summary>The bust, for an outfit; filled in once it has been read.</summary>
    public ImageSource? Picture
    {
        get => _picture;
        set { _picture = value; OnPropertyChanged(); }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }
}
