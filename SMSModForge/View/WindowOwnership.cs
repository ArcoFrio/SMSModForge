using System.Windows;

namespace SMSModForge.View;

/// <summary>
/// Keeps the main window where the author left it when a child window closes.
/// <para/>
/// Reported from a tutorial recording: closing the mask editor sometimes
/// minimized ModForge behind everything else. Two things cause that, and both
/// are here because either one alone leaves the other able to do it:
/// <list type="bullet">
///   <item>A modal shown by a window that is ITSELF closing. Its owner is
///   being destroyed while the modal is up, so when both go, Windows is left
///   to pick a new foreground window on its own — and what it picks is not
///   necessarily the owner.</item>
///   <item>A child closing while it is the active window. The owner should be
///   activated, and usually is; when it is not, the application looks like it
///   quit.</item>
/// </list>
/// <para/>
/// Neither is reliably reproducible, which is exactly why they are worth
/// closing off rather than waiting to see one.
/// </summary>
internal static class WindowOwnership
{
    /// <summary>
    /// Whichever window a modal should belong to.
    /// <para/>
    /// Normally the window asking. While that window is closing, its owner
    /// instead — a modal parented to something about to be destroyed is the
    /// case that strands the activation chain.
    /// </summary>
    public static Window ModalOwner(Window asking, bool whileClosing)
        => whileClosing && asking?.Owner != null ? asking.Owner : asking!;

    /// <summary>
    /// Make sure this child hands focus back to its owner when it closes.
    /// <para/>
    /// Called once, where the child is created. Restoring a minimized owner is
    /// deliberate rather than defensive: the symptom being fixed is the main
    /// window ending up minimized, so leaving it minimized and merely
    /// activating it would fix nothing.
    /// </summary>
    public static void ReturnFocusToOwner(Window child)
    {
        if (child == null) return;

        child.Closed += (_, _) =>
        {
            var owner = child.Owner;
            if (owner == null) return;

            if (owner.WindowState == WindowState.Minimized)
                owner.WindowState = WindowState.Normal;
            owner.Activate();
        };
    }
}
