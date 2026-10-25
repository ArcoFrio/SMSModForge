using System.Windows.Media;

namespace SMSModForge.View.Controls;

/// <summary>
/// How a preview says it has nothing to show - "No scene sprite path set." -
/// light words on a dark chip of their own.
/// <para/>
/// A preview sits on the dark frame art is judged on in some places and on
/// the theme's own panel in others, and its message used to be a fixed mid
/// grey that fell short on both (measured, 2026-10-21: 4.0:1 on the frame,
/// 3.9:1 on a white panel). On its own chip it reads the same everywhere.
/// </summary>
internal static class PreviewMessage
{
    public static readonly Brush Ink = Frozen(Color.FromRgb(0xE6, 0xE6, 0xE6));
    public static readonly Brush Chip = Frozen(Color.FromArgb(0xC8, 0x14, 0x14, 0x14));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
