using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using Newtonsoft.Json;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What a pack changed on one of the game's screens stays changed when a step
/// is put back (1.10.0). Up to 1.9.2 every undo read the whole pack back and
/// laid each of its game screens over the game's own again, as a file's is -
/// and a screen as the editor works on it does not say which of its values the
/// pack means (that is worked out as the file is written), so every place,
/// size, picture, word and showing the pack had changed on the game's objects
/// came back as the game's: after an undo of anything, anywhere in the pack.
/// The next save wrote the loss. Found by the walk over every control
/// (UndoEveryControlTests); held here on the screen the in-game suite's pack
/// rewords:
/// <list type="bullet">
/// <item>an undo that reads the whole pack back, one that puts back a section,
/// and a copy of the screen's record all keep the pack's words;</item>
/// <item>control: the same tree without the editor's mark on it
/// (Model.UiDef.Working), laid over the game's screen, loses them - so the
/// check can tell, and the mark is what keeps them;</item>
/// <item>the mark is in the editor's own copies of the pack and never in the
/// file.</item>
/// </list>
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UndoKeepsGameScreenChangesTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-screenundo-" + Guid.NewGuid().ToString("N"));

    public UndoKeepsGameScreenChangesTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static IEnumerable<UiNodeDef> Flat(IEnumerable<UiNodeDef> nodes)
    {
        foreach (var n in nodes) { yield return n; foreach (var c in Flat(n.Children)) yield return c; }
    }

    private static string? Words(UiDef screen, string label)
        => Flat(screen.Nodes).FirstOrDefault(n => n.Bind == label)?.Text?.Value;

    [Fact]
    public void WhatThePackChangedOnAGameScreenIsStillChangedAfterAnUndo()
    {
        PackRepository.Save(InGameSuitePacks.Story.Make(_dir), _dir);
        string label = InGameSuitePacks.Story.PatchedLabel, wanted = InGameSuitePacks.Story.PatchedWords;
        Assert.False(string.IsNullOrEmpty(label));
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();
            UiDef Screen() => vm.Pack.Uis.Single(u => u.Id == "quitpatch");
            Assert.Equal(wanted, Words(Screen(), label));
            string before = PackRepository.Serialize(vm.Pack);

            // Control: the same tree without the editor's mark, laid over the game's screen as a file's is.
            var unmarked = JsonConvert.DeserializeObject<UiDef>(JsonConvert.SerializeObject(Screen(), PackRepository.ManifestSettings), PackRepository.ManifestSettings)!;
            Assert.True(unmarked.Working, "the editor's copy of the screen does not carry its mark");
            unmarked.Working = false;
            _ = new UiViewModel(unmarked);
            string? lost = Words(unmarked, label);
            _out.WriteLine($"without the mark the label reads \"{lost}\"; the pack's words are \"{wanted}\"");
            Assert.NotEqual(wanted, lost);

            // An undo that reads the whole pack back (the pack's own version is not on any tab).
            vm.Undo.Checkpoint();
            vm.Pack.Version = "9.9.9";
            vm.Undo.Checkpoint();
            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();
            _out.WriteLine("the version put back by: " + vm.LastRestore);
            Assert.Equal("whole", vm.LastRestore);
            Assert.Equal(wanted, Words(Screen(), label));
            Assert.Equal(before, PackRepository.Serialize(vm.Pack));
            vm.RedoCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal(wanted, Words(Screen(), label));
            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();

            // One that puts back a section that has nothing to do with the screen, and one that puts back the screens.
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Variables;
            WindowHarness.Pump();
            vm.AddVariableCommand.Execute(null);
            WindowHarness.Pump();
            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();
            _out.WriteLine("a variable added put back by: " + vm.LastRestore);
            Assert.Equal(wanted, Words(Screen(), label));
            Assert.Equal(before, PackRepository.Serialize(vm.Pack));

            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Ui;
            WindowHarness.Pump();
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            WindowHarness.Pump();
            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();
            _out.WriteLine("a screen added put back by: " + vm.LastRestore);
            Assert.Equal(wanted, Words(Screen(), label));
            Assert.Equal(before, PackRepository.Serialize(vm.Pack));

            // A copy of the record: the pack's words with it.
            var copy = Services.EditorClipboard.CloneOne(Screen());
            _ = new UiViewModel(copy);
            Assert.Equal(wanted, Words(copy, label));

            // And the file knows nothing of the mark.
            string saved = PackRepository.SerializeAsSaved(vm.Pack);
            Assert.DoesNotContain("\"working\"", saved);
            Assert.Contains("\"working\"", before);

            typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        });
    }
}
