using System.Windows;
using System.Windows.Controls;
using SMSModForge.Localization;

namespace SMSModForge.View.Controls;

/// <summary>
/// How a field that borrows the game's own art (<see cref="Shared.GameArt"/>)
/// is told apart from one that names a file (the author, 1.6.3): set apart in
/// the colours the editor gives markup, in italics, with a tooltip saying
/// whose art it is and how to use art of one's own instead. Typing a path, or
/// picking a file, replaces it like any other value.
/// </summary>
internal static class BorrowedLook
{
    /// <summary>Show <paramref name="box"/> as borrowing or not, by its value.
    /// True when it borrows.</summary>
    public static bool Apply(TextBox box, string? value)
    {
        bool borrowed = Shared.GameArt.IsBorrowed(value);
        if (borrowed)
        {
            box.FontStyle = FontStyles.Italic;
            box.SetResourceReference(Control.ForegroundProperty, "Theme.Markup");
            box.SetResourceReference(Control.BackgroundProperty, "Theme.MarkupBack");
        }
        else
        {
            box.ClearValue(Control.FontStyleProperty);
            box.ClearValue(Control.ForegroundProperty);
            box.ClearValue(Control.BackgroundProperty);
        }
        return borrowed;
    }

    /// <summary>What the tooltip says of a borrowing field.</summary>
    public static string Tip(string? value)
        => Loc.F("picker.fromGame.tip", "bust", Shared.GameArt.BustOf(value) ?? "");
}
