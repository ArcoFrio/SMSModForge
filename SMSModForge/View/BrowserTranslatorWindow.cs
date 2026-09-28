using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SMSModForge.Localization;
using SMSModForge.Services.Translation;

namespace SMSModForge.View;

/// <summary>
/// The Google Translate web page in a small window, used as a translator when
/// Google's free translation service fails its test sentence.
/// <para/>
/// On screen rather than hidden, on purpose: it is the author's own
/// connection talking to Google, and if Google stops to ask something - a
/// captcha, a consent question - the page is right there. None of those is
/// answered here; the run stops, the window stays, and the person decides.
/// <para/>
/// Drawn with Windows' own browser control (WebView2), which comes with
/// Windows 11. Where it is missing the window says so and nothing is sent.
/// What it has to decide - the address, the page's language codes, when the
/// translation is there - is <see cref="GoogleWebPage"/>'s.
/// </summary>
public sealed class BrowserTranslatorWindow : Window
{
    /// <summary>
    /// The page's cookies and whatever Google was told on its consent page,
    /// kept beside the editor's other settings so a question answered once is
    /// not asked every run.
    /// </summary>
    internal static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SMSModForge", "WebView2");

    private readonly WebView2 _web = new();

    /// <summary>
    /// Locks every page the window loads while ModForge works it: a click or a
    /// keypress there would land in the text being translated, or move the
    /// page from under the reading of it. From inside the page, because the
    /// browser draws in a window of its own that the editor's "disabled" does
    /// not reach - that was tried, and the page still took clicks. The top page
    /// only: a captcha's own frame is never touched. <see cref="Unlock"/> lifts it.
    /// </summary>
    private const string LockScript =
        "(function(){if(window.top!==window||window.__smsLock!==undefined)return;window.__smsLock=true;var stop=function(e){if(window.__smsLock){e.preventDefault();e.stopImmediatePropagation();}};['mousedown','mouseup','click','dblclick','auxclick','contextmenu','keydown','keypress','keyup','pointerdown','pointerup','touchstart','touchend','paste','drop','dragstart'].forEach(function(t){window.addEventListener(t,stop,true);});})();"; // English on purpose: script run in the page, never shown

    private const string UnlockScript = "window.__smsLock=false;"; // English on purpose: script run in the page, never shown

    /// <summary>The lock's registration, so it can be taken off again.</summary>
    private string? _lockId;

    /// <summary>The page is showing something the person should see - a
    /// check or a question - so the window stays when the run is over.</summary>
    public bool KeepOpen { get; private set; }

    private BrowserTranslatorWindow()
    {
        Title = Loc.T("packText.web.title");
        Width = 560;
        Height = 460;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var explain = new TextBlock
        {
            Text = Loc.T("packText.web.explain"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10, 8, 10, 8),
        };
        var layout = new DockPanel();
        DockPanel.SetDock(explain, Dock.Top);
        layout.Children.Add(explain);
        layout.Children.Add(_web);
        Content = layout;

        Closed += (_, _) => _closed = true;
    }

    /// <summary>Closed by the person while a run was going: the run stops.</summary>
    private bool _closed;

    /// <summary>
    /// Open the window with the browser ready in it, or say why it cannot be.
    /// On the UI thread.
    /// </summary>
    public static async Task<(BrowserTranslatorWindow? Window, string? WhyNot)> Open(Window? owner)
    {
        var window = new BrowserTranslatorWindow();
        if (owner != null && owner.IsLoaded) window.Owner = owner;

        // Never over somebody's work while the tests run: it is still made and
        // used, where nobody can see it.
        if (Services.TestMode.Active)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -32000;
            window.Top = -32000;
            window.ShowActivated = false;
        }
        window.Show();

        try
        {
            Directory.CreateDirectory(DataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, DataFolder);
            await window._web.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            window.Close();
            return (null, Loc.T("packText.web.noRuntime"));
        }
        catch (Exception ex)
        {
            window.Close();
            return (null, Loc.F("packText.web.couldNotOpen", "why", ex.Message));
        }

