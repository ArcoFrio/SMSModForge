using System;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Services.Translation;

/// <summary>
/// The Google Translate web page, as a translator: what to open, and how to
/// read what it shows. The engine behind the page is the same one
/// <see cref="GoogleTranslator"/> asks directly; this is the way in used when
/// that one stops passing the test sentence - it has changed how it answers,
/// or it no longer answers the way it is asked.
/// <para/>
/// The window that holds the page is <see cref="View.BrowserTranslatorWindow"/>;
/// everything it has to decide is here, where it can be tested without one.
/// <para/>
/// <b>Read off the real page in September 2026</b>, not from documentation,
/// because there is none:
/// <list type="bullet">
/// <item>The text goes in the address - <c>?sl=..&amp;tl=..&amp;text=..</c> -
/// and the page translates it as it opens.</item>
/// <item>The translation is in a <c>span</c> whose <c>lang</c> is the page's
/// code for the language it translated into. Its class names are generated and
/// change; a <c>lang</c> attribute is there for screen readers, so it stays.
/// The line markers <see cref="GoogleTranslator.Join"/> puts in came back
/// intact, in Spanish and in Chinese.</item>
/// <item>The page's codes are not Windows': <c>zh-Hans</c> is <c>zh-CN</c>
/// and <c>pt-BR</c> is <c>pt</c>. A code it does not know is not refused - it
/// quietly translates into the LAST language used instead and rewrites the
/// address to say so. So the language in the address is checked every time;
/// a line in the wrong language filed as a translation is worse than none.</item>
/// <item>The translation can take several seconds to appear.</item>
/// </list>
/// <para/>
/// <b>What it will not do.</b> A captcha, an "unusual traffic" page or a
/// question Google asks before it translates (its consent page) stops the run
/// and stays on screen for the person to see. None of them is answered here.
/// </summary>
public static class GoogleWebPage
{
    private const string Address = "https://translate.google.com/";

    /// <summary>
    /// The longest joined text sent at once. The page takes five thousand
    /// characters; less than that, so the markers never push a batch over.
    /// </summary>
    public const int LongestText = 4500;

    /// <summary>How often the page is read while the translation is coming,
    /// and how long it is given.</summary>
    public static readonly TimeSpan ReadEvery = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The page's code for a language: the few it spells differently from
    /// Windows, and the rest as they are. What the page does not know, it
    /// says by rewriting the address - see <see cref="Judge"/>.
    /// </summary>
    public static string CodeFor(string? code)
    {
        string c = (code ?? "").Trim();
        if (c.Length == 0 || string.Equals(c, "auto", StringComparison.OrdinalIgnoreCase)) return "auto";
        switch (c.ToLowerInvariant())
        {
            case "zh-hans": case "zh-cn": case "zh-sg": case "zh": return "zh-CN";
            case "zh-hant": case "zh-tw": case "zh-hk": case "zh-mo": return "zh-TW";
            case "pt-br": case "pt": return "pt";
            case "pt-pt": return "pt-PT";
            default: return c;
        }
    }

    /// <summary>The page, opened on <paramref name="text"/>.</summary>
    public static string Url(string text, string? from, string to)
        => Address
           + "?sl=" + Uri.EscapeDataString(CodeFor(from))
           + "&tl=" + Uri.EscapeDataString(CodeFor(to))
           + "&text=" + Uri.EscapeDataString(text ?? "")
           + "&op=translate";

    /// <summary>
    /// Run in the page: what it says it translated into, whether it is a block
    /// or a question rather than the translator, and the translation if there
    /// is one yet. Returns a JSON text for <see cref="Read"/>.
    /// </summary>
    // English on purpose: JavaScript run inside the page, never shown to anyone.
    public const string ReadScript = @"(() => {
  const tl = new URLSearchParams(location.search).get('tl') || '';
  const text = document.body ? document.body.innerText : '';
  const blocked = location.pathname.indexOf('/sorry/') >= 0
    || !!document.querySelector('iframe[src*=""recaptcha""]')
    || /unusual traffic/i.test(text);
  const consent = location.hostname.indexOf('consent.') === 0;
  let result = null;
  if (tl) {
    const span = document.querySelector('span[lang=""' + CSS.escape(tl) + '""]');
    if (span) result = span.innerText;
  }
  return JSON.stringify({ host: location.hostname, tl: tl, blocked: blocked, consent: consent, result: result });
})()";

    /// <summary>What <see cref="ReadScript"/> found.</summary>
    public sealed record Reading(string Host, string Tl, bool Blocked, bool Consent, string? Result);

    /// <summary>
    /// <see cref="ReadScript"/>'s answer as the browser hands it back: the
    /// script returns a JSON text, and the browser returns THAT as JSON again.
    /// Null when it is not what the script makes.
    /// </summary>
    public static Reading? Read(string? fromBrowser)
    {
        if (string.IsNullOrWhiteSpace(fromBrowser)) return null;
        try
        {
            var outer = JToken.Parse(fromBrowser);
            string? inner = outer.Type == JTokenType.String ? (string?)outer : outer.ToString();
            if (string.IsNullOrWhiteSpace(inner)) return null;
            var o = JObject.Parse(inner);
            return new Reading((string?)o["host"] ?? "", (string?)o["tl"] ?? "",
                               (bool?)o["blocked"] ?? false, (bool?)o["consent"] ?? false,
                               (string?)o["result"]);
        }
        catch (Newtonsoft.Json.JsonException) { return null; }
    }

    public enum State
    {
        /// <summary>Still loading or still translating: read again.</summary>
        Waiting,
        /// <summary>The translation is there, and the same as last time.</summary>
        Done,
        /// <summary>A captcha or an "unusual traffic" page.</summary>
        Blocked,
        /// <summary>Google is asking something before it translates.</summary>
        Consent,
        /// <summary>The page translated into another language than asked.</summary>
        OtherLanguage,
    }

    /// <summary>
    /// What a reading means. Done only when the same translation has been read
    /// twice in a row: the page fills its answer in as it arrives, and a first
    /// look can catch half of it.
    /// </summary>
    public static State Judge(Reading? now, string to, string? readBefore)
    {
        if (now == null) return State.Waiting;
        if (now.Blocked) return State.Blocked;
        if (now.Consent) return State.Consent;
        if (now.Tl.Length > 0 && !string.Equals(now.Tl, CodeFor(to), StringComparison.OrdinalIgnoreCase))
            return State.OtherLanguage;
        if (string.IsNullOrWhiteSpace(now.Result)) return State.Waiting;
        return string.Equals(now.Result, readBefore, StringComparison.Ordinal) ? State.Done : State.Waiting;
    }
}
