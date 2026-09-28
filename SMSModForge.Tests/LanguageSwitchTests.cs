using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Changing the editor's own language while it is open.
/// <para/>
/// <c>TranslationScreenTests</c> proves every word on screen follows. These
/// prove nothing else moves: the pack, what is unsaved, what is selected, the
/// undo history, and a translation of the pack being edited all come through a
/// switch exactly as they were. The author's condition for doing this at all
/// was that it could not break what it has no business touching.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class LanguageSwitchTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public LanguageSwitchTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Loc.Use(Loc.EnglishCode);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private void Prepare()
    {
        var pack = PackRepository.CreateEmpty("switch.pack");
        pack.Characters.Add(new CharacterDef { Key = "kiki", DisplayName = "Kiki" });
        var d = new DialogueDef { Key = "beach" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "kiki", Text = "Hello there!" });
        d.Nodes.Add(new DialogueNodeDef { Id = 2, Actor = "kiki", Text = "Nice day." });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _dir);

        var source = PackTranslations.Source(pack);
        var es = new TextFile();
        foreach (var e in source.Entries)
            es.Add(new TextFile.Entry { Key = e.Key, Text = "ES " + e.Text, English = e.Text });
        PackTranslations.Write(pack, _dir, "es", source, es);
    }

    private static DialogueNodeViewModel Line(MainViewModel vm, int id)
        => vm.Dialogues.Single(x => x.Key == "beach").Nodes.Single(n => n.Id == id);

    private string SavedManifest() => File.ReadAllText(Path.Combine(_dir, "modpack.json"));

    [Fact]
    public void SwitchingKeepsThePackTheSelectionAndTheUndoHistory()
    {
        Prepare();
        Loc.Use(Loc.EnglishCode);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = 5;
            vm.SelectedDialogue = vm.Dialogues.Single(x => x.Key == "beach");
            WindowHarness.Pump();
            vm.SelectedNode = Line(vm, 2);
            WindowHarness.Pump();

            // An edit nobody has saved, as an undo step.
            vm.Undo.Checkpoint();
            Line(vm, 2).Text = "Nice day, isn't it?";
            vm.Undo.Checkpoint();
            Assert.True(vm.HasUnsavedChanges);

            string packBefore = PackRepository.Serialize(vm.Pack);
            var tab = (TabControl)window.FindName("MainTabs");
            string headerBefore = ((TabItem)tab.Items[5]).Header as string ?? "";

            window.SwitchLanguage("es");
            WindowHarness.Pump();

            // The switch happened: the same tab's header is now Spanish.
            string headerAfter = ((TabItem)tab.Items[5]).Header as string ?? "";
            _out.WriteLine($"'{headerBefore}' -> '{headerAfter}'");
            Assert.Equal("es", Loc.Current.Code);
            Assert.Equal(Loc.T("dialogues.dialogues"), headerAfter);
            Assert.NotEqual(headerBefore, headerAfter);

            // And nothing else did.
            Assert.Same(vm, window.DataContext);
            Assert.Equal(packBefore, PackRepository.Serialize(vm.Pack));
            Assert.True(vm.HasUnsavedChanges);
            Assert.Equal(5, vm.SelectedTabIndex);
            Assert.Equal("beach", vm.SelectedDialogue?.Key);
            Assert.Equal(2, vm.SelectedNode?.Id);
            Assert.Equal("Nice day, isn't it?", Line(vm, 2).Text);

            // The history came through: the edit made before the switch can
            // be taken back after it.
            Assert.True(vm.Undo.CanUndo);
            vm.Undo.Undo();
            WindowHarness.Pump();
            Assert.Equal("Nice day.", Line(vm, 2).Text);
        });
    }

    [Fact]
    public void SwitchingWhileATranslationIsBeingEditedStaysInIt()
    {
        // Two languages at once: the editor's, and the pack's being edited.
        // Changing the first must not touch the second - not leave it, not
        // write it, not put its words into the pack.
        Prepare();
        Loc.Use(Loc.EnglishCode);
        string manifestBefore = SavedManifest();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.EditingLanguage = "es";
            WindowHarness.Pump();
            Assert.Equal("ES Hello there!", Line(vm, 1).Text);
            Assert.False(vm.HasUnsavedChanges);

            window.SwitchLanguage("pt-BR");
            WindowHarness.Pump();

            Assert.Equal("pt-BR", Loc.Current.Code);
            Assert.Equal("es", vm.EditingLanguage);
            Assert.True(vm.IsEditingTranslation);
            Assert.Equal("ES Hello there!", Line(vm, 1).Text);
            Assert.False(vm.HasUnsavedChanges);
            Assert.Equal(manifestBefore, SavedManifest());

            // The list names the pack's own words in the new language.
            Assert.Equal(Loc.F("editingLanguage.own", "language", TranslationFiles.NativeName("en")!),
                         vm.EditingLanguageOptions[0].Label);
        });
    }
}