        var core = window._web.CoreWebView2;
        // A translator, not a browser: nothing the page does opens another
        // window or saves a file.
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        // Locked from the first page on.
        try { window._lockId = await core.AddScriptToExecuteOnDocumentCreatedAsync(LockScript); }
        catch (Exception) { /* unlocked is how it always was */ }
        return (window, null);
    }

    /// <summary>
    /// Translate one joined text through the page. From any thread; the page
    /// is worked on its own. Matches <see cref="GoogleTranslator.Asker"/>.
    /// </summary>
    public Task<string> Ask(string text, string? from, string to, CancellationToken cancel)
        => Dispatcher.InvokeAsync(async () =>
        {
            try { return await AskHere(text, from, to, cancel); }
            catch (Exception ex) when (_closed && ex is InvalidOperationException or ObjectDisposedException)
            {
                throw Closing();
            }
        }).Task.Unwrap();

    /// <summary>Stopping, not retried: nobody is going to reopen the window.</summary>
    private static TranslationRun.ServiceRefused Closing()
        => new(0, Loc.T("packText.web.closed"), blocked: true);

    private async Task<string> AskHere(string text, string? from, string to, CancellationToken cancel)
    {
        if (_closed || _web.CoreWebView2 == null) throw Closing();
        var core = _web.CoreWebView2;

        var loaded = new TaskCompletionSource<CoreWebView2WebErrorStatus?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            => loaded.TrySetResult(e.IsSuccess ? null : e.WebErrorStatus);

        core.NavigationCompleted += Loaded;
        try
        {
            var started = DateTime.UtcNow;
            core.Navigate(GoogleWebPage.Url(text, from, to));

            var first = await Task.WhenAny(loaded.Task, Task.Delay(GoogleWebPage.GiveUpAfter, cancel));
            cancel.ThrowIfCancellationRequested();
            if (first != loaded.Task)
                throw new TranslationRun.ServiceRefused(0, Loc.T("packText.web.timedOut"));
            var error = await loaded.Task;
            if (error != null)
                throw new TranslationRun.ServiceRefused(0, Loc.F("packText.web.couldNotOpen", "why", error.Value.ToString()));

            string? before = null;
            while (DateTime.UtcNow - started < GoogleWebPage.GiveUpAfter)
            {
                cancel.ThrowIfCancellationRequested();
                if (_closed) throw Closing();
                var reading = GoogleWebPage.Read(await core.ExecuteScriptAsync(GoogleWebPage.ReadScript));
                switch (GoogleWebPage.Judge(reading, to, before))
                {
                    case GoogleWebPage.State.Done:
                        return reading!.Result!;

                    case GoogleWebPage.State.Blocked:
                        Unlock();
                        throw new TranslationRun.ServiceRefused(429, Loc.T("packText.web.blocked"), blocked: true);

                    case GoogleWebPage.State.Consent:
                        Unlock();
                        throw new TranslationRun.ServiceRefused(0, Loc.T("packText.web.consent"), blocked: true);

                    case GoogleWebPage.State.OtherLanguage:
                        // Not retried: the page does not know the code, and
                        // asking again gets the same other language.
                        throw new TranslationRun.ServiceRefused(400, Loc.F("packText.web.language", "code", to));
                }
                before = reading?.Result;
                await Task.Delay(GoogleWebPage.ReadEvery, cancel);
            }
            throw new TranslationRun.ServiceRefused(0, Loc.T("packText.web.timedOut"));
        }
        finally
        {
            core.NavigationCompleted -= Loaded;
        }
    }

    /// <summary>Google is asking the person something: the page is theirs to
    /// use - this one and any it goes on to - and the window stays when the
    /// run is over.</summary>
    private void Unlock()
    {
        KeepOpen = true;
        var core = _web.CoreWebView2;
        if (core != null)
        {
            if (_lockId != null) core.RemoveScriptToExecuteOnDocumentCreated(_lockId);
            _lockId = null;
            _ = core.ExecuteScriptAsync(UnlockScript);
        }
        Activate();
        _web.Focus();
    }

    /// <summary>The run is over: close, unless the page is showing the
    /// person something.</summary>
    public void Finished()
    {
        if (!KeepOpen && !_closed) Close();
    }
}
