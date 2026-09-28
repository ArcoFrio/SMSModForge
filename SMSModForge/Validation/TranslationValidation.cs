using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;

namespace SMSModForge.Validation;

/// <summary>
/// The pack's words against the language it says they are in, and against its
/// translations.
/// <para/>
/// A text typed while the editor showed a translation, where the pack had no
/// words of its own, belongs to that translation only: the pack's own words
/// for it stay empty (decided by the author, 2026-09-24). In game nobody sees a
/// blank - it is read in the player's language, else in the first translation
/// that has it - but a player of any other language reads it in a language
/// that is not theirs, and nothing on screen tells the author so. This does.
/// </summary>
public static class TranslationValidation
{
    public const string OnlyInATranslation = "text.onlyInTranslation";
    public const string OtherLanguage = "text.otherLanguage";
    public const string PackInOtherLanguage = "pack.otherLanguage";

    /// <summary>At least this many texts plainly in another language, and at
    /// least half of those that could be judged, is the pack being in that
    /// language rather than a few lines typed in the wrong place.</summary>
    public const int WholePackAfter = 10;

    /// <summary>
    /// Both checks, over the pack as it would be saved - which must be in its
    /// own words; the validator is run that way.
    /// </summary>
    public static void Check(List<ValidationIssue> issues, ModPack pack, string? packRoot)
    {
        if (pack == null) return;
        var json = JObject.Parse(PackRepository.SerializeAsSaved(pack));
        var sites = PackTexts.Of(json, withEmpty: true);
        var gameQuests = new HashSet<string>(
            (json["quests"] as JArray ?? new JArray()).OfType<JObject>()
                .Where(q => !string.IsNullOrEmpty((string?)q["source"]))
                .Select(q => (string?)q["key"] ?? ""),
            StringComparer.OrdinalIgnoreCase);

        CheckLanguage(issues, pack, sites, gameQuests);
        CheckOnlyInATranslation(issues, pack, packRoot, sites, gameQuests);
        CheckFit(issues, pack, packRoot, sites, gameQuests);
    }

    public const string LineTooLong = "dialogue.lineTooLong";

    /// <summary>
    /// Lines too long for the game's dialogue box - in the default text and in
    /// every translation, since a translation is often the longer of the two.
    /// <para/>
    /// Only a line that runs OUT of the box is reported. Being made smaller to
    /// fit is what the game's box does by design, and the Text box says so as
    /// the line is typed; a line that still does not fit at the smallest size
    /// is drawn past the panel and over whatever is below it, which is a
    /// mistake nobody would make on purpose. See <see cref="Rendering.DialogueFit"/>.
    /// </summary>
    private static void CheckFit(List<ValidationIssue> issues, ModPack pack, string? packRoot,
                                 List<PackTexts.Site> sites, HashSet<string> gameQuests)
    {
        if (!Rendering.VanillaUiLibrary.IsAvailable) return;
        var font = Rendering.VanillaUiLibrary.Assets.Font(Rendering.DialogueLook.FontName)?.Font;
        if (font == null) return;

        var files = new List<(string Code, TextFile File)>();
        if (!string.IsNullOrEmpty(packRoot) && Directory.Exists(PackTranslations.FolderOf(packRoot)))
            foreach (string code in Services.Translation.PackTranslationJob.Languages(packRoot))
            {
                var file = Loc.Read(PackTranslations.PathOf(packRoot, code));
                if (file != null) files.Add((code, file));
            }

        foreach (var site in sites)
        {
            if (site.Kind != PackTexts.Kind.Line) continue;
            // A choice's options are drawn somewhere else, in a box of their own.
            string kind = (string?)site.Holder["kind"] ?? nameof(DialogueNodeKind.Text);
            if (!string.Equals(kind, nameof(DialogueNodeKind.Text), StringComparison.OrdinalIgnoreCase)) continue;

            void Measure(string? words, string code)
            {
                if (PackTexts.IsEmpty(words)) return;
                if (Rendering.DialogueFit.Of(font, words).Verdict != Rendering.DialogueFit.Verdict.Spills) return;
                issues.Add(new ValidationIssue(Severity.Warning, Where(site, pack, gameQuests),
                    Loc.F("validation.dialogue.lineTooLong", "language", TranslationFiles.NativeName(code) ?? code),
                    LineTooLong));
            }

            Measure(site.Text, pack.OwnLanguage);
            foreach (var (code, file) in files)
                Measure(PackTexts.Usable(file, site.Key, site.Text), code);
        }
    }

