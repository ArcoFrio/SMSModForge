using System.Windows;
using SMSModForge.Services;

namespace SMSModForge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Started by the previous version to replace it. There is no editor to
        // show: this process IS the new build, running from where it was staged,
        // and its whole job is to copy itself over the install and start that.
        // Nothing below it should run - no theme, no window, no settings.
        if (UpdateApplier.WasAskedToApply(e.Args, out var installFolder, out int oldProcessId))
        {
            StartupUri = null;   // or WPF opens MainWindow after this returns
            if (!UpdateApplier.Apply(installFolder, oldProcessId, out var problem))
                MessageBox.Show(problem, "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        // Restore the persisted colour theme before the main window builds,
        // so its DynamicResource bindings resolve to the chosen palette.
        ThemeManager.ApplySaved();
        // Restore the saved live-preview quality preset (frame cap + AA) so
        // the Busts tab honours it from the first frame.
        PreviewQualityManager.ApplySaved();
        base.OnStartup(e);
    }
}
