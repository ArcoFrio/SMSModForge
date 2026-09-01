using System;
using System.IO;
using System.Text;

namespace SMSModForge.Services;

/// <summary>
/// Records tab changes nobody asked for.
/// <para/>
/// The editor has been reported switching tabs on its own after adding a unit
/// — most recently jumping to Characters after + Rule on the Integration tab.
/// It is not the view model: <c>SelectedTabIndex</c> does not move when the
/// command runs, and there is a test that drives the whole sequence against the
/// real pack to say so. So the write is happening somewhere in the view, and
/// the only thing that will name it is a stack trace taken while it happens.
/// <para/>
/// Every deliberate switch — a click on the tab strip, jumping to a validation
/// issue, a tutorial step — announces itself first. Anything left over is
/// written here with the stack, what had keyboard focus at the time, and where
/// it went. One file, appended to, next to the editor's other settings.
/// <para/>
/// This is diagnostic scaffolding for a live bug, not a permanent feature. It
/// costs a file append per unexplained tab change, which is zero in normal use
/// because in normal use there are none.
/// </summary>
public static class TabChangeWatch
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SMSModForge", "tab-changes.log");

    /// <summary>
    /// Append one unexplained change. Never throws: a diagnostic that can take
    /// the editor down with it is worse than the bug it is chasing.
    /// </summary>
    public static void Unexplained(string from, string to, string focus, string stack)
    {
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
