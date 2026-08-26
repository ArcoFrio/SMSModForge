using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Documentation;

/// <summary>
/// Filters <see cref="DocTopics.Parts"/> down to what matches a query.
/// <para/>
/// Returns a fresh tree rather than flagging the shared one: the catalog is
/// static and handed to every view, so filtering in place would leak one
/// search's state into everything else that reads it.
/// <para/>
/// A topic is kept when the query appears anywhere in it — its title, its
/// summary, a section heading, a bullet's label or a bullet's text. What is
/// then shown inside depends on where the hit was:
/// <list type="bullet">
///   <item>Title or summary matched — the whole topic is kept, because the
///   reader asked for that topic and wants all of it.</item>
///   <item>Only the contents matched — just the matching bullets are kept, so
///   a common word does not answer with thirty untouched topics.</item>
/// </list>
/// Plain case-insensitive substring matching, deliberately: for a reference of
/// this size, a reader can predict what a query will do, which matters more
/// than clever ranking.
/// </summary>
public static class DocSearch
{
    /// <summary>Number of bullets left after the last <see cref="Filter"/> —
    /// what the result count is drawn from.</summary>
    public static int LastMatchCount { get; private set; }

    public static IReadOnlyList<DocPart> Filter(IReadOnlyList<DocPart> parts, string? query)
    {
        query = query?.Trim() ?? "";
        if (query.Length == 0)
        {
            LastMatchCount = 0;
            return parts;
        }

        int matched = 0;
        var kept = new List<DocPart>();

        foreach (var part in parts)
        {
            var topics = new List<DocTopic>();
            foreach (var topic in part.Topics)
            {
                bool headingHit = Has(topic.Title, query) || Has(topic.Summary, query);
                var sections = new List<DocSection>();

                foreach (var section in topic.Sections)
                {
                    // A heading match keeps its whole section, for the same
                    // reason a title match keeps its whole topic.
                    bool wholeSection = headingHit || Has(section.Heading, query);
                    var bullets = wholeSection
                        ? section.Bullets.ToArray()
                        : section.Bullets.Where(b => Has(b.Term, query) || Has(b.Text, query)).ToArray();

                    if (bullets.Length > 0) sections.Add(new DocSection(section.Heading, bullets));
                }

                if (sections.Count == 0) continue;

                matched += sections.Sum(s => s.Bullets.Count);
                topics.Add(new DocTopic(topic.Title, topic.Summary, sections.ToArray())
                {
                    // Expanded, because a hit the reader still has to go and
                    // open is barely a search result.
                    StartExpanded = true,
                });
            }

            if (topics.Count > 0) kept.Add(new DocPart(part.Name, topics.ToArray()));
        }

        LastMatchCount = matched;
        return kept;
    }

    private static bool Has(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack) &&
           haystack!.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
