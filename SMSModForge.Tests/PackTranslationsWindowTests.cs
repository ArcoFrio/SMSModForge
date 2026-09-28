using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The pack's translations in a window of their own, opened from the Edit
/// button beside Editing in: how far along each is, and Transfer - one copied
/// over another language, for a translation typed while the wrong language was
/// up - and Delete, both asked first (the author, 2026-09-28).
/// </summary>
[Trait("Speed", "Slow")]   // builds real windows; see CLAUDE.md
public sealed class PackTranslationsWindowTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-packtr-" + Guid.NewGuid().ToString("N"));

    public PackTranslationsWindowTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    /// <summary>Four lines. Spanish typed into the French translation for
    /// the first three; Portuguese with a line of its own on the fourth.</summary>
    private ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("packtr.pack");
        var d = new DialogueDef { Key = "chat" };
        for (int i = 1; i <= 4; i++) d.Nodes.Add(new DialogueNodeDef { Id = i, Text = "Line " + i });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _root);

        var source = PackTranslations.Source(pack);
        var lines = source.Entries.Where(e => e.Key.StartsWith("dialogue.", StringComparison.Ordinal)).ToList();

        var fr = new TextFile();
        foreach (var e in lines.Take(3)) fr.Add(new TextFile.Entry { Key = e.Key, Text = "Línea (es) " + e.Text, English = e.Text });
        PackTranslations.Write(pack, _root, "fr", source, fr);

        var pt = new TextFile();
        pt.Add(new TextFile.Entry { Key = lines[0].Key, Text = "Fala antiga", English = lines[0].Text });
        pt.Add(new TextFile.Entry { Key = lines[3].Key, Text = "Fala quatro", English = lines[3].Text });
        PackTranslations.Write(pack, _root, "pt-BR", source, pt);
        return pack;
    }

    private string Line(string code, int index, ModPack pack)
    {
        var key = PackTranslations.Source(pack).Entries.Where(e => e.Key.StartsWith("dialogue.", StringComparison.Ordinal))
                                                        .ElementAt(index).Key;
        return Loc.Read(PackTranslations.PathOf(_root, code))!.Get(key);
    }

    private static PackTranslationsWindow.Row Pick(PackTranslationsWindow w, string code)
    {
        var list = (ListView)w.FindName("List");
        var row = list.Items.OfType<PackTranslationsWindow.Row>().First(r => r.Code == code);
        list.SelectedItem = row;
        return row;
    }

    [Fact]
    public void EachTranslationIsListedWithHowFarAlongItIs()
    {
        var pack = Pack();
        WindowHarness.Run(_ =>
        {
            var w = new PackTranslationsWindow(pack, _root);
            var rows = ((ListView)w.FindName("List")).Items.OfType<PackTranslationsWindow.Row>().ToList();
            foreach (var r in rows) _out.WriteLine($"{r.Language} | {r.Translated} | {r.OutOfDate} | {r.File}");

            Assert.Equal(new[] { "fr", "pt-BR" }, rows.Select(r => r.Code).ToArray());
            var fr = rows[0].Summary;
            Assert.Equal(3, fr.Translated);
            Assert.True(fr.Total >= 4);
            Assert.Equal((int)Math.Floor(300.0 / fr.Total), fr.Percent);
            Assert.Contains(fr.Percent + "%", rows[0].Translated);
            Assert.Equal("fr.txt", rows[0].File);
            w.Close();
        });
    }

    [Fact]
    public void TransferCopiesTheTextsAsTheyAre_OnlyOnceTheAuthorSaysYes()
    {
        var pack = Pack();
        WindowHarness.Run(_ =>
        {
            var w = new PackTranslationsWindow(pack, _root);
            Pick(w, "fr");

            // No: nothing changes.
            string? asked = null;
            w.Confirm = (message, title) => { asked = message; return false; };
            Assert.False(w.TransferSelected("pt-BR"));
            Assert.NotNull(asked);
            _out.WriteLine(asked);
            Assert.Equal("Fala antiga", Line("pt-BR", 0, pack));

            // Yes: French's three lines over Portuguese's, word for word;
            // Portuguese's fourth line stays, and French keeps its own.
            w.Confirm = (_, _) => true;
            Assert.True(w.TransferSelected("pt-BR"));
            Assert.Equal("Línea (es) Line 1", Line("pt-BR", 0, pack));
            Assert.Equal("Línea (es) Line 3", Line("pt-BR", 2, pack));
            Assert.Equal("Fala quatro", Line("pt-BR", 3, pack));
            Assert.Equal("Línea (es) Line 1", Line("fr", 0, pack));

            // Into a language the pack had no file for yet: made.
            Assert.True(w.TransferSelected("es"));
            Assert.Equal("Línea (es) Line 2", Line("es", 1, pack));
            Assert.True(w.Changed);
            w.Close();
        });
    }

    [Fact]
    public void TheTransferredTextsStillKnowWhatTheyWereTranslatedFrom()
    {
        // As they are: the note of the default text each was translated from
        // comes along, so a line whose default text changes goes out of date
        // in the new language as it would have in the old.
        var pack = Pack();
        PackTranslations.Transfer(pack, _root, "fr", "es");
        pack.Dialogues[0].Nodes[0].Text = "Line 1, reworded";
        var es = PackTranslations.Summaries(pack, _root).First(s => s.Code == "es");
        _out.WriteLine($"es: {es.Translated} of {es.Total}, {es.OutOfDate} out of date");
        Assert.Equal(1, es.OutOfDate);
    }

    [Fact]
    public void DeleteRemovesTheTranslation_OnlyOnceTheAuthorSaysYes()
    {
        var pack = Pack();
        string file = PackTranslations.PathOf(_root, "fr");
        WindowHarness.Run(_ =>
        {
            var w = new PackTranslationsWindow(pack, _root);
            Pick(w, "fr");

            w.Confirm = (_, _) => false;
            Assert.False(w.DeleteSelected());
            Assert.True(File.Exists(file));

            w.Confirm = (_, _) => true;
            Assert.True(w.DeleteSelected());
            Assert.False(File.Exists(file));
            Assert.Equal(new[] { "fr" }, w.Deleted.ToArray());
            Assert.DoesNotContain(((ListView)w.FindName("List")).Items.OfType<PackTranslationsWindow.Row>(),
                                  r => r.Code == "fr");
            w.Close();
        });
    }

    [Fact]
    public void UnderTheTestHarness_NobodyIsAskedAndNothingHappens()
    {
        // The real question is a message box: under the harness it answers no
        // rather than stopping the run on the machine of whoever runs it.
        var pack = Pack();
        WindowHarness.Run(_ =>
        {
            var w = new PackTranslationsWindow(pack, _root);
            Pick(w, "fr");
            Assert.False(w.DeleteSelected());
            Assert.True(File.Exists(PackTranslations.PathOf(_root, "fr")));
            w.Close();
        });
    }

    /// <summary>What the Edit button does around the window: whatever is
    /// typed and not saved goes into the files first, and the language up is
    /// read back from its file afterwards.</summary>
    private void ThroughTheEditor(MainViewModel vm, Action<PackTranslationsWindow> use)
    {
        var deleted = new List<string>();
        vm.WithTranslationFilesCurrent(pack =>
        {
            var w = new PackTranslationsWindow(pack, _root) { Confirm = (_, _) => true };
            use(w);
            deleted.AddRange(w.Deleted);
            w.Close();
            return w.Changed;
        });
        vm.AfterTranslationFilesEdited(deleted);
        WindowHarness.Pump();
    }

    private static DialogueNodeViewModel Shown(MainViewModel vm, int id)
        => vm.Dialogues.Single(x => x.Key == "chat").Nodes.Single(n => n.Id == id);

    [Fact]
    public void TransferringOverTheLanguageUp_ShowsWhatCameIn_WordsNotYetSavedIncluded()
    {
        // The case Transfer is for: a line typed into French that was meant
        // for Portuguese, not saved yet, and Portuguese up when the author
        // notices.
        Pack();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_root);
            vm.EditingLanguage = "fr";
            WindowHarness.Pump();
            Shown(vm, 2).Text = "Fala dois, no francês";
            vm.EditingLanguage = "pt-BR";
            WindowHarness.Pump();
            Assert.Equal("Fala quatro", Shown(vm, 4).Text);

            ThroughTheEditor(vm, w =>
            {
                Pick(w, "fr");
                Assert.True(w.TransferSelected("pt-BR"));
            });

            Assert.Equal("pt-BR", vm.EditingLanguage);
            _out.WriteLine("pt-BR now shows: " + string.Join(" | ", Enumerable.Range(1, 4).Select(i => Shown(vm, i).Text)));
            Assert.Equal("Fala dois, no francês", Shown(vm, 2).Text);
            Assert.Equal("Línea (es) Line 1", Shown(vm, 1).Text);
            Assert.Equal("Fala quatro", Shown(vm, 4).Text);
            Assert.Equal("Fala dois, no francês", Line("pt-BR", 1, vm.Pack));
        });
    }

    [Fact]
    public void DeletingTheLanguageUp_GoesBackToTheDefault_AndNoSaveBringsItBack()
    {
        Pack();
        string file = PackTranslations.PathOf(_root, "fr");
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_root);
            vm.EditingLanguage = "fr";
            WindowHarness.Pump();
            Shown(vm, 1).Text = "Typed, and then the whole language deleted";

            ThroughTheEditor(vm, w =>
            {
                Pick(w, "fr");
                Assert.True(w.DeleteSelected());
            });

            Assert.Equal("", vm.EditingLanguage);
            Assert.Equal("Line 1", Shown(vm, 1).Text);
            Assert.False(File.Exists(file));

            Assert.True(vm.SavePack());
            Assert.False(File.Exists(file), "the save wrote the deleted translation back");

            // French is still offered, to start again from nothing: nothing of
            // the deleted one was kept to come back with it.
            vm.EditingLanguage = "fr";
            WindowHarness.Pump();
            _out.WriteLine("French again: " + Shown(vm, 1).Text);
            Assert.DoesNotContain("deleted", Shown(vm, 1).Text);
            Assert.DoesNotContain("Línea", Shown(vm, 1).Text);
        });
    }

    [Fact]
    public void TheEditButtonSitsBesideEditingIn()
    {
        WindowHarness.Run(window =>
        {
            var button = (Button)window.FindName("EditTranslationsButton");
            var picker = (ComboBox)window.FindName("EditingLanguagePicker");
            Assert.True(button.IsVisible);
            Assert.Same(picker.Parent, button.Parent);
            var at = button.TranslatePoint(new Point(0, 0), picker);
            _out.WriteLine($"button at {at} from the list, {button.ActualWidth}x{button.ActualHeight}");
            Assert.True(at.X >= picker.ActualWidth, "the button is not after the list");
        });
    }
}
