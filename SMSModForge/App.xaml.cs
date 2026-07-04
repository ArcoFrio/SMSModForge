using System.Windows;
using SMSModForge.Services;

namespace SMSModForge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Restore the persisted colour theme before the main window builds,
        // so its DynamicResource bindings resolve to the chosen palette.
        ThemeManager.ApplySaved();
        // Restore the saved live-preview quality preset (frame cap + AA) so
        // the Busts tab honours it from the first frame.
        PreviewQualityManager.ApplySaved();
        base.OnStartup(e);
    }
}
