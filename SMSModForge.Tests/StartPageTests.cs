using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Where File ▸ New puts a pack (Model.NewPackPlace, 1.10.0): the chosen folder when it is empty or not there yet,
/// else a folder of the pack's name inside it - numbered when that one is taken too.
/// </summary>
public sealed class NewPackPlaceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-newpack-" + Guid.NewGuid().ToString("N"));

    public NewPackPlaceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    [Fact]
    public void AnEmptyFolderIsThePacksOwn_AndOneWithAnythingInItGetsAFolderOfThePacksName()
    {
        string empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);
        Assert.Equal(empty, NewPackPlace.Where(empty, "Dummy Days"));

        string notYet = Path.Combine(_dir, "not yet");
        Assert.Equal(notYet, NewPackPlace.Where(notYet, "Dummy Days"));

        string busy = Path.Combine(_dir, "busy");
        Directory.CreateDirectory(busy);
        File.WriteAllText(Path.Combine(busy, "notes.txt"), "something else");
        Assert.Equal(Path.Combine(busy, "Dummy Days"), NewPackPlace.Where(busy, "Dummy Days"));

        // A folder holding another pack is never written over.
        string packs = Path.Combine(_dir, "packs");
        Directory.CreateDirectory(packs);
        File.WriteAllText(Path.Combine(packs, PackRepository.ManifestFileName), "{}");
        Assert.Equal(Path.Combine(packs, "Dummy Days"), NewPackPlace.Where(packs, "Dummy Days"));

        // The pack's name taken by a folder with something in it: numbered. An empty one of that name is used.
        Directory.CreateDirectory(Path.Combine(busy, "Dummy Days"));
        Assert.Equal(Path.Combine(busy, "Dummy Days"), NewPackPlace.Where(busy, "Dummy Days"));
        File.WriteAllText(Path.Combine(busy, "Dummy Days", "x.txt"), "x");
        Assert.Equal(Path.Combine(busy, "Dummy Days 2"), NewPackPlace.Where(busy, "Dummy Days"));
    }

    [Fact]
    public void ANameWindowsWouldNotTakeAsAFolderIsCleaned()
    {
        Assert.Equal("Bad name", NewPackPlace.FolderName("Bad: name?"));
        Assert.Equal("Modpack", NewPackPlace.FolderName("<>|"));
        Assert.Equal("Dots", NewPackPlace.FolderName("Dots..."));
    }
}

