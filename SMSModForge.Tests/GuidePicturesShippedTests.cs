using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Documentation;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Every page of the guide shows at least two pictures (the author, 2026-11-21: "I was expecting more images, at least
/// more than one in each page. We should be not only telling the user where something is, but also adding a small
/// image to show them"). Counted from what ships in Resources/GuidePictures, in the Classic look: the page's own
/// picture and its sections'. A new page or section fails here until GuideSectionShots has a spot for it and the
/// pictures are taken again (Tools/Docs/guide-pictures.py).
/// </summary>
public sealed class GuidePicturesShippedTests
{
    private static string Folder => Path.Combine(TranslationCoverageTests.EditorDir, "Resources", "GuidePictures");

    private static bool Has(string name)
        => File.Exists(Path.Combine(Folder, name.Replace('/', Path.DirectorySeparatorChar) + ".png"));

    [Fact]
    public void EveryPageOfTheGuideHasAtLeastTwoPictures()
    {
        var thin = new List<string>();
        foreach (var topic in DocTopics.Parts.SelectMany(p => p.Topics))
        {
            int count = (Has(topic.PictureName) ? 1 : 0) + topic.Sections.Count(s => Has(s.PictureName));
            if (count < 2) thin.Add($"{topic.PictureName}: {count}");
        }
        Assert.True(thin.Count == 0, "guide pages with fewer than two pictures:\n" + string.Join("\n", thin));
    }

    [Fact]
    public void EveryPictureTheGuideShipsIsOneItShows()
    {
        var wanted = DocTopics.Parts.SelectMany(p => p.Topics)
            .SelectMany(t => t.Sections.Select(s => s.PictureName).Prepend(t.PictureName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stray = Directory.EnumerateFiles(Folder, "*.png", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Folder, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Select(f => f[..^4].Replace(".EX", ""))
            .Where(n => !wanted.Contains(n))
            .ToList();
        Assert.True(stray.Count == 0, "pictures no page shows (a renamed section's old picture?):\n" + string.Join("\n", stray));
    }

    /// <summary>The pictures ship lossless, at the size they were taken (1.10.0): as JPGs at 80, cut to 820 wide, the
    /// author found them "poorly compressed" once one could be opened at full size and zoomed into - the red boxes
    /// and the editor's small words smeared. A JPG here is one of those left behind.</summary>
    [Fact]
    public void ThePicturesAreLossless()
    {
        var lossy = Directory.EnumerateFiles(Folder, "*.jp*g", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Folder, f)).ToList();
        Assert.True(lossy.Count == 0, "JPGs among the guide's pictures (Tools/Docs/guide-pictures.py writes PNGs):\n" + string.Join("\n", lossy));
    }
}
