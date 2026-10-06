using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A button beside Edit… opening the pack's folder in File Explorer (the
/// author, 1.7.0). Explorer itself is swapped for a recorder here: the test is
/// whether the right folder is asked for, and a window opening on the desktop
/// of whoever runs the suite is the failure CLAUDE.md warns about.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PackFolderButtonTests
{
    private readonly ITestOutputHelper _out;
    public PackFolderButtonTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void ItSitsRightOfEdit_IsGreyUntilThePackHasAFolder_AndOpensThatFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "smsmodforge-packfolder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var opened = new System.Collections.Generic.List<string>();
        var was = MainViewModel.FolderOpener;
        MainViewModel.FolderOpener = opened.Add;
        try
        {
            PackRepository.Save(PackRepository.CreateEmpty("folder.pack"), dir);
            WindowHarness.Run(window =>
            {
                var bar = (StackPanel)window.FindName("EditingLanguageBar");
                var buttons = bar.Children.OfType<Button>().ToList();
                var edit = (Button)window.FindName("EditTranslationsButton");
                var folder = (Button)window.FindName("OpenPackFolderButton");
                // To the right of Edit…, next to it.
                Assert.Equal(buttons.IndexOf(edit) + 1, buttons.IndexOf(folder));
                Assert.Equal(SMSModForge.Localization.Loc.T("packFolder.open"), folder.Content);

                var vm = (MainViewModel)window.DataContext;
                vm.NewPackCommand.Execute(null);
                WindowHarness.Pump();
                _out.WriteLine($"new pack: root '{vm.PackRoot}', enabled {folder.IsEnabled}");
                Assert.Null(vm.PackRoot);
                Assert.False(folder.IsEnabled);              // nothing to open yet

                vm.OpenPackFromPath(dir);
                WindowHarness.Pump();
                _out.WriteLine($"saved pack: root '{vm.PackRoot}', enabled {folder.IsEnabled}");
                Assert.True(folder.IsEnabled);

                folder.Command.Execute(null);
                Assert.Equal(new[] { vm.PackRoot }, opened);
            });
        }
        finally
        {
            MainViewModel.FolderOpener = was;
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
