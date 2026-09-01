using System;
using System.IO;
using System.Text;

namespace SMSModForge.Services;

/// <summary>
/// Records tab changes nobody asked for.
/// <para/>
/// Written to catch the editor switching tabs on its own, and it did: the
/// stack it captured showed a toolbar button, at the end of its click, handing
/// keyboard focus back to a tab header, which selected that tab. The cause is
/// fixed — see the ToolBar style in App.xaml — but the watch stays, because it
/// took three attempts to find and a stack trace ended it in one.
/// <para/>
/// Every deliberate switch — a click on the tab strip, jumping to a validation
/// issue, a tutorial step — announces itself first. Anything left over is
/// written here with the stack, what had keyboard focus at the time, and where
/// it went. One file, appended to, next to the editor's other settings.
/// <para/>
/// It costs a file append per unexplained tab change, which is nothing at all
/// in normal use, because in normal use there are none.
/// </summary>
public static class TabChangeWatch
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SMSModForge", "tab-changes.log");

    /// <summary>
    /// Off for the window harness in the tests. Those drive tab changes nobody
    /// announced by design, and left on they wrote a page of false alarms into
    /// this file for every run - which had to be filtered back out of a real
    /// report by hand.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Append one unexplained change. Never throws: a diagnostic that can take
    /// the editor down with it is worse than the bug it is chasing.
    /// </summary>
    public static void Unexplained(string from, string to, string focus, string stack)
    {
        if (!Enabled) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(new string('-', 70));
            sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {from}  ->  {to}");
            sb.AppendLine($"keyboard focus: {focus}");
            sb.AppendLine(stack);

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, sb.ToString());
        }
        catch
        {
            // Nothing to do about it, and nothing worth interrupting the author for.
        }
    }
}
