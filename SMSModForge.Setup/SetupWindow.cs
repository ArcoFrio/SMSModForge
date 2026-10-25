using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMSModForge.Setup;

/// <summary>
/// The installer's steps (the author, 1.8.0): what to install - only the
/// plugin, to play mods (chosen already), or everything, to make them; the
/// Starmaker Story folder, required and checked to be the game; where
/// ModForge goes (everything only), with a good
/// folder chosen already and every word turning yellow when the choice is
/// inside the game folder (everything only: the plugin alone has nothing to
/// put there); then installing, and opening the editor.
/// </summary>
public sealed class SetupWindow : Window
{
    private readonly SetupTexts _t;
    private readonly Payload _payload;
    private readonly IWindowsPlaces _windows;

    /// <summary>The steps, in order.</summary>
    public const int KindStep = 0, GameStep = 1, FolderStep = 2, EndStep = 3;

    public readonly RadioButton Everything, PluginOnly;
    public readonly TextBox GamePath = SetupLook.Box();
    public readonly TextBlock GameVerdict = SetupLook.Line("");
    public readonly TextBox FolderPath = SetupLook.Box();
    public readonly TextBlock InsideGame;
    /// <summary>Why it cannot install yet - something that has to be closed.</summary>
    public readonly TextBlock FolderStatus = SetupLook.Line("", SetupLook.Bad);
    public readonly CheckBox StartMenu, Desktop;
    public readonly Button Back, Next, Cancel;

    private readonly StackPanel _kindPage = new(), _gamePage = new(), _folderPage = new(), _endPage = new();
    private readonly TextBlock _endHeading = SetupLook.Heading(""), _endText = SetupLook.Line("");
    private readonly Button _open, _close;
    private readonly List<TextBlock> _folderWords = new();
    private int _step;

    public int Step => _step;

    /// <summary>Whether the editor is going in too, or only the plugin.</summary>
    public bool WithEditor => Everything.IsChecked == true;

    /// <summary>The words the end page shows, for reading back.</summary>
    public string EndHeading => _endHeading.Text;
    public string EndText => _endText.Text;
    public bool OffersTheEditor => _open.Visibility == Visibility.Visible;

