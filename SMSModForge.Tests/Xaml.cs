using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SMSModForge.Tests;

/// <summary>
/// What the window actually contains, read out of MainWindow.xaml.
/// <para/>
/// The tutorials point at controls by anchor id, and every broken tutorial so
/// far has been a step aimed at an id that is not there any more — a rename, a
/// moved panel, a typo. That is a fact about the XAML, so it is checked against
/// the XAML rather than by opening a window: no WPF, no Application, and it
/// runs in milliseconds.
/// </summary>
internal static class Xaml
{
    private static string? _cachedPath;

    /// <summary>
    /// MainWindow.xaml, found by walking up from the test assembly.
    /// <para/>
    /// The tests read the SOURCE file, not a copy in the output directory. A
    /// copy would be one build behind whenever the build is what broke, which
    /// is exactly when these checks matter.
    /// </summary>
    public static string MainWindowPath
    {
        get
        {
            if (_cachedPath != null) return _cachedPath;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "SMSModForge", "MainWindow.xaml");
                if (File.Exists(candidate)) return _cachedPath = candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException(
                "Could not find SMSModForge/MainWindow.xaml above " + AppContext.BaseDirectory);
        }
    }

    public static string Text => File.ReadAllText(MainWindowPath);

    private static readonly Regex AnchorRe =
        new(@"TutorialAnchor\.Id\s*=\s*""([^""]+)""", RegexOptions.Compiled);

    private static readonly Regex TabRe =
        new(@"<TabItem[^>]*?Header\s*=\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Every anchor id the window declares.</summary>
    public static HashSet<string> AnchorIds()
        => new(AnchorRe.Matches(Text).Select(m => m.Groups[1].Value), StringComparer.Ordinal);

    /// <summary>Tab headers in the order they appear, which is the order the
    /// TabControl indexes them.</summary>
    public static List<string> TabHeaders()
        => TabRe.Matches(Text)
                .Select(m => m.Groups[1].Value.Replace("⚒", "").Trim())
                .ToList();

    /// <summary>
    /// Which tab index each anchor sits under, by position in the file. An
    /// anchor after tab N's opening tag and before tab N+1's belongs to N —
    /// crude, and exactly right for a file whose tabs do not nest.
    /// </summary>
    public static Dictionary<string, int> AnchorTabIndex()
    {
        string text = Text;
        var tabStarts = TabRe.Matches(text).Select(m => m.Index).ToList();
        var map = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match m in AnchorRe.Matches(text))
        {
            int tab = -1;
            for (int i = 0; i < tabStarts.Count; i++)
                if (m.Index > tabStarts[i]) tab = i;
            // First one wins: an id declared twice is its own failure, reported
            // by the duplicate check rather than silently overwritten here.
            if (!map.ContainsKey(m.Groups[1].Value))
                map[m.Groups[1].Value] = tab;
        }
        return map;
    }
}
