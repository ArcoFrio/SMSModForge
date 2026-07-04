using System.Windows;

namespace SMSModForge.View;

/// <summary>
/// Minimal modal single-line text prompt (the editor keeps its package
/// surface to Newtonsoft, so there's no third-party InputBox). Reusable for
/// naming flows — custom roomtalks, duplicated items, folders, etc.
/// </summary>
public partial class TextPromptWindow : Window
{
    public string ResultText { get; private set; } = "";

    public TextPromptWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => { InputBox.SelectAll(); InputBox.Focus(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultText = InputBox.Text;
        DialogResult = true;
    }

    /// <summary>Show a modal prompt; returns the entered text, or null if cancelled.</summary>
    public static string? Prompt(Window owner, string title, string message, string initial = "")
    {
        var w = new TextPromptWindow { Owner = owner, Title = title };
        w.PromptLabel.Text = message;
        w.InputBox.Text = initial;
        return w.ShowDialog() == true ? w.ResultText : null;
    }
}
