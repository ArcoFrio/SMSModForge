using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;

namespace SMSModForge.View;

/// <summary>
/// Translating a pack into the languages an author picks.
/// <para/>
/// The list is every language ModForge itself has, with what the pack already
/// has marked — so the ordinary case is "tick the ones you want and press
/// Translate", and a language already finished says so rather than being
/// silently skipped.
/// <para/>
/// What it will not do is start without saying what it is about to do. The
/// pack's text goes to somebody else's service, the service is rate limited
/// against the author's own connection, and neither of those should be
/// discovered afterwards — so both are on the window before the button is.
/// </summary>
public partial class TranslatePackWindow : Window
{
    /// <summary>One language, and whether it is wanted.</summary>
    public sealed class Choice : INotifyPropertyChanged
    {
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";

        private string _note = "";
        public string Note
        {
            get => _note;
            set { _note = value; Changed(); }
        }

        private bool _wanted;
        public bool Wanted
        {
            get => _wanted;
            set { _wanted = value; Changed(); }
        }

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; Changed(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>One name, and how it is written in each language of another
    /// alphabet that is ticked.</summary>
    public sealed class NameRow
    {
        public string Name { get; init; } = "";
        public ObservableCollection<NameCell> Cells { get; } = new();
    }

    /// <summary>How one name is written in one language.</summary>
    public sealed class NameCell : INotifyPropertyChanged
    {
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";

        private string _spelling = "";

        /// <summary>What the box says. Typing in it makes it the author's.</summary>
        public string Spelling
        {
            get => _spelling;
            set => SetSpelling(value, suggested: false);
        }

        private bool _suggested;

        /// <summary>Put there by the translator and not yet looked at - shown in
        /// italics until it is.</summary>
        public bool Suggested
        {
            get => _suggested;
            set { _suggested = value; Changed(); }
        }

        public void SetSpelling(string value, bool suggested)
        {
            _spelling = value ?? "";
            _suggested = suggested;
            Changed(nameof(Spelling));
            Changed(nameof(Suggested));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private readonly ModPack _pack;
    private readonly string _root;
    private readonly ObservableCollection<Choice> _choices = new();
    private CancellationTokenSource? _stopping;
    private bool _running;

    /// <summary>The names kept out of a translation, and the pack's texts
    /// they were found in.</summary>
    private readonly Shared.TextFile _source;
    private readonly List<string> _names;

    /// <summary>The pack's own characters' names: the ones only the author can
    /// spell. The game's are spelled already where that is certain (<see cref="CastNames"/>).</summary>
    private readonly HashSet<string> _ownNames;

    private readonly ObservableCollection<string> _heads = new();
    private readonly ObservableCollection<NameRow> _rows = new();

    /// <summary>Every spelling in the grid, by language and name - kept when
    /// the grid is rebuilt for a different set of languages.</summary>
    private readonly Dictionary<(string Code, string Name), NameCell> _spellings = new();

    /// <summary>The run's translator, while one is going: where what Google
    /// gives a person for passing its check is handed over.</summary>
    private GoogleTranslator? _translator;

    /// <summary>The browser Google's check is shown in, made the first time
    /// one is needed and kept for the window. The same profile as the Google
    /// Translate window's, so a check passed here counts there too.</summary>
    private Microsoft.Web.WebView2.Wpf.WebView2? _checkWeb;

    /// <summary>The Google Translate website's window, while a run or the
    /// names step is using it.</summary>
    private BrowserTranslatorWindow? _browser;

    private double? _heightBefore;

    /// <param name="startNow">Start translating as soon as the window is up,
    /// into every language that needs it. For the offer made before an export,
    /// where the author has already said yes to exactly that; the window is
    /// still where the run shows its progress and can be stopped.</param>
    public TranslatePackWindow(ModPack pack, string packRoot, bool startNow = false)
    {
        InitializeComponent();
        _pack = pack;
        _root = packRoot;
        LanguageList.ItemsSource = _choices;
        SpellingHeads.ItemsSource = _heads;
        SpellingRows.ItemsSource = _rows;
        _source = PackTranslations.Source(pack);
        _names = PackNames.Of(_source);
        _ownNames = PackNames.OwnNames(_source);
        TranslateNamesBox.IsChecked = Services.EditorPrefs.IsTranslatingNames(pack.PackId);
        Fill();
        foreach (var choice in _choices)
            choice.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Choice.Wanted)) RefreshNames(); };
        RefreshNames();
        Loaded += (_, _) => FitToContent(widen: true);
        SizeChanged += (_, e) => { if (e.WidthChanged) FitToContent(widen: false); };
        if (startNow) Loaded += (_, _) => Translate_Click(this, new RoutedEventArgs());
    }

