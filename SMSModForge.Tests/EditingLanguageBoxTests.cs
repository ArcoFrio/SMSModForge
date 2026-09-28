using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The Editing in box always shows the language being edited, and only a pick
/// from its list changes it. Switching the editor's own language renamed the
/// "Default" entry by swapping it for a new one, and the box went blank - which
/// read as the pack no longer being edited in any language (2026-09-27).
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class EditingLanguageBoxTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-editbox-" + Guid.NewGuid().ToString("N"));

    public EditingLanguageBoxTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Loc.Use(Loc.EnglishCode);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private void Prepare()
    {
        var pack = PackRepository.CreateEmpty("editbox.pack");
        var d = new DialogueDef { Key = "beach" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Hello there!" });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _dir);
        var source = PackTranslations.Source(pack);
        var es = new TextFile();
        foreach (var e in source.Entries) es.Add(new TextFile.Entry { Key = e.Key, Text = "ES " + e.Text, English = e.Text });
        PackTranslations.Write(pack, _dir, "es", source, es);
    }

    /// <summary>What the closed box draws.</summary>
    private static string Drawn(ComboBox box)
    {
        box.UpdateLayout();
        var texts = new System.Collections.Generic.List<string>();
        void Walk(DependencyObject d)
        {
            if (d is TextBlock tb && !string.IsNullOrEmpty(tb.Text)) texts.Add(tb.Text);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(box);
        return string.Join("|", texts);
    }

    [Fact]
    public void SwitchingTheEditorsLanguageKeepsTheBoxOnWhatIsBeingEdited()
    {
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var box = (ComboBox)window.FindName("EditingLanguagePicker");
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();

            // The pack's own words, whose entry is named in the editor's language.
            ((MainWindow)window).SwitchLanguage("de");
            WindowHarness.Pump();
            Assert.Equal("", vm.EditingLanguage);
            Assert.Equal(0, box.SelectedIndex);
            Assert.Equal(vm.EditingLanguageOptions[0].Label, Drawn(box));
            Assert.NotEqual("", Drawn(box));

            // A translation, through two more switches.
            vm.EditingLanguage = "es";
            WindowHarness.Pump();
            ((MainWindow)window).SwitchLanguage("pt-BR");
            WindowHarness.Pump();
            ((MainWindow)window).SwitchLanguage("en");
            WindowHarness.Pump();
            Assert.Equal("es", vm.EditingLanguage);
            Assert.True(vm.IsEditingTranslation);
            Assert.Contains("(es)", Drawn(box));
        });
    }

    [Fact]
    public void NothingChosen_OrALanguageNotOnTheList_SwitchesNothing()
    {
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.EditingLanguage = "es";
            WindowHarness.Pump();

            vm.EditingLanguage = null!;
            Assert.Equal("es", vm.EditingLanguage);
            vm.EditingLanguage = "not-a-language";
            Assert.Equal("es", vm.EditingLanguage);

            // The pack's own words are an entry like any other.
            vm.EditingLanguage = "";
            Assert.Equal("", vm.EditingLanguage);
            Assert.False(vm.IsEditingTranslation);
        });
    }
}
