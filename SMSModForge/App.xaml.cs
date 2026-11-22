using System.Windows;
using SMSModForge.Services;
using SMSModForge.Localization;

namespace SMSModForge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // First, before anything can say a word: the update applier below
        // reports in it too, and every window reads its texts as it is built.
        // Not under the test harness, whose Application runs this too: the
        // suite is written in English, and the language somebody picked for
        // their own editor must not decide whether it passes.
        if (!TestMode.Active)
            Localization.Loc.Use(Localization.Loc.StartingCode(EditorPrefs.Language));

        // What the first pack needs and takes long to make, made on a worker while the window is built (1.10.0,
        // UiDelayTests' profile): Windows' list of every language a pack can be written in, and every language's
        // words for a new item's name. Both are made once, whichever thread asks first.
        System.Threading.Tasks.Task.Run(() =>
        {
            try { ViewModel.MainViewModel.AllPackLanguages(); } catch (System.Exception) { }
            try { Localization.NewNames.In("en", "characters.newName"); } catch (System.Exception) { }
        });

        // Started by the previous version to replace it. There is no editor to
        // show: this process IS the new build, running from where it was staged,
        // and its whole job is to copy itself over the install and start that.
        // Nothing below it should run - no theme, no window, no settings.
        if (UpdateApplier.WasAskedToApply(e.Args, out var installFolder, out int oldProcessId))
        {
            if (!UpdateApplier.Apply(installFolder, oldProcessId, out var problem))
                MessageBox.Show(problem, Loc.T("update.problem.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        // The place pictures export (1.9.2): no window either. Every place is
        // drawn into the folder asked for, as the Places tab's preview draws it,
        // a line for each in export.log beside them, and the process ends (exit
        // code 1 when it failed). Debug builds only: it draws the game's own
        // levels, which a release must not hand out (see PlaceExport).
#if DEBUG
        if (PlaceExport.WasAsked(e.Args, out var placesFolder, out var placesPack, out var levelArt))
        {
            var lines = new System.Text.StringBuilder();
            int code = 0;
            try { PlaceExport.Run(placesFolder, placesPack, line => lines.AppendLine(line), levelArt: levelArt); }
            catch (System.Exception ex) { lines.AppendLine(ex.ToString()); code = 1; }
            try { System.IO.Directory.CreateDirectory(placesFolder); System.IO.File.WriteAllText(System.IO.Path.Combine(placesFolder, "export.log"), lines.ToString()); }
            catch (System.IO.IOException) { code = 1; }
            Shutdown(code);
            return;
        }
#endif

        // Restore the persisted colour theme before the main window builds,
        // so its DynamicResource bindings resolve to the chosen palette.
        // Not under the test harness, whose Application runs this too, AFTER
        // it has put the run's theme on (Light, or SMSMODFORGE_TEST_THEME):
        // this undid it, so the -WithEx pass ran in Light from 1.8.0's EX work
        // until 2026-10-24, when Frost becoming the default made it show
        // (HarnessThemeTests).
        if (!TestMode.Active)
            ThemeManager.ApplySaved();
        // Restore the saved live-preview quality preset (frame cap + AA) so
        // the Busts tab honours it from the first frame.
        PreviewQualityManager.ApplySaved();
        // And what the bust preview draws a bust over (1.9.1), loading it now.
        PreviewBackdropManager.ApplySaved();
        // And what draws them (1.9.1): the graphics card or the processor -
        // Automatic times both in the background now.
        PreviewRendererManager.ApplySaved();
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
