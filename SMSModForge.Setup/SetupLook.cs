using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMSModForge.Setup;

/// <summary>
/// The setup's look: the game's navy with ModForge's amber, as ModForge looks
/// in the game - and on a dark ground, so the yellow of a warning reads (the
/// author, 1.8.0: inside the game folder "the whole text turns yellow").
/// </summary>
public static class SetupLook
{
    public static readonly Brush Back = Solid(0x0F, 0x1A, 0x2E);
    public static readonly Brush Band = Solid(0x0A, 0x12, 0x22);
    public static readonly Brush Field = Solid(0x16, 0x24, 0x3C);
    public static readonly Brush FieldBorder = Solid(0x3A, 0x4C, 0x6A);
    public static readonly Brush Text = Solid(0xF2, 0xF4, 0xF8);
    public static readonly Brush Muted = Solid(0xAE, 0xB8, 0xCC);
    public static readonly Brush Accent = Solid(0xF5, 0xA9, 0x3B);
    public static readonly Brush OnAccent = Solid(0x0A, 0x1A, 0x33);
    public static readonly Brush Warning = Solid(0xFF, 0xD5, 0x4A);
    public static readonly Brush Good = Solid(0x7B, 0xD8, 0x8F);
    public static readonly Brush Bad = Solid(0xFF, 0x8A, 0x7A);

    private static Brush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Plain styles for the window's own controls, so no app-wide style reaches
    /// them: the setup has none, but a test builds its window inside the
    /// editor, whose theme painted the buttons' and tick boxes' words dark.
    /// </summary>
    public static void OwnStyles(FrameworkElement window)
    {
        foreach (var type in new[] { typeof(TextBlock), typeof(Button), typeof(CheckBox), typeof(RadioButton), typeof(TextBox) })
            window.Resources[type] = new Style(type);
    }

    public static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Text, Margin = new Thickness(0, 0, 0, 8),
        TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock Line(string text, Brush? ink = null) => new()
    {
        Text = text, Foreground = ink ?? Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
    };

    public static TextBox Box() => new()
    {
        Background = Field, Foreground = Text, BorderBrush = FieldBorder, CaretBrush = Text,
        Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center,
    };

    /// <summary>A button whose words are a TextBlock of their own, coloured
    /// here: inherited, they took a dark text colour from elsewhere (the
    /// editor's, when a test builds this window inside it).</summary>
    public static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, Foreground = primary ? OnAccent : Text },
            MinWidth = 96, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(8, 0, 0, 0),
            Background = primary ? Accent : Field,
            BorderBrush = primary ? Accent : FieldBorder, FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        return button;
    }

    public static void SetText(ContentControl control, string text) => ((TextBlock)control.Content).Text = text;

    /// <summary>The words a button or tick box shows, for reading back.</summary>
    public static TextBlock Words(ContentControl control) => (TextBlock)control.Content;

    public static CheckBox Tick(string text, bool on) => new()
    {
        Content = new TextBlock { Text = text, Foreground = Text, TextWrapping = TextWrapping.Wrap },
        IsChecked = on, Margin = new Thickness(0, 4, 0, 0),
    };

    /// <summary>One of a few choices: its name, and under it what it is for.
    /// The words are a TextBlock of their own, as on the buttons; the name is
    /// <see cref="Words"/>, the line under it the second.</summary>
    public static RadioButton Choice(string group, string name, string help, bool on)
    {
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = name, Foreground = Text, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        words.Children.Add(new TextBlock { Text = help, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        return new RadioButton { GroupName = group, Content = words, IsChecked = on, Margin = new Thickness(0, 6, 0, 8) };
    }

    /// <summary>A choice's name, and the line under it saying what it is for.</summary>
    public static (TextBlock Name, TextBlock Help) ChoiceWords(RadioButton choice)
    {
        var words = (StackPanel)choice.Content;
        return ((TextBlock)words.Children[0], (TextBlock)words.Children[1]);
    }
}
