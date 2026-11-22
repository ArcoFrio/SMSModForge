using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Every window the editor opens for a person is shut under the test harness (CLAUDE.md, "Anything that puts a
/// window on screen"): a message box, a dialog window, a file or folder picker. A window a test run raises does
/// not fail it - it stops it, on the screen of whoever started it.
/// <para/>
/// The rule was kept call by call, and two calls had slipped through (1.10.0): a game conversation's Reset all
/// asked with a bare MessageBox, and every colour button opened the colour wheel. The walk over every control
/// pressed both in EX - their buttons sit inside one of the editor's own controls there - and two testers stopped
/// dead for two minutes each. Read here from the editor's source: each line that opens one sits in a method
/// that asks <c>Services.TestMode</c>. Control: the scan finds the dialogs it should (more than a few), and a
/// made-up unguarded one in a string it is shown.
/// </summary>
public sealed class ModalsAreGuardedTests
{
    private readonly ITestOutputHelper _out;
    public ModalsAreGuardedTests(ITestOutputHelper o) => _out = o;

    private static string EditorSource([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "SMSModForge"));

    private static readonly Regex Opens = new(
        @"MessageBox\.Show\(|\.ShowDialog\(\)|new (Microsoft\.Win32\.)?(OpenFileDialog|SaveFileDialog|OpenFolderDialog)\b|FolderBrowserDialog\b",
        RegexOptions.Compiled);

    private static readonly Regex Member = new(@"^    (public|private|internal|protected|static|async)[^;=]*\(", RegexOptions.Compiled);

    /// <summary>Lines that open a window without a guard of their own, each with why that is safe.</summary>
    private static readonly Dictionary<string, string> Known = new(StringComparer.Ordinal)
    {
        ["View/SpritePickerWindow.xaml.cs|var dialog = new Microsoft.Win32.OpenFileDialog"] =
            "the Browse button inside the sprite picker, which only exists once the picker is open - and SpritePickerWindow.Pick opens nothing under the harness",
    };

    /// <summary>Each line of <paramref name="source"/> that opens a window outside a method that asks TestMode.</summary>
    internal static List<(int Line, string Text)> Unguarded(string source)
    {
        var found = new List<(int, string)>();
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("//", StringComparison.Ordinal) || !Opens.IsMatch(lines[i])) continue;
            int start = i;
            for (int j = i; j >= Math.Max(0, i - 80); j--)
                if (Member.IsMatch(lines[j])) { start = j; break; }
            bool guarded = false;
            for (int j = start; j <= i && !guarded; j++) guarded = lines[j].Contains("TestMode", StringComparison.Ordinal);
            if (!guarded) found.Add((i + 1, line));
        }
        return found;
    }

    [Fact]
    public void EveryWindowTheEditorOpensIsShutUnderTheHarness()
    {
        string root = EditorSource();
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();
        int dialogs = 0;
        var bad = new List<string>();
        foreach (var file in files)
        {
            string text = File.ReadAllText(file);
            dialogs += text.Split('\n').Count(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && Opens.IsMatch(l));
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            foreach (var (line, code) in Unguarded(text))
            {
                if (Known.Keys.Any(k => k.StartsWith(rel + "|", StringComparison.Ordinal) && code.StartsWith(k[(rel.Length + 1)..], StringComparison.Ordinal))) continue;
                bad.Add($"{rel}:{line}  {code}");
            }
        }
        _out.WriteLine($"{dialogs} lines that open a window, in {files.Count} files");
        Assert.True(dialogs > 20, "control: the scan found almost nothing that opens a window - it is not reading the editor");
        Assert.True(bad.Count == 0,
            "A window opened with no guard for the test harness: " + string.Join("; ", bad)
            + ". Answer as nobody being there would (Services.TestMode.Active: a notice is OK, a question is no, a picker is a "
            + "cancel) - in MainWindow through Box(...), in a dialog window at its static entry, in the view model through Tell/Ask.");
    }

    [Fact]
    public void TheScanSeesAnUnguardedOne()
    {
        const string unguarded = """
            public sealed class Somewhere
            {
                private void Click(object sender, RoutedEventArgs e)
                {
                    var answer = MessageBox.Show(this, "Sure?", "Title", MessageBoxButton.OKCancel);
                }

                private void Guarded(object sender, RoutedEventArgs e)
                {
                    if (Services.TestMode.Active) return;
                    var dialog = new OpenFileDialog();
                }
            }
            """;
        var found = Unguarded(unguarded);
        Assert.Single(found);
        Assert.Contains("MessageBox.Show", found[0].Text);
    }
}
