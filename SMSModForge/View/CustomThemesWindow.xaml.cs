using System.Windows;
using SMSModForge.Services;
using SMSModForge.ViewModel;

namespace SMSModForge.View;

/// <summary>
/// Themes of one's own (1.8.0): made, named and coloured here, with the editor
/// itself as the preview. See <see cref="CustomThemesViewModel"/>.
/// </summary>
public partial class CustomThemesWindow : Window
{
    public CustomThemesWindow()
    {
        InitializeComponent();
        // The colour picker is a window of its own: never under a test run.
        DataContext = new CustomThemesViewModel(current => TestMode.Active ? null : ColorPickerWindow.Pick(this, current));
    }

    public CustomThemesViewModel ViewModel => (CustomThemesViewModel)DataContext;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
