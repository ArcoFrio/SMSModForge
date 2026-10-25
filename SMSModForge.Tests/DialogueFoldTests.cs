using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A dialogue's Identity and Start conditions fold, each per dialogue and
/// remembered, still saying what they hold; and the preview is one line when
/// the line has no bust to show (the author, 1.8.0).
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class DialogueFoldTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-folds-" + Guid.NewGuid().ToString("N"));

    public DialogueFoldTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private void Write()
    {
        var pack = PackRepository.CreateEmpty("folds.pack");
        foreach (var key in new[] { "Alpha", "Beta" })
        {
            var d = new DialogueDef { Key = key, DisplayName = key, Priority = 3, QueueBehind = true };
            d.StartConditions.Add(new NodeConditionDef { Type = NodeConditionTypes.LevelActive, Params = { ["level"] = "vanilla:Beach" } });
            d.StartConditions.Add(new NodeConditionDef { Type = NodeConditionTypes.VariableEquals, Params = { ["name"] = "met" } });
            d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Narration." });
            d.Nodes.Add(new DialogueNodeDef { Id = 2, Actor = "anna", Text = "Hi!" });
            d.RootNodeIds.Add(1);
            pack.Dialogues.Add(d);
        }
        PackRepository.Save(pack, _dir);
    }

    private static void Show(MainWindow window, MainViewModel vm, string key)
    {
        vm.SelectedTabIndex = MainTab.Dialogues;
        vm.SelectedDialogue = vm.Dialogues.Single(d => d.Key == key);
        WindowHarness.Pump();
        window.UpdateLayout();
    }

    [Fact]
    public void AFoldIsPerDialogue_Remembered_AndStillSaysWhatThePanelHolds()
    {
        WindowHarness.Run(window =>
        {
            Write();
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            string saved = PackRepository.SerializeAsSaved(vm.Pack);
            Show(window, vm, "Alpha");

            var identity = (ToggleButton)window.FindName("DialogueIdentityFold");
            var start = (ToggleButton)window.FindName("DialogueStartFold");
            var folded = (FrameworkElement)window.FindName("DialogueIdentityFolded");
            var startFolded = (FrameworkElement)window.FindName("DialogueStartFolded");
            Assert.True(identity.IsVisible && identity.IsChecked == true, "Identity does not start open");
            Assert.False(folded.IsVisible);

            // Folded by its title, as a person does.
            identity.IsChecked = false;
            start.IsChecked = false;
            WindowHarness.Pump();
            window.UpdateLayout();
            Assert.True(folded.IsVisible, "folded, Identity says nothing");
            Assert.True(startFolded.IsVisible, "folded, Start conditions say nothing");
            var alpha = vm.SelectedDialogue!;
            Assert.Contains(Loc.F("dialogues.fold.priority", "value", 3), alpha.IdentityChips);
            Assert.Contains(Loc.T("dialogues.fold.queues"), alpha.IdentityChips);
            Assert.Contains("Variable: met", alpha.StartChips);
            Assert.Contains("Level Active: vanilla:Beach", alpha.StartChips);

            // Another dialogue is as it was: the fold is Alpha's.
            Show(window, vm, "Beta");
            Assert.True(identity.IsChecked == true, "folding one dialogue folded another");
            Assert.False(folded.IsVisible);

            Show(window, vm, "Alpha");
            Assert.True(identity.IsChecked == false, "Alpha's fold was forgotten");

            // A fold is the editor's, never the pack's.
            Assert.Equal(saved, PackRepository.SerializeAsSaved(vm.Pack));

            // And it is there the next time the pack is opened.
            DialogueFolds.Reload();
            vm.OpenPackFromPath(_dir);
            Show(window, vm, "Alpha");
            Assert.False(vm.SelectedDialogue!.IdentityOpen, "the fold did not last");
            Assert.False(vm.SelectedDialogue.StartOpen);
            Show(window, vm, "Beta");
            Assert.True(vm.SelectedDialogue!.IdentityOpen);
        });
    }

    [Fact]
    public void ThePreviewIsOneLineWhenTheLineHasNoBust()
    {
        WindowHarness.Run(window =>
        {
            Write();
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            Show(window, vm, "Alpha");
            var preview = (FrameworkElement)window.FindName("NodeBustPreview");
            var line = (FrameworkElement)window.FindName("NodeNoBustLine");

            vm.SelectedNode = vm.SelectedDialogue!.Nodes.Single(n => n.Id == 1);   // narration
            WindowHarness.Pump();
            window.UpdateLayout();
            Assert.True(line.IsVisible, "no line saying there is no bust");
            Assert.False(preview.IsVisible, "the black square is still there");

            // The control: Anna has a bust, and it shows.
            vm.SelectedNode = vm.SelectedDialogue.Nodes.Single(n => n.Id == 2);
            WindowHarness.Pump();
            window.UpdateLayout();
            Assert.NotEqual("", vm.SelectedNodeActorBustKey);
            Assert.True(preview.IsVisible, "a line with a bust shows no picture");
            Assert.False(line.IsVisible);
        });
    }
}