    public SetupWindow(SetupTexts texts, Payload payload, IWindowsPlaces windows, string? gameGuess, string? folderGuess)
    {
        _t = texts;
        _payload = payload;
        _windows = windows;
        SetupLook.OwnStyles(this);
        Title = _t.T("setup.title");
        Width = 640;
        Height = 430;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = SetupLook.Back;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Foreground = SetupLook.Text;

        // ── What to install ──
        // Only the plugin first, and chosen already: most people running
        // this want to play packs, not make them (the author, 1.8.0).
        PluginOnly = SetupLook.Choice("kind", _t.T("setup.kind.plugin"), _t.T("setup.kind.plugin.help"), true);
        Everything = SetupLook.Choice("kind", _t.T("setup.kind.everything"), _t.T("setup.kind.everything.help"), false);
        _kindPage.Children.Add(SetupLook.Heading(_t.T("setup.kind.heading")));
        _kindPage.Children.Add(PluginOnly);
        _kindPage.Children.Add(Everything);
        Everything.Checked += (_, _) => Go(_step);
        PluginOnly.Checked += (_, _) => Go(_step);

        // ── The game ──
        _gamePage.Children.Add(SetupLook.Heading(_t.T("setup.game.heading")));
        _gamePage.Children.Add(SetupLook.Line(_t.T("setup.game.help")));
        _gamePage.Children.Add(PathRow(GamePath, () => Pick(GamePath, "setup.game.pick")));
        _gamePage.Children.Add(GameVerdict);
        GamePath.TextChanged += (_, _) => Judge();

        // ── Where ModForge goes ──
        var heading = SetupLook.Heading(_t.T("setup.folder.heading"));
        var help = SetupLook.Line(_t.T("setup.folder.help"));
        InsideGame = SetupLook.Line(_t.T("setup.folder.insideGame"));
        InsideGame.Visibility = Visibility.Collapsed;
        _folderWords.AddRange(new[] { heading, help, InsideGame });
        StartMenu = SetupLook.Tick(_t.T("setup.shortcut.start"), true);
        Desktop = SetupLook.Tick(_t.T("setup.shortcut.desktop"), true);
        _folderPage.Children.Add(heading);
        _folderPage.Children.Add(help);
        _folderPage.Children.Add(PathRow(FolderPath, () => Pick(FolderPath, "setup.folder.pick")));
        _folderPage.Children.Add(InsideGame);
        _folderPage.Children.Add(StartMenu);
        _folderPage.Children.Add(Desktop);
        FolderPath.TextChanged += (_, _) => Judge();

        // Why it cannot install yet: under whichever page has the Install
        // button, the folder's or - for the plugin alone - the game's.
        FolderStatus.Margin = new Thickness(0, 10, 0, 0);

        // ── Installing, and done ──
        _open = SetupLook.Button(_t.T("setup.done.open"), primary: true);
        _close = SetupLook.Button(_t.T("setup.close"));
        _open.Click += (_, _) => OpenEditor();
        _close.Click += (_, _) => Close();
        var endButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 8, 0, 0) };
        endButtons.Children.Add(_open);
        endButtons.Children.Add(_close);
        _endPage.Children.Add(_endHeading);
        _endPage.Children.Add(_endText);
        _endPage.Children.Add(endButtons);

        Back = SetupLook.Button(_t.T("setup.back"));
        Next = SetupLook.Button(_t.T("setup.next"), primary: true);
        Cancel = SetupLook.Button(_t.T("setup.cancel"));
        Back.Click += (_, _) => Go(_step == FolderStep || _step == GameStep ? _step - 1 : _step);
        Next.Click += async (_, _) => { if (InstallsNow) await InstallAsync(); else Go(_step + 1); };
        Cancel.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Back);
        buttons.Children.Add(Next);
        buttons.Children.Add(Cancel);

        var band = new Border
        {
            Background = SetupLook.Band, Padding = new Thickness(20, 12, 20, 12),
            BorderBrush = SetupLook.Accent, BorderThickness = new Thickness(0, 0, 0, 2),
            Child = new TextBlock
            {
                Inlines =
                {
                    new System.Windows.Documents.Run("ModForge") { FontWeight = FontWeights.SemiBold, Foreground = SetupLook.Accent, FontSize = 16 },
                    new System.Windows.Documents.Run("   " + SMSModForge.Shared.ForgeVersion.Current) { Foreground = SetupLook.Muted },
                },
            },
        };
        var pages = new Grid { Margin = new Thickness(24, 18, 24, 0) };
        pages.Children.Add(_kindPage);
        pages.Children.Add(_gamePage);
        pages.Children.Add(_folderPage);
        pages.Children.Add(_endPage);
        var foot = new Border { Padding = new Thickness(20, 12, 20, 16), Child = buttons };

        var root = new DockPanel();
        DockPanel.SetDock(band, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(band);
        root.Children.Add(foot);
        root.Children.Add(pages);
        Content = root;

        GamePath.Text = gameGuess ?? "";
        FolderPath.Text = folderGuess ?? Places.DefaultInstallFolder;
        Go(KindStep);
    }

    /// <summary>Whether Next installs from here: the folder page, or the
    /// game's page when only the plugin goes in.</summary>
    private bool InstallsNow => _step == FolderStep || (_step == GameStep && !WithEditor);

    private Grid PathRow(TextBox box, Action browse)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var button = SetupLook.Button(_t.T("setup.browse"));
        button.Click += (_, _) => browse();
        Grid.SetColumn(button, 1);
        row.Children.Add(box);
        row.Children.Add(button);
        return row;
    }

    /// <summary>Windows' own folder picker - never while a test runs.</summary>
    private void Pick(TextBox box, string titleKey)
    {
        if (Program.Testing) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = _t.T(titleKey) };
        if (Directory.Exists(box.Text)) dialog.InitialDirectory = box.Text;
        if (dialog.ShowDialog(this) == true) box.Text = dialog.FolderName;
    }

    /// <summary>Show a step. There is no folder page for the plugin alone: its
    /// game page installs.</summary>
    public void Go(int step)
    {
        if (step == FolderStep && !WithEditor) step = GameStep;
        _step = Math.Clamp(step, KindStep, EndStep);
        _kindPage.Visibility = _step == KindStep ? Visibility.Visible : Visibility.Collapsed;
        _gamePage.Visibility = _step == GameStep ? Visibility.Visible : Visibility.Collapsed;
        _folderPage.Visibility = _step == FolderStep ? Visibility.Visible : Visibility.Collapsed;
        _endPage.Visibility = _step == EndStep ? Visibility.Visible : Visibility.Collapsed;
        Back.Visibility = _step == GameStep || _step == FolderStep ? Visibility.Visible : Visibility.Collapsed;
        Next.Visibility = _step < EndStep ? Visibility.Visible : Visibility.Collapsed;
        Cancel.Visibility = _step < EndStep ? Visibility.Visible : Visibility.Collapsed;
        SetupLook.SetText(Next, _t.T(InstallsNow ? "setup.install" : "setup.next"));

        // The line saying what has to be closed sits on the page with Install.
        (FolderStatus.Parent as Panel)?.Children.Remove(FolderStatus);
        (WithEditor ? _folderPage : _gamePage).Children.Add(FolderStatus);
        Judge();
    }

    /// <summary>Say what the boxes hold, and let the player on only from a
    /// real game folder.</summary>
    public void Judge()
    {
        var verdict = GameFolder.Check(GamePath.Text);
        (GameVerdict.Text, GameVerdict.Foreground) = verdict switch
        {
            GameFolder.Verdict.Game => ("✓ " + _t.T("setup.game.ok"), SetupLook.Good),
            GameFolder.Verdict.NoFolder => ("✗ " + _t.T("setup.game.missing"), SetupLook.Bad),
            GameFolder.Verdict.NotTheGame => ("✗ " + _t.F("setup.game.notGame", ("exe", GameFolder.Exe)), SetupLook.Bad),
            _ => (_t.T("setup.game.empty"), SetupLook.Muted),
        };

        bool inside = Places.IsInside(FolderPath.Text, GamePath.Text);
        InsideGame.Visibility = inside ? Visibility.Visible : Visibility.Collapsed;
        // Inside the game folder every word on the page turns yellow (the
        // author, 1.8.0) - allowed, but plainly not recommended.
        foreach (var words in _folderWords)
            words.Foreground = inside ? SetupLook.Warning : ReferenceEquals(words, _folderWords[0]) ? SetupLook.Text : SetupLook.Muted;
        SetupLook.Words(StartMenu).Foreground = SetupLook.Words(Desktop).Foreground = inside ? SetupLook.Warning : SetupLook.Text;

        Next.IsEnabled = _step switch
        {
            KindStep => true,
            GameStep => verdict == GameFolder.Verdict.Game,
            FolderStep => verdict == GameFolder.Verdict.Game && !string.IsNullOrWhiteSpace(FolderPath.Text),
            _ => false,
        };
    }

    /// <summary>The words the folder page is drawn in now - for a test to
    /// measure what is on screen.</summary>
    public IEnumerable<Brush> FolderPageInk()
        => _folderWords.Where(w => w.IsVisible).Select(w => w.Foreground)
                       .Concat(new[] { SetupLook.Words(StartMenu).Foreground, SetupLook.Words(Desktop).Foreground });

    private string? _problem;
    public string? Problem => _problem;

    public async Task InstallAsync()
    {
        string game = GamePath.Text.Trim(), folder = FolderPath.Text.Trim();
        bool editor = WithEditor;
        // Files that are running cannot be replaced: say which, and stay here.
        string? busy = editor && Running(Path.Combine(folder, Installer.EditorExe)) ? "setup.closeEditor"
                     : Running(Path.Combine(game, GameFolder.Exe)) ? "setup.closeGame" : null;
        if (busy != null)
        {
            FolderStatus.Text = _t.T(busy);
            return;
        }
        FolderStatus.Text = "";

        var options = new Installer.Options(game, folder, editor && StartMenu.IsChecked == true, editor && Desktop.IsChecked == true, editor);
        Next.IsEnabled = Back.IsEnabled = Cancel.IsEnabled = false;
        _endHeading.Text = _t.T("setup.installing");
        _endText.Text = "";
        _open.Visibility = _close.Visibility = Visibility.Collapsed;
        Go(EndStep);
        try
        {
            var result = await Task.Run(() => Installer.Run(options, _payload, _windows));
            _endHeading.Text = _t.T(editor ? "setup.done.heading" : "setup.done.pluginHeading");
            _endText.Text = (editor ? _t.F("setup.done.help", ("folder", folder)) : _t.T("setup.done.pluginHelp"))
                            + (result.BepInExWasThere ? "\n\n" + _t.T("setup.bepinexKept") : "");
            _open.Visibility = editor ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception e)
        {
            _problem = e.Message;
            _endHeading.Text = _t.T("setup.failedHeading");
            _endText.Text = _t.F("setup.failed", ("problem", e.Message));
        }
        _close.Visibility = Visibility.Visible;
    }

    private static bool Running(string exe)
    {
        string name = Path.GetFileNameWithoutExtension(exe);
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { if (string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true; }
            catch (Exception) { /* another user's, or gone */ }
        }
        return false;
    }

    private void OpenEditor()
    {
        if (Program.Testing) return;
        string exe = Path.Combine(FolderPath.Text.Trim(), Installer.EditorExe);
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! }); }
        catch (Exception) { }
        Close();
    }
}