    /// <summary>
    /// Every language ModForge has words of its own in, with how much of the
    /// pack each already has.
    /// <para/>
    /// Those rather than every language in the world: a translation of a pack
    /// into a language the editor cannot be read in is not much use to the
    /// person who has to check it, and the list has to end somewhere.
    /// </summary>
    private void Fill()
    {
        // The pack's own language is not offered: its words are that already.
        foreach (var language in PackTranslationJob.StillToTranslate(_pack, _root))
        {
            _choices.Add(new Choice
            {
                Code = language.Code,
                Name = language.Name + "  (" + language.Code + ")",
                Note = Note(language.HasFile, language.Missing, language.Total),
                // Nothing to do is not an error and not a thing to tick.
                Enabled = language.Missing > 0,
                Wanted = language.Missing > 0,
            });
        }

        if (_choices.Count == 0)
            ProgressText.Text = Loc.T("translatePack.noLanguages");
    }

    private static string Note(bool exists, int missing, int total)
    {
        if (!exists) return Loc.F("translatePack.notStarted", "count", total.ToString());
        if (missing == 0) return Loc.T("translatePack.finished");
        return Loc.F("translatePack.partly", "missing", missing.ToString(), "total", total.ToString());
    }

    private void TranslateNames_Changed(object sender, RoutedEventArgs e)
    {
        if (_names == null) return;   // while the window is being built
        Services.EditorPrefs.SetTranslatingNames(_pack.PackId, TranslateNamesBox.IsChecked == true);
        RefreshNames();
    }

    /// <summary>Whether names are kept out of the translation.</summary>
    private bool KeepingNames => TranslateNamesBox.IsChecked != true && _names.Count > 0;

    /// <summary>The ticked languages that write names in an alphabet of their own.</summary>
    private List<string> SpelledLanguages()
        // Every box is disabled while the window works, so a ticked language
        // counts then too - or the list of names went away the first time the
        // check panel was shown, and never came back.
        => _choices.Where(c => c.Wanted && (c.Enabled || _running) && PackNames.NeedsSpelling(c.Code))
                   .Select(c => c.Code).ToList();

    /// <summary>
    /// Build the names grid for the languages ticked now: a row per name, a
    /// box per language. A box starts with what is known already - the
    /// language's translation of a character's name, or a spelling checked
    /// before - and keeps whatever was typed in it through a rebuild.
    /// </summary>
    private void RefreshNames()
    {
        var codes = KeepingNames ? SpelledLanguages() : new List<string>();
        NamesBox.Visibility = codes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _heads.Clear();
        _rows.Clear();
        if (codes.Count == 0) return;

        // As the language list names them: the language in itself, and its code.
        foreach (string code in codes) _heads.Add((TranslationFiles.NativeName(code) ?? code) + "  (" + code + ")");
        foreach (string name in _names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
        {
            // One of the game's, spelled already in every language shown: nothing
            // for the author to do, so not listed.
            if (!_ownNames.Contains(name) && codes.All(c => CastNames.In(c, name) != null)) continue;
            var row = new NameRow { Name = name };
            foreach (string code in codes) row.Cells.Add(CellFor(code, name));
            _rows.Add(row);
        }
        if (IsLoaded) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                                             new Action(() => FitToContent(widen: true)));
    }

    /// <summary>The least room the list of names is given under the languages.</summary>
    private const double NamesAtLeast = 140;

    /// <summary>
    /// Room for everything, measured rather than guessed: every language shown
    /// in full, never scrolled; and when there is a list of names, wide enough
    /// for all its columns - when it opens and when a column is added - and at
    /// least <see cref="NamesAtLeast"/> tall. The list of names is what takes
    /// whatever else the window has.
    /// </summary>
    private void FitToContent(bool widen)
    {
        if (!IsLoaded) return;
        UpdateLayout();
        bool names = NamesBox.Visibility == Visibility.Visible;

        if (widen && names && SpellingScroll.ExtentWidth > SpellingScroll.ViewportWidth + 0.5)
        {
            double wanted = Width + SpellingScroll.ExtentWidth - SpellingScroll.ViewportWidth + 4;
            Width = Math.Min(wanted, SystemParameters.WorkArea.Width);
            UpdateLayout();
        }

        // Everything above and below the middle of the window, as it is laid
        // out now - the texts at the top wrap, so this changes with the width.
        double around = ActualHeight - MiddleGrid.ActualHeight;
        LanguagesBox.Measure(new Size(Math.Max(0, MiddleGrid.ActualWidth), double.PositiveInfinity));
        double least = around + LanguagesBox.DesiredSize.Height + (names ? NamesAtLeast + NamesBox.Margin.Top : 0);
        // Never taller than the screen can show: a window that cannot fit is
        // worse than a list that has to be scrolled.
        least = Math.Min(least, SystemParameters.WorkArea.Height);
        if (Math.Abs(MinHeight - least) > 0.5) MinHeight = least;
    }

