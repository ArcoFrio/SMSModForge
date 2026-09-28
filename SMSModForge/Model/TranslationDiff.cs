using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Shared;

namespace SMSModForge.Model;

/// <summary>
/// What a save would change in one of the pack's translations, for the list
/// shown before it: each text whose words in that language are added,
/// changed or taken out, against the file on disk.
/// <para/>
/// Only the words. A file carries more than that - each text's English note,
/// the lines nobody has translated yet in the pack's default words - and a
/// change to those is not a change to the translation: a line still in the
/// default words is untranslated, on both sides, whatever those words are.
/// </summary>
public static class TranslationDiff
{
    public static List<PackChange> Compute(string code, TextFile? before, TextFile after)
    {
        var changes = new List<PackChange>();
        if (after == null) return changes;

        string section = Loc.F("diff.translation", "language", TranslationFiles.NativeName(code) ?? code);
        var keys = after.Entries.Select(e => e.Key)
                        .Concat(before == null ? Enumerable.Empty<string>() : before.Entries.Select(e => e.Key))
                        .Distinct(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            string was = Words(before?.Find(key));
            string now = Words(after.Find(key));
            if (string.Equals(PackTexts.Normal(was), PackTexts.Normal(now), StringComparison.Ordinal)) continue;

            changes.Add(new PackChange
            {
                Kind = was.Length == 0 ? PackChangeKind.Added : now.Length == 0 ? PackChangeKind.Removed : PackChangeKind.Changed,
                Section = section,
                Path = key,
                Before = PackDiff.Truncate(was),
                After = PackDiff.Truncate(now),
            });
        }
        return changes;
    }

    /// <summary>
    /// The words a file gives a text in its language, or "" when it gives
    /// none: no entry, an empty one, or one still in the default words it was
    /// made from - unless it is marked as the same in this language, which is
    /// a translation that happens to read like the default.
    /// </summary>
    private static string Words(TextFile.Entry? entry)
    {
        if (entry == null || entry.Blank || string.IsNullOrEmpty(entry.Text)) return "";
        if (!entry.Same && entry.English != null
            && string.Equals(PackTexts.Normal(entry.Text), PackTexts.Normal(entry.English), StringComparison.Ordinal))
            return "";
        return entry.Text;
    }
}
