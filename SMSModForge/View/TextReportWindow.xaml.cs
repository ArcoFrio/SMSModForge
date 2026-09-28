using System.Windows;

namespace SMSModForge.View;

/// <summary>
/// A long report in a window of its own, with one optional thing to do about
/// it. Used by Language ▸ Check a translation, where the report can run to
/// hundreds of lines a message box would cut off.
/// </summary>
public partial class TextReportWindow : Window
{
    private bool _acted;

    public TextReportWindow()
    {
        InitializeComponent();
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        _acted = true;
        DialogResult = true;
    }

    /// <summary>
    /// Show <paramref name="report"/>. With <paramref name="action"/> set, a
    /// second button offers it; the answer is whether it was pressed. Never
    /// opens under the test harness, which is nobody to read it.
    /// </summary>
    public static bool Show(Window owner, string title, string report, string? action = null)
    {
        if (Services.TestMode.Active) return false;
        var w = new TextReportWindow { Owner = owner, Title = title };
        w.ReportBox.Text = report;
        if (!string.IsNullOrEmpty(action))
        {
            w.ActionButton.Content = action;
            w.ActionButton.Visibility = Visibility.Visible;
        }
        w.ShowDialog();
        return w._acted;
    }
}
