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

        // The window is opened HERE rather than by StartupUri in App.xaml, and
        // that is the whole of the fix above.
        //
        // The applier has to stop WPF opening a window — it is a second process
        // with nothing to show, whose only job is to copy itself over the
        // install. It did that with `StartupUri = null`, which reads as the
        // obvious way to say "no window" and is in fact an ArgumentNullException:
        // WPF's setter rejects null outright. So the applier died on its first
        // line, every time, and had copied nothing.
        //
        // Nothing caught it because it is a crash in a process nobody watches:
        // the old editor had already been told to close, so what an author saw
        // was a progress prompt, the editor closing, and the same version when
        // they opened it again. Auto-update had never once worked — not in
        // 1.2.0, which shipped it, and not in 1.3.0.
        //
        // With no StartupUri there is nothing to suppress: the ordinary path
        // opens the window itself, and the applier simply never reaches here.
        new MainWindow().Show();
    }
}
