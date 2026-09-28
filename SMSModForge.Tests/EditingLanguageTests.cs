using System;
using System.IO;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Switching the editor to one of a pack's translations, through the real
/// window and the real save.
/// <para/>
/// The unit tests prove the session changes only words. These prove the
/// editor never lets those words escape into the wrong file: saving, undoing
/// and checking for unsaved changes all read the live model, and while a
/// language is up the live model holds that language. Any one of them reading
/// it naively would put Spanish into the pack itself, for every player.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class EditingLanguageTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public EditingLanguageTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-editlang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>A saved pack with two lines, and a Spanish file translating
    /// both - so there is something to switch to.</summary>
    private void Prepare()
    {
        var pack = PackRepository.CreateEmpty("editlang.pack");
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

    private string SavedManifest() => File.ReadAllText(Path.Combine(_dir, "modpack.json"));

    private TextFile Spanish() => Loc.Read(PackTranslations.PathOf(_dir, "es"))!;

    private static DialogueNodeViewModel Line(MainViewModel vm, int id)
        => vm.Dialogues.Single(x => x.Key == "beach").Nodes.Single(n => n.Id == id);

    [Fact]
    public void SwitchingShowsTheTranslationEverywhereAndSayingSoStaysUp()
    {
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            Assert.Contains(vm.EditingLanguageOptions, o => o.Code == "es");

            vm.EditingLanguage = "es";
            WindowHarness.Pump();

            _out.WriteLine("line 1 shows: " + Line(vm, 1).Text);
            Assert.Equal("ES Hello there!", Line(vm, 1).Text);
            Assert.Equal("ES Kiki", vm.Characters.Single(c => c.Key == "kiki").DisplayName);
            Assert.True(vm.IsEditingTranslation);

            // ...and back.
            vm.EditingLanguage = "";
            WindowHarness.Pump();
            Assert.Equal("Hello there!", Line(vm, 1).Text);
            Assert.False(vm.IsEditingTranslation);
        });
    }

    [Fact]
    public void SavingInALanguageWritesThePacksOwnWordsAndTheTranslationBeside()
    {
        // The one that matters most. The live model holds Spanish while the
        // Spanish is up; a save that read it plainly would put Spanish into the
        // pack for every player of it.
        Prepare();

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);

            // The control: what the editor saves with no language up at all.
            // Compared against that rather than against the file Prepare wrote,
            // because opening and saving through the editor settles a field or
            // two on its own - and that is not the difference being looked for.
            Assert.True(vm.SavePack());
            string before = SavedManifest();

            vm.EditingLanguage = "es";
            WindowHarness.Pump();

            Line(vm, 1).Text = "¡Hola, qué tal!";
            Assert.True(vm.HasUnsavedChanges, "a translation edited and not saved does not count as unsaved");

            Assert.True(vm.SavePack());
            WindowHarness.Pump();

            // The pack on disk is exactly as it was: nothing of the Spanish in it.
            string after = SavedManifest();
            _out.WriteLine(after.Contains("Hola") ? "SPANISH LEAKED INTO THE PACK" : "pack clean");
            Assert.DoesNotContain("Hola", after);
            Assert.DoesNotContain("ES Hello", after);
            Assert.Equal(before, after);

            // The translation has the edit, recorded as a translation of the
            // pack's own words.
            var es = Spanish();
            Assert.Equal("¡Hola, qué tal!", es.Translated("dialogue.beach.1"));
            Assert.Equal("Hello there!", es.Find("dialogue.beach.1")!.English);

            // Saved is saved, in the language as well as the pack.
            Assert.False(vm.HasUnsavedChanges);

            // And the editor is still showing the Spanish it was showing.
            Assert.Equal("¡Hola, qué tal!", Line(vm, 1).Text);
        });
    }

    [Fact]
    public void LeavingALanguageKeepsItsEditsForTheSave_AndTheSaveListsAndWritesThem()
    {
        // Switching away must never be the way a translation is lost - nor the
        // way one is written. It used to write the file on the spot, so by the
        // time of the save there was nothing left to list, and the list of
        // changes before a save said nothing about the translation at all.
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            Assert.False(vm.HasUnsavedChanges);
            vm.EditingLanguage = "es";
            Line(vm, 2).Text = "Qué buen día.";

            vm.EditingLanguage = "";
            WindowHarness.Pump();

            Assert.Equal("Nice day.", Line(vm, 2).Text);
            // Not on disk yet, and not forgotten either.
            Assert.Equal("ES Nice day.", Spanish().Translated("dialogue.beach.2"));
            Assert.True(vm.HasUnsavedChanges, "a translation switched away from and not saved does not count as unsaved");

            // Back in the language, it is still there.
            vm.EditingLanguage = "es";
            WindowHarness.Pump();
            Assert.Equal("Qué buen día.", Line(vm, 2).Text);
            vm.EditingLanguage = "";
            WindowHarness.Pump();

            // The list before the save names it: the language, the line, what
            // it said and what it says now - and nothing else of the file.
            var listed = vm.TranslationChangesToSave();
            foreach (var c in listed) _out.WriteLine(c.Section + " | " + c.Path + " | " + c.Before + " -> " + c.After);
            var change = Assert.Single(listed);
            Assert.Equal("dialogue.beach.2", change.Path);
            Assert.Equal("ES Nice day.", change.Before);
            Assert.Equal("Qué buen día.", change.After);
            Assert.Contains("Español", change.Section);

            Assert.True(vm.SavePack());
            Assert.Equal("Qué buen día.", Spanish().Translated("dialogue.beach.2"));
            Assert.Equal("Nice day.", Spanish().Find("dialogue.beach.2")!.English);
            Assert.False(vm.HasUnsavedChanges);
            Assert.Empty(vm.TranslationChangesToSave());
        });
    }

    [Fact]
    public void TheLanguageUpAtTheSaveIsListedToo_AndALanguageLeftUnchangedIsNot()
    {
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);

            // Looked at and left alone: nothing to save.
            vm.EditingLanguage = "es";
            WindowHarness.Pump();
            vm.EditingLanguage = "";
            WindowHarness.Pump();
            Assert.False(vm.HasUnsavedChanges);
            Assert.Empty(vm.TranslationChangesToSave());

            // Edited and still up when saving.
            vm.EditingLanguage = "es";
            WindowHarness.Pump();
            Line(vm, 1).Text = "¡Hola!";
            var change = Assert.Single(vm.TranslationChangesToSave());
            Assert.Equal("dialogue.beach.1", change.Path);
            Assert.Equal("¡Hola!", change.After);
        });
    }

    [Fact]
    public void AHeldTranslationIsAskedAboutBeforeAnotherPackReplacesIt()
    {
        // Held and unsaved is unsaved: opening a pack over it asks first, like
        // any other edit. Unanswered - the harness answers Cancel - nothing is
        // opened and nothing is lost.
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.EditingLanguage = "es";
            Line(vm, 2).Text = "Qué buen día.";
            vm.EditingLanguage = "";
            WindowHarness.Pump();
            Assert.True(vm.HasUnsavedChanges);

            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();
            Assert.Single(vm.TranslationChangesToSave());
            Assert.Equal("ES Nice day.", Spanish().Translated("dialogue.beach.2"));
        });
    }

    [Fact]
    public void UndoInALanguageTakesBackTheTranslationAndStaysInTheLanguage()
    {
        // An undo step is the whole pack. Taken plainly while the Spanish is up
        // it would restore the Spanish AS the pack's words; taken in the pack's
        // words alone it would throw the correction away. Both halves come
        // back, and the language with them.
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.EditingLanguage = "es";
            WindowHarness.Pump();

            Line(vm, 1).Text = "Primera versión.";
            vm.Undo.Checkpoint();
            Line(vm, 1).Text = "Segunda versión.";
            vm.Undo.Checkpoint();

            vm.Undo.Undo();
            WindowHarness.Pump();

            _out.WriteLine("after one undo: " + Line(vm, 1).Text + "  (editing " + vm.EditingLanguage + ")");
            Assert.Equal("es", vm.EditingLanguage);
            Assert.Equal("Primera versión.", Line(vm, 1).Text);

            // Back to the pack's words: still its own, whatever was undone.
            vm.EditingLanguage = "";
            WindowHarness.Pump();
            Assert.Equal("Hello there!", Line(vm, 1).Text);
        });
    }

    [Fact]
    public void TheSwitchItselfCanBeUndone()
    {
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.EditingLanguage = "es";
            WindowHarness.Pump();

            vm.Undo.Undo();
            WindowHarness.Pump();

            Assert.Equal("", vm.EditingLanguage);
            Assert.Equal("Hello there!", Line(vm, 1).Text);
        });
    }

    [Fact]
    public void AddingALineInALanguageAddsItToThePack()
    {
        // Structure is shared. Added while looking at the Spanish, the line is
        // in the pack - and saved, it is there for every language.
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.EditingLanguage = "es";
            WindowHarness.Pump();

            var d = vm.Dialogues.Single(x => x.Key == "beach");
            int before = d.Nodes.Count;
            var added = d.AddNode();
            added.Text = "Una línea nueva.";
            Assert.True(vm.SavePack());

            var saved = PackRepository.Load(_dir);
            Assert.Equal(before + 1, saved.Dialogues.Single(x => x.Key == "beach").Nodes.Count);
        });
    }

    [Fact]
    public void AnUnsavedPackCannotBeSwitched_AndSaysWhy()
    {
        // A translation is a file beside the pack; with no folder there is
        // nowhere to put one. Refused, not half-done.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            Assert.Null(vm.PackRoot);

            vm.EditingLanguage = "es";
            WindowHarness.Pump();

            Assert.Equal("", vm.EditingLanguage);
            Assert.False(vm.IsEditingTranslation);
        });
    }
}
