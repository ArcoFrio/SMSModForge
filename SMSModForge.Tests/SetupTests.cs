extern alias setup;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using setup::SMSModForge.Setup;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The installer (the author, 1.8.0): the Starmaker Story folder is required
/// and checked to really be the game; where ModForge goes is the player's
/// choice, a good folder chosen already, and every word turns yellow when it
/// is inside the game folder. What it installs, and what an uninstall takes
/// away again - in temporary folders, with Windows' registry and Start menu
/// stood in for: a test never touches the real ones.
/// </summary>
public sealed class SetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-setup-" + Guid.NewGuid().ToString("N"));
    private readonly string _settingsWas = Places.EditorSettings;

    public SetupTests()
    {
        Directory.CreateDirectory(_root);
        Places.EditorSettings = Path.Combine(_root, "editor settings");
    }

    public void Dispose()
    {
        Places.EditorSettings = _settingsWas;
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Game(bool withBepInEx = false)
    {
        string game = Path.Combine(_root, "Starmaker Story");
        Directory.CreateDirectory(Path.Combine(game, GameFolder.Data));
        File.WriteAllText(Path.Combine(game, GameFolder.Exe), "game");
        if (withBepInEx)
        {
            Directory.CreateDirectory(Path.Combine(game, "BepInEx", "core"));
            Directory.CreateDirectory(Path.Combine(game, "BepInEx", "plugins"));
            File.WriteAllText(Path.Combine(game, "BepInEx", "core", "BepInEx.dll"), "the player's own loader");
            File.WriteAllText(Path.Combine(game, "BepInEx", "plugins", "Newtonsoft.Json.dll"), "another mod's");
        }
        return game;
    }

    private static byte[] Zip(params (string Name, string Text)[] files)
    {
        var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in files)
            {
                var entry = zip.CreateEntry(name);
                if (!name.EndsWith("/"))
                    using (var w = new StreamWriter(entry.Open())) w.Write(text);
            }
        return bytes.ToArray();
    }

    internal static Payload FakePayload() => new()
    {
        Editor = () => new MemoryStream(Zip(("SMSModForge.exe", "editor"), ("Languages/en.txt", "setup.title = ModForge setup"))),
        Plugin = () => new MemoryStream(Zip(("winhttp.dll", "doorstop"), ("BepInEx/core/BepInEx.dll", "shipped loader"),
                                            ("BepInEx/plugins/Newtonsoft.Json.dll", "shipped json"),
                                            ("BepInEx/plugins/SMSModForge.PackPlugin.dll", "modforge"),
                                            ("BepInEx/plugins/SMSModForge/Languages/de.txt", "deutsch"),
                                            ("Mods/", ""))),
        Uninstaller = () => new MemoryStream(Encoding.UTF8.GetBytes("uninstaller")),
    };

    /// <summary>Windows' places, kept in memory and temporary folders.</summary>
    internal sealed class FakeWindows : IWindowsPlaces
    {
        public readonly string Root;
        public FakeWindows(string root) { Root = root; Directory.CreateDirectory(StartMenuFolder); Directory.CreateDirectory(DesktopFolder); }
        public string? Registered, Icon;
        public string? InstalledFolder() => Registered;
        public void Register(string installFolder, string uninstaller, string icon, string version, long sizeKb)
            => (Registered, Icon) = (installFolder, icon);
        public void Unregister() => Registered = null;
        public string StartMenuFolder => Path.Combine(Root, "start");
        public string DesktopFolder => Path.Combine(Root, "desktop");
        public void Shortcut(string path, string target, string workingFolder) => File.WriteAllText(path, target);
    }

    // ── The game folder ─────────────────────────────────────────────────

    [Fact]
    public void OnlyTheGameIsTheGame()
    {
        Assert.Equal(GameFolder.Verdict.Empty, GameFolder.Check(" "));
        Assert.Equal(GameFolder.Verdict.NoFolder, GameFolder.Check(Path.Combine(_root, "nowhere")));
        Assert.Equal(GameFolder.Verdict.NotTheGame, GameFolder.Check(_root));
        Assert.Equal(GameFolder.Verdict.Game, GameFolder.Check(Game()));

        // The control: the exe alone, without its data, is not the game.
        string half = Path.Combine(_root, "half");
        Directory.CreateDirectory(half);
        File.WriteAllText(Path.Combine(half, GameFolder.Exe), "");
        Assert.Equal(GameFolder.Verdict.NotTheGame, GameFolder.Check(half));
    }

    [Fact]
    public void SteamsLibrariesAreReadFromItsOwnList()
    {
        string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}";
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, GameFolder.LibrariesIn(vdf));
    }

    [Fact]
    public void InsideTheGameFolderIsByWholeFolderNames()
    {
        string game = @"D:\Games\Starmaker Story 1.8E";
        Assert.True(Places.IsInside(@"D:\Games\Starmaker Story 1.8E\ModForge", game));
        Assert.True(Places.IsInside(@"d:\games\starmaker story 1.8e\", game));
        // The control: a folder whose name only begins the same is beside it.
        Assert.False(Places.IsInside(@"D:\Games\Starmaker Story 1.8E - ModForge test", game));
        Assert.False(Places.IsInside(Places.DefaultInstallFolder, game));
    }

    [Fact]
    public void TheFolderChosenAlreadyIsWindowsOwnForOnePerson()
        => Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SMSModForge"),
                        Places.DefaultInstallFolder);

    // ── Installing ──────────────────────────────────────────────────────

    [Fact]
    public void AGameWithoutBepInExGetsTheWholeBundle_AndTheEditorItsFolder()
    {
        string game = Game();
        string install = Path.Combine(_root, "ModForge");
        var windows = new FakeWindows(Path.Combine(_root, "windows"));
        var result = Installer.Run(new Installer.Options(game, install, true, true), FakePayload(), windows);

        Assert.False(result.BepInExWasThere);
        Assert.True(File.Exists(Path.Combine(install, "SMSModForge.exe")));
        Assert.True(File.Exists(Path.Combine(install, Installer.UninstallerExe)));
        Assert.True(File.Exists(Path.Combine(game, "winhttp.dll")));
        Assert.Equal("shipped loader", File.ReadAllText(Path.Combine(game, "BepInEx", "core", "BepInEx.dll")));
        Assert.True(File.Exists(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
        Assert.True(Directory.Exists(Path.Combine(game, "Mods")));
        Assert.Equal(install, windows.Registered);
        Assert.True(File.Exists(Path.Combine(windows.StartMenuFolder, Installer.ShortcutName)));
        Assert.True(File.Exists(Path.Combine(windows.DesktopFolder, Installer.ShortcutName)));

        // The editor is told where the game is - into its settings, keeping theirs.
        string prefs = File.ReadAllText(Path.Combine(Places.EditorSettings, "prefs.json"));
        Assert.Contains("gameFolder", prefs);
        Assert.Contains("Starmaker Story", prefs);
    }

    [Fact]
    public void AGameWithBepInExKeepsItsLoaderAndItsJson()
    {
        string game = Game(withBepInEx: true);
        string install = Path.Combine(_root, "ModForge");
        Directory.CreateDirectory(Places.EditorSettings);
        File.WriteAllText(Path.Combine(Places.EditorSettings, "prefs.json"), "{ \"theme\": \"Frost\" }");
        var result = Installer.Run(new Installer.Options(game, install, false, false), FakePayload(), new FakeWindows(Path.Combine(_root, "windows")));

        Assert.True(result.BepInExWasThere);
        Assert.Equal("the player's own loader", File.ReadAllText(Path.Combine(game, "BepInEx", "core", "BepInEx.dll")));
        Assert.Equal("another mod's", File.ReadAllText(Path.Combine(game, "BepInEx", "plugins", "Newtonsoft.Json.dll")));
        Assert.False(File.Exists(Path.Combine(game, "winhttp.dll")));
        Assert.Equal("modforge", File.ReadAllText(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
        string prefs = File.ReadAllText(Path.Combine(Places.EditorSettings, "prefs.json"));
        Assert.Contains("Frost", prefs);        // theirs, kept
        Assert.Contains("gameFolder", prefs);   // ours, added
    }

    [Fact]
    public void OnlyThePluginPutsThePluginInTheGame_AndNoEditorNoShortcutsNoSettings()
    {
        string game = Game();
        string install = Path.Combine(_root, "ModForge");
        var windows = new FakeWindows(Path.Combine(_root, "windows"));
        // Shortcuts asked for, and still none: there is no editor to point at.
        var result = Installer.Run(new Installer.Options(game, install, true, true, Editor: false), FakePayload(), windows);

        Assert.Equal("modforge", File.ReadAllText(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
        Assert.True(File.Exists(Path.Combine(game, "winhttp.dll")));
        Assert.True(Directory.Exists(Path.Combine(game, "Mods")));
        Assert.False(File.Exists(Path.Combine(install, Installer.EditorExe)));
        Assert.False(File.Exists(Path.Combine(install, "Languages", "en.txt")));
        Assert.True(File.Exists(Path.Combine(install, Installer.UninstallerExe)));   // the Apps entry's way out
        Assert.Empty(Directory.GetFiles(windows.StartMenuFolder));
        Assert.Empty(Directory.GetFiles(windows.DesktopFolder));
        Assert.False(File.Exists(Path.Combine(Places.EditorSettings, "prefs.json")));
        Assert.Equal(install, windows.Registered);
        Assert.Equal(Path.Combine(install, Installer.UninstallerExe), windows.Icon);
        Assert.Equal(0, result.EditorFiles);
        Assert.False(Manifest.Read(install)!.HasEditor);
    }

    [Fact]
    public void UninstallingOnlyThePluginTakesThePluginOut_EvenUnasked()
    {
        // Only the plugin went in, so the plugin is what an uninstall is for:
        // it comes out though the question was never asked.
        string game = Game();
        string install = Path.Combine(_root, "ModForge");
        var windows = new FakeWindows(Path.Combine(_root, "windows"));
        Installer.Run(new Installer.Options(game, install, false, false, Editor: false), FakePayload(), windows);
        File.WriteAllText(Path.Combine(game, "Mods", "my pack.smspack"), "a player's pack");

        Uninstaller.Run(install, plugin: false, windows);

        Assert.False(Directory.Exists(install));
        Assert.Null(windows.Registered);
        Assert.False(File.Exists(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
        Assert.True(File.Exists(Path.Combine(game, "Mods", "my pack.smspack")));
    }

    [Fact]
    public void OnlyThePluginOverEverythingKeepsTheEditor()
    {
        string game = Game();
        string install = Path.Combine(_root, "ModForge");
        var windows = new FakeWindows(Path.Combine(_root, "windows"));
        Installer.Run(new Installer.Options(game, install, true, false), FakePayload(), windows);
        Installer.Run(new Installer.Options(game, install, false, false, Editor: false), FakePayload(), windows);

        Assert.True(File.Exists(Path.Combine(install, Installer.EditorExe)));
        Assert.True(File.Exists(Path.Combine(windows.StartMenuFolder, Installer.ShortcutName)));
        var manifest = Manifest.Read(install)!;
        Assert.True(manifest.HasEditor);
        Assert.Equal(Path.Combine(install, Installer.EditorExe), windows.Icon);

        // Still the editor's install: unasked, the plugin stays (the control
        // for the case above), and the editor and its shortcut go.
        Uninstaller.Run(install, plugin: false, windows);
        Assert.False(File.Exists(Path.Combine(install, Installer.EditorExe)));
        Assert.False(File.Exists(Path.Combine(windows.StartMenuFolder, Installer.ShortcutName)));
        Assert.True(File.Exists(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
    }

    [Fact]
    public void ANotGameFolderIsRefused()
        => Assert.Throws<InvalidOperationException>(() =>
               Installer.Run(new Installer.Options(_root, Path.Combine(_root, "ModForge"), false, false), FakePayload(), new FakeWindows(Path.Combine(_root, "w"))));

    // ── Uninstalling ────────────────────────────────────────────────────

    [Fact]
    public void UninstallingTakesWhatWasPutIn_AndLeavesPacksAndBepInEx()
    {
        string game = Game(withBepInEx: true);
        string install = Path.Combine(_root, "ModForge");
        var windows = new FakeWindows(Path.Combine(_root, "windows"));
        Installer.Run(new Installer.Options(game, install, true, false), FakePayload(), windows);
        File.WriteAllText(Path.Combine(game, "Mods", "my pack.smspack"), "a player's pack");

        Uninstaller.Run(install, plugin: true, windows);

        Assert.False(Directory.Exists(install));
        Assert.Null(windows.Registered);
        Assert.False(File.Exists(Path.Combine(windows.StartMenuFolder, Installer.ShortcutName)));
        Assert.False(File.Exists(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
        Assert.False(Directory.Exists(Path.Combine(game, "BepInEx", "plugins", "SMSModForge")));
        Assert.True(File.Exists(Path.Combine(game, "Mods", "my pack.smspack")));
        Assert.Equal("the player's own loader", File.ReadAllText(Path.Combine(game, "BepInEx", "core", "BepInEx.dll")));
        Assert.True(File.Exists(Path.Combine(game, "BepInEx", "plugins", "Newtonsoft.Json.dll")));
    }

    [Fact]
    public void AFolderThatWasNotEmptyKeepsWhatWasThere()
    {
        string game = Game();
        string install = Path.Combine(_root, "Tools");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "theirs.txt"), "not ModForge's");
        var windows = new FakeWindows(Path.Combine(_root, "windows"));
        Installer.Run(new Installer.Options(game, install, false, false), FakePayload(), windows);

        Uninstaller.Run(install, plugin: false, windows);

        Assert.True(File.Exists(Path.Combine(install, "theirs.txt")));
        Assert.False(File.Exists(Path.Combine(install, "SMSModForge.exe")));
        // Not asked to: the plugin stays in the game.
        Assert.True(File.Exists(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
    }

    /// <summary>
    /// The real release zips, installed into a stand-in game and folder, and
    /// uninstalled again - when SMSMODFORGE_SETUP_ZIPS names the folder that
    /// holds them (its newest Editor and Plugin zips). A release's layout is
    /// what the installer has to get right, and no fake zip proves that.
    /// </summary>
    [Fact]
    public void TheRealZipsInstallAndUninstall()
    {
        string? release = Environment.GetEnvironmentVariable("SMSMODFORGE_SETUP_ZIPS");
        if (string.IsNullOrWhiteSpace(release)) return;
        string Newest(string kind) => Directory.GetFiles(release, $"Starmaker - ModForge {kind} *.zip")
            .OrderBy(f => Version.Parse(Path.GetFileNameWithoutExtension(f).Split(' ').Last())).Last();
        var payload = new Payload
        {
            Editor = () => File.OpenRead(Newest("Editor")),
            Plugin = () => File.OpenRead(Newest("Plugin")),
            Uninstaller = () => new MemoryStream(Encoding.UTF8.GetBytes("uninstaller")),
        };
        string game = Game();
        string install = Path.Combine(_root, "Programs", "SMSModForge");
        var windows = new FakeWindows(Path.Combine(_root, "windows"));

        var result = Installer.Run(new Installer.Options(game, install, true, false), payload, windows);
        Assert.False(result.BepInExWasThere);
        Assert.True(File.Exists(Path.Combine(install, Installer.EditorExe)));
        Assert.True(File.Exists(Path.Combine(install, "Languages", "en.txt")));
        Assert.True(File.Exists(Path.Combine(game, "BepInEx", "core", "BepInEx.dll")));
        Assert.True(File.Exists(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
        Assert.True(File.Exists(Path.Combine(game, "winhttp.dll")));

        Uninstaller.Run(install, plugin: true, windows);
        Assert.False(Directory.Exists(install));
        Assert.False(File.Exists(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
        Assert.True(File.Exists(Path.Combine(game, "BepInEx", "core", "BepInEx.dll")));   // BepInEx stays
    }

    // ── Its words ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("zh-TW", "zh-Hans")]
    [InlineData("fr-CA", "fr")]
    [InlineData("de-AT", "de")]
    [InlineData("sv-SE", "en")]
    public void ItSpeaksTheComputersLanguage(string culture, string code)
        => Assert.Equal(code, SetupTexts.Pick(new CultureInfo(culture)));

    [Fact]
    public void ItsWordsComeFromTheEditorsLanguageFiles()
    {
        string languages = Path.Combine(TranslationCoverageTests.EditorDir, "Languages");
        var english = SetupTexts.FromFolder(languages, new CultureInfo("en-US"));
        var german = SetupTexts.FromFolder(languages, new CultureInfo("de-DE"));
        Assert.Equal("Where is Starmaker Story?", english.T("setup.game.heading"));
        Assert.NotEqual(english.T("setup.game.heading"), german.T("setup.game.heading"));
        Assert.Equal("setup.no.such", english.T("setup.no.such"));   // the control: a missing key shows as itself
    }
}

/// <summary>The installer's window, built on the harness thread.</summary>
[Trait("Speed", "Slow")]
public sealed class SetupWindowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-setupwin-" + Guid.NewGuid().ToString("N"));
    private readonly string _settingsWas = Places.EditorSettings;

    public SetupWindowTests()
    {
        Directory.CreateDirectory(_root);
        Program.Testing = true;
        Places.EditorSettings = Path.Combine(_root, "editor settings");
    }

    public void Dispose()
    {
        Places.EditorSettings = _settingsWas;
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private sealed class NoWindows : IWindowsPlaces
    {
        public string? InstalledFolder() => null;
        public void Register(string a, string b, string c, string d, long e) => throw new InvalidOperationException("a test registered");
        public void Unregister() { }
        public string StartMenuFolder => "";
        public string DesktopFolder => "";
        public void Shortcut(string a, string b, string c) => throw new InvalidOperationException("a test made a shortcut");
    }

    private static SetupTexts English()
        => SetupTexts.FromFolder(Path.Combine(TranslationCoverageTests.EditorDir, "Languages"), new CultureInfo("en-US"));

    private string Game()
    {
        string game = Path.Combine(_root, "Starmaker Story");
        Directory.CreateDirectory(Path.Combine(game, GameFolder.Data));
        File.WriteAllText(Path.Combine(game, GameFolder.Exe), "");
        return game;
    }

    private SetupWindow Open(IWindowsPlaces windows, Payload payload, string? game)
    {
        var window = new SetupWindow(English(), payload, windows, game, Path.Combine(_root, "ModForge"))
        {
            Left = -10000, Top = -10000, ShowInTaskbar = false,
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static void Press(System.Windows.Controls.Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        WindowHarness.Pump();
        button.UpdateLayout();
    }

    /// <summary>Wait for the end page to say something other than "Installing".</summary>
    private static void UntilInstalled(SetupWindow window, SetupTexts texts)
    {
        var deadline = DateTime.Now.AddSeconds(15);
        while ((window.Step != SetupWindow.EndStep || window.EndHeading == texts.T("setup.installing")) && DateTime.Now < deadline)
        {
            WindowHarness.Pump();
            System.Threading.Thread.Sleep(20);
        }
        window.UpdateLayout();
    }

    [Fact]
    public void ItAsksFirstWhatToInstall_AndSaysWhatEachIsFor()
    {
        WindowHarness.Run(_ =>
        {
            var texts = English();
            var window = Open(new NoWindows(), new Payload(), null);

            Assert.Equal(SetupWindow.KindStep, window.Step);
            // Only the plugin chosen already, and drawn first: what most people
            // running this want (the author, 1.8.0).
            Assert.True(window.PluginOnly.IsChecked, "only the plugin is not chosen already");
            Assert.False(window.Everything.IsChecked);
            Assert.False(window.WithEditor);
            double pluginAt = window.PluginOnly.TranslatePoint(new Point(0, 0), window).Y;
            double everythingAt = window.Everything.TranslatePoint(new Point(0, 0), window).Y;
            Assert.True(pluginAt < everythingAt, $"only the plugin is drawn at {pluginAt}, below everything at {everythingAt}");
            foreach (var (choice, name, help) in new[] { (window.PluginOnly, "setup.kind.plugin", "setup.kind.plugin.help"),
                                                         (window.Everything, "setup.kind.everything", "setup.kind.everything.help") })
            {
                var words = SetupLook.ChoiceWords(choice);
                Assert.True(words.Name.IsVisible && words.Help.IsVisible, name + " is not drawn with its help");
                Assert.Equal(texts.T(name), words.Name.Text);
                Assert.Equal(texts.T(help), words.Help.Text);
                Assert.True(words.Help.ActualHeight > words.Name.ActualHeight, name + "'s help is cut to one line");
            }
            Assert.True(window.Next.IsEnabled);
            Assert.False(window.Back.IsVisible);
            Snap(window, "setup-0-kind");
            window.Close();
        });
    }

    [Fact]
    public void OnlyARealGameFolderLetsThePlayerOn_AndInsideItEveryWordTurnsYellow()
    {
        WindowHarness.Run(_ =>
        {
            string game = Game();
            var window = Open(new NoWindows(), new Payload(), null);
            window.Everything.IsChecked = true;
            WindowHarness.Pump();
            Press(window.Next);
            Assert.Equal(SetupWindow.GameStep, window.Step);

            Assert.False(window.Next.IsEnabled, "on with no game folder");
            window.GamePath.Text = _root;
            Assert.False(window.Next.IsEnabled, "on with a folder that is not the game");
            window.GamePath.Text = game;
            Assert.True(window.Next.IsEnabled, "a real game folder does not let the player on");

            Snap(window, "setup-1-game");
            Press(window.Next);
            window.UpdateLayout();
            Assert.Equal(SetupWindow.FolderStep, window.Step);
            Snap(window, "setup-2-folder");

            // Outside the game: no yellow (the control).
            Assert.DoesNotContain(window.FolderPageInk(), ink => ReferenceEquals(ink, SetupLook.Warning));
            Assert.False(window.InsideGame.IsVisible);

            window.FolderPath.Text = Path.Combine(game, "ModForge");
            WindowHarness.Pump();
            window.UpdateLayout();
            Assert.True(window.InsideGame.IsVisible, "no warning inside the game folder");
            Assert.All(window.FolderPageInk(), ink => Assert.Same(SetupLook.Warning, ink));
            Assert.True(window.Next.IsEnabled, "inside the game folder is warned against, not forbidden");
            Snap(window, "setup-3-inside-game");
            window.Close();
        });
    }

    [Fact]
    public void OnlyThePluginInstallsFromTheGamePage_AndTheEndSaysWhereModsGo()
    {
        WindowHarness.Run(_ =>
        {
            var texts = English();
            string game = Game();
            var windows = new SetupTests.FakeWindows(Path.Combine(_root, "windows"));
            var window = Open(windows, SetupTests.FakePayload(), game);

            // Everything: the game page goes on to the folder (the control).
            window.Everything.IsChecked = true;
            WindowHarness.Pump();
            Press(window.Next);
            Assert.Equal(SetupWindow.GameStep, window.Step);
            Assert.Equal(texts.T("setup.next"), SetupLook.Words(window.Next).Text);
            Press(window.Back);
            Assert.Equal(SetupWindow.KindStep, window.Step);

            // Only the plugin: the game page installs - there is no folder to ask about.
            window.PluginOnly.IsChecked = true;
            WindowHarness.Pump();
            Assert.False(window.WithEditor);
            Press(window.Next);
            Assert.Equal(SetupWindow.GameStep, window.Step);
            Assert.Equal(texts.T("setup.install"), SetupLook.Words(window.Next).Text);
            Assert.True(window.Next.IsEnabled);
            Snap(window, "setup-4-plugin-game");

            Press(window.Next);
            UntilInstalled(window, texts);
            Snap(window, "setup-5-plugin-done");
            Assert.Null(window.Problem);
            Assert.Equal(texts.T("setup.done.pluginHeading"), window.EndHeading);
            Assert.StartsWith(texts.T("setup.done.pluginHelp"), window.EndText);
            Assert.False(window.OffersTheEditor, "the end offers an editor that was not installed");

            Assert.True(File.Exists(Path.Combine(game, "BepInEx", "plugins", "SMSModForge.PackPlugin.dll")));
            Assert.False(File.Exists(Path.Combine(_root, "ModForge", Installer.EditorExe)));
            Assert.Empty(Directory.GetFiles(windows.StartMenuFolder));
            Assert.Empty(Directory.GetFiles(windows.DesktopFolder));
            window.Close();
        });
    }

    [Fact]
    public void EverythingEndsOfferingTheEditor()
    {
        // The control for the end page above.
        WindowHarness.Run(_ =>
        {
            var texts = English();
            string game = Game();
            var windows = new SetupTests.FakeWindows(Path.Combine(_root, "windows"));
            var window = Open(windows, SetupTests.FakePayload(), game);
            window.Everything.IsChecked = true;
            WindowHarness.Pump();
            Press(window.Next);
            Press(window.Next);
            Assert.Equal(SetupWindow.FolderStep, window.Step);
            Assert.Equal(texts.T("setup.install"), SetupLook.Words(window.Next).Text);

            Press(window.Next);
            UntilInstalled(window, texts);
            Assert.Null(window.Problem);
            Assert.Equal(texts.T("setup.done.heading"), window.EndHeading);
            Assert.True(window.OffersTheEditor);
            Assert.True(File.Exists(Path.Combine(_root, "ModForge", Installer.EditorExe)));
            window.Close();
        });
    }

    /// <summary>A picture of the window, when SMSMODFORGE_SNAPSHOTS names a folder.</summary>
    private static void Snap(Window window, string name)
    {
        string? folder = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        ThemeSnapshotTests.Save(ThemeSnapshotTests.Render((FrameworkElement)window.Content, window.Background), Path.Combine(folder, name + ".png"));
    }
}
