using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Lines are spell-checked in the language they are written in: the pack's
/// own, or the one being edited. They were always checked as US English, so a
/// pack written in Portuguese, or a line edited in Russian, was underlined from
/// end to end (2026-09-27).
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class SpellingLanguageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-spelllang-" + Guid.NewGuid().ToString("N"));

    public SpellingLanguageTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void TheSpellerFollowsThePacksLanguage_ThenTheOneBeingEdited_AndBack()
    {
        var pack = PackRepository.CreateEmpty("spelllang.pack");
        var d = new DialogueDef { Key = "beach" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Hello there!" });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _dir);
        var source = PackTranslations.Source(pack);
        var es = new TextFile();
        foreach (var e in source.Entries) es.Add(new TextFile.Entry { Key = e.Key, Text = "ES " + e.Text, English = e.Text });
        PackTranslations.Write(pack, _dir, "es", source, es);

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();
            Assert.Equal("en-US", vm.TextLanguageTag);
            Assert.Equal("en-US", Services.Speller.Language);

            vm.PackLanguage = "pt-BR";
            WindowHarness.Pump();
            Assert.Equal("pt-BR", vm.TextLanguageTag);
            Assert.Equal("pt-BR", Services.Speller.Language);

            vm.EditingLanguage = "es";
            WindowHarness.Pump();
            Assert.Equal("es-ES", vm.TextLanguageTag);
            Assert.Equal("es-ES", Services.Speller.Language);
            // WPF keeps the tag in lower case; the same language.
            Assert.Equal("es-ES", vm.TextLanguage.IetfLanguageTag, ignoreCase: true);

            vm.EditingLanguage = "";
            WindowHarness.Pump();
            Assert.Equal("pt-BR", Services.Speller.Language);
        });
    }

    /// <summary>
    /// A line preview is told the spelling changed by whichever thread changed
    /// it, and redraws on its own. It redrew on the caller's, so another
    /// thread switching its speller's language threw at the preview - which
    /// is what a second editor window, or the test suite, does (2026-09-27).
    /// </summary>
    [Fact]
    public void AnotherThreadChangingItsLanguageDoesNotReachIntoAPreview()
    {
        WindowHarness.Run(window =>
        {
            var preview = new SMSModForge.View.Controls.DialogueLinePreview { Line = "Hello there!" };
            var host = Descendants<Grid>(window).First(g => g.IsVisible);
            host.Children.Add(preview);
            WindowHarness.Pump();
            Assert.True(preview.IsLoaded, "the preview never loaded, so it is not listening and this proves nothing");

            Exception? thrown = null;
            var other = new Thread(() =>
            {
                try { Services.Speller.UseLanguage("de-DE"); }
                catch (Exception e) { thrown = e; }
            });
            other.Start();
            other.Join();
            WindowHarness.Pump();

            host.Children.Remove(preview);
            Assert.Null(thrown);
        });
    }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root)
        where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }
}
