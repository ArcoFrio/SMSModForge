using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Services;

namespace SMSModForge.View.Controls;

/// <summary>
/// A panel's help that folds (the author, 1.8.0: "I like all 4 visuals").
/// Classic shows its first line with "more" after it, and the whole of it
/// with "less"; EX shows nothing until the "?" in the panel's band is pressed,
/// then the whole of it. Folded until opened, and remembered per panel
/// (<see cref="PanelHelp"/>): an author reads it once, and it took the room of
/// the fields under it on every visit after.
/// </summary>
public sealed class HelpText : DockPanel
{
    private readonly TextBlock _text = new() { FontSize = 10, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _link = new() { FontSize = 10, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly Hyperlink _toggle = new();
    private GroupBox? _panel;

    public HelpText()
    {
        _text.SetResourceReference(TextBlock.ForegroundProperty, ThemeManager.KeyTextMuted);
        _toggle.Click += (_, _) => IsOpen = !IsOpen;
        _link.Inlines.Add(_toggle);
        SetDock(_link, Dock.Right);
        Children.Add(_link);
        Children.Add(_text);
        LocText.Follow(this, Layout);
        Loaded += (_, _) => Join();
        Unloaded += (_, _) => ThemeManager.Changed -= Layout;
        Layout();
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(HelpText),
            new PropertyMetadata("", (d, e) => ((HelpText)d)._text.Text = (string)e.NewValue));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The name its panel's choice is remembered by, when it is the
    /// panel's first help.</summary>
    public string Key { get; set; } = "";

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(HelpText),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HelpText)d).Layout()));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>Its panel holds whether it is open, for every help line in it
    /// and for EX's "?".</summary>
    private void Join()
    {
        ThemeManager.Changed -= Layout;
        ThemeManager.Changed += Layout;
        for (DependencyObject? up = VisualTreeHelper.GetParent(this); up != null; up = VisualTreeHelper.GetParent(up))
            if (up is GroupBox box) { _panel = box; break; }
        if (_panel != null)
        {
            PanelHelp.SetHasHelp(_panel, true);
            if (PanelHelp.GetKey(_panel).Length == 0) PanelHelp.SetKey(_panel, Key);
            SetBinding(IsOpenProperty, new Binding { Source = _panel, Path = new PropertyPath(PanelHelp.OpenProperty), Mode = BindingMode.TwoWay });
        }
        else if (Key.Length > 0) IsOpen = EditorPrefs.GetHelpOpen(Key);
        Layout();
    }

    private void Layout()
    {
        // EX keeps it behind the panel's "?"; with no panel to hold one, it
        // folds as Classic does.
        bool behindQuestion = ThemeManager.Current.IsEx && _panel != null;
        _text.TextWrapping = IsOpen ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _text.TextTrimming = IsOpen ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        _text.Visibility = behindQuestion && !IsOpen ? Visibility.Collapsed : Visibility.Visible;
        _link.Visibility = behindQuestion ? Visibility.Collapsed : Visibility.Visible;
        _toggle.Inlines.Clear();
        _toggle.Inlines.Add(new Run(Loc.T(IsOpen ? "help.less" : "help.more")));
    }
}
