using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Shared;

namespace SMSModForge.Localization;

/// <summary>
/// What something new starts out called - "New SFX", "Nuevo personaje" - in
/// each language the editor has.
/// <para/>
/// A new record is named in the language its name is written in, not the one
/// the editor is shown in (see <c>MainViewModel.NewName</c>), so the name it
/// arrives with can be in any of them; and the tutorial asks four times a
/// second whether a name is still one of those. Every language file is read
/// once for that and kept until the editor's languages change.
/// </summary>
public static class NewNames
{
    private static readonly object Gate = new();
    private static Dictionary<string, Texts>? _languages;

    static NewNames() => Loc.Changed += Forget;

    /// <summary>Read the language files again next time: one was switched
    /// to, or one of its texts corrected.</summary>
    public static void Forget()
    {
        lock (Gate) _languages = null;
    }

    private static Dictionary<string, Texts> Languages()
    {
        lock (Gate)
        {
            if (_languages != null) return _languages;
            var all = new Dictionary<string, Texts>(StringComparer.OrdinalIgnoreCase);
            foreach (var language in Loc.Available()) all[language.Code] = Loc.Preview(language);
            return _languages = all;
        }
    }

    /// <summary>
    /// <paramref name="key"/> in <paramref name="language"/>, or in the
    /// nearest one the editor has (pt-BR for pt); English when it has none.
    /// </summary>
    public static string In(string? language, string key)
    {
        var all = Languages();
        string? code = LanguageMatch.Best(language ?? "", all.Keys);
        return code != null && all.TryGetValue(code, out var texts)
            ? texts.T(key)
            : Loc.Preview(Loc.EnglishCode).T(key);
    }

    /// <summary><paramref name="key"/> in every language the editor has.</summary>
    public static IEnumerable<string> Everywhere(string key)
        => Languages().Values.Select(t => t.T(key)).Distinct();
}
