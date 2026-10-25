using System;
using System.IO;
using System.Linq;
using System.Threading;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// EX's unsaved dot (1.8.0): read again, once things are quiet, after an edit
/// or a save - on the window's own thread only. Before that last part it was
/// queued onto the window's thread from any thread, and a test's view model
/// with no window was written out there while the test was still changing it:
/// it crashed a tester in the EX pass and showed another test a game
/// conversation's lines in their saved, pruned form.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UnsavedDotTests
{
    private static void Quiet()
    {
        var until = DateTime.Now.AddSeconds(0.3);
        do WindowHarness.Pump(); while (DateTime.Now < until);
    }

    [Fact]
    public void In_EX_an_edit_shows_the_dot_and_a_save_takes_it_away()
    {
        string dir = Path.Combine(Path.GetTempPath(), "smsmodforge-dot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            WindowHarness.Run(window =>
            {
                var was = ThemeManager.Current;
                try
                {
                    var vm = (MainViewModel)window.DataContext;
                    PackRepository.Save(PackRepository.CreateEmpty("dot.pack"), dir);
                    vm.OpenPackFromPath(dir);

                    ThemeManager.Apply(ThemeManager.All.First(t => t.Name == "Neon"));
                    Quiet();
                    Assert.False(vm.ShowsUnsaved, "the dot is up on a pack just opened");
                    vm.AddCharacterCommand.Execute(null);
                    Quiet();
                    Assert.True(vm.ShowsUnsaved, "an edit in EX did not show the dot");
                    vm.SavePackCommand.Execute(null);
                    Quiet();
                    Assert.False(vm.ShowsUnsaved, "the dot stayed up after saving");

                    // Classic does not show it at all (the control for the edit).
                    ThemeManager.Apply(ThemeManager.All.First(t => t.Name == "Light"));
                    vm.AddCharacterCommand.Execute(null);
                    Quiet();
                    Assert.False(vm.ShowsUnsaved, "Classic has no dot to show");
                }
                finally { ThemeManager.Apply(was); }
            });
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void A_view_model_on_another_thread_queues_nothing_onto_the_window()
    {
        WindowHarness.Run(window =>
        {
            var was = ThemeManager.Current;
            ThemeManager.Apply(ThemeManager.All.First(t => t.Name == "Neon"));
            try
            {
                MainViewModel? elsewhere = null;
                Exception? failed = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        TestDesktop.Enter();
                        elsewhere = new MainViewModel();
                        for (int i = 0; i < 5; i++) elsewhere.AddCharacterCommand.Execute(null);
                    }
                    catch (Exception e) { failed = e; }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
                Assert.Null(failed);

                // Had a check been queued here, it would have run on this thread
                // and raised the dot for edits made on that one.
                Quiet();
                Assert.False(elsewhere!.ShowsUnsaved, "a check from another thread ran on the window's");
            }
            finally { ThemeManager.Apply(was); }
        });
    }
}
