using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SMSModForge.Localization;

namespace SMSModForge.View.Controls;

/// <summary>
/// A file field whose path leads outside the pack, drawn so it cannot be
/// missed (the author, 1.6.3): a red border, and a tooltip saying why it will
/// not work and what to do. Players get only what is inside the pack's folder,
/// and a path the author's own machine can follow reads as working right up
/// until somebody else plays the pack.
/// </summary>
internal static class OutsideLook
{
    public enum Where { Fine, FullPathInPack, Outside }

    /// <summary>
    /// Where <paramref name="value"/> leads, for a pack in
    /// <paramref name="packRoot"/>: fine, a full path that happens to be in the
    /// pack (it works on this machine only), or outside the pack altogether.
    /// Fine when there is no pack folder to measure against yet, or the value
    /// is empty or borrows the game's art.
    /// </summary>
    public static Where Of(string? value, string? packRoot)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrEmpty(packRoot) || Shared.GameArt.IsBorrowed(value))
            return Where.Fine;
        if (Shared.PackPaths.IsFullPath(value))
            return Inside(value, packRoot) ? Where.FullPathInPack : Where.Outside;
        return Shared.PackPaths.LeavesThePack(value) ? Where.Outside : Where.Fine;
    }

    /// <summary>Draw <paramref name="box"/> as leading outside the pack or not.
    /// True when it does.</summary>
    public static bool Apply(TextBox box, string? value, string? packRoot)
    {
        var where = Of(value, packRoot);
        if (where == Where.Fine)
        {
            box.ClearValue(Control.BorderBrushProperty);
            box.ClearValue(Control.BorderThicknessProperty);
            return false;
        }
        box.SetResourceReference(Control.BorderBrushProperty, "Theme.Danger");
        box.BorderThickness = new Thickness(2);
        box.ToolTip = Loc.T(where == Where.Outside ? "picker.outside.tip" : "picker.fullPathInPack.tip");
        return true;
    }

    private static bool Inside(string fullPath, string packRoot)
    {
        try
        {
            string root = Path.GetFullPath(packRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(fullPath).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }
}