    private NameCell CellFor(string code, string name)
    {
        if (_spellings.TryGetValue((code, name), out var had)) return had;
        var cell = new NameCell { Code = code, Name = name };
        string? known = KnownSpelling(code, name);
        // Google's suggestion from an earlier time the window was open, still
        // to be checked: shown as one again, rather than lost with the window.
        string? suggested = known == null ? Services.EditorPrefs.NameSuggestion(code, name) : null;
        if (!string.IsNullOrEmpty(suggested)) cell.SetSpelling(suggested!, suggested: true);
        else cell.SetSpelling(known ?? "", suggested: false);
        // Typed by the author: theirs, and kept from that moment - closing the
        // window before translating does not lose it. Emptied, the name stays
        // in Latin letters, and Google is not asked for it again.
        cell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(NameCell.Spelling) || cell.Suggested) return;
            Services.EditorPrefs.SetNameSpelling(code, name, cell.Spelling);
            Services.EditorPrefs.SetNameSuggestion(code, name, "");
        };
        _spellings[(code, name)] = cell;
        return cell;
    }

    /// <summary>Whether Google is still to be asked how a name is written:
    /// nothing in its box, and never asked before - asked once, the author's
    /// word stands, and a name still empty stays in Latin letters.</summary>
    private bool NeedsAsking(string code, string name)
        => string.IsNullOrWhiteSpace(CellFor(code, name).Spelling)
           && Services.EditorPrefs.NameSuggestion(code, name) == null;

    /// <summary>How a name is already written in a language: the language's
    /// translation of the character's name field, or a spelling the author
    /// checked before, in this pack or another.</summary>
    private string? KnownSpelling(string code, string name)
    {
        var file = Loc.Read(PackTranslations.PathOf(_root, code));
        if (file != null)
            foreach (var entry in _source.Entries)
            {
                if (!PackNames.IsNameKey(entry.Key) || !string.Equals(entry.Text.Trim(), name, StringComparison.Ordinal)) continue;
                string? theirs = file.Translated(entry.Key);
                if (!string.IsNullOrWhiteSpace(theirs) && !string.Equals(theirs.Trim(), name, StringComparison.Ordinal))
                    return theirs.Trim();
            }
        return Services.EditorPrefs.NameSpelling(code, name) ?? CastNames.In(code, name);
    }

    private void All_Click(object sender, RoutedEventArgs e)
    {
        foreach (var c in _choices) if (c.Enabled) c.Wanted = true;
    }

    private void None_Click(object sender, RoutedEventArgs e)
    {
        foreach (var c in _choices) c.Wanted = false;
    }

    /// <summary>Close, or stop a run that is going. The same button, because
    /// while it is running there is nothing else to want.</summary>
    private void CloseOrStop_Click(object sender, RoutedEventArgs e)
    {
        if (_running) { _stopping?.Cancel(); return; }
        Close();
    }

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;

        var wanted = _choices.Where(c => c.Wanted && c.Enabled).Select(c => c.Code).ToList();
        if (wanted.Count == 0)
        {
            ProgressText.Text = Loc.T("translatePack.pickOne");
            return;
        }

        _running = true;
        _stopping = new CancellationTokenSource();
        TranslateButton.IsEnabled = false;
        AllButton.IsEnabled = NoneButton.IsEnabled = false;
        TranslateNamesBox.IsEnabled = false;
        CloseButton.Content = Loc.T("translatePack.stop");
        var enabledBefore = _choices.ToDictionary(c => c, c => c.Enabled);
        foreach (var c in _choices) c.Enabled = false;

        using var translator = new GoogleTranslator();
        _translator = translator;

        // Names kept: every language of another alphabet needs each name
        // spelled in it, and the author checks what the translator suggests
        // before any of it is used. Asked once per language; the run starts on
        // the next press.
        PackTranslationJob.KeptNames? kept = null;
        if (KeepingNames)
        {
            var spelled = wanted.Where(PackNames.NeedsSpelling).ToList();
            var ask = spelled.Where(code => _names.Any(n => NeedsAsking(code, n))).ToList();
            if (ask.Count > 0)
            {
                await SuggestSpellings(translator, ask, _stopping.Token);
                _browser?.Finished();
                _browser = null;
                foreach (var pair in enabledBefore) pair.Key.Enabled = pair.Value;
                Finish();
                // The suggestions are in the list: it is shown, to be checked.
                RefreshNames();
                return;
            }
            kept = KeptNames(spelled);
        }
        else
        {
            // Names translated like any other text - except the game's own
            // characters', which are never translated as words: kept, and
            // spelled where that is certain.
            kept = CastOnly(wanted);
        }
        List<PackTranslationJob.Done> done;
        try
        {
            done = await PackTranslationJob.Run(
                _pack, _root, wanted, translator.Send,
                (howLong, cancel) => Task.Delay(howLong, cancel),
                // Onto the UI thread deliberately. The run does its waiting
                // and its writing off it, so this arrives on a pool thread -
                // and a WPF property set from one throws rather than being
                // slightly wrong, which is at least honest but not useful
                // halfway through somebody's five thousand lines.
                (code, so, far) => Dispatcher.Invoke(() =>
                    ProgressText.Text = Loc.F("translatePack.progress", "language", code,
                                              "done", so.ToString(), "total", far.ToString())),
                _stopping.Token,
                probeFirst: true,
                // The Google Translate page, if the free service fails its
                // test sentence, or refuses with nothing for a person to answer.
                fallback: OpenWebsite,
                said: note => Dispatcher.Invoke(() => ProgressText.Text = note),
                keepNames: kept,
                person: AskAPerson);
        }
        catch (Exception ex)
        {
            ProgressText.Text = ex.Message;
            _browser?.Finished();
            _browser = null;
            Finish();
            return;
        }

        _browser?.Finished();
        _browser = null;
        ProgressText.Text = (done.Any(d => d.ByFallback) ? Loc.T("packText.web.used") + "\n" : "") + Report(done);
        Finish();
        Refresh();
    }

    /// <summary>What happened, per language, in one place. A run that stopped
    /// says why and says the work is kept, because that is the thing somebody
    /// needs to know before deciding whether to start again.</summary>
    private static string Report(IReadOnlyList<PackTranslationJob.Done> done)
    {
        if (done.Count == 0) return Loc.T("translatePack.nothingDone");

        var lines = new List<string>();
        foreach (var one in done)
        {
            string line = Loc.F("translatePack.did", "language", one.Code,
                                "count", one.Translated.ToString());
            if (one.Damaged > 0)
                line += " " + Loc.F("translatePack.damaged", "count", one.Damaged.ToString());
            if (!one.Finished) line += " " + one.StoppedBecause;
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    private void Finish()
    {
        _translator = null;
        _running = false;
        _stopping?.Dispose();
        _stopping = null;
        TranslateButton.IsEnabled = true;
        AllButton.IsEnabled = NoneButton.IsEnabled = true;
        TranslateNamesBox.IsEnabled = true;
        CloseButton.Content = Loc.T("translatePack.close");
    }

    /// <summary>
    /// The Google Translate website, in its own small window: opened the first
    /// time something needs it, then shared by the names step and the run.
    /// Made on the UI thread, which owns windows. From any thread.
    /// </summary>
    private Task<(TranslationRun.Send? Send, string? WhyNot)> OpenWebsite(CancellationToken cancel)
        => Dispatcher.InvokeAsync(async () =>
        {
            if (_browser == null || !_browser.IsVisible)
            {
                var (window, whyNot) = await BrowserTranslatorWindow.Open(this);
                if (window == null) return ((TranslationRun.Send?)null, whyNot);
                _browser = window;
            }
            return (new GoogleTranslator(_browser.Ask, GoogleWebPage.LongestText).Send, (string?)null);
        }).Task.Unwrap();

    /// <summary>What finds a check on Google's page: its captcha's form, the
    /// captcha itself, or the frame it is drawn in.</summary>
    private const string FindCheck =
        "!!document.querySelector('#captcha-form, .g-recaptcha, iframe[src*=\"recaptcha\"], iframe[title*=\"reCAPTCHA\"]')"; // English on purpose: script run in the page, never shown

    /// <summary>
    /// Show Google's check in the window and wait for the person to answer it.
    /// Passed once they have, with what Google set for it handed to the run's
    /// translator; not passed when they stop the run or it cannot be shown;
    /// nothing to answer when Google's page has no check on it at all - only
    /// "try again later", which is what its free service shows. From any thread.
    /// <para/>
    /// ModForge never answers it - that is the point of asking somebody.
    /// </summary>
    private Task<TranslationRun.CheckOutcome> AskAPerson(string address, CancellationToken cancel)
        => Dispatcher.InvokeAsync(() => AskAPersonHere(address, cancel)).Task.Unwrap();

    private async Task<TranslationRun.CheckOutcome> AskAPersonHere(string address, CancellationToken cancel)
    {
        const TranslationRun.CheckOutcome notPassed = TranslationRun.CheckOutcome.NotPassed;

        // Nobody to answer it under the tests.
        if (Services.TestMode.Active) return notPassed;

        // On screen first. The browser does not start until it is shown, and
        // started behind a hidden panel it waited for ever: the window sat on
        // "Asking Google..." with no check and nothing Stop could reach.
        ShowCheck(true);
        ProgressText.Text = Loc.T("translatePack.check.waiting");
        try
        {
            if (_checkWeb == null)
            {
                var web = new Microsoft.Web.WebView2.Wpf.WebView2();
                CheckHost.Content = web;
                System.IO.Directory.CreateDirectory(BrowserTranslatorWindow.DataFolder);
                var environment = await CoreWebView2Environment.CreateAsync(null, BrowserTranslatorWindow.DataFolder);
                var starting = web.EnsureCoreWebView2Async(environment);
                if (await Task.WhenAny(starting, Task.Delay(TimeSpan.FromSeconds(30), cancel)) != starting)
                {
                    ShowCheck(false);
                    if (!cancel.IsCancellationRequested)
                        ProgressText.Text = Loc.F("packText.web.couldNotOpen", "why", Loc.T("packText.web.timedOut"));
                    return notPassed;
                }
                await starting;
                // A check, not a browser: nothing opens another window or saves a file.
                web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
                web.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
                _checkWeb = web;
            }
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowCheck(false);
            ProgressText.Text = Loc.T("packText.web.noRuntime");
            return notPassed;
        }
        catch (Exception ex)
        {
            ShowCheck(false);
            ProgressText.Text = Loc.F("packText.web.couldNotOpen", "why", ex.Message);
            return notPassed;
        }

        var core = _checkWeb.CoreWebView2;
        var outcome = new TaskCompletionSource<TranslationRun.CheckOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Passed when the page has moved on from the check to what was asked
        // for - Google sends the person back there once they have answered.
        // Still Google's page, with no check on it once it has had time to
        // draw one: nothing for anybody to answer.
        async void Landed(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                string json = await core.ExecuteScriptAsync("document.body ? document.body.innerText : ''"); // English on purpose: script run in the page, never shown
                string text = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(json) ?? "";
                bool onGooglesPage = core.Source.Contains("/sorry/", StringComparison.OrdinalIgnoreCase)
                                     || GoogleReply.IsBlockPage(text);
                if (!onGooglesPage)
                {
                    outcome.TrySetResult(TranslationRun.CheckOutcome.Passed);
                    return;
                }
                for (int i = 0; i < 10; i++)
                {
                    if (await core.ExecuteScriptAsync(FindCheck) == "true") return;   // there is one: wait for the person
                    await Task.Delay(800, cancel);
                }
                outcome.TrySetResult(TranslationRun.CheckOutcome.NothingToAnswer);
            }
            catch (Exception) { /* read again on the next page */ }
        }

        core.NavigationCompleted += Landed;
        using var stop = cancel.Register(() => outcome.TrySetResult(notPassed));
        try
        {
            core.Navigate(address);
            var ended = await outcome.Task;
            if (ended == TranslationRun.CheckOutcome.NothingToAnswer)
            {
                ProgressText.Text = Loc.T("translatePack.check.none");
                return ended;
            }
            if (ended != TranslationRun.CheckOutcome.Passed) return ended;

            var cookies = await core.CookieManager.GetCookiesAsync(null);
            _translator?.TakeCookies(cookies.Select(c => c.ToSystemNetCookie()));
            ProgressText.Text = Loc.T("translatePack.check.passed");
            return ended;
        }
        finally
        {
            core.NavigationCompleted -= Landed;
            ShowCheck(false);
        }
    }

    /// <summary>The check over the list of languages, with room for it: a
    /// captcha's pictures need more than the list has.</summary>
    private void ShowCheck(bool on)
    {
        if (on == (CheckPanel.Visibility == Visibility.Visible)) return;
        CheckPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
        {
            NamesBox.Visibility = Visibility.Collapsed;
            if (ActualHeight < 860) { _heightBefore = Height; Height = 860; }
            return;
        }
        if (_heightBefore != null) { Height = _heightBefore.Value; _heightBefore = null; }
        RefreshNames();
    }

    /// <summary>
    /// Ask the translator how the names still without a spelling are written
    /// in <paramref name="codes"/>, and put its answers in the grid, marked as
    /// suggestions for the author to check. Nothing is translated yet.
    /// </summary>
    private async Task SuggestSpellings(GoogleTranslator translator, IReadOnlyList<string> codes, CancellationToken cancel)
    {
        ProgressText.Text = Loc.T("translatePack.spellings.asking");
        var unread = new List<string>();
        foreach (string code in codes)
        {
            var blank = _names.Where(n => NeedsAsking(code, n)).ToList();
            try
            {
                string language = TranslationFiles.NativeName(code) ?? code;
                var got = await PackTranslationJob.SuggestSpellings(translator.Send, blank, code, cancel, AskAPerson,
                                                                    async c => (await OpenWebsite(c)).Send,
                                                                    answer => unread.Add(Loc.F("translatePack.spellings.unread",
                                                                        "language", language, "answer", answer)));
                foreach (var pair in got) CellFor(code, pair.Key).SetSpelling(pair.Value, suggested: true);
                // Kept until checked; a name with nothing suggested is marked as
                // asked, so it is not asked again.
                foreach (string name in blank)
                    Services.EditorPrefs.SetNameSuggestion(code, name, got.TryGetValue(name, out var s) ? s : "");
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Not counted as asked: once the service answers again, the
                // next press asks again rather than going on without. And the
                // other languages are not asked either - a refused connection
                // asked again is only more traffic towards a longer block.
                // The reason on a line of its own, under what to do about it.
                ProgressText.Text = Loc.T("translatePack.spellings.failed") + "\n" + ex.Message;
                return;
            }
        }
        ProgressText.Text = Loc.T("translatePack.spellings.check")
                            + (unread.Count > 0 ? "\n" + string.Join("\n", unread) : "");
    }

    /// <summary>The game's characters' names alone - not the pack's - with their
    /// spellings from <see cref="CastNames"/>; null when no line names one.</summary>
    private PackTranslationJob.KeptNames? CastOnly(IReadOnlyList<string> languages)
    {
        var cast = _names.Where(n => !_ownNames.Contains(n) && CastNames.Has(n)).ToList();
        if (cast.Count == 0) return null;
        var spellings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string code in languages)
        {
            var inLanguage = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in cast)
            {
                string? spelled = CastNames.In(code, name);
                if (spelled != null) inLanguage[name] = spelled;
            }
            spellings[code] = inLanguage;
        }
        return new PackTranslationJob.KeptNames { Names = cast, Spellings = spellings };
    }

    /// <summary>
    /// The names to keep, with the spellings in the grid - which count as
    /// checked from here on, and are remembered for next time. A name left
    /// empty is kept in Latin letters.
    /// </summary>
    private PackTranslationJob.KeptNames KeptNames(IReadOnlyList<string> spelledLanguages)
    {
        var spellings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string code in spelledLanguages)
        {
            var inLanguage = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in _names)
            {
                var cell = CellFor(code, name);
                cell.Suggested = false;
                string spelling = cell.Spelling.Trim();
                if (spelling.Length == 0) continue;
                inLanguage[name] = spelling;
                Services.EditorPrefs.SetNameSpelling(code, name, spelling);
                // Checked now: no longer a suggestion.
                Services.EditorPrefs.SetNameSuggestion(code, name, "");
            }
            spellings[code] = inLanguage;
        }
        return new PackTranslationJob.KeptNames { Names = _names, Spellings = spellings };
    }

    /// <summary>Re-read what each language now has, so the notes say what is
    /// true after the run rather than what was true before it.</summary>
    private void Refresh()
    {
        var now = PackTranslationJob.StillToTranslate(_pack, _root)
                                    .ToDictionary(w => w.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var choice in _choices)
        {
            if (!now.TryGetValue(choice.Code, out var language)) continue;
            choice.Note = Note(language.HasFile, language.Missing, language.Total);
            choice.Enabled = language.Missing > 0;
            choice.Wanted = false;
        }
    }
}
