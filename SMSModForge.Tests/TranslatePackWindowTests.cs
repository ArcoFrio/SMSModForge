using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;
using SMSModForge.View;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The window that offers to translate a pack.
/// <para/>
/// What it says before anything is sent is the part worth checking. An author
/// is deciding whether to hand their pack's text to somebody else's service,
/// and the two things they need in order to decide — which languages still
/// need doing, and what the service is — are both worked out here rather than
/// read off anything. A list that said "not started" about a finished language
/// would have somebody translating a pack twice; one that said "fully
/// translated" about an empty language would have them shipping a pack in the
/// wrong words believing it was done.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class TranslatePackWindowTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public TranslatePackWindowTests(ITestOutputHelper o)
    {
        _out = o;
        _root = Path.Combine(Path.GetTempPath(), "smsmodforge-mtwin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("win.pack");
        var dialogue = new DialogueDef { Key = "chat" };
        for (int i = 0; i < 4; i++)
            dialogue.Nodes.Add(new DialogueNodeDef { Text = "A line of dialogue, number " + i + "." });
        pack.Dialogues.Add(dialogue);
        PackRepository.Save(pack, _root);
        return pack;
    }

    private static IReadOnlyList<TranslatePackWindow.Choice> Choices(TranslatePackWindow window)
        => (IReadOnlyList<TranslatePackWindow.Choice>)
           ((IEnumerable<object>)((ItemsControl)window.FindName("LanguageList")!).ItemsSource!)
               .Cast<TranslatePackWindow.Choice>().ToList();

    [Fact]
    public void NamesAreKeptUnlessTicked_AndTheirSpellingsAreAskedOnlyForOtherAlphabets()
    {
        var pack = PackRepository.CreateEmpty("names.window.test");
        pack.Characters.Add(new CharacterDef { Key = "hope", DisplayName = "Hope" });
        var dialogue = new DialogueDef { Key = "chat" };
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "hope", Text = "Hope is here." });
        pack.Dialogues.Add(dialogue);
        PackRepository.Save(pack, _root);

        WindowHarness.Run(_ =>
        {
            try
            {
                var window = new TranslatePackWindow(pack, _root);
                var box = (CheckBox)window.FindName("TranslateNamesBox")!;
                var names = (GroupBox)window.FindName("NamesBox")!;
                var choices = Choices(window);

                // Off unless asked for.
                Assert.NotEqual(true, box.IsChecked);

                // Only Latin-alphabet languages ticked: nothing to spell.
                foreach (var c in choices) c.Wanted = c.Code is "de" or "fr";
                Assert.Equal(System.Windows.Visibility.Collapsed, names.Visibility);

                // Russian ticked: a row for the name, a box for Russian.
                choices.Single(c => c.Code == "ru").Wanted = true;
                Assert.Equal(System.Windows.Visibility.Visible, names.Visibility);
                var rows = ((IEnumerable<object>)((ItemsControl)window.FindName("SpellingRows")!).ItemsSource!)
                           .Cast<TranslatePackWindow.NameRow>().ToList();
                var row = Assert.Single(rows);
                Assert.Equal("Hope", row.Name);
                Assert.Equal("ru", Assert.Single(row.Cells).Code);

                // Names translated like anything else: no spellings to ask for.
                box.IsChecked = true;
                Assert.Equal(System.Windows.Visibility.Collapsed, names.Visibility);
            }
            finally
            {
                SMSModForge.Services.EditorPrefs.SetTranslatingNames(pack.PackId, false);
            }
        });
    }

    [Fact]
    public void EveryLanguageIsShownInFull_AndTheNamesListFitsAcross_EvenInTheSmallestWindow()
    {
        var pack = PackRepository.CreateEmpty("layout.window.test");
        foreach (var name in new[] { "Hope", "Kiki", "Marciana" })
            pack.Characters.Add(new CharacterDef { Key = name.ToLowerInvariant(), DisplayName = name });
        var dialogue = new DialogueDef { Key = "chat" };
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "hope", Text = "Hope, Kiki and Marciana." });
        pack.Dialogues.Add(dialogue);
        PackRepository.Save(pack, _root);

        WindowHarness.Run(_ =>
        {
            var window = new TranslatePackWindow(pack, _root)
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -32000, Top = -32000, ShowActivated = false,
            };
            window.Show();
            try
            {
                WindowHarness.Pump();
                var box = (GroupBox)window.FindName("LanguagesBox")!;
                var list = (ItemsControl)window.FindName("LanguageList")!;
                var names = (GroupBox)window.FindName("NamesBox")!;
                var scroll = (ScrollViewer)window.FindName("SpellingScroll")!;
                var root = (System.Windows.Media.Visual)window.Content;

                // As it opens: every column of names across, no sideways scrolling.
                window.UpdateLayout();
                _out.WriteLine($"opened {window.ActualWidth:0} wide; names extent {scroll.ExtentWidth:0}, viewport {scroll.ViewportWidth:0}");
                Assert.True(names.IsVisible, "the list of names should be up: Russian and others are ticked");
                Assert.True(scroll.ExtentWidth <= scroll.ViewportWidth + 0.5, "the columns of names do not fit across");

                // As small as the author can make it.
                window.Height = 200;
                window.Width = 200;
                WindowHarness.Pump();
                window.UpdateLayout();
                var boxAt = box.TransformToAncestor(root).TransformBounds(new System.Windows.Rect(box.RenderSize));
                _out.WriteLine($"window {window.ActualWidth:0}x{window.ActualHeight:0}; languages box {boxAt}; names shown {names.IsVisible}");

                for (int i = 0; i < list.Items.Count; i++)
                {
                    var row = (System.Windows.FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(i);
                    var at = row.TransformToAncestor(root).TransformBounds(new System.Windows.Rect(row.RenderSize));
                    Assert.True(at.Bottom <= boxAt.Bottom + 0.5 && at.Bottom <= ((System.Windows.FrameworkElement)root).ActualHeight,
                                $"language {i} ends at {at.Bottom:0}, past the box ({boxAt.Bottom:0}) or the window");
                }

                // And room for some names under the languages.
                Assert.True(names.ActualHeight >= 100, $"the list of names is {names.ActualHeight:0} tall");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ItOffersTheLanguagesModForgeHas_AndNotEnglish()
    {
        WindowHarness.Run(_ =>
        {
            var window = new TranslatePackWindow(Pack(), _root);
            var choices = Choices(window);
            foreach (var c in choices) _out.WriteLine($"{c.Code}: {c.Note} (wanted={c.Wanted})");

            Assert.NotEmpty(choices);

            // English is what packs are already written in, so offering to
            // translate into it is offering to do nothing.
            Assert.DoesNotContain(choices, c =>
                string.Equals(c.Code, "en", StringComparison.OrdinalIgnoreCase));

            // The ones ModForge itself ships are there to pick.
            Assert.Contains(choices, c => c.Code == "es");
        });
    }

    [Fact]
    public void ALanguageThePackHasNothingForSaysHowMuchThereIsToDo()
    {
        WindowHarness.Run(_ =>
        {
            var window = new TranslatePackWindow(Pack(), _root);
            var spanish = Choices(window).Single(c => c.Code == "es");
            _out.WriteLine(spanish.Note);

            Assert.Contains("not started", spanish.Note);
            Assert.True(spanish.Enabled);
            Assert.True(spanish.Wanted, "a language with everything to do is not offered by default");
        });
    }

    [Fact]
    public void AFinishedLanguageSaysSoAndCannotBeTicked()
    {
        // The control for the test above. A window that said "not started"
        // about everything would look identical until somebody translated a
        // pack twice and paid for it twice.
        var pack = Pack();
        WindowHarness.Run(_ =>
        {
            PackTranslationJob.Run(pack, _root, new[] { "es" },
                (t, a, b, c) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<string>>(
                    t.Select(x => "[es] " + x).ToList()),
                (_, __) => System.Threading.Tasks.Task.CompletedTask).GetAwaiter().GetResult();

            var window = new TranslatePackWindow(pack, _root);
            var spanish = Choices(window).Single(c => c.Code == "es");
            _out.WriteLine(spanish.Note);

            Assert.Contains("fully translated", spanish.Note);
            Assert.False(spanish.Enabled, "a finished language is still tickable");
            Assert.False(spanish.Wanted);
        });
    }

    [Fact]
    public void APartlyDoneLanguageSaysWhatIsLeft()
    {
        var pack = Pack();
        WindowHarness.Run(_ =>
        {
            // One line translated by hand, the rest still to do.
            PackTranslations.CreateOrUpdate(pack, _root, "es");
            var source = PackTranslations.Source(pack);
            var file = Loc.Read(PackTranslations.PathOf(_root, "es"))!;
            file.Find(source.Entries.First().Key)!.Text = "Una línea traducida.";
            Loc.Write(PackTranslations.PathOf(_root, "es"),
                      SMSModForge.Shared.TextFileWriter.Build(source, file, "es", new List<string>()));

            var spanish = Choices(new TranslatePackWindow(pack, _root)).Single(c => c.Code == "es");
            _out.WriteLine(spanish.Note);

            Assert.Contains("still to translate", spanish.Note);
            Assert.True(spanish.Enabled);
        });
    }

    [Fact]
    public void SelectAllAndNoneLeaveFinishedLanguagesAlone()
    {
        var pack = Pack();
        WindowHarness.Run(_ =>
        {
            PackTranslationJob.Run(pack, _root, new[] { "es" },
                (t, a, b, c) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<string>>(
                    t.Select(x => "[es] " + x).ToList()),
                (_, __) => System.Threading.Tasks.Task.CompletedTask).GetAwaiter().GetResult();

            var window = new TranslatePackWindow(pack, _root);
            ((Button)window.FindName("AllButton")!).RaiseEvent(
                new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            var choices = Choices(window);
            Assert.False(choices.Single(c => c.Code == "es").Wanted,
                         "Select all ticked a language that has nothing to do");
            Assert.Contains(choices, c => c.Enabled && c.Wanted);

            ((Button)window.FindName("NoneButton")!).RaiseEvent(
                new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.All(Choices(window), c => Assert.False(c.Wanted));
        });
    }

    [Fact]
    public void TheWarningIsOnTheWindowBeforeTheButtonIs()
    {
        // Not decoration. The pack's text goes to a service that is not
        // official and that rate limits against the author's own connection;
        // both belong in front of somebody before they press Translate, not in
        // a changelog afterwards.
        string warning = Loc.T("translatePack.warning");
        _out.WriteLine(warning);

        Assert.Contains("Google", warning);
        Assert.Contains("block", warning);
        Assert.DoesNotContain("translatePack.warning", warning);   // it is a real text, not a missing key
    }

    [Fact]
    public void ItWaitsForTranslateToBeClicked_AndEnterDoesNotStartIt()
    {
        // Every language unticked before it opens: a run started by anything
        // gets no further than "tick a language" - which is what shows one was
        // started, and a test must never send text anywhere. It used to start
        // on its own when opened from the offer before an export, and Enter in
        // a name's spelling started it too (the author, 2026-09-28).
        var pack = Pack();
        WindowHarness.Run(_ =>
        {
            var window = new TranslatePackWindow(pack, _root)
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -32000, Top = -32000, ShowInTaskbar = false,
            };
            foreach (var c in Choices(window)) c.Wanted = false;
            var said = (TextBlock)window.FindName("ProgressText")!;
            string started = Loc.T("translatePack.pickOne");
            try
            {
                window.Show();
                window.Activate();
                WindowHarness.Pump();
                Assert.NotEqual(started, said.Text);

                // Enter, on something in the window that does nothing with it.
                var box = (CheckBox)window.FindName("TranslateNamesBox")!;
                Assert.True(box.Focus());
                System.Windows.Input.InputManager.Current.ProcessInput(
                    new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                        System.Windows.PresentationSource.FromVisual(window)!, 0, System.Windows.Input.Key.Enter)
                    { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
                WindowHarness.Pump();
                Assert.NotEqual(started, said.Text);

                // The control: a click is what starts one.
                ((Button)window.FindName("TranslateButton")!).RaiseEvent(
                    new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                WindowHarness.Pump();
                Assert.Equal(started, said.Text);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void WhenARunIsOver_TheWindowSaysSo_AndNothingLooksStillGoing()
    {
        // After a run the bars stayed full under "...translated...", which
        // reads as a run still going (the author, 2026-09-28).
        var pack = Pack();
        WindowHarness.Run(_ =>
        {
            var window = new TranslatePackWindow(pack, _root)
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -32000, Top = -32000, ShowInTaskbar = false,
            };
            var all = (TextBlock)window.FindName("AllProgressText")!;
            var allBar = (ProgressBar)window.FindName("AllProgressBar")!;
            var language = (TextBlock)window.FindName("LanguageProgressText")!;
            var languageBar = (ProgressBar)window.FindName("LanguageProgressBar")!;
            try
            {
                window.Show();
                WindowHarness.Pump();

                // Going: both bars, and the dots.
                window.ShowProgress(new PackTranslationJob.Progress("de", 2, 2, 10, 10, 30, 30));
                WindowHarness.Pump();
                Assert.True(language.IsVisible && languageBar.IsVisible);
                Assert.EndsWith("...", language.Text);

                // Over: the language bar gone, and the other says finished.
                window.ShowOver(finished: true);
                WindowHarness.Pump();
                _out.WriteLine("over: " + all.Text);
                Assert.False(language.IsVisible);
                Assert.False(languageBar.IsVisible);
                Assert.True(all.IsVisible && allBar.IsVisible);
                Assert.Equal(Loc.T("translatePack.over.finished"), all.Text);
                Assert.Equal(allBar.Maximum, allBar.Value);
                Assert.DoesNotContain("...", all.Text);

                // Another run: its language bar back.
                window.ShowProgress(new PackTranslationJob.Progress("fr", 1, 1, 3, 40, 3, 40));
                WindowHarness.Pump();
                Assert.True(language.IsVisible && languageBar.IsVisible);
                Assert.NotEqual(Loc.T("translatePack.over.finished"), all.Text);

                // Stopped part of the way: said so, and the bar left where it got to.
                window.ShowOver(finished: false);
                WindowHarness.Pump();
                Assert.Equal(Loc.T("translatePack.over.stopped"), all.Text);
                Assert.Equal(3, allBar.Value);
                Assert.False(language.IsVisible);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ARunIsFinishedOnlyWhenEveryLanguagePickedGotToItsEnd()
    {
        var picked = new[] { "de", "fr" };
        var de = new PackTranslationJob.Done("de", 5, 0, null);
        var fr = new PackTranslationJob.Done("fr", 5, 0, null);
        Assert.True(TranslatePackWindow.Completed(picked, new[] { de, fr }));
        // Stopped in French.
        Assert.False(TranslatePackWindow.Completed(picked, new[] { de, new PackTranslationJob.Done("fr", 2, 0, "cancelled") }));
        // Stopped between languages: French never started.
        Assert.False(TranslatePackWindow.Completed(picked, new[] { de }));
    }
}
