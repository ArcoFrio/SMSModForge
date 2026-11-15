using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// "Load this pack on start" (the author, 1.9.0): one setting, on once a pack
/// has been loaded, until it is unticked - and then off for every pack until it
/// is ticked again; changeable only with a pack loaded; the same tick in the
/// File menu and on the ModForge tab. Measured on the window's own controls.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class LoadPackOnStartTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-onstart-" + Guid.NewGuid().ToString("N"));
    private readonly (bool On, string Last, string Pending) _was;

    public LoadPackOnStartTests(ITestOutputHelper o)
    {
        _out = o;
        _was = (EditorPrefs.LoadPackOnStart, EditorPrefs.LastPack, EditorPrefs.StartLoadPending);
        // As on a machine that never loaded a pack.
        EditorPrefs.LoadPackOnStart = true;
        EditorPrefs.LastPack = "";
        EditorPrefs.StartLoadPending = "";
    }

    public void Dispose()
    {
        EditorPrefs.LoadPackOnStart = _was.On;
        EditorPrefs.LastPack = _was.Last;
        EditorPrefs.StartLoadPending = _was.Pending;
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string APack(string name)
    {
        string dir = Path.Combine(_root, name);
        PackRepository.Save(PackRepository.CreateEmpty(name), dir);
        return dir;
    }

    [Fact]
    public void TheTickIsOnOnceAPackIsLoaded_StaysOffOnceUnticked_AndOnlyChangesWithAPack()
    {
        string a = APack("onstart.a"), b = APack("onstart.b");
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var menu = (MenuItem)window.FindName("LoadPackOnStartMenu");
            var box = (CheckBox)window.FindName("LoadPackOnStartBox");
            void Say(string when) => _out.WriteLine($"{when}: menu {menu.IsChecked}/{(menu.IsEnabled ? "on" : "off")}, box {box.IsChecked}/{(box.IsEnabled ? "on" : "off")}");

            // Nothing ever loaded: unticked, and nothing to change it on.
            Say("no pack");
            Assert.False(menu.IsChecked);
            Assert.False(menu.IsEnabled);
            Assert.False(box.IsEnabled);

            // A pack loaded: ticked, in both places, and changeable.
            vm.OpenPackFromPath(a);
            WindowHarness.Pump();
            Say("pack A");
            Assert.True(menu.IsChecked);
            Assert.True(box.IsChecked);
            Assert.True(menu.IsEnabled && box.IsEnabled);
            Assert.Equal(a, EditorPrefs.LastPack);

            // Unticked on the ModForge tab, as a person unticks it: off in the menu too.
            ((System.Windows.Automation.Provider.IToggleProvider)
                new System.Windows.Automation.Peers.CheckBoxAutomationPeer(box)
                    .GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)).Toggle();
            WindowHarness.Pump();
            Say("unticked");
            Assert.False(box.IsChecked);
            Assert.False(menu.IsChecked);
            Assert.False(EditorPrefs.LoadPackOnStart);

            // Another pack: still off.
            vm.OpenPackFromPath(b);
            WindowHarness.Pump();
            Say("pack B");
            Assert.False(menu.IsChecked);
            Assert.False(box.IsChecked);
            Assert.Equal(b, EditorPrefs.LastPack);

            // Ticked again from the menu: on in both.
            menu.IsChecked = true;
            WindowHarness.Pump();
            Say("ticked from the menu");
            Assert.True(box.IsChecked);
            Assert.True(EditorPrefs.LoadPackOnStart);
        });
    }

    [Fact]
    public void AtStart_TheLastPackIsOpened_UnlessOff_Gone_OrItDidNotSurviveTheLastStart()
    {
        string a = APack("onstart.a");
        WindowHarness.Run(window =>
        {
            var fresh = new MainViewModel();

            // Never loaded: nothing.
            Assert.Null(fresh.PackToOpenOnStart().Open);

            EditorPrefs.LastPack = a;
            Assert.Equal(a, fresh.PackToOpenOnStart().Open);

            // Off: nothing.
            EditorPrefs.LoadPackOnStart = false;
            Assert.Null(fresh.PackToOpenOnStart().Open);
            EditorPrefs.LoadPackOnStart = true;

            // The start before did not survive opening it: not opened, said, and
            // only that once.
            EditorPrefs.StartLoadPending = a;
            var (open, say, warn) = fresh.PackToOpenOnStart();
            _out.WriteLine("after a start that did not survive: " + warn);
            Assert.Null(open);
            Assert.Contains(a, warn);
            Assert.Equal("", EditorPrefs.StartLoadPending);
            Assert.Equal(a, fresh.PackToOpenOnStart().Open);

            // Gone from its folder: not opened, said in passing.
            string gone = Path.Combine(_root, "moved-away");
            EditorPrefs.LastPack = gone;
            (open, say, warn) = fresh.PackToOpenOnStart();
            _out.WriteLine("gone: " + say);
            Assert.Null(open);
            Assert.Contains(gone, say);
            Assert.Equal("", warn);

            // A pack already open is never replaced.
            EditorPrefs.LastPack = a;
            var busy = (MainViewModel)window.DataContext;
            busy.OpenPackFromPath(APack("onstart.open"));
            WindowHarness.Pump();
            EditorPrefs.LastPack = a;
            Assert.Null(busy.PackToOpenOnStart().Open);

            // And a test run never opens anything by itself.
            busy.OpenPackOnStart();
            Assert.EndsWith("onstart.open", busy.PackRoot);
        });
    }
}
