using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

namespace SMSModForge.View;

/// <summary>
/// Gets a tooltip out of the way of a preview.
/// <para/>
/// A tooltip is a window of its own, floating above everything, and WPF closes
/// it when the mouse leaves the control that owns it. Move quickly enough — or
/// off a control that then changes — and the close does not happen, leaving a
/// yellow box sitting over the one part of the screen an author is trying to
/// look at.
/// <para/>
/// So a preview says "not here": on the way in, anything still open is shut,
/// and nothing of the preview's own is offered while the pointer is inside it.
/// </summary>
internal static class ToolTipDismisser
{
    /// <summary>
    /// Close every tooltip currently on screen.
    /// <para/>
    /// Tooltips live in their own top-level windows rather than in the visual
    /// tree of whatever raised them, which is why this goes looking through
    /// the presentation sources instead of walking up from a control.
    /// </summary>
    public static void CloseAll()
    {
        foreach (var source in PresentationSource.CurrentSources.OfType<HwndSource>())
        {
            if (source.RootVisual is not FrameworkElement root) continue;

            // A tooltip's popup root is either the ToolTip itself or holds one.
            if (root is ToolTip direct) { direct.IsOpen = false; continue; }
            if (LogicalTreeHelper.GetParent(root) is Popup { Child: ToolTip inPopup })
                inPopup.IsOpen = false;
        }
    }

    /// <summary>
    /// Keep tooltips off this element while the pointer is over it.
    /// <para/>
    /// Called once, where the preview is built. Both halves matter: closing
    /// what is already up deals with one left behind by a neighbour, and
    /// suppressing this element's own stops a new one appearing over the very
    /// thing it would cover.
    /// </summary>
    public static void KeepClearOf(FrameworkElement preview)
    {
        if (preview == null) return;

        ToolTipService.SetIsEnabled(preview, false);
        preview.MouseEnter += (_, _) => CloseAll();

        // Moving WITHIN the preview too: a tooltip raised by something
        // underneath can appear after the pointer is already inside.
        preview.MouseMove += (_, _) => CloseAll();
    }
}