    /// <summary>
    /// Texts the pack says are in its language that plainly are not - Spanish
    /// typed into the pack's own words while meaning to type it into the
    /// Spanish. See <see cref="LanguageGuess"/> for what counts as plainly.
    /// <para/>
    /// Warnings, not errors: a character who speaks Japanese in an English
    /// pack is a choice, and one the author can silence line by line. When
    /// most of the pack is in another language, one issue says so instead of
    /// hundreds - that pack is not full of mistakes, it says the wrong language.
    /// </summary>
    private static void CheckLanguage(List<ValidationIssue> issues, ModPack pack,
                                      List<PackTexts.Site> sites, HashSet<string> gameQuests)
    {
        string own = pack.OwnLanguage;
        string ownName = TranslationFiles.NativeName(own) ?? own;
        var found = new List<(PackTexts.Site Site, LanguageGuess.Finding Finding)>();
        int judged = 0;
        foreach (var site in sites)
        {
            if (PackTexts.IsEmpty(site.Text) || !LanguageGuess.Judgeable(site.Text)) continue;
            judged++;
            var finding = LanguageGuess.NotIn(site.Text, own);
            if (finding != null) found.Add((site, finding));
        }
        if (found.Count == 0) return;

        if (found.Count >= WholePackAfter && found.Count * 2 >= judged)
        {
            // The language most of them look like, if most of them say one.
            var named = found.Where(f => f.Finding.Language != null)
                             .GroupBy(f => f.Finding.Language!).OrderByDescending(g => g.Count()).FirstOrDefault();
            issues.Add(new ValidationIssue(Severity.Warning, "$." + PackTexts.LanguageField,
                named != null && named.Count() * 2 > found.Count
                    ? Loc.F("validation.pack.looksLike", "language", Name(named.Key), "own", ownName)
                    : Loc.F("validation.pack.notOwnLanguage", "own", ownName),
                PackInOtherLanguage));
            return;
        }

        foreach (var (site, finding) in found)
            issues.Add(new ValidationIssue(Severity.Warning, Where(site, pack, gameQuests),
                finding.Language != null
                    ? Loc.F("validation.text.looksLike", "language", Name(finding.Language), "own", ownName)
                    : Loc.F("validation.text.notOwnLanguage", "own", ownName),
                OtherLanguage));
    }

    private static string Name(string code) => TranslationFiles.NativeName(code) ?? code;

    /// <summary>Every text with no words of the pack's own that one of its
    /// translations has words for.</summary>
    private static void CheckOnlyInATranslation(List<ValidationIssue> issues, ModPack pack, string? packRoot,
                                                List<PackTexts.Site> sites, HashSet<string> gameQuests)
    {
        if (string.IsNullOrEmpty(packRoot)) return;
        string folder = PackTranslations.FolderOf(packRoot);
        if (!Directory.Exists(folder)) return;

        // By code, the order the game tries them in.
        var files = new List<(string Code, TextFile File)>();
        foreach (string code in Services.Translation.PackTranslationJob.Languages(packRoot))
        {
            var file = Loc.Read(PackTranslations.PathOf(packRoot, code));
            if (file != null) files.Add((code, file));
        }
        if (files.Count == 0) return;

        foreach (var site in sites)
        {
            if (!PackTexts.IsEmpty(site.Text)) continue;

            var has = files.Where(f => PackTexts.Usable(f.File, site.Key, site.Text) != null)
                           .Select(f => f.Code).ToList();
            if (has.Count == 0) continue;

            string own = TranslationFiles.NativeName(pack.OwnLanguage) ?? pack.OwnLanguage;
            issues.Add(new ValidationIssue(Severity.Error, Where(site, pack, gameQuests),
                Loc.F("validation.text.onlyInTranslation",
                      "own", own,
                      "languages", string.Join(", ", has.Select(c => TranslationFiles.NativeName(c) ?? c)),
                      "first", TranslationFiles.NativeName(has[0]) ?? has[0]),
                OnlyInATranslation));
        }
    }

    /// <summary>Where the text is, in the form double-clicking an issue
    /// understands - see <c>MainWindow.NavigateToIssue</c>.</summary>
    private static string Where(PackTexts.Site site, ModPack pack, HashSet<string> gameQuests)
    {
        switch (site.Kind)
        {
            case PackTexts.Kind.Line:
                return $"dialogues[{site.Owner}].nodes[id={site.Detail}].text";
            case PackTexts.Kind.CharacterName:
                return $"actors[{site.Owner}].displayName";
            case PackTexts.Kind.QuestTitle:
                return $"quests[{site.Owner}].title";
            case PackTexts.Kind.QuestDescription:
                return $"quests[{site.Owner}].description";
            case PackTexts.Kind.TaskName:
            case PackTexts.Kind.TaskQuestDescription:
            {
                string list = gameQuests.Contains(site.Owner) ? "addedTasks" : "tasks";
                string field = site.Kind == PackTexts.Kind.TaskName ? "name" : "questDescription";
                return $"quests[{site.Owner}].{list}[{site.Detail}].{field}";
            }
            case PackTexts.Kind.GameTaskQuestDescription:
                return $"quests[{site.Owner}].vanillaTasks[{site.Detail}].questDescription";
            case PackTexts.Kind.NavigatorLabel:
            {
                int i = pack.VanillaExtensions.FindIndex(e => string.Equals(e.Source, site.Owner, StringComparison.OrdinalIgnoreCase));
                return i >= 0
                    ? $"vanillaExtensions[{i}:{site.Owner}].navigatorButtons[{site.Detail}].label"
                    : $"places[{site.Owner}].navigatorButtons[{site.Detail}].label";
            }
            case PackTexts.Kind.MapLabel:
            {
                int i = pack.MapButtons.FindIndex(b => (b.District ?? "") == site.Owner && (b.Target ?? "") == site.Detail);
                return $"mapButtons[{i}:{site.Owner}→{site.Detail}].label";
            }
            case PackTexts.Kind.UiText:
                return $"uis[{site.Owner}].{site.Detail}.text";
            default:
                return site.Key;
        }
    }
}