/// <summary>
/// The editor with no pack open (1.10.0, the author: "Locking the UI until there's a modpack open ... 2 big
/// buttons, one for creating a new modpack ... and one for loading an existing one, as well as recent ones shown on
/// a list below"). Held in the real window, started as a person starts it:
/// <list type="bullet">
/// <item>the start page and nothing of the tabs; Save and Validate cannot run, Options and Help can - control: the
/// harness's usual start has the tabs and no start page;</item>
/// <item>both buttons are on screen, side by side, whole;</item>
/// <item>New with a folder that has other things in it: the pack made in a folder of its name inside it, saved,
/// open, the tabs back;</item>
/// <item>the recent list: a pack that is there opens; one that is gone is greyed, says so, and its ✕ takes it off
/// the list while its folder stays.</item>
/// </list>
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class StartPageTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-start-" + Guid.NewGuid().ToString("N"));

    public StartPageTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static void AsAPersonStartsIt(Action<MainWindow> body)
    {
        TestMode.StartsWithNoPack = true;
        try { WindowHarness.Run(body); }
        finally { TestMode.StartsWithNoPack = false; }
    }

    private static IEnumerable<T> Under<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) yield return hit;
            foreach (var deeper in Under<T>(child)) yield return deeper;
        }
    }

    private static Rect OnWindow(FrameworkElement e, Window w)
        => e.TransformToAncestor(w).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    [Fact]
    public void WithNoPackOpenThereIsTheStartPageAndNothingOfTheTabs()
    {
        AsAPersonStartsIt(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var start = (FrameworkElement)window.FindName("StartPage");
            var tabs = (FrameworkElement)window.FindName("MainTabs");
            window.UpdateLayout();
            Assert.False(vm.IsPackOpen);
            Assert.True(start.IsVisible, "the start page is not shown");
            Assert.False(tabs.IsVisible, "the tabs are shown with no pack open");
            Assert.False(vm.SavePackCommand.CanExecute(null));
            Assert.False(vm.ValidateCommand.CanExecute(null));
            Assert.False(vm.ExportPackCommand.CanExecute(null));
            Assert.True(vm.NewPackCommand.CanExecute(null));
            Assert.True(vm.OpenPackCommand.CanExecute(null));

            // The menus stay: every top menu can be opened.
            var menu = Under<Menu>(window).First();
            var tops = menu.Items.OfType<MenuItem>().ToList();
            Assert.True(tops.Count >= 4);
            Assert.All(tops, m => Assert.True(m.IsEnabled, $"the {m.Header} menu is greyed"));

            // The two buttons: on screen, side by side, whole.
            var newButton = (FrameworkElement)start.FindName("StartNewButton");
            var openButton = (FrameworkElement)start.FindName("StartOpenButton");
            var a = OnWindow(newButton, window);
            var b = OnWindow(openButton, window);
            var page = OnWindow(start, window);
            _out.WriteLine($"New at {a}, Open at {b}, the page {page}");
            Assert.True(a.Width > 150 && a.Height > 80 && b.Width > 150 && b.Height > 80, "the buttons are not big");
            Assert.True(page.Contains(a) && page.Contains(b), "a button is cut by the page");
            Assert.True(Math.Abs(a.Top - b.Top) < 1 && b.Left >= a.Right, "the buttons are not side by side");
        });

        // Control: the harness's own start - an untitled pack ready to edit - has the tabs and no start page.
        WindowHarness.Run(window =>
        {
            window.UpdateLayout();
            Assert.True(((MainViewModel)window.DataContext).IsPackOpen);
            Assert.False(((FrameworkElement)window.FindName("StartPage")).IsVisible);
            Assert.True(((FrameworkElement)window.FindName("MainTabs")).IsVisible);
        });
    }

    [Fact]
    public void NewInAFolderWithOtherThingsInItMakesThePackInAFolderOfItsName_AndOpensIt()
    {
        string folder = Path.Combine(_dir, "My mods");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "readme.txt"), "not a pack");
        AsAPersonStartsIt(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.ChooseNewPack = () => (folder, "Dummy Days");
            vm.NewPackCommand.Execute(null);
            WindowHarness.Pump();
            window.UpdateLayout();

            string expected = Path.Combine(folder, "Dummy Days");
            _out.WriteLine($"made in {vm.PackRoot}");
            Assert.Equal(expected, vm.PackRoot);
            Assert.True(File.Exists(Path.Combine(expected, PackRepository.ManifestFileName)), "the pack was not saved");
            Assert.Equal("Dummy Days", vm.PackName);
            Assert.True(vm.IsPackOpen);
            Assert.True(((FrameworkElement)window.FindName("MainTabs")).IsVisible);
            Assert.False(((FrameworkElement)window.FindName("StartPage")).IsVisible);
            Assert.False(vm.HasUnsavedChanges);
            Assert.True(File.Exists(Path.Combine(folder, "readme.txt")), "what was in the folder was touched");
            Assert.Contains(expected, vm.RecentFiles, StringComparer.OrdinalIgnoreCase);

            // Control: cancelled, nothing is made.
            vm.ChooseNewPack = () => null;
            vm.NewPackCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal(expected, vm.PackRoot);
        });
    }

    [Fact]
    public void ThePacksOpenedLatelyAreListed_AGoneOneGreyedAndTakenOffByItsX()
    {
        string here = Path.Combine(_dir, "Here");
        Directory.CreateDirectory(here);
        PackRepository.Save(PackRepository.CreateEmpty("Still here"), here);
        string gone = Path.Combine(_dir, "Gone");
        RecentFilesService.Add(gone);
        RecentFilesService.Add(here);
        try
        {
            AsAPersonStartsIt(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                window.UpdateLayout();
                var start = (FrameworkElement)window.FindName("StartPage");
                var list = (ItemsControl)start.FindName("StartRecentList");
                Assert.True(list.IsVisible);
                var rows = vm.RecentPacks;
                var there = rows.Single(r => r.Path == here);
                var missing = rows.Single(r => r.Path == gone);
                Assert.Equal("Still here", there.Name);
                Assert.True(there.Exists);
                Assert.False(missing.Exists);

                // On screen: the gone one's button greyed, with its line saying so; the other one live.
                var buttons = Under<Button>(list).Where(b => b.CommandParameter is string).ToList();
                var openHere = buttons.Single(b => (string)b.CommandParameter == here && b.Content is StackPanel);
                var openGone = buttons.Single(b => (string)b.CommandParameter == gone && b.Content is StackPanel);
                Assert.True(openHere.IsEnabled);
                Assert.False(openGone.IsEnabled);
                Assert.Contains(Under<TextBlock>(openGone), t => t.IsVisible && t.Text == SMSModForge.Localization.Loc.T("start.recent.missing"));
                Assert.DoesNotContain(Under<TextBlock>(openHere), t => t.IsVisible && t.Text == SMSModForge.Localization.Loc.T("start.recent.missing"));

                // Its x: off the list.
                var x = buttons.Single(b => (string)b.CommandParameter == gone && b.Content is string);
                x.Command.Execute(x.CommandParameter);
                WindowHarness.Pump();
                Assert.DoesNotContain(vm.RecentPacks, r => r.Path == gone);

                // The one that is there: opened.
                openHere.Command.Execute(openHere.CommandParameter);
                WindowHarness.Pump();
                window.UpdateLayout();
                Assert.Equal(here, vm.PackRoot);
                Assert.Equal("Still here", vm.PackName);
                Assert.True(((FrameworkElement)window.FindName("MainTabs")).IsVisible);
                typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            });
        }
        finally
        {
            RecentFilesService.Remove(here);
            RecentFilesService.Remove(gone);
        }
    }
}
